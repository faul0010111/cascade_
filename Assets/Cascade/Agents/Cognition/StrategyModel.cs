using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cascade.Agents
{
    public sealed class StrategyEstimate
    {
        public StrategyKey Key;
        public int Context;
        public float Value;          // expected success 0..1
        public float Evidence;       // effective number of observations (decays)
        public float LastUpdate;
        public int Successes, Failures;
        public readonly List<float> Recent = new List<float>(); // last outcomes, for the inspector

        public float EffectiveEvidence(float now) => Evidence * Mathf.Exp(-0.693f * Mathf.Max(0f, now - LastUpdate) / StrategyModel.EvidenceHalfLife);
        public float Confidence(float now) { float n = EffectiveEvidence(now); return n / (n + 2f); }
    }

    /// <summary>
    /// What an agent has learned about its strategies, per context bucket plus a general (context-free) estimate.
    /// Rescorla–Wagner style updates: value += rate × (outcome − value). Evidence decays, so old lessons fade and
    /// strategies can be tried again (unlearning). Bounded: least-recently-updated entries are evicted.
    /// </summary>
    public sealed class StrategyModel
    {
        public const float EvidenceHalfLife = 300f;
        public int Capacity = 96;
        private readonly Dictionary<long, StrategyEstimate> _entries = new Dictionary<long, StrategyEstimate>();
        private readonly Func<StrategyKey, float> _prior;

        public StrategyModel(Func<StrategyKey, float> prior) { _prior = prior; }

        public IEnumerable<StrategyEstimate> Entries => _entries.Values;
        public int Count => _entries.Count;

        private static long Id(StrategyKey k, int context) => ((long)k.GetHashCode() << 8) ^ (context + 1);

        public float Prior(StrategyKey key) => _prior(key);

        public StrategyEstimate Get(StrategyKey key, int context)
        {
            StrategyEstimate e;
            return _entries.TryGetValue(Id(key, context), out e) && e.Key.Equals(key) && e.Context == context ? e : null;
        }

        /// <summary>Prior → general estimate → context estimate, each blended by its confidence.</summary>
        public Prediction Predict(StrategyKey key, int context, float now)
        {
            float p = _prior(key), conf = 0f;
            var general = Get(key, Contexts.General);
            if (general != null) { float c = general.Confidence(now); p = Mathf.Lerp(p, general.Value, c); conf = c; }
            var specific = context >= 0 ? Get(key, context) : null;
            if (specific != null) { float c = specific.Confidence(now); p = Mathf.Lerp(p, specific.Value, c); conf = Mathf.Max(conf, c); }
            return new Prediction { Key = key, Context = context, Expected = Mathf.Clamp01(p), Confidence = conf, Time = now, Valid = true };
        }

        public void Update(StrategyKey key, int context, float outcome, float weight, float now)
        {
            Apply(key, context, outcome, weight, now);
            if (context >= 0) Apply(key, Contexts.General, outcome, weight * 0.7f, now);
            if (_entries.Count > Capacity) EvictOldest();
        }

        private void Apply(StrategyKey key, int context, float outcome, float weight, float now)
        {
            long id = Id(key, context);
            StrategyEstimate e;
            if (!_entries.TryGetValue(id, out e))
            {
                e = new StrategyEstimate { Key = key, Context = context, Value = _prior(key), LastUpdate = now };
                _entries[id] = e;
            }
            float n = e.EffectiveEvidence(now);
            float rate = Mathf.Clamp(weight / (1f + 0.5f * n), 0.05f, 0.8f);
            e.Value = Mathf.Clamp01(e.Value + rate * (outcome - e.Value));
            e.Evidence = n + weight;
            e.LastUpdate = now;
            if (outcome >= 0.5f) e.Successes++; else e.Failures++;
            e.Recent.Add(outcome);
            if (e.Recent.Count > 8) e.Recent.RemoveAt(0);
        }

        /// <summary>New evidence makes old lessons less certain (e.g. a door seen open again): the estimate drifts back toward the prior.</summary>
        public void Soften(StrategyKey key, float keepFraction, float now)
        {
            foreach (var e in _entries.Values)
                if (e.Key.Equals(key))
                {
                    e.Evidence = e.EffectiveEvidence(now) * keepFraction;
                    e.LastUpdate = now;
                    e.Value = Mathf.Lerp(_prior(key), e.Value, keepFraction);
                }
        }

        public void Clear() { _entries.Clear(); }

        /// <summary>Moves all timestamps to 'now' (used when carrying lessons into a new episode with a new clock).</summary>
        public void Retime(float now)
        {
            foreach (var e in _entries.Values) e.LastUpdate = now;
        }

        private void EvictOldest()
        {
            long oldest = 0;
            float t = float.MaxValue;
            foreach (var kv in _entries) if (kv.Value.LastUpdate < t) { t = kv.Value.LastUpdate; oldest = kv.Key; }
            _entries.Remove(oldest);
        }

        public void CopyFrom(StrategyModel other)
        {
            _entries.Clear();
            foreach (var kv in other._entries)
            {
                var s = kv.Value;
                var c = new StrategyEstimate { Key = s.Key, Context = s.Context, Value = s.Value, Evidence = s.Evidence, LastUpdate = s.LastUpdate, Successes = s.Successes, Failures = s.Failures };
                c.Recent.AddRange(s.Recent);
                _entries[kv.Key] = c;
            }
        }
    }
}
