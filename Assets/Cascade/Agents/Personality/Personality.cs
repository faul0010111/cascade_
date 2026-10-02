using System;
using System.Text;
using Cascade.Core;
using UnityEngine;

namespace Cascade.Agents
{
    public enum Trait { Courage, Fearfulness, Leadership, Empathy, Curiosity, Aggression, Trust, RiskTolerance, Patience, Independence }

    /// <summary>
    /// Static traits in [0, 1]. Personality never selects behavior directly; it acts through consideration weights,
    /// internal-state dynamics and thresholds (ARCHITECTURE.md 7.4).
    /// </summary>
    public sealed class Personality
    {
        public const int Count = 10;
        private readonly float[] _values = new float[Count];

        public Personality()
        {
            for (int i = 0; i < Count; i++) _values[i] = 0.5f;
        }

        public float this[Trait trait]
        {
            get { return _values[(int)trait]; }
            set { _values[(int)trait] = Mathf.Clamp01(value); }
        }

        public Personality Clone()
        {
            var p = new Personality();
            Array.Copy(_values, p._values, Count);
            return p;
        }

        public static Personality Sample(PersonalityArchetype archetype, Rng rng)
        {
            var p = new Personality();
            for (int i = 0; i < Count; i++)
                p._values[i] = Mathf.Clamp01(rng.Gaussian(archetype.Mean[i], archetype.Spread[i]));
            return p;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append((Trait)i).Append(' ').Append(Mathf.RoundToInt(_values[i] * 100));
            }
            return sb.ToString();
        }
    }

    [Serializable]
    public sealed class PersonalityArchetype
    {
        public string Name;
        public float[] Mean = new float[Personality.Count];
        public float[] Spread = new float[Personality.Count];
        [Tooltip("Probability that an agent of this archetype is an employee who knows every exit.")]
        public float EmployeeChance = 0.6f;

        public static PersonalityArchetype Create(string name, float courage, float fearfulness, float leadership, float empathy,
            float curiosity, float aggression, float trust, float risk, float patience, float independence,
            float spread = 0.1f, float employeeChance = 0.6f)
        {
            var a = new PersonalityArchetype { Name = name, EmployeeChance = employeeChance };
            var m = new[] { courage, fearfulness, leadership, empathy, curiosity, aggression, trust, risk, patience, independence };
            for (int i = 0; i < Personality.Count; i++) { a.Mean[i] = m[i]; a.Spread[i] = spread; }
            return a;
        }
    }

    public static class Archetypes
    {
        //                                                     Cour  Fear  Lead  Emp   Cur   Aggr  Trust Risk  Pat   Indep
        public static readonly PersonalityArchetype Protector = PersonalityArchetype.Create("Protector", .80f, .25f, .50f, .90f, .40f, .20f, .60f, .65f, .60f, .50f);
        public static readonly PersonalityArchetype Anxious   = PersonalityArchetype.Create("Anxious",   .20f, .85f, .15f, .50f, .30f, .15f, .45f, .15f, .30f, .30f, employeeChance: 0.4f);
        public static readonly PersonalityArchetype Organizer = PersonalityArchetype.Create("Organizer", .65f, .35f, .90f, .60f, .40f, .30f, .50f, .50f, .55f, .70f, employeeChance: 0.9f);
        public static readonly PersonalityArchetype Loner     = PersonalityArchetype.Create("Loner",     .50f, .40f, .20f, .20f, .35f, .40f, .15f, .55f, .50f, .95f);
        public static readonly PersonalityArchetype Curious   = PersonalityArchetype.Create("Curious",   .60f, .30f, .35f, .45f, .90f, .20f, .55f, .70f, .40f, .60f);
        public static readonly PersonalityArchetype Average   = PersonalityArchetype.Create("Average",   .50f, .50f, .50f, .50f, .50f, .50f, .50f, .50f, .50f, .50f, spread: 0.15f, employeeChance: 0.5f);

        public static readonly PersonalityArchetype[] All = { Protector, Anxious, Organizer, Loner, Curious, Average };

        public static PersonalityArchetype Find(string name)
        {
            foreach (var a in All)
                if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
            return Average;
        }
    }
}
