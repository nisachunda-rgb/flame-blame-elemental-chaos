using UnityEngine;

namespace FlameBlame
{
    /// <summary>
    /// Entry point. Starts by itself when you press Play in ANY scene (no setup needed),
    /// builds the level from the text map, spawns you + 2 AI teammates, runs the round and draws the UI.
    /// </summary>
    public class FlameBlameGame : MonoBehaviour
    {
        // Legend: # wall | w wood floor | F burning wood | o oil | O burning oil | D wooden door
        //         E live wire | B breaker | g gas canister | H hydrant | P player | 1 2 AI teammates | . concrete
        static readonly string[] Level =
        {
            "####################################",
            "#wwwwwwwww#wwwwwwwwwwww#...........#",
            "#wwwwwwwww#wwwwwwwwwwww#..oooooo...#",
            "#wwwFwwwww#wwwwwEwwwwww#..oooooo.g.#",
            "#wwwwwwwww#wwwwwwwwwwww#..ooOooo...#",
            "#wwwwwwwwwDwwwgwwwwwwww#..oooooo...#",
            "#wwwwwwwww#wwwwwwwwwwwwD...........#",
            "#wwwwwwwww#wwwwwwwwwwww#...........#",
            "####D#########DB########D###########",
            "#..................................#",
            "#..................................#",
            "#.H...P..1..2......................#",
            "#..................................#",
            "####################################",
        };

        const float RoundTime = 210f;
        const float Tick = 0.1f;

        public enum State { Intro, Playing, Results }
        public State Phase { get; private set; }
        public bool IsPlaying => Phase == State.Playing;

        public GridSim Sim { get; private set; }
        public WaterStream Water { get; private set; }
        public BlameTracker Blame { get; private set; }

        GridView view;
        Firefighter player;
        Firefighter[] actors;
        BotController[] bots;
        Camera cam;
        GameObject root;
        float tickAcc, timeLeft, shake, elapsed;
        bool saved;
        string endTitle;

        static readonly Color[] ActorColors =
        {
            new Color(0.92f, 0.25f, 0.2f),   // You - red
            new Color(1f, 0.62f, 0.12f),     // Petro - orange
            new Color(0.3f, 0.8f, 1f),       // Olya - cyan
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (FindAnyObjectByType<FlameBlameGame>() != null) return;
            new GameObject("FlameBlameGame").AddComponent<FlameBlameGame>();
        }

        void Start()
        {
            // switch off the template scene's camera/light, we make our own
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.gameObject.SetActive(false);
            Application.targetFrameRate = 60;
            BuildWorld();
            Phase = State.Intro;
        }

        // ------------------------------------------------------------------ world
        void BuildWorld()
        {
            if (root != null) Destroy(root);
            root = new GameObject("World");
            tickAcc = 0f; elapsed = 0f; timeLeft = RoundTime; shake = 0f;

            Sim = new GridSim(Level);
            Blame = new BlameTracker();
            Blame.AddActor("You", ActorColors[0]);
            Blame.AddActor("Petro", ActorColors[1]);
            Blame.AddActor("Olya", ActorColors[2]);

            view = new GameObject("GridView").AddComponent<GridView>();
            view.transform.SetParent(root.transform, false);
            view.Init(Sim, ActorColors);

            Water = new GameObject("Water").AddComponent<WaterStream>();
            Water.transform.SetParent(root.transform, false);
            Water.Init(Sim);

            // light
            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.9f, 0.8f);
            sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.42f, 0.5f);

            // camera
            var camGo = new GameObject("Camera");
            camGo.transform.SetParent(root.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.06f, 0.07f);
            cam.fieldOfView = 50f;
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";

            // actors
            actors = new Firefighter[3];
            bots = new BotController[2];
            player = SpawnActor(0, "You", Sim.PlayerSpawn, false);
            var pc = player.gameObject.AddComponent<PlayerController>();
            pc.Actor = player;
            pc.Cam = cam;

            string[][] barks =
            {
                new[] { "WATER FIXES EVERYTHING!", "Is that oil? Doesn't matter!", "I was born for this!", "Spray first, think later!" },
                new[] { "Is that wire supposed to spark?", "Why is the floor buzzing?", "I'm helping! I think.", "Who left a gas can here?!" },
            };
            for (int b = 0; b < 2; b++)
            {
                var spawn = b < Sim.BotSpawns.Count ? Sim.BotSpawns[b] : Sim.PlayerSpawn;
                var a = SpawnActor(b + 1, b == 0 ? "Petro" : "Olya", spawn, true);
                var bot = a.gameObject.AddComponent<BotController>();
                bot.Actor = a;
                bot.Barks = barks[b];
                bot.TriggerHappy = b == 0;
                bot.Sloppiness = b == 0 ? 1.3f : 0.6f;
                bots[b] = bot;
            }

            cam.transform.position = player.transform.position + CamOffset;
            cam.transform.LookAt(player.transform.position);

            // wire the simulation events into blame + effects
            Sim.Ignited += Blame.Ignited;
            Sim.Extinguished += Blame.Extinguished;
            Sim.OilSplashed += (owner, x, y) =>
            {
                Blame.OilSplashed(owner);
                if (owner >= 1 && owner <= 2) bots[owner - 1].OnSplashedOil();
            };
            Sim.Exploded += (owner, x, y) =>
            {
                Blame.Exploded(owner);
                view.SpawnExplosion(x, y);
                shake = 0.6f;
                Vector3 c = Sim.CellCenter(x, y);
                foreach (var a in actors)
                {
                    Vector3 d = a.transform.position - c;
                    d.y = 0f;
                    if (d.magnitude < 4f)
                    {
                        a.Knock = d.normalized * 9f;
                        a.Stun = Mathf.Max(a.Stun, 1f);
                        a.Say("AAAAH!", 1.5f);
                    }
                }
            };
            Sim.Sparked += (x, y) => view.SpawnSpark(x, y);
        }

        static readonly Vector3 CamOffset = new Vector3(0f, 13f, -8.5f);

        Firefighter SpawnActor(int id, string name, Vector2Int cell, bool isBot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var f = go.AddComponent<Firefighter>();
            f.Build(this, id, name, ActorColors[id], Sim.CellCenter(cell), isBot);
            actors[id] = f;
            return f;
        }

        // ------------------------------------------------------------------ callbacks from actors
        public void OnShocked(Firefighter victim, int culprit)
        {
            Blame.Shocked(victim.Id, culprit);
            shake = Mathf.Max(shake, 0.25f);
            victim.Say(culprit >= 0 && culprit != victim.Id ? $"BZZZT! Thanks, {Blame.NameOf(culprit)}..." : "BZZZZZT!", 2f);
            if (victim.IsBot) bots[victim.Id - 1].OnShocked();
        }

        public void OnScorched(Firefighter victim, int owner)
        {
            Blame.Scorched(victim.Id, owner);
            victim.Say("HOT HOT HOT!", 1.5f);
        }

        public void TryInteract(Firefighter who)
        {
            if ((Sim.CellCenter(Sim.Breaker) - who.transform.position).sqrMagnitude > 1.8f * 1.8f) return;
            Sim.PowerOn = !Sim.PowerOn;
            Blame.Power(who.Id, Sim.PowerOn);
            if (!Sim.PowerOn) who.Say("Power's off!", 2f);
        }

        // ------------------------------------------------------------------ loop
        void Update()
        {
            switch (Phase)
            {
                case State.Intro:
                    if (InputCompat.StartPressed())
                    {
                        Phase = State.Playing;
                        Blame.Info("Fires in the bedroom and the garage. Go!");
                    }
                    break;

                case State.Playing:
                    float dt = Time.deltaTime;
                    elapsed += dt;
                    timeLeft -= dt;
                    tickAcc += dt;
                    while (tickAcc >= Tick) { tickAcc -= Tick; Sim.Step(Tick); }
                    if (InputCompat.ViewPressed()) view.Mode = (GridView.ViewMode)(((int)view.Mode + 1) % 3);
                    CheckEnd();
                    break;

                case State.Results:
                    if (InputCompat.RestartPressed() || InputCompat.StartPressed())
                    {
                        BuildWorld();
                        Phase = State.Intro;
                    }
                    break;
            }
            if (Phase == State.Playing && InputCompat.RestartPressed()) { BuildWorld(); Phase = State.Intro; }
        }

        void CheckEnd()
        {
            int fires = Sim.FireCount();
            float integrity = Sim.HouseIntegrity();
            if (fires == 0 && !Sim.PowerOn && elapsed > 5f) End(true, "FIRE'S OUT!");
            else if (integrity < 0.35f) End(false, "THE HOUSE IS LOST");
            else if (timeLeft <= 0f) End(fires == 0, fires == 0 ? "TIME'S UP - BUT IT'S OUT" : "TIME'S UP");
        }

        void End(bool ok, string title)
        {
            saved = ok;
            endTitle = title;
            Phase = State.Results;
        }

        void LateUpdate()
        {
            if (player == null || cam == null) return;
            Vector3 targetPos = player.transform.position + CamOffset;
            cam.transform.position = Vector3.Lerp(cam.transform.position, targetPos, 5f * Time.deltaTime);
            cam.transform.rotation = Quaternion.LookRotation(-CamOffset.normalized);
            if (shake > 0f)
            {
                shake -= Time.deltaTime;
                cam.transform.position += Random.insideUnitSphere * shake * 0.6f;
            }
        }

        // ------------------------------------------------------------------ UI (IMGUI: zero setup, works in WebGL)
        GUIStyle big, mid, small, center, bubble;
        Texture2D white;
        float uiScale;

        void InitStyles()
        {
            if (white != null) return;
            white = Texture2D.whiteTexture;
            big = new GUIStyle(GUI.skin.label) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            big.normal.textColor = new Color(1f, 0.82f, 0.35f);
            mid = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            mid.normal.textColor = Color.white;
            small = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            small.normal.textColor = new Color(0.95f, 0.92f, 0.88f);
            center = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
            bubble = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            bubble.normal.textColor = new Color(0.15f, 0.1f, 0.08f);
        }

        void Panel(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        void OnGUI()
        {
            if (Sim == null) return;
            InitStyles();
            uiScale = Screen.height / 900f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(uiScale, uiScale, 1f));
            float vw = Screen.width / uiScale, vh = 900f;

            DrawNameTags();
            if (Phase == State.Intro) { DrawIntro(vw, vh); return; }
            DrawHud(vw, vh);
            if (Phase == State.Results) DrawResults(vw, vh);
        }

        void DrawNameTags()
        {
            foreach (var a in actors)
            {
                if (a == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(a.transform.position + Vector3.up * 2.1f);
                if (sp.z < 0f) continue;
                float x = sp.x / uiScale, y = (Screen.height - sp.y) / uiScale;
                var old = mid.normal.textColor;
                mid.normal.textColor = a.Tint;
                mid.alignment = TextAnchor.MiddleCenter;
                GUI.Label(new Rect(x - 80f, y - 14f, 160f, 28f), a.DisplayName, mid);
                mid.alignment = TextAnchor.UpperLeft;
                mid.normal.textColor = old;

                if (a.SpeechTimer > 0f && !string.IsNullOrEmpty(a.Speech))
                {
                    var r = new Rect(x - 120f, y - 62f, 240f, 46f);
                    Panel(r, new Color(1f, 0.97f, 0.9f, 0.95f));
                    GUI.Label(r, a.Speech, bubble);
                }
            }
        }

        void DrawHud(float vw, float vh)
        {
            // top-left status
            Panel(new Rect(16, 16, 330, 182), new Color(0f, 0f, 0f, 0.6f));
            int fires = Sim.FireCount();
            float integ = Sim.HouseIntegrity();
            GUI.Label(new Rect(30, 24, 300, 30), $"TIME  {Mathf.CeilToInt(Mathf.Max(0f, timeLeft))}s", mid);
            GUI.Label(new Rect(30, 54, 300, 30), $"FIRES  {fires}", mid);
            GUI.Label(new Rect(30, 84, 300, 30), $"HOUSE  {Mathf.RoundToInt(integ * 100f)}%", mid);
            var oldC = mid.normal.textColor;
            mid.normal.textColor = Sim.PowerOn ? new Color(1f, 0.9f, 0.2f) : new Color(0.4f, 1f, 0.5f);
            GUI.Label(new Rect(30, 114, 300, 30), Sim.PowerOn ? "POWER  LIVE!" : "POWER  OFF", mid);
            mid.normal.textColor = oldC;
            // tank bar
            GUI.Label(new Rect(30, 146, 80, 30), "TANK", small);
            Panel(new Rect(90, 152, 230, 18), new Color(1f, 1f, 1f, 0.15f));
            Panel(new Rect(90, 152, 230 * player.Tank / Firefighter.TankMax, 18), player.Tank > 15f ? new Color(0.3f, 0.65f, 1f) : new Color(1f, 0.3f, 0.2f));
            if (player.Tank < 15f) GUI.Label(new Rect(30, 172, 320, 26), "Refill at the hydrant (bottom-left)!", small);

            // right: blame feed
            float fw = 430f;
            Panel(new Rect(vw - fw - 16, 16, fw, 40 + Blame.Feed.Count * 30), new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(new Rect(vw - fw, 22, fw, 30), "BLAME FEED", mid);
            for (int i = 0; i < Blame.Feed.Count; i++)
            {
                var e = Blame.Feed[i];
                var old = small.normal.textColor;
                var c = e.Color;
                c.a = Mathf.Clamp01(1.2f - (Time.time - e.Time) / 20f) * 0.6f + 0.4f;
                small.normal.textColor = c;
                GUI.Label(new Rect(vw - fw, 52 + i * 30, fw - 10, 30), e.Count > 1 ? $"{e.Text}  x{e.Count}" : e.Text, small);
                small.normal.textColor = old;
            }

            // bottom hint
            string viewName = view.Mode == GridView.ViewMode.Normal ? "Normal" : view.Mode == GridView.ViewMode.Blame ? "BLAME (who caused what)" : "ELECTRICITY";
            string hint = $"WASD move · Mouse aim · LMB/Space spray · E breaker · V view: {viewName} · R restart";
            if (fires == 0 && Sim.PowerOn && elapsed > 5f) hint = "Fire's out — but the wiring is still LIVE. Cut the power at the breaker (E)!";
            Panel(new Rect(0, vh - 44, vw, 44), new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(new Rect(0, vh - 44, vw, 44), hint, center);

            if (view.Mode == GridView.ViewMode.Blame)
            {
                Panel(new Rect(16, 210, 330, 120), new Color(0f, 0f, 0f, 0.6f));
                GUI.Label(new Rect(30, 216, 300, 26), "Colour = who caused it:", small);
                for (int i = 0; i < Blame.Actors.Count; i++)
                {
                    Panel(new Rect(30, 246 + i * 22, 16, 16), Blame.Actors[i].Color);
                    GUI.Label(new Rect(54, 242 + i * 22, 260, 24), Blame.Actors[i].Name, small);
                }
                Panel(new Rect(180, 246, 16, 16), new Color(0.67f, 0.47f, 1f));
                GUI.Label(new Rect(204, 242, 140, 24), "the house", small);
            }
        }

        void DrawIntro(float vw, float vh)
        {
            Panel(new Rect(0, 0, vw, vh), new Color(0.05f, 0.03f, 0.04f, 0.82f));
            GUI.Label(new Rect(0, 120, vw, 70), "FLAME & BLAME", big);
            var old = mid.normal.textColor;
            mid.normal.textColor = new Color(1f, 0.55f, 0.3f);
            mid.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(0, 190, vw, 34), "ELEMENTAL CHAOS — a systemic fire & water demo", mid);
            mid.alignment = TextAnchor.UpperLeft;
            mid.normal.textColor = old;

            float w = 640f, x = (vw - w) / 2f;
            string rules =
                "• Water puts out wood fires — and turns into STEAM that blinds everyone.\n" +
                "• Water on burning OIL makes it splash and spread. Don't.\n" +
                "• Puddles conduct electricity from the LIVE WIRE. Cut the power at the breaker.\n" +
                "• Fire near a GAS canister = boom.\n" +
                "• Your AI teammates Petro & Olya are eager, clumsy… and they learn.\n" +
                "• EVERY fire and puddle remembers who caused it. At the end — who's to blame?";
            GUI.Label(new Rect(x, 260, w, 260), rules, small);
            GUI.Label(new Rect(0, 540, vw, 30), "WASD move · Mouse aim · LMB / Space spray · E breaker · V switch view", center);
            var o2 = big.normal.textColor;
            big.fontSize = 30;
            GUI.Label(new Rect(0, 600, vw, 50), Mathf.Repeat(Time.time, 1f) < 0.6f ? "CLICK TO START" : "", big);
            big.fontSize = 46;
            big.normal.textColor = o2;
        }

        void DrawResults(float vw, float vh)
        {
            Panel(new Rect(0, 0, vw, vh), new Color(0.04f, 0.02f, 0.03f, 0.85f));
            var oc = big.normal.textColor;
            big.normal.textColor = saved ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.4f, 0.3f);
            GUI.Label(new Rect(0, 80, vw, 70), endTitle, big);
            big.normal.textColor = oc;
            GUI.Label(new Rect(0, 150, vw, 30), $"House saved: {Mathf.RoundToInt(Sim.HouseIntegrity() * 100f)}%", center);

            float w = 820f, x = (vw - w) / 2f, y = 210f;
            string[] head = { "", "Fires spread", "Oil splashes", "Shocked others", "Explosions", "Put out", "BLAME" };
            float[] cols = { 0f, 150f, 280f, 410f, 550f, 660f, 750f };
            for (int i = 0; i < head.Length; i++) GUI.Label(new Rect(x + cols[i], y, 130, 30), head[i], small);
            y += 34f;
            foreach (var a in Blame.Actors)
            {
                Panel(new Rect(x - 10, y - 2, w, 32), new Color(1f, 1f, 1f, 0.06f));
                var old = mid.normal.textColor;
                mid.normal.textColor = a.Color;
                GUI.Label(new Rect(x, y, 150, 30), a.Name, mid);
                mid.normal.textColor = old;
                GUI.Label(new Rect(x + cols[1], y + 2, 120, 30), a.FiresStarted.ToString(), small);
                GUI.Label(new Rect(x + cols[2], y + 2, 120, 30), a.OilSplashes.ToString(), small);
                GUI.Label(new Rect(x + cols[3], y + 2, 120, 30), a.ShockedOthers.ToString(), small);
                GUI.Label(new Rect(x + cols[4], y + 2, 120, 30), a.Explosions.ToString(), small);
                GUI.Label(new Rect(x + cols[5], y + 2, 120, 30), Mathf.RoundToInt(a.Extinguished * 10f).ToString(), small);
                GUI.Label(new Rect(x + cols[6], y + 2, 120, 30), Mathf.RoundToInt(a.BlameScore).ToString(), small);
                y += 38f;
            }

            var worst = Blame.MostToBlame();
            if (worst != null)
            {
                y += 30f;
                GUI.Label(new Rect(0, y, vw, 30), "MOST TO BLAME", center);
                var o = big.normal.textColor;
                big.normal.textColor = worst.Color;
                GUI.Label(new Rect(0, y + 28, vw, 70), worst.Name.ToUpperInvariant(), big);
                big.normal.textColor = o;
            }
            GUI.Label(new Rect(0, vh - 90, vw, 30), "Press R or click to play again", center);
        }
    }
}
