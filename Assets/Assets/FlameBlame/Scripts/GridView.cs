using System.Collections.Generic;
using UnityEngine;

namespace FlameBlame
{
    /// <summary>
    /// Draws the simulation: floor texture (water, oil, burnt wood, live current), walls, props,
    /// flames and steam. Has 3 view modes (V key) to show the system for the video:
    /// Normal / Blame (who owns each fire & puddle) / Electricity.
    /// </summary>
    public class GridView : MonoBehaviour
    {
        public enum ViewMode { Normal, Blame, Electricity }
        public ViewMode Mode;

        GridSim sim;
        Color[] ownerColors;
        Texture2D tex;
        Color32[] px;
        Transform[] flames, steams;
        Material flameA, flameB, steamMat;
        readonly Dictionary<int, GameObject> props = new Dictionary<int, GameObject>();
        readonly List<Transform> sparks = new List<Transform>();
        Renderer breakerLight;
        readonly List<(Transform t, float age)> blasts = new List<(Transform, float)>();
        readonly List<(Transform t, float age)> sparkFx = new List<(Transform, float)>();

        static readonly Color32 ConcreteA = new Color32(128, 128, 134, 255), ConcreteB = new Color32(120, 120, 126, 255);
        static readonly Color32 WoodA = new Color32(168, 116, 66, 255), WoodB = new Color32(150, 102, 56, 255);
        static readonly Color32 OilC = new Color32(42, 34, 48, 255);
        static readonly Color32 DoorC = new Color32(110, 66, 32, 255);
        static readonly Color32 BurntC = new Color32(32, 26, 24, 255);
        static readonly Color32 WaterC = new Color32(40, 120, 230, 255);
        static readonly Color32 ZapC = new Color32(255, 245, 90, 255);
        static readonly Color32 WireC = new Color32(40, 40, 40, 255);

        public void Init(GridSim s, Color[] owners)
        {
            sim = s;
            ownerColors = owners;

            // floor = one quad with a tiny texture, 1 pixel per cell
            tex = new Texture2D(sim.W, sim.H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            px = new Color32[sim.W * sim.H];
            var floor = Prim.Make(PrimitiveType.Quad, transform, new Vector3(sim.W / 2f, 0f, sim.H / 2f), new Vector3(sim.W, sim.H, 1f), Color.white, "Floor");
            floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            floor.GetComponent<Renderer>().material.mainTexture = tex;

            // outside ground
            var outside = Prim.Make(PrimitiveType.Quad, transform, new Vector3(sim.W / 2f, -0.02f, sim.H / 2f), new Vector3(sim.W + 60f, sim.H + 60f, 1f), Prim.C(52, 70, 44), "Grass");
            outside.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var wallMat = Prim.NewMaterial(Prim.C(92, 78, 72));
            flameA = Prim.NewMaterial(Prim.C(255, 190, 40));
            flameB = Prim.NewMaterial(Prim.C(245, 90, 25));
            steamMat = Prim.NewMaterial(Prim.C(235, 238, 242));
            flames = new Transform[sim.W * sim.H];
            steams = new Transform[sim.W * sim.H];

            for (int y = 0; y < sim.H; y++)
            for (int x = 0; x < sim.W; x++)
            {
                int i = sim.Idx(x, y);
                Vector3 c = sim.CellCenter(x, y);
                switch (sim.cells[i].type)
                {
                    case CellType.Wall:
                        var wall = Prim.MakeShared(PrimitiveType.Cube, transform, c + Vector3.up * 0.8f, new Vector3(1f, 1.6f, 1f), wallMat, "Wall");
                        wall.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                        break;
                    case CellType.Breaker:
                        Prim.Make(PrimitiveType.Cube, transform, c + Vector3.up * 0.8f, new Vector3(1f, 1.6f, 1f), Prim.C(70, 75, 85), "Breaker");
                        Prim.Make(PrimitiveType.Cube, transform, c + new Vector3(0f, 1.0f, -0.52f), new Vector3(0.6f, 0.7f, 0.08f), Prim.C(160, 165, 175), "BreakerPanel");
                        breakerLight = Prim.Make(PrimitiveType.Sphere, transform, c + new Vector3(0f, 1.2f, -0.58f), Vector3.one * 0.25f, Color.red, "BreakerLight", false).GetComponent<Renderer>();
                        break;
                    case CellType.Gas:
                        var gas = Prim.Make(PrimitiveType.Cylinder, transform, c + Vector3.up * 0.45f, new Vector3(0.55f, 0.45f, 0.55f), Prim.C(210, 40, 35), "GasCanister");
                        Prim.Make(PrimitiveType.Cylinder, gas.transform, new Vector3(0f, 1.1f, 0f), new Vector3(0.3f, 0.2f, 0.3f), Prim.C(90, 90, 90), "Valve");
                        props[i] = gas;
                        break;
                    case CellType.Hydrant:
                        var hy = Prim.Make(PrimitiveType.Cylinder, transform, c + Vector3.up * 0.4f, new Vector3(0.45f, 0.4f, 0.45f), Prim.C(220, 50, 40), "Hydrant");
                        Prim.Make(PrimitiveType.Sphere, hy.transform, new Vector3(0f, 1.05f, 0f), new Vector3(1.1f, 0.8f, 1.1f), Prim.C(240, 200, 60), "HydrantTop");
                        props[i] = hy;
                        break;
                    case CellType.Wire:
                        Prim.Make(PrimitiveType.Cylinder, transform, c + Vector3.up * 0.6f, new Vector3(0.08f, 0.6f, 0.08f), Prim.C(30, 30, 30), "Wire");
                        var sp = Prim.Make(PrimitiveType.Cube, transform, c + Vector3.up * 1.2f, Vector3.one * 0.2f, Prim.C(255, 250, 120), "Spark", false);
                        sparks.Add(sp.transform);
                        break;
                }
            }
        }

        public void SpawnExplosion(int x, int y)
        {
            int i = sim.Idx(x, y);
            if (props.TryGetValue(i, out var go)) { Destroy(go); props.Remove(i); }
            var b = Prim.Make(PrimitiveType.Sphere, transform, sim.CellCenter(x, y) + Vector3.up * 0.5f, Vector3.one, Prim.C(255, 150, 40), "Blast", false);
            blasts.Add((b.transform, 0f));
        }

        public void SpawnSpark(int x, int y)
        {
            for (int k = 0; k < 4; k++)
            {
                var s = Prim.Make(PrimitiveType.Cube, transform, sim.CellCenter(x, y) + Vector3.up * 1.1f, Vector3.one * 0.1f, Prim.C(255, 240, 100), "SparkFx", false);
                sparkFx.Add((s.transform, 0f));
            }
        }

        void LateUpdate()
        {
            if (sim == null) return;
            DrawFloor();
            DrawFlamesAndSteam();
            AnimateFx();
        }

        void DrawFloor()
        {
            float t = Time.time;
            bool zapPhase = Mathf.Repeat(t * 14f, 1f) < 0.5f;
            for (int y = 0; y < sim.H; y++)
            for (int x = 0; x < sim.W; x++)
            {
                int i = sim.Idx(x, y);
                Cell c = sim.cells[i];
                bool alt = ((x + y) & 1) == 0;
                Color32 col;
                switch (c.type)
                {
                    case CellType.Wood: col = c.burnt ? BurntC : Color32.Lerp(BurntC, alt ? WoodA : WoodB, 0.35f + 0.65f * c.fuel); break;
                    case CellType.Oil: col = c.burnt ? BurntC : OilC; break;
                    case CellType.Door: col = DoorC; break;
                    case CellType.Wire: col = WireC; break;
                    default: col = c.burnt ? BurntC : (alt ? ConcreteA : ConcreteB); break;
                }

                if (Mode == ViewMode.Normal)
                {
                    if (c.water > 0.01f) col = Color32.Lerp(col, WaterC, Mathf.Clamp01(c.water * 2.2f) * 0.8f);
                    if (c.electrified && c.water >= GridSim.ConductThreshold && zapPhase) col = Color32.Lerp(col, ZapC, 0.65f);
                    if (c.fire > 0f) col = Color32.Lerp(col, new Color32(255, 120, 30, 255), 0.4f * c.fire);
                }
                else if (Mode == ViewMode.Blame)
                {
                    col = Color32.Lerp(col, new Color32(20, 20, 24, 255), 0.75f);
                    if (c.fire > 0f) col = OwnerColor(c.fireOwner);
                    else if (c.water > 0.05f) col = Color32.Lerp(col, OwnerColor(c.waterOwner), 0.35f + Mathf.Clamp01(c.water) * 0.45f);
                }
                else // Electricity
                {
                    byte w = (byte)Mathf.Clamp(c.water * 255f, 0f, 255f);
                    col = new Color32((byte)(w / 6), (byte)(w / 3), w, 255);
                    if (c.electrified) col = zapPhase ? ZapC : new Color32(255, 200, 40, 255);
                }
                px[i] = col;
            }
            tex.SetPixels32(px);
            tex.Apply(false);

            if (breakerLight != null)
                breakerLight.material.color = sim.PowerOn ? (zapPhase ? Color.red : new Color(0.5f, 0f, 0f)) : Color.green;
            foreach (var s in sparks)
            {
                s.gameObject.SetActive(sim.PowerOn && Random.value < 0.6f);
                s.localScale = Vector3.one * Random.Range(0.1f, 0.3f);
            }
        }

        Color32 OwnerColor(int owner)
        {
            if (owner >= 0 && owner < ownerColors.Length) return ownerColors[owner];
            return new Color32(170, 120, 255, 255); // environment = purple
        }

        void DrawFlamesAndSteam()
        {
            float t = Time.time;
            for (int i = 0; i < sim.cells.Length; i++)
            {
                Cell c = sim.cells[i];
                int x = i % sim.W, y = i / sim.W;

                // flames
                if (c.fire > 0f)
                {
                    if (flames[i] == null)
                    {
                        var f = Prim.MakeShared(PrimitiveType.Cube, transform, Vector3.zero, Vector3.one, ((x + y) & 1) == 0 ? flameA : flameB, "Flame");
                        f.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 90f), 0f);
                        flames[i] = f.transform;
                    }
                    var ft = flames[i];
                    if (!ft.gameObject.activeSelf) ft.gameObject.SetActive(true);
                    float flicker = 0.75f + 0.5f * Mathf.PerlinNoise(t * 7f, i * 0.37f);
                    float h = (0.2f + c.fire * 1.3f) * flicker;
                    float wdt = 0.35f + c.fire * 0.45f;
                    ft.localScale = new Vector3(wdt, h, wdt);
                    ft.localPosition = sim.CellCenter(x, y) + Vector3.up * (h * 0.5f);
                }
                else if (flames[i] != null && flames[i].gameObject.activeSelf) flames[i].gameObject.SetActive(false);

                // steam
                if (c.steam > 0.06f)
                {
                    if (steams[i] == null)
                        steams[i] = Prim.MakeShared(PrimitiveType.Cube, transform, Vector3.zero, Vector3.one, steamMat, "Steam").transform;
                    var st = steams[i];
                    if (!st.gameObject.activeSelf) st.gameObject.SetActive(true);
                    float s = Mathf.Clamp01(c.steam) * 1.15f;
                    float bob = Mathf.Sin(t * 2f + i) * 0.1f;
                    st.localScale = new Vector3(s, s * 0.55f, s);
                    st.localPosition = sim.CellCenter(x, y) + Vector3.up * (1.5f + bob + c.steam * 0.3f);
                }
                else if (steams[i] != null && steams[i].gameObject.activeSelf) steams[i].gameObject.SetActive(false);
            }
        }

        void AnimateFx()
        {
            float dt = Time.deltaTime;
            for (int k = blasts.Count - 1; k >= 0; k--)
            {
                var (tr, age) = blasts[k];
                age += dt;
                if (age > 0.5f) { Destroy(tr.gameObject); blasts.RemoveAt(k); continue; }
                tr.localScale = Vector3.one * (1f + age * 12f);
                blasts[k] = (tr, age);
            }
            for (int k = sparkFx.Count - 1; k >= 0; k--)
            {
                var (tr, age) = sparkFx[k];
                age += dt;
                if (age > 0.35f) { Destroy(tr.gameObject); sparkFx.RemoveAt(k); continue; }
                tr.localPosition += new Vector3(Mathf.Sin(k * 7.1f), 1.5f - age * 6f, Mathf.Cos(k * 3.3f)) * dt * 3f;
                sparkFx[k] = (tr, age);
            }
        }
    }
}
