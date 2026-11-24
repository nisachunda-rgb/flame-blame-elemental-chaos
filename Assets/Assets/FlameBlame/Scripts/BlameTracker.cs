using System.Collections.Generic;
using UnityEngine;

namespace FlameBlame
{
    /// <summary>
    /// Listens to the simulation and remembers who caused what.
    /// Feeds the live event feed and the end-of-round "Who's to blame" board.
    /// </summary>
    public class BlameTracker
    {
        public class Stats
        {
            public string Name;
            public Color Color;
            public int FiresStarted;      // fire cells that spread from a fire this actor caused
            public int OilSplashes;
            public int ShockedOthers;
            public int ShockedSelf;
            public int Explosions;
            public float Extinguished;

            public float BlameScore =>
                FiresStarted * 1f + OilSplashes * 3f + ShockedOthers * 15f + ShockedSelf * 5f + Explosions * 25f
                - Extinguished * 1.5f;
        }

        public struct Entry
        {
            public string Text;
            public Color Color;
            public float Time;
            public string Key;
            public int Count;
        }

        public readonly List<Stats> Actors = new List<Stats>();
        public readonly List<Entry> Feed = new List<Entry>();
        const int FeedSize = 7;

        public void AddActor(string name, Color color) => Actors.Add(new Stats { Name = name, Color = color });

        public string NameOf(int id) => id >= 0 && id < Actors.Count ? Actors[id].Name : "The house";

        Color ColorOf(int id) => id >= 0 && id < Actors.Count ? Actors[id].Color : new Color(0.75f, 0.6f, 1f);

        // ------------------------------------------------------------- events
        public void Ignited(int owner)
        {
            if (owner >= 0) Actors[owner].FiresStarted++;
        }

        public void Extinguished(int owner, float amount)
        {
            if (owner >= 0) Actors[owner].Extinguished += amount;
        }

        public void OilSplashed(int owner)
        {
            if (owner < 0) return;
            Actors[owner].OilSplashes++;
            Log($"{NameOf(owner)} hosed burning oil - it splashes!", owner, "splash" + owner);
        }

        public void Exploded(int owner)
        {
            if (owner >= 0)
            {
                Actors[owner].Explosions++;
                Log($"BOOM! {NameOf(owner)}'s fire blew up a gas canister", owner, "boom" + Time.time);
            }
            else Log("BOOM! A gas canister exploded", owner, "boom" + Time.time);
        }

        public void Shocked(int victim, int culprit)
        {
            if (culprit < 0)
                Log($"{NameOf(victim)} touched a live wire", victim, "shock" + victim);
            else if (culprit == victim)
            {
                Actors[victim].ShockedSelf++;
                Log($"{NameOf(victim)} got shocked in their OWN puddle", victim, "self" + victim);
            }
            else
            {
                Actors[culprit].ShockedOthers++;
                Log($"{NameOf(culprit)}'s water electrocuted {NameOf(victim)}!", culprit, "shock" + culprit + "_" + victim);
            }
        }

        public void Scorched(int victim, int owner)
        {
            string who = owner < 0 ? "the fire" : owner == victim ? "their own fire" : NameOf(owner) + "'s fire";
            Log($"{NameOf(victim)} got toasted by {who}", owner, "scorch" + victim);
        }

        public void Power(int actor, bool on)
        {
            Log(on ? $"{NameOf(actor)} turned the power back ON (why?!)" : $"{NameOf(actor)} cut the power", actor, "power" + Time.time);
        }

        public void Info(string text) => Log(text, -1, text);

        // ------------------------------------------------------------- feed
        void Log(string text, int colorId, string key)
        {
            float now = Time.time;
            for (int i = 0; i < Feed.Count; i++)
            {
                if (Feed[i].Key == key && now - Feed[i].Time < 3f)
                {
                    var e = Feed[i];
                    e.Count++;
                    e.Time = now;
                    Feed.RemoveAt(i);
                    Feed.Insert(0, e);
                    return;
                }
            }
            Feed.Insert(0, new Entry { Text = text, Color = ColorOf(colorId), Time = now, Key = key, Count = 1 });
            if (Feed.Count > FeedSize) Feed.RemoveAt(Feed.Count - 1);
        }

        public Stats MostToBlame()
        {
            Stats worst = null;
            foreach (var a in Actors)
                if (worst == null || a.BlameScore > worst.BlameScore) worst = a;
            return worst;
        }
    }
}
