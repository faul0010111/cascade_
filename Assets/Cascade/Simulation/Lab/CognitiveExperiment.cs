using System.Collections.Generic;
using System.Text;
using Cascade.Agents;
using Cascade.Simulation.Headless;
using UnityEngine;

namespace Cascade.Simulation
{
    public sealed class GroupMetrics
    {
        public string Group;
        public int Agents, Survived;
        public int GoalChanges, LearningEvents, TrustChanges, StrategyShifts, InformationSeeking, FollowChoices, HelpChoices;
        public float TrustDelta;

        public string Row()
        {
            float n = Mathf.Max(1, Agents);
            return Group.PadRight(12) + (Survived * 100f / n).ToString("0").PadLeft(6) + "%" +
                   (GoalChanges / n).ToString("0.0").PadLeft(9) + (LearningEvents / n).ToString("0.0").PadLeft(9) +
                   (TrustChanges / n).ToString("0.0").PadLeft(8) + (StrategyShifts / n).ToString("0.0").PadLeft(9) +
                   (InformationSeeking / n).ToString("0.0").PadLeft(8) + (FollowChoices / n).ToString("0.0").PadLeft(8) + (HelpChoices / n).ToString("0.0").PadLeft(7);
        }

        public const string Header = "group        survive  goalChg  learnEv  trustEv  stratShf  infoSk  follow   help";
    }

    /// <summary>
    /// Runs the same scenario over many seeds (optionally several episodes with carried-over minds) and aggregates
    /// cognitive metrics per archetype, so personalities and mechanisms can be compared statistically.
    /// </summary>
    public static class CognitiveExperiment
    {
        public static Dictionary<string, GroupMetrics> Run(ScenarioConfig config, int seeds, float seconds, int episodes = 1,
                                                          System.Func<ScenarioConfig, HeadlessSimulation> factory = null)
        {
            var groups = new Dictionary<string, GroupMetrics>();
            for (int s = 0; s < seeds; s++)
            {
                MindSnapshot carried = null;
                for (int ep = 0; ep < episodes; ep++)
                {
                    var cfg = config.Clone();
                    cfg.Seed = config.Seed + s * 7919 + ep * 104729;
                    var sim = factory != null ? factory(cfg) : new HeadlessSimulation(cfg);
                    if (carried != null) carried.Apply(sim.Brains);
                    sim.Run(seconds);
                    if (ep == episodes - 1) Collect(sim, groups);
                    carried = MindSnapshot.Capture(sim.Brains);
                }
            }
            return groups;
        }

        private static void Collect(HeadlessSimulation sim, Dictionary<string, GroupMetrics> groups)
        {
            foreach (var b in sim.Brains)
            {
                GroupMetrics g;
                if (!groups.TryGetValue(b.Profile.Archetype, out g)) { g = new GroupMetrics { Group = b.Profile.Archetype }; groups[g.Group] = g; }
                g.Agents++;
                if (b.AtSafety) g.Survived++;
                g.GoalChanges += b.Log.TotalGoalChanges;
                g.LearningEvents += b.Learning.TotalEvents;
                foreach (var e in b.Learning.Events)
                {
                    if (e.Kind == LearningEventKind.Trust) { g.TrustChanges++; g.TrustDelta += e.Magnitude; }
                    if (e.Kind == LearningEventKind.Strategy && Mathf.Abs(e.Magnitude) > 0.15f) g.StrategyShifts++;
                }
                foreach (var t in b.Log.GoalChanges)
                {
                    if (t.Chosen == null) continue;
                    var type = t.Chosen.Option.Type;
                    if (type == GoalType.Verify || type == GoalType.AskForInfo || type == GoalType.Investigate) g.InformationSeeking++;
                    if (type == GoalType.Follow) g.FollowChoices++;
                    if (type == GoalType.HelpOther) g.HelpChoices++;
                }
            }
        }

        public static string Report(Dictionary<string, GroupMetrics> groups)
        {
            var sb = new StringBuilder(GroupMetrics.Header).Append('\n');
            var keys = new List<string>(groups.Keys);
            keys.Sort();
            foreach (var k in keys) sb.Append(groups[k].Row()).Append('\n');
            return sb.ToString();
        }
    }
}
