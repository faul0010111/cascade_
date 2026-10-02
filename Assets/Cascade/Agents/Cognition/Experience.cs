using System;
using System.Collections.Generic;
using Cascade.Core;
using UnityEngine;

namespace Cascade.Agents
{
    /// <summary>How the agent came to know an outcome. Direct experience weighs most, hearsay least.</summary>
    public enum ExperienceSource { Direct, Observed, Reported }

    /// <summary>
    /// A way of reaching a goal whose success the agent can learn about. The parameter identifies the instance
    /// (which exit door, which person). Strategies are deliberately few and meaningful: every one has consumers.
    /// </summary>
    public enum StrategyKind { ExitRoute, FollowPerson, AskPerson, ExploreAlone, FightFire, HelpPerson, Hide }

    public readonly struct StrategyKey : IEquatable<StrategyKey>
    {
        public readonly StrategyKind Kind;
        public readonly int Param;

        public StrategyKey(StrategyKind kind, int param = 0) { Kind = kind; Param = param; }

        public static StrategyKey Exit(int door) => new StrategyKey(StrategyKind.ExitRoute, door);
        public static StrategyKey Follow(EntityId e) => new StrategyKey(StrategyKind.FollowPerson, e.Value);
        public static StrategyKey Ask(EntityId e) => new StrategyKey(StrategyKind.AskPerson, e.Value);
        public static readonly StrategyKey Explore = new StrategyKey(StrategyKind.ExploreAlone);
        public static readonly StrategyKey Firefight = new StrategyKey(StrategyKind.FightFire);
        public static readonly StrategyKey Help = new StrategyKey(StrategyKind.HelpPerson);
        public static readonly StrategyKey HideAway = new StrategyKey(StrategyKind.Hide);

        public bool Equals(StrategyKey o) => Kind == o.Kind && Param == o.Param;
        public override bool Equals(object obj) => obj is StrategyKey k && Equals(k);
        public override int GetHashCode() => ((int)Kind * 7919) ^ Param;

        public string Describe(AgentBrain b)
        {
            switch (Kind)
            {
                case StrategyKind.ExitRoute: return "exit via " + b.FloorPlan.RoomName(ExitSide(b.FloorPlan, Param));
                case StrategyKind.FollowPerson: return "follow " + b.NameOf(new EntityId(Param));
                case StrategyKind.AskPerson: return "ask " + b.NameOf(new EntityId(Param));
                case StrategyKind.ExploreAlone: return "explore alone";
                case StrategyKind.FightFire: return "fight fire";
                case StrategyKind.HelpPerson: return "help others";
                default: return "hide";
            }
        }

        public static int ExitSide(World.IFloorPlan plan, int door)
        {
            for (int r = 0; r < plan.RoomCount; r++)
            {
                if (!plan.IsExterior(r)) continue;
                var doors = plan.DoorsOf(r);
                for (int i = 0; i < doors.Count; i++) if (doors[i] == door) return r;
            }
            return -1;
        }
    }

    /// <summary>
    /// Coarse situation buckets for contextual learning ("route A works when calm, not in smoke").
    /// Computed from beliefs only.
    /// </summary>
    public static class Contexts
    {
        public const int General = -1;
        public const int Threat = 1;
        public const int Smoke = 2;
        public const int Count = 4;

        public static int Of(AgentBrain b, float now)
        {
            int c = 0;
            if (b.KnownThreatLevel(now) > 0.5f) c |= Threat;
            if (b.CurrentRoom >= 0 && b.Beliefs.Weighted(FactKey.Smoke(b.CurrentRoom), now) > 0.25f) c |= Smoke;
            return c;
        }

        public static string Name(int c)
        {
            if (c < 0) return "any";
            if (c == 0) return "calm";
            return (c & Threat) != 0 ? ((c & Smoke) != 0 ? "threat+smoke" : "threat") : "smoke";
        }
    }

    /// <summary>An expectation formed before acting.</summary>
    public struct Prediction
    {
        public StrategyKey Key;
        public int Context;
        public float Expected;    // 0..1 probability-like expectation of success
        public float Confidence;  // 0..1, how much evidence backs it
        public float Time;
        public bool Valid;
    }

    /// <summary>
    /// One structured experience: context, what was tried, what was expected, what happened, how it felt.
    /// Experiences are the input of learning; they are also the episodic record the debugger shows.
    /// </summary>
    public sealed class Experience
    {
        public float Time;
        public ExperienceSource Source;
        public EntityId Actor;          // who acted (self for direct experience)
        public StrategyKey Strategy;
        public int Context;
        public int Place;
        public EntityId Social;         // person involved (leader, helper, informant)
        public float Expected;
        public float Actual;            // 0 failure .. 1 success
        public float PredictionError => Actual - Expected;
        public float Reward;            // -1..1
        public float Confidence;        // confidence of the prediction
        public float Weight;            // learning weight actually applied
        public EmotionState Emotion;
        public string Note;

        public bool Success => Actual >= 0.5f;

        public string Describe(AgentBrain b)
        {
            string who = Source == ExperienceSource.Direct ? "I" : b.NameOf(Actor);
            string how = Source == ExperienceSource.Direct ? "" : Source == ExperienceSource.Observed ? " (seen)" : " (heard)";
            return who + " tried to " + Strategy.Describe(b) + ": " + (Success ? "worked" : "failed") +
                   (string.IsNullOrEmpty(Note) ? "" : " (" + Note + ")") + how +
                   "  expected " + Mathf.RoundToInt(Expected * 100) + "%, error " + (PredictionError >= 0 ? "+" : "") + PredictionError.ToString("0.00");
        }
    }

    public sealed class ExperienceLog
    {
        public int Capacity = 64;
        private readonly List<Experience> _items = new List<Experience>();
        public IReadOnlyList<Experience> Items => _items;
        public int Total { get; private set; }

        public void Add(Experience e)
        {
            Total++;
            _items.Add(e);
            if (_items.Count > Capacity) _items.RemoveAt(0);
        }
    }
}
