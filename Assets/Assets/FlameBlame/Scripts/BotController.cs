using System.Collections.Generic;
using UnityEngine;

namespace FlameBlame
{
    /// <summary>
    /// AI teammate. Enthusiastic, clumsy, and initially clueless about the system:
    /// it hoses whatever burns (including oil) and walks through puddles near live wires.
    /// But it LEARNS: after getting shocked it avoids electrified water and goes to cut the power;
    /// after splashing burning oil a couple of times it stops spraying oil fires.
    /// </summary>
    public class BotController : MonoBehaviour
    {
        public Firefighter Actor;
        public float Sloppiness = 0.8f;     // aim jitter
        public bool TriggerHappy;           // sprays while still walking
        public string[] Barks;

        public bool KnowsElectricity;
        public bool KnowsOil;
        int oilSplashes;

        enum Mode { Fight, Refill, CutPower }
        Mode mode;
        List<Vector2Int> path;
        int pathIndex;
        bool hasTarget;
        Vector2Int target;
        float replanTimer, barkTimer = 4f, jitterTimer;
        Vector3 jitter;
        readonly List<Vector2Int> fires = new List<Vector2Int>();

        GridSim Sim => Actor.Game.Sim;

        // ---------------------------------------------------------------- learning hooks
        public void OnShocked()
        {
            if (!KnowsElectricity)
            {
                KnowsElectricity = true;
                Actor.Say("OW! Water + wires = bad. Going for the breaker!", 3.5f);
            }
            replanTimer = 0f;
        }

        public void OnSplashedOil()
        {
            oilSplashes++;
            if (!KnowsOil && oilSplashes >= 2)
            {
                KnowsOil = true;
                Actor.Say("Note to self: do NOT hose the oil.", 3.5f);
                replanTimer = 0f;
            }
        }

        // ---------------------------------------------------------------- brain
        void Update()
        {
            if (Actor == null || Actor.Game == null) return;
            var game = Actor.Game;
            if (!game.IsPlaying) { Actor.MoveInput = Vector2.zero; Actor.Spraying = false; return; }
            float dt = Time.deltaTime;
            replanTimer -= dt;
            barkTimer -= dt;
            jitterTimer -= dt;

            if (Actor.IsStunned) { Actor.MoveInput = Vector2.zero; Actor.Spraying = false; path = null; return; }

            Vector2Int me = Sim.WorldToCell(Actor.transform.position);

            // choose mode
            Mode wanted = Mode.Fight;
            if (Actor.Tank < 4f || (mode == Mode.Refill && Actor.Tank < Firefighter.TankMax * 0.95f)) wanted = Mode.Refill;
            else if (KnowsElectricity && Sim.PowerOn) wanted = Mode.CutPower;
            if (wanted != mode) { mode = wanted; replanTimer = 0f; }

            if (replanTimer <= 0f) { Replan(me); replanTimer = 0.6f; }

            // arrived at breaker?
            if (mode == Mode.CutPower && Near(Sim.Breaker, 1.7f))
            {
                game.TryInteract(Actor);
                replanTimer = 0f;
            }

            FollowPath();
            HandleSpray(dt);

            if (barkTimer <= 0f && Barks != null && Barks.Length > 0 && Actor.Spraying)
            {
                Actor.Say(Barks[Random.Range(0, Barks.Length)]);
                barkTimer = Random.Range(8f, 14f);
            }
        }

        bool Near(Vector2Int c, float dist) => (Sim.CellCenter(c) - Actor.transform.position).sqrMagnitude < dist * dist;

        bool Passable(int x, int y)
        {
            if (!Sim.IsWalkable(x, y)) return false;
            var c = Sim.cells[Sim.Idx(x, y)];
            if (c.fire > 0.4f) return false;
            if (KnowsElectricity && c.electrified) return false;
            return true;
        }

        void Replan(Vector2Int me)
        {
            hasTarget = false;
            switch (mode)
            {
                case Mode.Refill:
                    SetPath(Sim.FindPath(me, (x, y) => Mathf.Abs(x - Sim.Hydrant.x) <= 1 && Mathf.Abs(y - Sim.Hydrant.y) <= 1, Passable));
                    break;

                case Mode.CutPower:
                    SetPath(Sim.FindPath(me, (x, y) => Mathf.Abs(x - Sim.Breaker.x) <= 1 && Mathf.Abs(y - Sim.Breaker.y) <= 1, Passable));
                    break;

                default:
                    CollectVisibleFires();
                    if (fires.Count == 0) { path = null; return; }
                    var p = Sim.FindPath(me, (x, y) => NearestFireWithin(x, y, 4.5f, out _), Passable);
                    SetPath(p);
                    if (p != null)
                    {
                        Vector2Int end = p.Count > 0 ? p[p.Count - 1] : me;
                        hasTarget = NearestFireWithin(end.x, end.y, 4.5f, out target);
                    }
                    break;
            }
        }

        void CollectVisibleFires()
        {
            fires.Clear();
            for (int y = 0; y < Sim.H; y++)
            for (int x = 0; x < Sim.W; x++)
            {
                var c = Sim.cells[Sim.Idx(x, y)];
                if (c.fire <= 0.05f) continue;
                if (c.steam > 0.6f) continue;                       // steam hides fire from the bots
                if (KnowsOil && c.type == CellType.Oil) continue;    // learned lesson
                fires.Add(new Vector2Int(x, y));
            }
        }

        bool NearestFireWithin(int x, int y, float range, out Vector2Int best)
        {
            best = default;
            float bestD = range * range;
            bool found = false;
            foreach (var f in fires)
            {
                float d = (f.x - x) * (f.x - x) + (f.y - y) * (f.y - y);
                if (d <= bestD && LineOfSight(x, y, f.x, f.y)) { bestD = d; best = f; found = true; }
            }
            return found;
        }

        /// <summary>No spraying through walls: sample the segment between two cells.</summary>
        bool LineOfSight(int ax, int ay, int bx, int by)
        {
            Vector2 a = new Vector2(ax + 0.5f, ay + 0.5f), b = new Vector2(bx + 0.5f, by + 0.5f);
            int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 3f);
            for (int s = 1; s < steps; s++)
            {
                Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                int cx = Mathf.FloorToInt(p.x), cy = Mathf.FloorToInt(p.y);
                if (GridSim.BlocksWater(Sim.cells[Sim.Idx(cx, cy)].type)) return false;
            }
            return true;
        }

        void SetPath(List<Vector2Int> p)
        {
            path = p;
            pathIndex = 0;
        }

        void FollowPath()
        {
            if (path == null || pathIndex >= path.Count) { Actor.MoveInput = Vector2.zero; return; }
            Vector3 next = Sim.CellCenter(path[pathIndex]);
            Vector3 d = next - Actor.transform.position;
            d.y = 0f;
            if (d.magnitude < 0.2f) { pathIndex++; FollowPath(); return; }
            d.Normalize();
            Actor.MoveInput = new Vector2(d.x, d.z);
        }

        void HandleSpray(float dt)
        {
            Actor.Spraying = false;
            if (mode != Mode.Fight || !hasTarget) return;

            var c = Sim.cells[Sim.Idx(target.x, target.y)];
            if (c.fire <= 0f) { replanTimer = 0f; hasTarget = false; return; }

            Vector3 tp = Sim.CellCenter(target);
            float dist = (tp - Actor.transform.position).magnitude;
            bool arrived = path == null || pathIndex >= path.Count;
            if (dist > 6f || (!arrived && !TriggerHappy)) return;

            if (jitterTimer <= 0f)
            {
                Vector2Int me = Sim.WorldToCell(Actor.transform.position);
                float steam = Sim.InBounds(me.x, me.y) ? Sim.cells[Sim.Idx(me.x, me.y)].steam : 0f;
                Vector2 j = Random.insideUnitCircle * (Sloppiness + steam * 1.5f);
                jitter = new Vector3(j.x, 0f, j.y);
                jitterTimer = 0.4f;
            }
            Actor.AimPoint = tp + jitter;
            Actor.Spraying = true;
        }
    }
}
