using System;
using System.Collections.Generic;
using Cascade.Agents;
using Cascade.Core;
using Cascade.World;

namespace Cascade.Simulation
{
    [Serializable]
    public sealed class ArchetypeWeight
    {
        public string Archetype = "Average";
        public float Weight = 1f;
    }

    /// <summary>Builds agent profiles and applies initial knowledge (priors) and relationships.</summary>
    public static class AgentFactory
    {
        public static readonly string[] Names =
        {
            "Ana", "Bruno", "Carla", "Diego", "Elisa", "Felipe", "Gabriela", "Hugo", "Isabel", "João", "Karina", "Lucas",
            "Marina", "Nicolas", "Olivia", "Pedro", "Quésia", "Rafael", "Sofia", "Tiago", "Úrsula", "Vitor", "Wanda", "Yuri",
            "Zara", "Alice", "Bento", "Clara", "Davi", "Eva", "Fábio", "Giovana", "Heitor", "Iris", "Joana", "Kauã"
        };

        public static PersonalityArchetype PickArchetype(IList<ArchetypeWeight> weights, Rng rng)
        {
            if (weights == null || weights.Count == 0) return Archetypes.All[rng.Range(0, Archetypes.All.Length)];
            float total = 0f;
            foreach (var w in weights) total += Math.Max(0f, w.Weight);
            float pick = rng.Value() * total;
            foreach (var w in weights)
            {
                pick -= Math.Max(0f, w.Weight);
                if (pick <= 0f) return Archetypes.Find(w.Archetype);
            }
            return Archetypes.Average;
        }

        public static AgentProfile CreateProfile(int index, PersonalityArchetype archetype, Rng rng)
        {
            string name = Names[index % Names.Length] + (index >= Names.Length ? " " + (index / Names.Length + 1) : "");
            return new AgentProfile
            {
                Name = name,
                Archetype = archetype.Name,
                Personality = Personality.Sample(archetype, rng),
                KnowsAllExits = rng.Chance(archetype.EmployeeChance),
                Background = archetype.Name + (rng.Chance(archetype.EmployeeChance) ? " employee" : " visitor")
            };
        }

        /// <summary>
        /// Initial knowledge. Everyone knows the main entrance they came in through; employees know every exit and the
        /// usual state of internal doors. Nobody starts knowing where the fire is.
        /// </summary>
        public static void ApplyPriors(AgentBrain brain, IFloorPlan plan, int mainExitDoor)
        {
            for (int d = 0; d < plan.DoorCount; d++)
            {
                if (!plan.IsExitDoor(d)) continue;
                if (d == mainExitDoor || brain.Profile.KnowsAllExits) brain.AddPrior(FactKey.Exit(d), 1f);
            }
        }

        /// <summary>Creates reciprocal initial relationships by name, plus random colleague links and family pairs.</summary>
        public static void ResolveRelationships(IList<AgentBrain> brains, Rng rng, int familyPairs, float colleagueLinkChance)
        {
            var byName = new Dictionary<string, AgentBrain>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in brains) byName[b.Name] = b;

            foreach (var b in brains)
            {
                foreach (var r in b.Profile.Relationships)
                {
                    AgentBrain other;
                    if (r.TargetName == null || !byName.TryGetValue(r.TargetName, out other) || other == b) continue;
                    b.Relationships.SetInitial(other.Id, r.Kind, r.Trust, r.Affection);
                }
                AgentBrain target;
                if (b.Profile.LongTermGoal == LongTermGoalType.FindPerson && b.Profile.LongTermTargetName != null &&
                    byName.TryGetValue(b.Profile.LongTermTargetName, out target) && target != b)
                    b.LongTermTarget = target.Id;
            }

            var pool = new List<AgentBrain>(brains);
            for (int i = 0; i < familyPairs && pool.Count >= 2; i++)
            {
                var a = pool[rng.Range(0, pool.Count)];
                pool.Remove(a);
                var c = pool[rng.Range(0, pool.Count)];
                pool.Remove(c);
                a.Relationships.SetInitial(c.Id, RelationshipKind.Family, 0.9f, 0.9f);
                c.Relationships.SetInitial(a.Id, RelationshipKind.Family, 0.9f, 0.9f);
                a.LongTermTarget = c.Id;
                c.LongTermTarget = a.Id;
            }

            for (int i = 0; i < brains.Count; i++)
            for (int j = i + 1; j < brains.Count; j++)
            {
                var a = brains[i]; var c = brains[j];
                if (!a.Profile.KnowsAllExits || !c.Profile.KnowsAllExits) continue; // colleagues are employees
                if (a.Relationships.Has(c.Id) || !rng.Chance(colleagueLinkChance)) continue;
                a.Relationships.SetInitial(c.Id, RelationshipKind.Colleague, 0.55f + rng.Range(-0.1f, 0.1f), 0.3f);
                c.Relationships.SetInitial(a.Id, RelationshipKind.Colleague, 0.55f + rng.Range(-0.1f, 0.1f), 0.3f);
            }
        }
    }
}
