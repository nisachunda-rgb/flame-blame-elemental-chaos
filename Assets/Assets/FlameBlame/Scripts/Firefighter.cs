using UnityEngine;

namespace FlameBlame
{
    /// <summary>
    /// A firefighter body: movement, hose, water tank, getting shocked/scorched.
    /// It is driven either by PlayerController (you) or BotController (AI teammates).
    /// </summary>
    public class Firefighter : MonoBehaviour
    {
        public int Id;
        public string DisplayName;
        public Color Tint;
        public FlameBlameGame Game;

        // Inputs (set by a controller every frame)
        public Vector2 MoveInput;
        public bool Spraying;
        public Vector3 AimPoint;

        // State
        public float Tank = 100f;
        public const float TankMax = 100f;
        public float Stun;
        public string Speech;
        public float SpeechTimer;
        public Vector3 Knock;
        public bool IsBot;

        const float Radius = 0.28f;
        const float Speed = 4.2f;
        const float SprayRange = 7f;
        const float DropsPerSecond = 28f;
        const float TankUsePerSecond = 12f;

        float shockImmunity, scorchCooldown, sprayAcc;
        Renderer body;
        Transform hose;

        public bool IsStunned => Stun > 0f;

        public void Build(FlameBlameGame game, int id, string displayName, Color tint, Vector3 position, bool isBot)
        {
            Game = game; Id = id; DisplayName = displayName; Tint = tint; IsBot = isBot;
            transform.position = position;
            AimPoint = position + Vector3.forward * 3f;

            body = Prim.Make(PrimitiveType.Capsule, transform, new Vector3(0f, 0.75f, 0f), new Vector3(0.65f, 0.75f, 0.65f), tint, "Body").GetComponent<Renderer>();
            Prim.Make(PrimitiveType.Cylinder, transform, new Vector3(0f, 1.55f, 0f), new Vector3(0.75f, 0.12f, 0.75f), Prim.C(200, 30, 25), "HelmetBrim");
            Prim.Make(PrimitiveType.Sphere, transform, new Vector3(0f, 1.62f, 0f), new Vector3(0.55f, 0.4f, 0.55f), Prim.C(220, 40, 30), "Helmet");
            Prim.Make(PrimitiveType.Cube, transform, new Vector3(0f, 1.3f, 0.3f), new Vector3(0.45f, 0.15f, 0.05f), Prim.C(255, 230, 150), "Visor", false);
            var h = Prim.Make(PrimitiveType.Cylinder, transform, new Vector3(0.25f, 0.9f, 0.45f), new Vector3(0.12f, 0.35f, 0.12f), Prim.C(60, 60, 60), "Nozzle", false);
            h.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            hose = h.transform;
        }

        public void Say(string text, float seconds = 2.5f)
        {
            Speech = text;
            SpeechTimer = seconds;
        }

        void Update()
        {
            if (Game == null || !Game.IsPlaying) return;
            var sim = Game.Sim;
            float dt = Time.deltaTime;

            Stun -= dt;
            shockImmunity -= dt;
            scorchCooldown -= dt;
            SpeechTimer -= dt;

            bool stunned = IsStunned;
            Vector3 aimDir = AimPoint - transform.position;
            aimDir.y = 0f;
            bool canSpray = Spraying && !stunned && Tank > 0f;

            // ---- movement
            Vector3 vel = Vector3.zero;
            if (!stunned)
            {
                var m = new Vector3(MoveInput.x, 0f, MoveInput.y);
                if (m.sqrMagnitude > 1f) m.Normalize();
                vel = m * Speed;
            }
            if (canSpray)
            {
                vel *= 0.55f;
                if (aimDir.sqrMagnitude > 0.01f) vel -= aimDir.normalized * 0.9f; // hose recoil
            }
            Vector2Int cell = sim.WorldToCell(transform.position);
            if (sim.InBounds(cell.x, cell.y) && sim.cells[sim.Idx(cell.x, cell.y)].fire > 0.3f) vel *= 0.6f;
            vel += Knock;
            Knock = Vector3.MoveTowards(Knock, Vector3.zero, 12f * dt);
            Move(vel * dt);

            if (aimDir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(aimDir), 15f * dt);

            // ---- hose
            if (canSpray)
            {
                Tank = Mathf.Max(0f, Tank - TankUsePerSecond * dt);
                sprayAcc += dt * DropsPerSecond;
                Vector3 origin = hose.position + transform.forward * 0.3f;
                Vector3 flat = AimPoint - origin;
                flat.y = 0f;
                if (flat.magnitude > SprayRange) flat = flat.normalized * SprayRange;
                Vector3 target = new Vector3(origin.x, 0f, origin.z) + flat;
                while (sprayAcc >= 1f)
                {
                    sprayAcc -= 1f;
                    Vector2 j = Random.insideUnitCircle * (0.25f + flat.magnitude * 0.05f);
                    Game.Water.Spawn(origin, target + new Vector3(j.x, 0f, j.y), Id);
                }
            }
            else sprayAcc = 0f;

            // ---- refill at hydrant
            if ((sim.CellCenter(sim.Hydrant) - transform.position).sqrMagnitude < 1.9f * 1.9f)
                Tank = Mathf.Min(TankMax, Tank + 45f * dt);

            CheckHazards(sim);

            // ---- visuals
            body.material.color = stunned && Mathf.Repeat(Time.time * 12f, 2f) < 1f ? Color.yellow : Tint;
        }

        void CheckHazards(GridSim sim)
        {
            Vector2Int c = sim.WorldToCell(transform.position);
            if (!sim.InBounds(c.x, c.y)) return;
            Cell cell = sim.cells[sim.Idx(c.x, c.y)];

            if (cell.electrified && shockImmunity <= 0f)
            {
                int culprit = cell.type == CellType.Wire ? GridSim.Env : cell.waterOwner;
                Stun = 2.2f;
                shockImmunity = 4f;
                Vector2 k = Random.insideUnitCircle.normalized * 3f;
                Knock = new Vector3(k.x, 0f, k.y);
                Game.OnShocked(this, culprit);
            }

            if (cell.fire > 0.35f && scorchCooldown <= 0f)
            {
                scorchCooldown = 3f;
                Game.OnScorched(this, cell.fireOwner);
            }
        }

        void Move(Vector3 delta)
        {
            Vector3 p = transform.position;
            Vector3 nx = p + new Vector3(delta.x, 0f, 0f);
            if (Free(nx)) p = nx;
            Vector3 nz = p + new Vector3(0f, 0f, delta.z);
            if (Free(nz)) p = nz;
            transform.position = p;
        }

        bool Free(Vector3 q)
        {
            var sim = Game.Sim;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Vector2Int c = sim.WorldToCell(q + new Vector3(sx * Radius, 0f, sz * Radius));
                if (!sim.IsWalkable(c.x, c.y)) return false;
            }
            return true;
        }
    }
}
