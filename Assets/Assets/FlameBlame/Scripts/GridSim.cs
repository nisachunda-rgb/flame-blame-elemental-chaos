using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlameBlame
{
    public enum CellType { Floor, Wood, Oil, Door, Wall, Wire, Breaker, Gas, Hydrant }

    /// <summary>One square of the level. Every element (fire, water, steam, power) lives here.</summary>
    public struct Cell
    {
        public CellType type;
        public float fuel;      // how much there is left to burn (for Gas: fuse before explosion)
        public float fire;      // 0..1 fire intensity
        public float water;     // 0..1.5 puddle depth
        public float steam;     // 0..2 steam density
        public bool burnt;
        public bool electrified;
        public int fireOwner;   // who caused this fire (actor id, or Env)
        public int waterOwner;  // whose water this is (actor id, or Env)
    }

    /// <summary>
    /// The whole "system": a cellular simulation where fire, water, steam, oil, gas and electricity
    /// interact through simple local rules. Every fire and every puddle remembers who caused it,
    /// which is what makes the Blame part possible.
    /// </summary>
    public class GridSim
    {
        public const int Env = -1;                 // "nobody" (the house, the wiring...)
        public const float ConductThreshold = 0.12f;

        // Tuning
        const float SpreadRate = 0.12f;
        const float WaterFlowRate = 1.2f;
        const float SteamFlowRate = 0.8f;
        const float SparkChancePerSecond = 0.1f;

        public readonly int W, H;
        public readonly Cell[] cells;
        public bool PowerOn = true;
        public readonly float InitialFuel;

        public Vector2Int PlayerSpawn;
        public readonly List<Vector2Int> BotSpawns = new List<Vector2Int>();
        public readonly List<Vector2Int> Wires = new List<Vector2Int>();
        public Vector2Int Breaker, Hydrant;

        public event Action<int> Ignited;                 // owner
        public event Action<int, float> Extinguished;     // owner, amount
        public event Action<int, int, int> OilSplashed;   // owner, x, y
        public event Action<int, int, int> Exploded;      // owner, x, y
        public event Action<int, int> Sparked;            // x, y

        readonly float[] buf;
        readonly Queue<int> queue = new Queue<int>();
        static readonly int[] DX = { 1, -1, 0, 0 };
        static readonly int[] DY = { 0, 0, 1, -1 };

        // ------------------------------------------------------------------ setup
        public GridSim(string[] map)
        {
            H = map.Length;
            W = map[0].Length;
            cells = new Cell[W * H];
            buf = new float[W * H];

            for (int row = 0; row < H; row++)
            {
                int y = H - 1 - row; // first text row is the "north" side of the level
                for (int x = 0; x < W; x++)
                {
                    char ch = x < map[row].Length ? map[row][x] : '#';
                    ref Cell c = ref cells[Idx(x, y)];
                    c.fireOwner = Env;
                    c.waterOwner = Env;
                    switch (ch)
                    {
                        case '#': c.type = CellType.Wall; break;
                        case 'w': c.type = CellType.Wood; c.fuel = 1f; break;
                        case 'F': c.type = CellType.Wood; c.fuel = 1f; c.fire = 0.8f; break;
                        case 'o': c.type = CellType.Oil; c.fuel = 0.7f; break;
                        case 'O': c.type = CellType.Oil; c.fuel = 0.7f; c.fire = 0.8f; break;
                        case 'D': c.type = CellType.Door; c.fuel = 0.8f; break;
                        case 'E': c.type = CellType.Wire; Wires.Add(new Vector2Int(x, y)); break;
                        case 'B': c.type = CellType.Breaker; Breaker = new Vector2Int(x, y); break;
                        case 'g': c.type = CellType.Gas; c.fuel = 4f; break;
                        case 'H': c.type = CellType.Hydrant; Hydrant = new Vector2Int(x, y); break;
                        case 'P': c.type = CellType.Floor; PlayerSpawn = new Vector2Int(x, y); break;
                        case '1':
                        case '2':
                        case '3': c.type = CellType.Floor; BotSpawns.Add(new Vector2Int(x, y)); break;
                        default: c.type = CellType.Floor; break;
                    }
                }
            }
            InitialFuel = HouseFuel();
        }

        // ------------------------------------------------------------------ helpers
        public int Idx(int x, int y) => y * W + x;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
        public Vector2Int WorldToCell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z));
        public Vector3 CellCenter(int x, int y) => new Vector3(x + 0.5f, 0f, y + 0.5f);
        public Vector3 CellCenter(Vector2Int c) => CellCenter(c.x, c.y);

        public static bool BlocksWater(CellType t) => t == CellType.Wall || t == CellType.Breaker;

        public bool IsWalkable(int x, int y)
        {
            if (!InBounds(x, y)) return false;
            var t = cells[Idx(x, y)].type;
            return t != CellType.Wall && t != CellType.Breaker && t != CellType.Gas && t != CellType.Hydrant;
        }

        public static float Flammability(CellType t)
        {
            switch (t)
            {
                case CellType.Wood: return 0.35f;
                case CellType.Door: return 0.3f;
                case CellType.Oil: return 1.4f;
                default: return 0f;
            }
        }

        static float Growth(CellType t) => t == CellType.Oil ? 1.2f : 0.4f;

        static float BurnRate(CellType t)
        {
            switch (t)
            {
                case CellType.Oil: return 0.09f;
                case CellType.Door: return 0.045f;
                default: return 0.03f;
            }
        }

        public void AddWater(int x, int y, float amount, int owner)
        {
            if (!InBounds(x, y)) return;
            ref Cell c = ref cells[Idx(x, y)];
            if (BlocksWater(c.type)) return;
            c.water = Mathf.Min(c.water + amount, 1.5f);
            c.waterOwner = owner;
        }

        public void Ignite(int x, int y, float amount, int owner)
        {
            ref Cell c = ref cells[Idx(x, y)];
            if (Flammability(c.type) <= 0f || c.fuel <= 0f) return;
            c.fire = Mathf.Max(c.fire, amount);
            c.fireOwner = owner;
            Ignited?.Invoke(owner);
        }

        public int FireCount()
        {
            int n = 0;
            for (int i = 0; i < cells.Length; i++) if (cells[i].fire > 0f) n++;
            return n;
        }

        float HouseFuel()
        {
            float f = 0f;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].type == CellType.Wood || cells[i].type == CellType.Door) f += cells[i].fuel;
            return f;
        }

        /// <summary>0..1 — how much of the wooden house is still standing.</summary>
        public float HouseIntegrity() => InitialFuel <= 0f ? 1f : Mathf.Clamp01(HouseFuel() / InitialFuel);

        // ------------------------------------------------------------------ simulation
        public void Step(float dt)
        {
            UpdateFire(dt);
            FlowWater(dt);
            FlowSteam(dt);
            Sparks(dt);
            ComputeElectricity();
        }

        void UpdateFire(float dt)
        {
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = Idx(x, y);
                if (cells[i].fire <= 0f) continue;

                // --- water meets fire
                if (cells[i].water > 0.02f)
                {
                    if (cells[i].type == CellType.Oil)
                    {
                        // Water on burning oil does NOT help: it flares up and splashes burning oil around.
                        float used = cells[i].water;
                        int splasher = cells[i].waterOwner;
                        cells[i].water = 0f;
                        cells[i].steam += used * 0.5f;
                        cells[i].fire = Mathf.Min(1f, cells[i].fire + used);
                        if (UnityEngine.Random.value < used * 4f) SplashOil(x, y, splasher);
                    }
                    else
                    {
                        float k = Mathf.Min(cells[i].fire, cells[i].water * 2f);
                        cells[i].fire -= k;
                        cells[i].water -= k * 0.5f;
                        cells[i].steam += k * 1.5f;
                        if (cells[i].waterOwner != Env) Extinguished?.Invoke(cells[i].waterOwner, k);
                        if (cells[i].fire < 0.02f) { cells[i].fire = 0f; continue; }
                    }
                }

                // --- burning
                CellType type = cells[i].type;
                float f = cells[i].fire = Mathf.Min(1f, cells[i].fire + Growth(type) * dt);
                cells[i].fuel -= f * BurnRate(type) * dt;
                if (cells[i].fuel <= 0f)
                {
                    cells[i].fuel = 0f;
                    cells[i].fire = 0f;
                    cells[i].burnt = true;
                    if (type == CellType.Door) cells[i].type = CellType.Floor; // burnt door = open passage
                    continue;
                }

                // --- spreading (the new fire inherits the owner → blame travels with the flames)
                int owner = cells[i].fireOwner;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], ny = y + DY[d];
                    if (!InBounds(nx, ny)) continue;
                    int j = Idx(nx, ny);

                    if (cells[j].type == CellType.Gas)
                    {
                        cells[j].fuel -= f * dt; // the canister heats up
                        if (cells[j].fuel <= 0f) Explode(nx, ny, owner);
                        continue;
                    }
                    if (cells[j].fire > 0f || cells[j].fuel <= 0f) continue;
                    float fl = Flammability(cells[j].type);
                    if (fl <= 0f) continue;
                    float p = f * fl * SpreadRate * dt * (1f - Mathf.Clamp01(cells[j].water * 4f)); // wet = safe
                    if (UnityEngine.Random.value < p) Ignite(nx, ny, 0.15f, owner);
                }
            }
        }

        void SplashOil(int x, int y, int owner)
        {
            int count = 0;
            for (int attempt = 0; attempt < 6 && count < 2; attempt++)
            {
                int dx = UnityEngine.Random.Range(-2, 3), dy = UnityEngine.Random.Range(-2, 3);
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (!InBounds(nx, ny)) continue;
                // can't splash through walls
                int mx = x + dx / 2, my = y + dy / 2;
                if (cells[Idx(mx, my)].type == CellType.Wall) continue;

                int j = Idx(nx, ny);
                CellType t = cells[j].type;
                if (t == CellType.Wall || t == CellType.Breaker || t == CellType.Hydrant || t == CellType.Wire) continue;
                if (t == CellType.Gas)
                {
                    cells[j].fuel -= 1f;
                    if (cells[j].fuel <= 0f) Explode(nx, ny, owner);
                    continue;
                }
                if (cells[j].fire > 0.3f) continue;
                if (t == CellType.Floor)
                {
                    // burning oil lands on the floor → new oil puddle
                    cells[j].type = CellType.Oil;
                    cells[j].fuel = 0.35f;
                    cells[j].burnt = false;
                }
                if (Flammability(cells[j].type) > 0f && cells[j].fuel > 0f)
                {
                    Ignite(nx, ny, 0.5f, owner);
                    count++;
                }
            }
            if (count > 0) OilSplashed?.Invoke(owner, x, y);
        }

        public void Explode(int x, int y, int owner)
        {
            int gi = Idx(x, y);
            if (cells[gi].type != CellType.Gas) return;
            cells[gi].type = CellType.Floor;
            cells[gi].fuel = 0f;
            cells[gi].burnt = true;

            for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                if (dx * dx + dy * dy > 6.5f) continue;
                int nx = x + dx, ny = y + dy;
                if (!InBounds(nx, ny)) continue;
                int j = Idx(nx, ny);
                if (cells[j].type == CellType.Wall) continue;
                cells[j].water = 0f;      // blast evaporates puddles
                cells[j].steam += 0.3f;
                if (Flammability(cells[j].type) > 0f && cells[j].fuel > 0f) Ignite(nx, ny, 0.9f, owner);
            }
            Exploded?.Invoke(owner, x, y);
        }

        void FlowWater(float dt)
        {
            Array.Clear(buf, 0, buf.Length);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = Idx(x, y);
                if (BlocksWater(cells[i].type)) continue;
                ExchangeWater(i, x + 1, y, dt);
                ExchangeWater(i, x, y + 1, dt);
            }
            for (int i = 0; i < cells.Length; i++)
            {
                float w = cells[i].water + buf[i];
                w -= (0.015f + w * 0.04f + cells[i].fire * 0.3f) * dt; // evaporation
                cells[i].water = Mathf.Max(0f, w);
            }
        }

        void ExchangeWater(int i, int nx, int ny, float dt)
        {
            if (!InBounds(nx, ny)) return;
            int j = Idx(nx, ny);
            if (BlocksWater(cells[j].type)) return;
            float flow = (cells[i].water - cells[j].water) * WaterFlowRate * dt;
            buf[i] -= flow;
            buf[j] += flow;
            // water that flows into a dry cell keeps its owner
            if (flow > 0.002f && cells[j].water < cells[i].water * 0.5f) cells[j].waterOwner = cells[i].waterOwner;
            else if (flow < -0.002f && cells[i].water < cells[j].water * 0.5f) cells[i].waterOwner = cells[j].waterOwner;
        }

        void FlowSteam(float dt)
        {
            Array.Clear(buf, 0, buf.Length);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = Idx(x, y);
                if (BlocksWater(cells[i].type)) continue;
                ExchangeSteam(i, x + 1, y, dt);
                ExchangeSteam(i, x, y + 1, dt);
            }
            for (int i = 0; i < cells.Length; i++)
            {
                float s = cells[i].steam + buf[i];
                s -= (0.25f * s + 0.02f) * dt;
                cells[i].steam = Mathf.Clamp(s, 0f, 2f);
            }
        }

        void ExchangeSteam(int i, int nx, int ny, float dt)
        {
            if (!InBounds(nx, ny)) return;
            int j = Idx(nx, ny);
            if (BlocksWater(cells[j].type)) return;
            float flow = (cells[i].steam - cells[j].steam) * SteamFlowRate * dt;
            buf[i] -= flow;
            buf[j] += flow;
        }

        void Sparks(float dt)
        {
            if (!PowerOn) return;
            foreach (var w in Wires)
            {
                if (UnityEngine.Random.value > SparkChancePerSecond * dt) continue;
                Sparked?.Invoke(w.x, w.y);
                int nx = w.x + UnityEngine.Random.Range(-1, 2), ny = w.y + UnityEngine.Random.Range(-1, 2);
                if (!InBounds(nx, ny)) continue;
                int j = Idx(nx, ny);
                if (cells[j].fire <= 0f && cells[j].water < 0.1f) Ignite(nx, ny, 0.3f, Env);
            }
        }

        void ComputeElectricity()
        {
            for (int i = 0; i < cells.Length; i++) cells[i].electrified = false;
            if (!PowerOn) return;
            queue.Clear();
            foreach (var w in Wires)
            {
                int i = Idx(w.x, w.y);
                cells[i].electrified = true;
                queue.Enqueue(i);
            }
            // current travels through every connected puddle
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % W, y = i / W;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], ny = y + DY[d];
                    if (!InBounds(nx, ny)) continue;
                    int j = Idx(nx, ny);
                    if (cells[j].electrified || BlocksWater(cells[j].type)) continue;
                    if (cells[j].water < ConductThreshold) continue;
                    cells[j].electrified = true;
                    queue.Enqueue(j);
                }
            }
        }

        // ------------------------------------------------------------------ pathfinding (for AI teammates)
        int[] prevBuf;
        int[] stampBuf;
        int stamp;

        /// <summary>BFS on the grid. Returns the cells to walk through (start excluded), or null.</summary>
        public List<Vector2Int> FindPath(Vector2Int from, Func<int, int, bool> isGoal, Func<int, int, bool> passable)
        {
            if (prevBuf == null) { prevBuf = new int[W * H]; stampBuf = new int[W * H]; }
            stamp++;
            if (!InBounds(from.x, from.y)) return null;
            queue.Clear();
            int s = Idx(from.x, from.y);
            stampBuf[s] = stamp;
            prevBuf[s] = -1;
            queue.Enqueue(s);
            int found = -1;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % W, y = i / W;
                if (isGoal(x, y)) { found = i; break; }
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], ny = y + DY[d];
                    if (!InBounds(nx, ny)) continue;
                    int j = Idx(nx, ny);
                    if (stampBuf[j] == stamp) continue;
                    if (!passable(nx, ny)) continue;
                    stampBuf[j] = stamp;
                    prevBuf[j] = i;
                    queue.Enqueue(j);
                }
            }
            queue.Clear();
            if (found < 0) return null;
            var path = new List<Vector2Int>();
            for (int i = found; i != s && i >= 0; i = prevBuf[i]) path.Add(new Vector2Int(i % W, i / W));
            path.Reverse();
            return path;
        }
    }
}
