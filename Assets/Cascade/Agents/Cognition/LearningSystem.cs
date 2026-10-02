using System.Collections.Generic;
using Cascade.Core;
using UnityEngine;

namespace Cascade.Agents
{
    public enum LearningEventKind { Strategy, Surprise, Belief, Trust, Disposition }

    public struct LearningEvent
    {
        public float Time;
        public LearningEventKind Kind;
        public float Magnitude;
        public string Text;
    }

    /// <summary>
    /// Slow, bounded shifts produced by experience on top of stable personality traits. Traits never change;
    /// these offsets do, by an amount that depends on the traits (the same failure shakes a fearful person more).
    /// </summary>
    public sealed class LearnedDispositions
    {
        public const float Limit = 0.3f;
        public float TrustBias;          // toward strangers and hearsay
        public float RiskPerception;     // how dangerous the world feels
        public float SelfConfidence;     // trust in own judgement
        public float SocialExpectation;  // expectation that others will help / lead well
        public float InformationSeeking; // learned drive to check before acting

        public void Shift(ref float field, float delta) { field = Mathf.Clamp(field + delta, -Limit, Limit); }

        public void CopyFrom(LearnedDispositions o)
        {
            TrustBias = o.TrustBias; RiskPerception = o.RiskPerception; SelfConfidence = o.SelfConfidence;
            SocialExpectation = o.SocialExpectation; InformationSeeking = o.InformationSeeking;
        }

        public override string ToString()
            => "trust " + TrustBias.ToString("+0.00;-0.00") + "  risk " + RiskPerception.ToString("+0.00;-0.00") + "  self " + SelfConfidence.ToString("+0.00;-0.00") +
               "  social " + SocialExpectation.ToString("+0.00;-0.00") + "  info " + InformationSeeking.ToString("+0.00;-0.00");
    }

    /// <summary>
    /// Single entry point for learning. Every outcome goes through Outcome(): it closes the open prediction,
    /// computes the prediction error, updates the strategy model with a personality- and source-weighted rate,
    /// shifts dispositions, records an Experience and a LearningEvent, and appraises surprise.
    /// Deterministic: no randomness, only event-driven updates.
    /// </summary>
    public sealed class LearningSystem
    {
        private readonly AgentBrain _b;
        private readonly Dictionary<StrategyKey, Prediction> _open = new Dictionary<StrategyKey, Prediction>();
        private readonly List<LearningEvent> _events = new List<LearningEvent>();

        public readonly StrategyModel Strategies;
        public readonly ExperienceLog Experiences = new ExperienceLog();
        public readonly LearnedDispositions Dispositions = new LearnedDispositions();
        public IReadOnlyList<LearningEvent> Events => _events;
        public int TotalEvents { get; private set; }
        public float LastPredictionError { get; private set; }
        public float MeanAbsPredictionError { get; private set; }

        public const float SurpriseThreshold = 0.5f;

        public LearningSystem(AgentBrain brain)
        {
            _b = brain;
            Strategies = new StrategyModel(Prior);
        }

        /// <summary>Default expectations before any experience. Personality shapes them (the anxious expect less of themselves).</summary>
        public float Prior(StrategyKey key)
        {
            var p = _b.Personality;
            switch (key.Kind)
            {
                case StrategyKind.ExitRoute: return 0.75f;
                case StrategyKind.FollowPerson:
                    {
                        var r = _b.Relationships.Peek(new EntityId(key.Param));
                        float trust = r != null ? r.Trust : 0.4f;
                        return Mathf.Clamp01(0.35f + 0.4f * trust + 0.3f * Dispositions.SocialExpectation);
                    }
                case StrategyKind.AskPerson:
                    {
                        var r = _b.Relationships.Peek(new EntityId(key.Param));
                        return Mathf.Clamp01(0.4f + 0.3f * (r != null ? r.Reliability : 0.5f) + 0.2f * Dispositions.SocialExpectation);
                    }
                case StrategyKind.ExploreAlone: return Mathf.Clamp01(0.35f + 0.3f * p[Trait.Independence] + 0.3f * Dispositions.SelfConfidence);
                case StrategyKind.FightFire: return Mathf.Clamp01(0.3f + 0.4f * p[Trait.Courage] + 0.3f * Dispositions.SelfConfidence);
                case StrategyKind.HelpPerson: return Mathf.Clamp01(0.5f + 0.3f * p[Trait.Courage] + 0.2f * Dispositions.SelfConfidence);
                default: return 0.5f;
            }
        }

        public Prediction Predict(StrategyKey key) => Strategies.Predict(key, Contexts.Of(_b, _b.Now), _b.Now);

        /// <summary>Forms and remembers an expectation before trying a strategy.</summary>
        public Prediction Expect(StrategyKey key)
        {
            var p = Predict(key);
            _open[key] = p;
            return p;
        }

        public void CancelExpectation(StrategyKey key) { _open.Remove(key); }

        public bool HasOpen(StrategyKey key) => _open.ContainsKey(key);

        public float SourceWeight(ExperienceSource s) => s == ExperienceSource.Direct ? 1f : s == ExperienceSource.Observed ? 0.5f : 0.3f;

        /// <summary>Negative outcomes weigh more for the fearful and less for the brave; positive ones more for the confident.</summary>
        public float Sensitivity(float outcome)
        {
            var p = _b.Personality;
            if (outcome < 0.5f) return (0.6f + 0.8f * p[Trait.Fearfulness]) * (1.2f - 0.4f * p[Trait.Courage]);
            return 0.7f + 0.4f * _b.State[StateVar.Confidence] + 0.2f * p[Trait.Courage];
        }

        public Experience Outcome(StrategyKey key, float actual, ExperienceSource source, string note,
                                  EntityId social = default(EntityId), EntityId actor = default(EntityId), float reward = float.NaN)
        {
            float now = _b.Now;
            var cfg = _b.Cognition;
            if (!cfg.Learning || (source != ExperienceSource.Direct && !cfg.SocialLearning)) { _open.Remove(key); return null; }
            Prediction p;
            if (!_open.TryGetValue(key, out p)) p = Predict(key);
            _open.Remove(key);

            actual = Mathf.Clamp01(actual);
            float error = actual - p.Expected;
            float surprise = Mathf.Abs(error);
            // Surprise speeds learning (the core of prediction-error learning), bounded to keep it stable.
            float weight = SourceWeight(source) * Sensitivity(actual) * (0.6f + 0.8f * surprise);
            Strategies.Update(key, p.Context, actual, weight, now);

            var e = new Experience
            {
                Time = now, Source = source, Actor = actor.IsValid ? actor : _b.Id, Strategy = key, Context = p.Context,
                Place = _b.CurrentRoom, Social = social, Expected = p.Expected, Actual = actual, Confidence = p.Confidence,
                Weight = weight, Emotion = _b.Emotion.Current, Note = note,
                Reward = float.IsNaN(reward) ? actual * 2f - 1f : reward
            };
            Experiences.Add(e);
            LastPredictionError = error;
            MeanAbsPredictionError = Mathf.Lerp(MeanAbsPredictionError, surprise, 0.2f);

            if (source == ExperienceSource.Direct) ShiftDispositions(key, actual, surprise);
            if (surprise >= SurpriseThreshold && source == ExperienceSource.Direct)
            {
                _b.Appraise(error < 0 ? AppraisalEvent.PlanFailed : AppraisalEvent.HelpedSomeone, surprise);
                Event(LearningEventKind.Surprise, surprise, "Surprised: " + e.Describe(_b));
            }
            var after = Predict(key);
            Event(LearningEventKind.Strategy, after.Expected - p.Expected,
                  key.Describe(_b) + " " + Mathf.RoundToInt(p.Expected * 100) + "% -> " + Mathf.RoundToInt(after.Expected * 100) + "% (" + (source == ExperienceSource.Direct ? "" : source.ToString().ToLowerInvariant() + ", ") + Contexts.Name(p.Context) + ")");
            return e;
        }

        private void ShiftDispositions(StrategyKey key, float actual, float surprise)
        {
            var p = _b.Personality;
            var d = Dispositions;
            bool failed = actual < 0.5f;
            float before = d.RiskPerception + d.SelfConfidence + d.SocialExpectation + d.InformationSeeking;
            float scale = 0.02f + 0.04f * surprise;
            if (failed)
            {
                d.Shift(ref d.RiskPerception, scale * (0.4f + 1.2f * p[Trait.Fearfulness]) * (1.2f - 0.6f * p[Trait.Courage]));
                if (key.Kind == StrategyKind.FollowPerson || key.Kind == StrategyKind.AskPerson)
                    d.Shift(ref d.SocialExpectation, -scale * (1.5f - p[Trait.Trust]));
                else d.Shift(ref d.SelfConfidence, -scale * (1.3f - p[Trait.Courage]));
                // The curious respond to failure by wanting to know more rather than by fearing more.
                d.Shift(ref d.InformationSeeking, scale * (0.3f + 1.2f * p[Trait.Curiosity]));
            }
            else
            {
                if (key.Kind == StrategyKind.FollowPerson || key.Kind == StrategyKind.AskPerson) d.Shift(ref d.SocialExpectation, scale * (0.5f + p[Trait.Trust]));
                else d.Shift(ref d.SelfConfidence, scale * (0.6f + 0.6f * p[Trait.Courage]));
                d.Shift(ref d.RiskPerception, -scale * 0.3f);
            }
            float after = d.RiskPerception + d.SelfConfidence + d.SocialExpectation + d.InformationSeeking;
            if (Mathf.Abs(after - before) > 0.02f) Event(LearningEventKind.Disposition, after - before, "Dispositions: " + d);
        }

        /// <summary>Evidence that the world changed re-opens a lesson (e.g. an exit door seen open again).</summary>
        public void Reconsider(StrategyKey key, float keepFraction, string why)
        {
            if (Strategies.Get(key, Contexts.General) == null) return;
            float before = Predict(key).Expected;
            Strategies.Soften(key, keepFraction, _b.Now);
            float after = Predict(key).Expected;
            if (Mathf.Abs(after - before) > 0.01f)
                Event(LearningEventKind.Strategy, after - before, "Reconsidering " + key.Describe(_b) + " (" + why + "): " + Mathf.RoundToInt(before * 100) + "% -> " + Mathf.RoundToInt(after * 100) + "%");
        }

        public void Event(LearningEventKind kind, float magnitude, string text)
        {
            TotalEvents++;
            _events.Add(new LearningEvent { Time = _b.Now, Kind = kind, Magnitude = magnitude, Text = text });
            if (_events.Count > 40) _events.RemoveAt(0);
            _b.History.Mark(_b.Now, text);
        }

        public void Reset()
        {
            _open.Clear();
            _events.Clear();
            Strategies.Clear();
            Dispositions.CopyFrom(new LearnedDispositions());
            MeanAbsPredictionError = 0f;
        }

        public void CopyFrom(LearningSystem other)
        {
            Strategies.CopyFrom(other.Strategies);
            Dispositions.CopyFrom(other.Dispositions);
        }
    }
}
