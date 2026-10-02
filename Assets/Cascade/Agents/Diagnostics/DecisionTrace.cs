using System.Collections.Generic;
using System.Text;

namespace Cascade.Agents
{
    public struct ConsiderationTrace
    {
        public string Name;
        public float Input;
        public float Score;
    }

    public sealed class OptionTrace
    {
        public GoalOption Option;
        public string Label;
        public float FinalScore;
        public float Combined;
        public float PersonalityMultiplier;
        public float OptionMultiplier = 1f;
        public float NeedUrgency;
        public float LongTermBias;
        public float InertiaBonus;
        public float FailurePenalty;
        public readonly List<ConsiderationTrace> Considerations = new List<ConsiderationTrace>();
        public readonly List<Influence> Influences = new List<Influence>();
    }

    /// <summary>Everything needed to explain one decision (principle P4).</summary>
    public sealed class DecisionTrace
    {
        public float Time;
        public EmotionState Emotion;
        public string Trigger;
        public readonly List<OptionTrace> Options = new List<OptionTrace>();
        public int ChosenIndex = -1;
        public string ChosenReason;
        public bool GoalChanged;
        /// <summary>How clear-cut the choice was: separation from the runner-up and absolute strength (0..1).</summary>
        public float DecisionConfidence;
        public string Reason;               // human explanation of the chosen option
        public float KnowledgeConfidence, PlanConfidence, SocialConfidence, Uncertainty;
        public float PredictedSuccess = -1f; // expected success of the strategy behind the chosen option
        public string PredictedStrategy;
        public float LastPredictionError;
        public string Dispositions;

        public OptionTrace Chosen => ChosenIndex >= 0 && ChosenIndex < Options.Count ? Options[ChosenIndex] : null;

        public string ToJson(string agentName)
        {
            var sb = new StringBuilder();
            sb.Append("{\"agent\":\"").Append(Escape(agentName)).Append("\",\"t\":").Append(Time.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"emotion\":\"").Append(Emotion).Append("\",\"trigger\":\"").Append(Escape(Trigger)).Append("\",\"chosen\":\"")
              .Append(Chosen != null ? Escape(Chosen.Label) : "").Append("\",\"reason\":\"").Append(Escape(Reason ?? ChosenReason))
              .Append("\",\"decisionConfidence\":").Append(F(DecisionConfidence)).Append(",\"uncertainty\":").Append(F(Uncertainty))
              .Append(",\"predicted\":").Append(F(PredictedSuccess)).Append(",\"predictionError\":").Append(F(LastPredictionError))
              .Append(",\"dispositions\":\"").Append(Escape(Dispositions)).Append("\",\"options\":[");
            for (int i = 0; i < Options.Count; i++)
            {
                var o = Options[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"goal\":\"").Append(Escape(o.Label)).Append("\",\"score\":").Append(F(o.FinalScore))
                  .Append(",\"personality\":").Append(F(o.PersonalityMultiplier)).Append(",\"need\":").Append(F(o.NeedUrgency))
                  .Append(",\"longTerm\":").Append(F(o.LongTermBias)).Append(",\"inertia\":").Append(F(o.InertiaBonus)).Append(",\"influences\":{");
                for (int k = 0; k < o.Influences.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append('"').Append(Escape(o.Influences[k].Name)).Append("\":").Append(F(o.Influences[k].Contribution));
                }
                sb.Append("},\"considerations\":{");
                for (int k = 0; k < o.Considerations.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append('"').Append(Escape(o.Considerations[k].Name)).Append("\":").Append(F(o.Considerations[k].Score));
                }
                sb.Append("}}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        private static string Escape(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>Ring buffers of decision traces and human-readable events for the debugger.</summary>
    public sealed class DecisionLog
    {
        public int TraceCapacity = 32;
        public int EventCapacity = 40;
        private readonly List<DecisionTrace> _traces = new List<DecisionTrace>();
        private readonly List<string> _events = new List<string>();
        private readonly List<DecisionTrace> _goalChanges = new List<DecisionTrace>();
        public int GoalChangeCapacity = 48;

        /// <summary>Decisions that changed the goal, kept longer than the rolling buffer (for "why at time t").</summary>
        public IReadOnlyList<DecisionTrace> GoalChanges => _goalChanges;
        public int TotalGoalChanges { get; private set; }

        public IReadOnlyList<DecisionTrace> Traces => _traces;
        public IReadOnlyList<string> Events => _events;
        public DecisionTrace Latest => _traces.Count > 0 ? _traces[_traces.Count - 1] : null;
        public int TotalDecisions { get; private set; }

        public void Add(DecisionTrace t)
        {
            TotalDecisions++;
            _traces.Add(t);
            if (_traces.Count > TraceCapacity) _traces.RemoveAt(0);
            if (t.GoalChanged)
            {
                TotalGoalChanges++;
                _goalChanges.Add(t);
                if (_goalChanges.Count > GoalChangeCapacity) _goalChanges.RemoveAt(0);
            }
        }

        public void Event(float time, string text)
        {
            _events.Add(time.ToString("0.0") + "s  " + text);
            if (_events.Count > EventCapacity) _events.RemoveAt(0);
        }
    }
}
