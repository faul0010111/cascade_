using System.Collections.Generic;
using System.Text;
using Cascade.Core;
using Cascade.World;
using UnityEngine;

namespace Cascade.Agents
{
    public struct ReportRow
    {
        public string Label;
        public float Bar;      // 0..1, or -1 for text-only rows
        public string Text;

        public static ReportRow Value(string label, float v, string text = null) => new ReportRow { Label = label, Bar = Mathf.Clamp01(v), Text = text ?? Mathf.RoundToInt(v * 100) + "%" };
        public static ReportRow Info(string label, string text) => new ReportRow { Label = label, Bar = -1f, Text = text };
    }

    public sealed class ReportSection
    {
        public string Title;
        public readonly List<ReportRow> Rows = new List<ReportRow>();
    }

    /// <summary>
    /// Engine-independent content of the NPC Debugger. The in-game overlay and the Editor window only draw this,
    /// so what the debugger shows is testable and identical everywhere.
    /// </summary>
    public static class BrainReport
    {
        public static ReportSection Overview(AgentBrain b)
        {
            var s = new ReportSection { Title = b.Name + "  (" + b.Profile.Archetype + ")" };
            s.Rows.Add(ReportRow.Info("Status", b.Incapacitated ? "INCAPACITATED" : b.IsInjured ? "Injured" : b.AtSafety ? "Safe" : "OK"));
            s.Rows.Add(ReportRow.Info("Emotion", b.Emotion.Current.ToString()));
            s.Rows.Add(ReportRow.Info("Goal", b.Goals.HasGoal ? b.Goals.Current.Label(b) + " (" + Mathf.RoundToInt(Mathf.Min(1f, b.Goals.CurrentScore) * 100) + "%)" : "-"));
            s.Rows.Add(ReportRow.Info("Doing", b.Executor.Describe()));
            s.Rows.Add(ReportRow.Info("Plan", PlanText(b)));
            s.Rows.Add(ReportRow.Info("Room", b.CurrentRoom >= 0 ? b.FloorPlan.RoomName(b.CurrentRoom) : "?"));
            s.Rows.Add(ReportRow.Info("LOD", b.Lod.ToString()));
            if (b.LongTermTarget.IsValid) s.Rows.Add(ReportRow.Info("Long-term", "Find " + b.NameOf(b.LongTermTarget)));
            var leader = b.Groups.LeaderOf(b.Id);
            if (leader.IsValid) s.Rows.Add(ReportRow.Info("Following", b.NameOf(leader)));
            int followers = b.Groups.FollowerCount(b.Id);
            if (followers > 0) s.Rows.Add(ReportRow.Info("Leading", followers + " people"));
            if (!string.IsNullOrEmpty(b.Profile.Background)) s.Rows.Add(ReportRow.Info("Background", b.Profile.Background));
            return s;
        }

        public static string PlanText(AgentBrain b)
        {
            var plan = b.Executor.Plan;
            if (plan.Count == 0) return "-";
            var sb = new StringBuilder();
            for (int i = 0; i < plan.Count; i++)
            {
                if (i > 0) sb.Append(" > ");
                if (i == b.Executor.StepIndex) sb.Append('[').Append(plan[i].Name).Append(']');
                else sb.Append(plan[i].Name);
            }
            if (!b.Executor.PlanIsComplete) sb.Append(" (partial)");
            return sb.ToString();
        }

        public static ReportSection Personality(AgentBrain b)
        {
            var s = new ReportSection { Title = "Personality" };
            for (int i = 0; i < Agents.Personality.Count; i++) s.Rows.Add(ReportRow.Value(((Trait)i).ToString(), b.Personality[(Trait)i]));
            return s;
        }

        public static ReportSection InternalState(AgentBrain b)
        {
            var s = new ReportSection { Title = "Internal state" };
            for (int i = 0; i < Agents.InternalState.Count; i++) s.Rows.Add(ReportRow.Value(((StateVar)i).ToString(), b.State[(StateVar)i]));
            return s;
        }

        public static ReportSection Needs(AgentBrain b)
        {
            var s = new ReportSection { Title = "Needs" };
            for (int i = 0; i < NeedSystem.Count; i++) s.Rows.Add(ReportRow.Value(((NeedType)i).ToString(), b.Needs[(NeedType)i]));
            return s;
        }

        /// <summary>Latest decision: every option with its final score, sorted.</summary>
        public static ReportSection Decision(AgentBrain b)
        {
            var t = b.Log.Latest;
            var s = new ReportSection { Title = t == null ? "Decision" : "Decision at " + t.Time.ToString("0.0") + "s (" + t.Trigger + ")" };
            if (t == null) return s;
            var sorted = new List<OptionTrace>(t.Options);
            sorted.Sort((x, y) => y.FinalScore.CompareTo(x.FinalScore));
            foreach (var o in sorted)
                s.Rows.Add(ReportRow.Value((o == t.Chosen ? "> " : "  ") + o.Label, Mathf.Min(1f, o.FinalScore), Mathf.RoundToInt(o.FinalScore * 100) + "%"));
            s.Rows.Add(ReportRow.Info("Reason", t.Reason ?? t.ChosenReason));
            s.Rows.Add(ReportRow.Value("Decision confidence", t.DecisionConfidence));
            if (t.PredictedSuccess >= 0f) s.Rows.Add(ReportRow.Value("Expects " + t.PredictedStrategy + " to work", t.PredictedSuccess));
            s.Rows.Add(ReportRow.Value("Uncertainty", t.Uncertainty));
            s.Rows.Add(ReportRow.Info("Selection", t.ChosenReason));
            return s;
        }

        /// <summary>Breakdown of one option: each consideration and each multiplier.</summary>
        public static ReportSection Breakdown(OptionTrace o)
        {
            var s = new ReportSection { Title = o == null ? "Breakdown" : "Why " + o.Label + " = " + Mathf.RoundToInt(o.FinalScore * 100) + "%" };
            if (o == null) return s;
            foreach (var c in o.Considerations)
                s.Rows.Add(ReportRow.Value(c.Name, c.Score, Mathf.RoundToInt(c.Score * 100) + "%  (in " + c.Input.ToString("0.00") + ")"));
            s.Rows.Add(ReportRow.Info("-- influences", "(vs a typical agent in a typical situation)"));
            foreach (var inf in o.Influences)
                s.Rows.Add(ReportRow.Info(inf.Name + " [" + inf.Category + "]", (inf.Contribution >= 0 ? "+" : "") + inf.Contribution.ToString("0.00")));
            var byCat = new Dictionary<InfluenceCategory, float>();
            Attribution.ByCategory(o, byCat);
            var cats = new List<KeyValuePair<InfluenceCategory, float>>(byCat);
            cats.Sort((a, b) => Mathf.Abs(b.Value).CompareTo(Mathf.Abs(a.Value)));
            var sb = new StringBuilder();
            foreach (var kv in cats) sb.Append(kv.Key).Append(' ').Append(kv.Value >= 0 ? "+" : "").Append(kv.Value.ToString("0.00")).Append("  ");
            s.Rows.Add(ReportRow.Info("By category", sb.ToString()));
            s.Rows.Add(ReportRow.Info("Combined", o.Combined.ToString("0.00")));
            s.Rows.Add(ReportRow.Info("x Personality", o.PersonalityMultiplier.ToString("0.00")));
            s.Rows.Add(ReportRow.Info("x Need urgency", o.NeedUrgency.ToString("0.00")));
            s.Rows.Add(ReportRow.Info("x Long-term", o.LongTermBias.ToString("0.00")));
            s.Rows.Add(ReportRow.Info("x Inertia", o.InertiaBonus.ToString("0.00")));
            s.Rows.Add(ReportRow.Info("x Failures", o.FailurePenalty.ToString("0.00")));
            return s;
        }

        public static ReportSection SelfAssessment(AgentBrain b)
        {
            var s = new ReportSection { Title = "Self-assessment" };
            var a = b.Self;
            s.Rows.Add(ReportRow.Value("Knowledge confidence", a.KnowledgeConfidence));
            s.Rows.Add(ReportRow.Value("Plan confidence", a.PlanConfidence));
            s.Rows.Add(ReportRow.Value("Decision confidence", a.DecisionConfidence));
            s.Rows.Add(ReportRow.Value("Social confidence", a.SocialConfidence));
            if (a.MostDoubtedAmount > 0.2f) s.Rows.Add(ReportRow.Value("Most doubted: " + a.MostDoubted, a.MostDoubtedAmount));
            s.Rows.Add(ReportRow.Info("Learned dispositions", b.Learning.Dispositions.ToString()));
            s.Rows.Add(ReportRow.Info("Mean |prediction error|", b.Learning.MeanAbsPredictionError.ToString("0.00")));
            return s;
        }

        /// <summary>What the agent has learned about its strategies, per context.</summary>
        public static ReportSection Learning(AgentBrain b)
        {
            var s = new ReportSection { Title = "Learned strategies (" + b.Learning.Strategies.Count + ")" };
            var list = new List<StrategyEstimate>(b.Learning.Strategies.Entries);
            list.Sort((x, y) => y.LastUpdate.CompareTo(x.LastUpdate));
            foreach (var e in list)
                s.Rows.Add(ReportRow.Value(e.Key.Describe(b) + " [" + Contexts.Name(e.Context) + "]", e.Value,
                    Mathf.RoundToInt(e.Value * 100) + "%  " + e.Successes + "ok/" + e.Failures + "fail  conf " + Mathf.RoundToInt(e.Confidence(b.Now) * 100) + "%"));
            return s;
        }

        public static ReportSection Experiences(AgentBrain b, int count = 12)
        {
            var items = b.Learning.Experiences.Items;
            var s = new ReportSection { Title = "Experiences (" + b.Learning.Experiences.Total + ")" };
            for (int i = items.Count - 1; i >= 0 && items.Count - i <= count; i--)
                s.Rows.Add(ReportRow.Info(items[i].Time.ToString("0.0") + "s", items[i].Describe(b)));
            return s;
        }

        public static ReportSection LearningEvents(AgentBrain b)
        {
            var s = new ReportSection { Title = "Learning events (" + b.Learning.TotalEvents + ")" };
            var ev = b.Learning.Events;
            for (int i = ev.Count - 1; i >= 0; i--) s.Rows.Add(ReportRow.Info(ev[i].Time.ToString("0.0") + "s " + ev[i].Kind, ev[i].Text));
            return s;
        }

        /// <summary>"Why was the agent acting this way at time t?": state sample, decision in force, recent marks.</summary>
        public static ReportSection WhyAt(AgentBrain b, float t)
        {
            var s = new ReportSection { Title = "At t = " + t.ToString("0.0") + "s" };
            CognitiveSample sample;
            if (b.History.TrySampleAt(t, out sample))
            {
                s.Rows.Add(ReportRow.Info("Emotion / goal", sample.Emotion + " / " + sample.Goal));
                s.Rows.Add(ReportRow.Value("Fear", sample.Fear));
                s.Rows.Add(ReportRow.Value("Knowledge confidence", sample.KnowledgeConfidence));
                s.Rows.Add(ReportRow.Value("Decision confidence", sample.DecisionConfidence));
                s.Rows.Add(ReportRow.Info("Last prediction error", sample.PredictionError.ToString("+0.00;-0.00")));
            }
            DecisionTrace inForce = null;
            foreach (var tr in b.Log.GoalChanges) if (tr.Time <= t) inForce = tr;
            if (inForce != null && inForce.Chosen != null)
            {
                s.Rows.Add(ReportRow.Info("Decision in force", inForce.Chosen.Label + " (chosen at " + inForce.Time.ToString("0.0") + "s, " + inForce.Trigger + ")"));
                s.Rows.Add(ReportRow.Info("Because", inForce.Reason));
                int shown = 0;
                foreach (var inf in inForce.Chosen.Influences)
                {
                    if (shown++ >= 5) break;
                    s.Rows.Add(ReportRow.Info("  " + inf.Name, (inf.Contribution >= 0 ? "+" : "") + inf.Contribution.ToString("0.00") + " [" + inf.Category + "]"));
                }
            }
            else s.Rows.Add(ReportRow.Info("Decision in force", "(older than the trace buffer)"));
            foreach (var m in b.History.Marks)
                if (m.Time <= t && m.Time > t - 15f) s.Rows.Add(ReportRow.Info("  " + m.Time.ToString("0.0") + "s", m.Text));
            return s;
        }

        public static ReportSection Memories(AgentBrain b, int count = 10)
        {
            var s = new ReportSection { Title = "Memories (" + b.Memory.Count + ")" };
            var list = new List<EpisodicMemory>();
            b.Memory.TopRelevant(b.Now, count, list);
            foreach (var m in list)
                s.Rows.Add(ReportRow.Value(m.Describe(b.NameOf, b.FloorPlan.RoomName), b.Memory.Relevance(m, b.Now), (b.Now - m.Time).ToString("0") + "s ago"));
            for (int r = 0; r < b.FloorPlan.RoomCount; r++)
            {
                float d = b.Memory.PlaceDanger(r, b.Now);
                if (d > 0.05f) s.Rows.Add(ReportRow.Value("Remembers " + b.FloorPlan.RoomName(r) + " as dangerous", d));
            }
            return s;
        }

        public static ReportSection Relationships(AgentBrain b, int count = 8)
        {
            var s = new ReportSection { Title = "Relationships (" + b.Relationships.Count + ")" };
            var list = new List<Relationship>(b.Relationships.All);
            list.Sort((x, y) => (y.Affection + y.Trust + y.Respect).CompareTo(x.Affection + x.Trust + x.Respect));
            for (int i = 0; i < list.Count && i < count; i++)
                s.Rows.Add(ReportRow.Info(b.NameOf(list[i].Other), list[i].ToString()));
            return s;
        }

        public static ReportSection Beliefs(AgentBrain b)
        {
            var s = new ReportSection { Title = "Beliefs (" + b.Beliefs.Count + ")" };
            float now = b.Now;
            foreach (var belief in b.Beliefs.All)
            {
                string text = Describe(belief, b);
                if (text == null) continue;
                string status = belief.StatusAt(now).ToString().ToLowerInvariant() + (belief.Contradictions > 0 ? ", " + belief.Contradictions + " contradictions" : "") + (belief.Evidence > 1 ? ", " + belief.Evidence + " sources" : "");
                s.Rows.Add(ReportRow.Value(text, belief.EffectiveConfidence(now), Mathf.RoundToInt(belief.EffectiveConfidence(now) * 100) + "% " + SourceText(belief.Source, b) + " [" + status + "]"));
            }
            return s;
        }

        public static string SourceText(BeliefSource src, AgentBrain b)
            => src.Kind == BeliefSourceKind.Told ? "told by " + b.NameOf(src.Teller) + (src.Hops > 1 ? " (" + src.Hops + " hops)" : "") : src.Kind.ToString().ToLowerInvariant();

        private static string Describe(Belief belief, AgentBrain b)
        {
            var plan = b.FloorPlan;
            switch (belief.Key.Type)
            {
                case FactType.HazardInRoom: return belief.Value > 0.05f ? "Fire in " + plan.RoomName(belief.Key.Subject) + " (" + belief.Value.ToString("0.0") + ")" : null;
                case FactType.SmokeInRoom: return belief.Value > 0.1f ? "Smoke in " + plan.RoomName(belief.Key.Subject) : null;
                case FactType.DoorState:
                    var st = (DoorState)(int)belief.Value;
                    return st == DoorState.Open ? null : "Door " + belief.Key.Subject + " is " + st.ToString().ToLowerInvariant();
                case FactType.ExitKnown: return "Knows exit door " + belief.Key.Subject;
                case FactType.AgentInjured: return belief.Value > 0.5f ? b.NameOf(new EntityId(belief.Key.Subject)) + " is injured" : null;
                case FactType.AlarmActive: return belief.Value > 0.5f ? "Alarm is ringing" : null;
                case FactType.ExtinguisherAt: return belief.Value > 0.5f ? "Extinguisher in " + plan.RoomName(plan.RoomAt(belief.Position)) : null;
                case FactType.OrderedToFollow: return belief.Value > 0.5f ? b.NameOf(new EntityId(belief.Key.Subject)) + " told me to follow" : null;
                default: return null;
            }
        }

        public static string ToPlainText(AgentBrain b)
        {
            var sb = new StringBuilder();
            foreach (var section in new[] { Overview(b), Personality(b), InternalState(b), Needs(b), SelfAssessment(b), Decision(b), Breakdown(b.Log.Latest != null ? b.Log.Latest.Chosen : null), Learning(b), Experiences(b), LearningEvents(b), Memories(b), Relationships(b), Beliefs(b) })
            {
                sb.Append("== ").Append(section.Title).Append('\n');
                foreach (var r in section.Rows) sb.Append("  ").Append(r.Label).Append(": ").Append(r.Text).Append('\n');
            }
            sb.Append("== Recent events\n");
            foreach (var e in b.Log.Events) sb.Append("  ").Append(e).Append('\n');
            return sb.ToString();
        }
    }
}
