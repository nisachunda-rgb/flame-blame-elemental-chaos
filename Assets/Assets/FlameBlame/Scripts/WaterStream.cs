using System.Collections.Generic;
using UnityEngine;

namespace FlameBlame
{
    /// <summary>Hose water: pooled droplets that fly in an arc and turn into puddle water (with an owner) where they land.</summary>
    public class WaterStream : MonoBehaviour
    {
        const int PoolSize = 500;
        const float WaterPerDrop = 0.035f;
        const float WallHeight = 1.6f;

        struct Drop
        {
            public Vector3 from, to;
            public float t, duration, arc;
            public int owner;
            public Vector2Int lastFree;
            public bool alive;
        }

        GridSim sim;
        readonly Drop[] drops = new Drop[PoolSize];
        readonly Transform[] visuals = new Transform[PoolSize];
        readonly Stack<int> free = new Stack<int>();

        public void Init(GridSim s)
        {
            sim = s;
            var mat = Prim.NewMaterial(Prim.C(110, 185, 255));
            for (int i = 0; i < PoolSize; i++)
            {
                var go = Prim.MakeShared(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one * 0.16f, mat, "Drop");
                go.SetActive(false);
                visuals[i] = go.transform;
                free.Push(i);
            }
        }

        public void Spawn(Vector3 from, Vector3 to, int owner)
        {
            if (free.Count == 0) return;
            int i = free.Pop();
            float dist = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(to.x, 0f, to.z));
            drops[i] = new Drop
            {
                from = from,
                to = new Vector3(to.x, 0.05f, to.z),
                t = 0f,
                duration = dist / 12f + 0.08f,
                arc = 0.15f + dist * 0.08f,
                owner = owner,
                lastFree = sim.WorldToCell(from),
                alive = true
            };
            visuals[i].position = from;
            visuals[i].gameObject.SetActive(true);
        }

        void Update()
        {
            if (sim == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < PoolSize; i++)
            {
                if (!drops[i].alive) continue;
                ref Drop d = ref drops[i];
                d.t += dt;
                float u = Mathf.Clamp01(d.t / d.duration);
                Vector3 p = Vector3.Lerp(d.from, d.to, u);
                p.y += d.arc * 4f * u * (1f - u);
                visuals[i].position = p;

                Vector2Int c = sim.WorldToCell(p);
                bool blocked = !sim.InBounds(c.x, c.y)
                               || (GridSim.BlocksWater(sim.cells[sim.Idx(c.x, c.y)].type) && p.y < WallHeight);
                if (blocked) { Land(i, d.lastFree); continue; }
                d.lastFree = c;
                if (u >= 1f) Land(i, c);
            }
        }

        void Land(int i, Vector2Int c)
        {
            sim.AddWater(c.x, c.y, WaterPerDrop, drops[i].owner);
            drops[i].alive = false;
            visuals[i].gameObject.SetActive(false);
            free.Push(i);
        }
    }
}
