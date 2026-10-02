using System.Collections.Generic;
using Cascade.Agents;

namespace Cascade.Simulation
{
    /// <summary>
    /// Carries what agents learned from one episode into the next (a "fire drill" series). Keyed by name so it
    /// survives new entity ids. Carries strategies, dispositions, relationships and remembered place danger;
    /// deliberately NOT beliefs about the current state of the world, which belong to one episode.
    /// </summary>
    public sealed class MindSnapshot
    {
        private sealed class Entry
        {
            public LearningSystem Learning;
            public Dictionary<string, Relationship> Relationships = new Dictionary<string, Relationship>();
            public Dictionary<int, float> PlaceDanger = new Dictionary<int, float>();
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        public int Count => _entries.Count;

        public static MindSnapshot Capture(IEnumerable<AgentBrain> brains)
        {
            var s = new MindSnapshot();
            foreach (var b in brains)
            {
                var e = new Entry { Learning = b.Learning };
                foreach (var r in b.Relationships.All) e.Relationships[b.NameOf(r.Other)] = r;
                for (int room = 0; room < b.FloorPlan.RoomCount; room++)
                {
                    float d = b.Memory.PlaceDanger(room, b.Now);
                    if (d > 0.05f) e.PlaceDanger[room] = d;
                }
                s._entries[b.Name] = e;
            }
            return s;
        }

        /// <summary>Apply to a fresh population (same names). Strategy evidence is re-timed to the new clock.</summary>
        public void Apply(IList<AgentBrain> brains)
        {
            var byName = new Dictionary<string, AgentBrain>();
            foreach (var b in brains) byName[b.Name] = b;
            foreach (var b in brains)
            {
                Entry e;
                if (!_entries.TryGetValue(b.Name, out e)) continue;
                b.Learning.CopyFrom(e.Learning);
                b.Learning.Strategies.Retime(b.Now);
                foreach (var kv in e.Relationships)
                {
                    AgentBrain other;
                    if (!byName.TryGetValue(kv.Key, out other)) continue;
                    var r = b.Relationships.Get(other.Id);
                    var src = kv.Value;
                    r.Kind = src.Kind; r.Trust = src.Trust; r.Fear = src.Fear; r.Respect = src.Respect; r.Affection = src.Affection;
                    r.Dependency = src.Dependency; r.Suspicion = src.Suspicion; r.Reliability = src.Reliability; r.ClaimsChecked = src.ClaimsChecked;
                }
                foreach (var kv in e.PlaceDanger)
                    b.Memory.Record(MemoryKind.RouteDangerous, Core.EntityId.None, kv.Key, b.Now, -0.5f, kv.Value);
            }
        }
    }
}
