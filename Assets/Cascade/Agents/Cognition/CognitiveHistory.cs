using System.Collections.Generic;

namespace Cascade.Agents
{
    public struct CognitiveSample
    {
        public float Time;
        public EmotionState Emotion;
        public float Fear, Stress, Confidence;
        public float KnowledgeConfidence, DecisionConfidence, PlanConfidence, SocialConfidence;
        public float Uncertainty;
        public float PredictionError;
        public GoalType Goal;
        public float TopTrust;
    }

    public struct TimelineMark
    {
        public float Time;
        public string Text;
    }

    /// <summary>
    /// Bounded time series of the agent's cognitive state (sampled at 1 Hz) and discrete marks (goal changes,
    /// learning events, revisions). Feeds the inspector timelines and "why was it doing that at time t".
    /// </summary>
    public sealed class CognitiveHistory
    {
        public int SampleCapacity = 300;
        public int MarkCapacity = 120;
        public float SampleInterval = 1f;
        private readonly List<CognitiveSample> _samples = new List<CognitiveSample>();
        private readonly List<TimelineMark> _marks = new List<TimelineMark>();
        private float _next;

        public IReadOnlyList<CognitiveSample> Samples => _samples;
        public IReadOnlyList<TimelineMark> Marks => _marks;

        public bool Due(float now) => now >= _next;

        public void Add(CognitiveSample s)
        {
            _next = s.Time + SampleInterval;
            _samples.Add(s);
            if (_samples.Count > SampleCapacity) _samples.RemoveAt(0);
        }

        public void Mark(float time, string text)
        {
            _marks.Add(new TimelineMark { Time = time, Text = text });
            if (_marks.Count > MarkCapacity) _marks.RemoveAt(0);
        }

        public bool TrySampleAt(float time, out CognitiveSample sample)
        {
            sample = default(CognitiveSample);
            if (_samples.Count == 0) return false;
            int best = 0;
            for (int i = 1; i < _samples.Count; i++)
                if (System.Math.Abs(_samples[i].Time - time) < System.Math.Abs(_samples[best].Time - time)) best = i;
            sample = _samples[best];
            return true;
        }

        public void Clear()
        {
            _samples.Clear();
            _marks.Clear();
            _next = 0f;
        }
    }
}
