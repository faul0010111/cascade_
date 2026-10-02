using System;
using System.Collections.Generic;
using Cascade.Core;
using UnityEngine;

namespace Cascade.Agents
{
    public enum MemoryKind
    {
        SawFire, RouteDangerous, DoorWasBlocked, WasHelpedBy, WasAbandonedBy, HelpedSomeone, SawSomeoneHelp,
        WasWarnedBy, InfoWasWrong, InfoWasRight, ReachedSafety, WitnessedInjury, WasPushedBy, IgnoredBy, LedBy, LedIntoDanger
    }

    public sealed class EpisodicMemory
    {
        public MemoryKind Kind;
        public EntityId Subject;
        public int Place;
        public float Time;
        public float Valence;    // -1 bad .. +1 good
        public float Intensity;  // 0..1 salience when stored
        public BeliefSourceKind Source;

        public string Describe(Func<EntityId, string> names, Func<int, string> rooms)
        {
            string who = Subject.IsValid ? names(Subject) : "";
            string where = Place >= 0 ? rooms(Place) : "";
            switch (Kind)
            {
                case MemoryKind.SawFire: return "Saw fire in " + where;
                case MemoryKind.RouteDangerous: return where + " was dangerous";
                case MemoryKind.DoorWasBlocked: return "A door in " + where + " was blocked";
                case MemoryKind.WasHelpedBy: return who + " helped me";
                case MemoryKind.WasAbandonedBy: return who + " abandoned me";
                case MemoryKind.HelpedSomeone: return "I helped " + who;
                case MemoryKind.SawSomeoneHelp: return "Saw " + who + " helping someone";
                case MemoryKind.WasWarnedBy: return who + " warned me";
                case MemoryKind.InfoWasWrong: return who + " told me something false";
                case MemoryKind.InfoWasRight: return who + " told me the truth";
                case MemoryKind.ReachedSafety: return "Reached safety at " + where;
                case MemoryKind.WitnessedInjury: return "Saw " + who + " injured";
                case MemoryKind.WasPushedBy: return who + " pushed me";
                case MemoryKind.IgnoredBy: return who + " ignored my request";
                case MemoryKind.LedBy: return who + " led me to safety";
                case MemoryKind.LedIntoDanger: return who + " led me into danger";
                default: return Kind.ToString();
            }
        }
    }

    /// <summary>
    /// Episodic memory plus consolidated semantic knowledge. Every memory kind must be read by something that
    /// changes behavior (route costs, considerations, relationships); memory is never decorative.
    /// </summary>
    public sealed class MemorySystem
    {
        public int Capacity = 64;
        public const float PlaceDangerHalfLife = 180f;
        public const float EpisodeTimeScale = 300f;

        private readonly List<EpisodicMemory> _episodes = new List<EpisodicMemory>();
        private readonly Dictionary<int, float> _placeDanger = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _placeDangerTime = new Dictionary<int, float>();
        private readonly Dictionary<EntityId, float> _impressions = new Dictionary<EntityId, float>();
        private readonly Dictionary<int, int> _visits = new Dictionary<int, int>();

        public IReadOnlyList<EpisodicMemory> Episodes => _episodes;
        public int Count => _episodes.Count;

        public EpisodicMemory Record(MemoryKind kind, EntityId subject, int place, float now, float valence, float intensity,
                                     BeliefSourceKind source = BeliefSourceKind.Perceived)
        {
            intensity = Mathf.Clamp01(intensity);
            // Merge repeated observations of the same thing instead of flooding memory.
            foreach (var m in _episodes)
            {
                if (m.Kind == kind && m.Subject == subject && m.Place == place && now - m.Time < 15f)
                {
                    m.Intensity = Mathf.Max(m.Intensity, intensity);
                    m.Time = now;
                    Consolidate(m, now);
                    return m;
                }
            }

            var mem = new EpisodicMemory { Kind = kind, Subject = subject, Place = place, Time = now, Valence = valence, Intensity = intensity, Source = source };
            _episodes.Add(mem);
            Consolidate(mem, now);
            if (_episodes.Count > Capacity) ForgetLeastRelevant(now);
            return mem;
        }

        private void Consolidate(EpisodicMemory m, float now)
        {
            float danger = 0f;
            switch (m.Kind)
            {
                case MemoryKind.SawFire: danger = m.Intensity; break;
                case MemoryKind.RouteDangerous: danger = m.Intensity; break;
                case MemoryKind.DoorWasBlocked: danger = 0.5f * m.Intensity; break;
                case MemoryKind.WitnessedInjury: danger = 0.4f * m.Intensity; break; // someone got hurt here
                case MemoryKind.LedIntoDanger: danger = 0.6f * m.Intensity; break;
            }
            if (danger > 0f && m.Place >= 0)
            {
                float current = PlaceDanger(m.Place, now);
                _placeDanger[m.Place] = Mathf.Max(current, danger);
                _placeDangerTime[m.Place] = now;
            }
            if (m.Subject.IsValid && m.Valence != 0f && ShapesImpression(m.Kind))
            {
                float imp;
                _impressions.TryGetValue(m.Subject, out imp);
                _impressions[m.Subject] = Mathf.Clamp(imp + m.Valence * m.Intensity * 0.5f, -1f, 1f);
            }
        }

        /// <summary>
        /// Only memories where the subject is responsible for what happened shape my impression of them.
        /// (Seeing someone hurt, or helping someone, says nothing about their character.)
        /// </summary>
        public static bool ShapesImpression(MemoryKind k)
        {
            switch (k)
            {
                case MemoryKind.WasHelpedBy: case MemoryKind.WasAbandonedBy: case MemoryKind.SawSomeoneHelp: case MemoryKind.WasWarnedBy:
                case MemoryKind.InfoWasWrong: case MemoryKind.InfoWasRight: case MemoryKind.WasPushedBy: case MemoryKind.IgnoredBy:
                case MemoryKind.LedBy: case MemoryKind.LedIntoDanger:
                    return true;
                default:
                    return false;
            }
        }

        public void Clear()
        {
            _episodes.Clear(); _placeDanger.Clear(); _placeDangerTime.Clear(); _impressions.Clear(); _visits.Clear();
        }

        /// <summary>Remembered danger of a place, decaying slowly. Read by route costs and goal considerations.</summary>
        public float PlaceDanger(int room, float now)
        {
            float v, t;
            if (!_placeDanger.TryGetValue(room, out v)) return 0f;
            _placeDangerTime.TryGetValue(room, out t);
            return v * Mathf.Exp(-0.693f * (now - t) / PlaceDangerHalfLife);
        }

        /// <summary>Overall impression of an entity from lived experience, -1..1.</summary>
        public float Impression(EntityId e)
        {
            float v;
            return _impressions.TryGetValue(e, out v) ? v : 0f;
        }

        public void RecordVisit(int room)
        {
            int n;
            _visits.TryGetValue(room, out n);
            _visits[room] = n + 1;
        }

        public int Visits(int room)
        {
            int n;
            return _visits.TryGetValue(room, out n) ? n : 0;
        }

        public bool Has(MemoryKind kind, EntityId subject)
        {
            foreach (var m in _episodes) if (m.Kind == kind && m.Subject == subject) return true;
            return false;
        }

        public float Relevance(EpisodicMemory m, float now, int place = -1, EntityId subject = default(EntityId))
        {
            float match = 1f;
            if (place >= 0 && m.Place == place) match += 1f;
            if (subject.IsValid && m.Subject == subject) match += 1f;
            return m.Intensity * Mathf.Exp(-(now - m.Time) / EpisodeTimeScale) * match;
        }

        public void TopRelevant(float now, int count, List<EpisodicMemory> into, int place = -1, EntityId subject = default(EntityId))
        {
            into.Clear();
            into.AddRange(_episodes);
            into.Sort((a, b) => Relevance(b, now, place, subject).CompareTo(Relevance(a, now, place, subject)));
            if (into.Count > count) into.RemoveRange(count, into.Count - count);
        }

        private void ForgetLeastRelevant(float now)
        {
            int worst = 0;
            float worstScore = float.MaxValue;
            for (int i = 0; i < _episodes.Count; i++)
            {
                float r = Relevance(_episodes[i], now);
                if (r < worstScore) { worstScore = r; worst = i; }
            }
            _episodes.RemoveAt(worst); // already consolidated into semantic knowledge
        }
    }
}
