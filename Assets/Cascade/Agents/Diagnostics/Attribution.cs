using System.Collections.Generic;
using UnityEngine;

namespace Cascade.Agents
{
    public enum InfluenceCategory { Need, Emotion, Personality, Relationship, Memory, Prediction, Uncertainty, Belief, Situation, Commitment }

    public struct Influence
    {
        public string Name;
        public InfluenceCategory Category;
        public float Factor;        // the value that entered the formula
        public float Contribution;  // final score minus final score with this factor at its reference value
    }

    /// <summary>
    /// Explains a utility score as signed contributions. Leave-one-out: each factor is replaced by a reference
    /// value (considerations 0.75, a "typical" score; multipliers 1.0, "no effect") and the score is recomputed.
    /// The contribution is how much this factor moved the score away from a typical agent in a typical situation.
    /// </summary>
    public static class Attribution
    {
        public const float ConsiderationReference = 0.75f;

        private static readonly Dictionary<string, InfluenceCategory> Categories = new Dictionary<string, InfluenceCategory>
        {
            { "Escape need", InfluenceCategory.Need }, { "Information need", InfluenceCategory.Need },
            { "Composure", InfluenceCategory.Emotion }, { "Terror", InfluenceCategory.Emotion }, { "Calm", InfluenceCategory.Emotion },
            { "Bond", InfluenceCategory.Relationship }, { "Trust in leader", InfluenceCategory.Relationship }, { "Source quality", InfluenceCategory.Relationship },
            { "Social pull", InfluenceCategory.Relationship },
            { "Expected success", InfluenceCategory.Prediction }, { "Expected outcome", InfluenceCategory.Prediction },
            { "Expected answer", InfluenceCategory.Prediction }, { "Hiding worked before", InfluenceCategory.Prediction },
            { "Doubt", InfluenceCategory.Uncertainty }, { "Uncertainty", InfluenceCategory.Uncertainty }, { "Knows a way out", InfluenceCategory.Uncertainty },
            { "No way out", InfluenceCategory.Uncertainty },
            { "Risk vs bravery", InfluenceCategory.Belief }, { "Place not dangerous", InfluenceCategory.Belief }, { "Here is not burning", InfluenceCategory.Belief },
            { "Target in need", InfluenceCategory.Belief }, { "Target unaware", InfluenceCategory.Belief }, { "I know the danger", InfluenceCategory.Belief },
            { "Fire still small", InfluenceCategory.Belief }, { "Not known safe", InfluenceCategory.Belief }, { "Asked to follow", InfluenceCategory.Belief },
            { "Unaware or already safe", InfluenceCategory.Belief }, { "Extinguisher", InfluenceCategory.Belief },
            { "People around", InfluenceCategory.Situation }, { "Group still small", InfluenceCategory.Situation },
            { "Distance", InfluenceCategory.Situation }, { "Close", InfluenceCategory.Situation }, { "Leader nearby", InfluenceCategory.Situation },
            { "Not yet safe", InfluenceCategory.Situation }, { "Able", InfluenceCategory.Situation }, { "Able to move", InfluenceCategory.Situation },
            { "Curiosity", InfluenceCategory.Personality },
        };

        public static InfluenceCategory CategoryOf(string considerationName)
        {
            InfluenceCategory c;
            return Categories.TryGetValue(considerationName, out c) ? c : InfluenceCategory.Situation;
        }

        public static void Explain(OptionTrace o, float baseWeight, List<float> scratch)
        {
            o.Influences.Clear();
            float mults = o.PersonalityMultiplier * o.OptionMultiplier * o.NeedUrgency * o.LongTermBias * o.InertiaBonus * o.FailurePenalty;
            for (int i = 0; i < o.Considerations.Count; i++)
            {
                scratch.Clear();
                for (int k = 0; k < o.Considerations.Count; k++) scratch.Add(k == i ? ConsiderationReference : o.Considerations[k].Score);
                float without = baseWeight * AI.Utility.UtilityMath.Combine(scratch) * mults;
                o.Influences.Add(new Influence
                {
                    Name = o.Considerations[i].Name, Category = CategoryOf(o.Considerations[i].Name),
                    Factor = o.Considerations[i].Score, Contribution = o.FinalScore - without
                });
            }
            Multiplier(o, "Personality", InfluenceCategory.Personality, o.PersonalityMultiplier);
            Multiplier(o, "Who is involved", InfluenceCategory.Relationship, o.OptionMultiplier);
            Multiplier(o, "Need urgency", InfluenceCategory.Need, o.NeedUrgency);
            Multiplier(o, "Long-term goal", InfluenceCategory.Memory, o.LongTermBias);
            Multiplier(o, "Commitment (inertia)", InfluenceCategory.Commitment, o.InertiaBonus);
            Multiplier(o, "Past failures / abandoned", InfluenceCategory.Memory, o.FailurePenalty);
            o.Influences.Sort((a, b) => Mathf.Abs(b.Contribution).CompareTo(Mathf.Abs(a.Contribution)));
        }

        private static void Multiplier(OptionTrace o, string name, InfluenceCategory cat, float value)
        {
            if (Mathf.Approximately(value, 1f)) return;
            o.Influences.Add(new Influence { Name = name, Category = cat, Factor = value, Contribution = o.FinalScore - o.FinalScore / Mathf.Max(0.0001f, value) });
        }

        /// <summary>Sums contributions per category (for "Empathy +0.22, Relationship +0.18, Fear -0.08" style summaries).</summary>
        public static void ByCategory(OptionTrace o, Dictionary<InfluenceCategory, float> into)
        {
            into.Clear();
            foreach (var inf in o.Influences)
            {
                float v;
                into.TryGetValue(inf.Category, out v);
                into[inf.Category] = v + inf.Contribution;
            }
        }

        /// <summary>A human sentence for the strongest positive reason, grounded in the agent's own memories where possible.</summary>
        public static string Reason(AgentBrain b, OptionTrace o)
        {
            Influence top = default(Influence);
            bool found = false;
            foreach (var inf in o.Influences)
            {
                if (IsGate(inf.Name)) continue; // preconditions are not reasons
                if (inf.Contribution > 0f && (!found || inf.Contribution > top.Contribution)) { top = inf; found = true; }
            }
            if (!found) return "no strong reason; best of weak options";
            var t = o.Option.Target;
            switch (top.Category)
            {
                case InfluenceCategory.Relationship:
                    if (t.IsValid)
                    {
                        var r = b.Relationships.Peek(t);
                        if (r != null && r.History.Count > 0)
                        {
                            var last = r.History[r.History.Count - 1].Kind;
                            return b.NameOf(t) + " " + SocialPhrase(last) + " (" + top.Name.ToLowerInvariant() + ")";
                        }
                        if (r != null && r.Kind != RelationshipKind.Stranger && r.Kind != RelationshipKind.Acquaintance) return b.NameOf(t) + " is my " + r.Kind.ToString().ToLowerInvariant();
                    }
                    return top.Name + " is high";
                case InfluenceCategory.Prediction:
                    var key = StrategyFor(b, o.Option);
                    if (key.HasValue)
                    {
                        var p = b.Learning.Predict(key.Value);
                        return "I expect " + key.Value.Describe(b) + " to work (" + Mathf.RoundToInt(p.Expected * 100) + "%, from experience)";
                    }
                    return "it worked before";
                case InfluenceCategory.Uncertainty: return "I am not sure of what I know (" + top.Name.ToLowerInvariant() + ")";
                case InfluenceCategory.Need: return "I urgently need it (" + top.Name.ToLowerInvariant() + ")";
                case InfluenceCategory.Personality: return "it is in my nature (" + top.Name.ToLowerInvariant() + " x" + top.Factor.ToString("0.00") + ")";
                case InfluenceCategory.Memory: return top.Name.ToLowerInvariant();
                case InfluenceCategory.Emotion: return "I feel able to (" + top.Name.ToLowerInvariant() + ")";
                case InfluenceCategory.Commitment: return "I was already doing it";
                default: return top.Name.ToLowerInvariant() + " (" + Mathf.RoundToInt(top.Factor * 100) + "%)";
            }
        }

        public static bool IsGate(string name) => name == "Not yet safe" || name == "Able" || name == "Able to move";

        private static string SocialPhrase(SocialEventKind k)
        {
            switch (k)
            {
                case SocialEventKind.HelpedMe: return "helped me before";
                case SocialEventKind.SavedMyLife: return "saved my life";
                case SocialEventKind.LedMeToSafety: return "led me to safety before";
                case SocialEventKind.GaveCorrectInfo: return "told me the truth before";
                case SocialEventKind.HelpedOther: return "I saw them help others";
                case SocialEventKind.WarnedMe: return "warned me";
                case SocialEventKind.LedMeIntoDanger: return "led someone into danger (but still the best option)";
                default: return "is someone I know";
            }
        }

        /// <summary>The learnable strategy an option relies on, if any.</summary>
        public static StrategyKey? StrategyFor(AgentBrain b, GoalOption o)
        {
            switch (o.Type)
            {
                case GoalType.Follow: return StrategyKey.Follow(o.Target);
                case GoalType.HelpOther: return StrategyKey.Help;
                case GoalType.FightFire: return StrategyKey.Firefight;
                case GoalType.Hide: return StrategyKey.HideAway;
                case GoalType.AskForInfo: return StrategyKey.Ask(o.Target);
                case GoalType.Evacuate:
                    int door = b.Routes.PlannedExitDoor(b.Now);
                    return door >= 0 ? StrategyKey.Exit(door) : (StrategyKey?)null;
                default: return null;
            }
        }
    }
}
