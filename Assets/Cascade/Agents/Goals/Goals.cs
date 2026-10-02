using System;
using System.Collections.Generic;
using Cascade.AI.Utility;
using Cascade.Core;
using UnityEngine;

namespace Cascade.Agents
{
    public enum GoalType { Routine, Evacuate, HelpOther, Follow, SearchFor, Investigate, WarnOther, GatherGroup, FightFire, Hide, Verify, AskForInfo }

    /// <summary>A goal instance: type plus optional target entity or place (room).</summary>
    public struct GoalOption : IEquatable<GoalOption>
    {
        public GoalType Type;
        public EntityId Target;
        public int Place;

        public static GoalOption Of(GoalType type) => new GoalOption { Type = type, Place = -1 };
        public static GoalOption WithTarget(GoalType type, EntityId target) => new GoalOption { Type = type, Target = target, Place = -1 };
        public static GoalOption AtPlace(GoalType type, int place) => new GoalOption { Type = type, Place = place };

        public bool Equals(GoalOption o) => Type == o.Type && Target == o.Target && Place == o.Place;
        public override bool Equals(object obj) => obj is GoalOption o && Equals(o);
        public override int GetHashCode() => ((int)Type * 7919) ^ (Target.Value * 31) ^ Place;

        public string Label(AgentBrain b)
        {
            if (Target.IsValid) return Type + "(" + b.NameOf(Target) + ")";
            if (Place >= 0) return Type + "(" + b.FloorPlan.RoomName(Place) + ")";
            return Type.ToString();
        }
    }

    public struct DecisionContext
    {
        public AgentBrain Brain;
        public GoalOption Option;
        public float Now;
    }

    public sealed class GoalDefinition
    {
        public GoalType Type;
        public float BaseWeight = 1f;
        public float CooldownOnSuccess;
        public NeedType[] Needs;
        public Func<AgentBrain, float> PersonalityMultiplier = b => 1f;
        /// <summary>Optional per-option multiplier (e.g. who is asking). Traced together with the personality multiplier.</summary>
        public Func<AgentBrain, GoalOption, float> OptionMultiplier;
        public Action<AgentBrain, float, List<GoalOption>> Enumerate;
        public readonly List<IConsideration<DecisionContext>> Considerations = new List<IConsideration<DecisionContext>>();

        public GoalDefinition Add(string name, InputFn<DecisionContext> input, ResponseCurve curve)
        {
            Considerations.Add(new Consideration<DecisionContext>(name, input, curve));
            return this;
        }
    }

    /// <summary>
    /// Data describing every goal the agent can pursue. Adding a behavior means adding a definition here plus the
    /// actions that achieve it; no other system needs to change.
    /// </summary>
    public static class GoalLibrary
    {
        private static float F(bool v) => v ? 1f : 0f;

        public static List<GoalDefinition> CreateDefault()
        {
            var list = new List<GoalDefinition>();

            list.Add(new GoalDefinition
            {
                Type = GoalType.Routine, BaseWeight = 0.35f,
                Enumerate = (b, now, o) => o.Add(GoalOption.Of(GoalType.Routine))
            }
            .Add("Unaware or already safe", c => Mathf.Max(1f - c.Brain.State[StateVar.Awareness], F(c.Brain.AtSafety)), ResponseCurve.Linear)
            .Add("Calm", c => c.Brain.State[StateVar.Fear], ResponseCurve.InverseLinear));

            list.Add(new GoalDefinition
            {
                Type = GoalType.Evacuate, BaseWeight = 1f, Needs = new[] { NeedType.Escape, NeedType.Safety },
                PersonalityMultiplier = b => 0.8f + 0.35f * b.Personality[Trait.Fearfulness] + 0.2f * (1f - b.Personality[Trait.Empathy]),
                Enumerate = (b, now, o) => o.Add(GoalOption.Of(GoalType.Evacuate))
            }
            .Add("Escape need", c => c.Brain.Needs[NeedType.Escape], ResponseCurve.Logistic(10f, 0.35f))
            .Add("Not yet safe", c => F(!c.Brain.AtSafety), ResponseCurve.Step(0.5f))
            .Add("Knows a way out", c => c.Brain.Routes.ExitRouteConfidence(c.Now), ResponseCurve.Floor(0.45f))
            .Add("Able to move", c => F(!c.Brain.Incapacitated), ResponseCurve.Step(0.5f)));

            list.Add(new GoalDefinition
            {
                Type = GoalType.HelpOther, BaseWeight = 1f, CooldownOnSuccess = 25f, Needs = new[] { NeedType.ProtectOthers },
                PersonalityMultiplier = b => 0.3f + 1.2f * b.Personality[Trait.Empathy] + 0.2f * b.Personality[Trait.Courage],
                Enumerate = EnumerateHelpTargets
            }
            .Add("Target in need", c => TargetNeed(c), ResponseCurve.Linear)
            .Add("Expected success", c => c.Brain.Learning.Predict(StrategyKey.Help).Expected, ResponseCurve.Floor(0.4f))
            .Add("Bond", c => Bond(c.Brain, c.Option.Target), ResponseCurve.Linear)
            .Add("Risk vs bravery", c => Mathf.Clamp01(1.3f * Mathf.Max(TargetDanger(c), PathDanger(c)) - 0.7f * Bravery(c.Brain)), ResponseCurve.InverseFloor(0.05f))
            .Add("Composure", c => c.Brain.State[StateVar.Fear] * (1f - 0.5f * Affection(c.Brain, c.Option.Target)), ResponseCurve.Logistic(10f, 0.8f, invert: true))
            .Add("Distance", c => c.Brain.DistanceToBelieved(c.Option.Target, c.Now) / 40f, ResponseCurve.InverseFloor(0.3f))
            .Add("Able", c => F(!c.Brain.IsInjured), ResponseCurve.Step(0.5f)));

            list.Add(new GoalDefinition
            {
                Type = GoalType.Follow, BaseWeight = 1f, Needs = new[] { NeedType.Escape, NeedType.Social },
                PersonalityMultiplier = b => 0.5f + 0.8f * (1f - b.Personality[Trait.Independence]) + 0.2f * (1f - b.Personality[Trait.Leadership]),
                // Being called by someone you respect is a strong pull (authority / social proof).
                OptionMultiplier = (b, o) => 1f + 0.5f * b.Beliefs.Weighted(FactKey.Ordered(o.Target), b.Now) + 0.3f * RespectFor(b, o.Target),
                Enumerate = EnumerateLeaders
            }
            .Add("Escape need", c => c.Brain.Needs[NeedType.Escape], ResponseCurve.Logistic(10f, 0.3f))
            .Add("Trust in leader", c => LeaderTrust(c.Brain, c.Option.Target), ResponseCurve.Floor(0.4f))
            .Add("Social pull", c => SocialPull(c), ResponseCurve.Linear)
            .Add("Leader nearby", c => c.Brain.DistanceToBelieved(c.Option.Target, c.Now) / 20f, ResponseCurve.InverseFloor(0.4f))
            .Add("Expected outcome", c => c.Brain.Learning.Predict(StrategyKey.Follow(c.Option.Target)).Expected, ResponseCurve.Floor(0.15f))
            .Add("Not yet safe", c => F(!c.Brain.AtSafety), ResponseCurve.Step(0.5f)));

            list.Add(new GoalDefinition
            {
                Type = GoalType.SearchFor, BaseWeight = 1f, CooldownOnSuccess = 20f, Needs = new[] { NeedType.ProtectOthers, NeedType.Social },
                PersonalityMultiplier = b => 0.8f + 0.4f * b.Personality[Trait.Empathy],
                Enumerate = (b, now, o) =>
                {
                    var t = b.LongTermTarget;
                    if (!t.IsValid || b.State[StateVar.Awareness] < 0.3f || b.Beliefs.Weighted(FactKey.Safe(t), now) > 0.5f) return;
                    if (b.Perception.CanSee(t) && b.DistanceToBelieved(t, now) < 6f) return; // found: helping/leading takes over
                    if (b.Groups.LeaderOf(t) == b.Id || b.Groups.LeaderOf(b.Id) == t) return;
                    o.Add(GoalOption.WithTarget(GoalType.SearchFor, t));
                }
            }
            .Add("Bond", c => Affection(c.Brain, c.Option.Target), ResponseCurve.Linear)
            .Add("Not known safe", c => 1f - c.Brain.Beliefs.Weighted(FactKey.Safe(c.Option.Target), c.Now), ResponseCurve.Linear)
            .Add("Composure", c => c.Brain.State[StateVar.Fear], ResponseCurve.Logistic(8f, 0.9f, invert: true))
            .Add("Able", c => F(!c.Brain.IsInjured), ResponseCurve.Step(0.5f)));

            list.Add(new GoalDefinition
            {
                Type = GoalType.Investigate, BaseWeight = 0.8f, CooldownOnSuccess = 30f, Needs = new[] { NeedType.Information },
                PersonalityMultiplier = b => 0.4f + 0.9f * b.Personality[Trait.Curiosity] + 0.2f * b.Personality[Trait.Courage],
                Enumerate = EnumerateInvestigation
            }
            .Add("Information need", c => c.Brain.Needs[NeedType.Information], ResponseCurve.Linear)
            .Add("Curiosity", c => c.Brain.State[StateVar.Curiosity], ResponseCurve.Floor(0.2f))
            .Add("Place not dangerous", c => c.Brain.DangerAt(c.Option.Place, c.Now), ResponseCurve.InverseFloor(0.05f))
            .Add("Composure", c => c.Brain.State[StateVar.Fear], ResponseCurve.Logistic(10f, 0.5f, invert: true))
            .Add("Distance", c => Vector3.Distance(c.Brain.Body.Position, c.Brain.FloorPlan.RoomCenter(c.Option.Place)) / 40f, ResponseCurve.InverseFloor(0.3f)));

            list.Add(new GoalDefinition
            {
                Type = GoalType.WarnOther, BaseWeight = 0.85f, CooldownOnSuccess = 40f, Needs = new[] { NeedType.ProtectOthers },
                PersonalityMultiplier = b => 0.3f + 0.6f * b.Personality[Trait.Empathy] + 0.6f * b.Personality[Trait.Leadership],
                Enumerate = EnumerateWarnTargets
            }
            .Add("I know the danger", c => Mathf.Max(c.Brain.KnownThreatLevel(c.Now), 0.6f * c.Brain.Beliefs.Weighted(FactKey.Alarm, c.Now)), ResponseCurve.Linear)
            .Add("Target unaware", c => 1f - c.Brain.Beliefs.Weighted(FactKey.Aware(c.Option.Target), c.Now), ResponseCurve.Linear)
            .Add("Close", c => c.Brain.DistanceToBelieved(c.Option.Target, c.Now) / 15f, ResponseCurve.InverseFloor(0.2f))
            .Add("Composure", c => c.Brain.State[StateVar.Fear], ResponseCurve.Logistic(10f, 0.7f, invert: true)));

            list.Add(new GoalDefinition
            {
                Type = GoalType.GatherGroup, BaseWeight = 0.9f, CooldownOnSuccess = 25f, Needs = new[] { NeedType.Escape, NeedType.ProtectOthers },
                PersonalityMultiplier = b => 0.1f + 1.3f * b.Personality[Trait.Leadership] * b.Personality[Trait.Leadership],
                Enumerate = (b, now, o) =>
                {
                    if (b.AtSafety || b.Groups.LeaderOf(b.Id).IsValid) return;
                    o.Add(GoalOption.Of(GoalType.GatherGroup));
                }
            }
            .Add("Knows a way out", c => c.Brain.Routes.ExitRouteConfidence(c.Now), ResponseCurve.Floor(0.1f))
            .Add("People around", c => c.Brain.UngroupedPeopleVisible(c.Now) / 4f, ResponseCurve.Linear)
            .Add("Escape need", c => c.Brain.Needs[NeedType.Escape], ResponseCurve.Logistic(10f, 0.35f))
            .Add("Group still small", c => c.Brain.Groups.FollowerCount(c.Brain.Id) / 6f, ResponseCurve.InverseFloor(0.2f)));

            list.Add(new GoalDefinition
            {
                Type = GoalType.FightFire, BaseWeight = 0.75f, CooldownOnSuccess = 10f, Needs = new[] { NeedType.Safety, NeedType.ProtectOthers },
                PersonalityMultiplier = b => 0.1f + 1.6f * b.Personality[Trait.Courage] * b.Personality[Trait.RiskTolerance] + 0.2f * b.Personality[Trait.Aggression],
                Enumerate = EnumerateFires
            }
            .Add("Expected success", c => c.Brain.Learning.Predict(StrategyKey.Firefight).Expected, ResponseCurve.Floor(0.1f))
            .Add("Fire still small", c => c.Brain.Beliefs.Value(FactKey.Hazard(c.Option.Place), c.Now), ResponseCurve.Logistic(12f, 0.45f, invert: true))
            .Add("Extinguisher", c => c.Brain.HeldExtinguisher >= 0 ? 1f : (c.Brain.HasKnownExtinguisher(c.Now) ? 0.7f : 0f), ResponseCurve.Linear)
            .Add("Composure", c => c.Brain.State[StateVar.Fear], ResponseCurve.Logistic(10f, 0.55f, invert: true))
            .Add("Close", c => Vector3.Distance(c.Brain.Body.Position, c.Brain.FloorPlan.RoomCenter(c.Option.Place)) / 30f, ResponseCurve.InverseFloor(0.3f))
            .Add("Able", c => F(!c.Brain.IsInjured), ResponseCurve.Step(0.5f)));

            list.Add(new GoalDefinition
            {
                Type = GoalType.Hide, BaseWeight = 0.8f, Needs = new[] { NeedType.Safety },
                PersonalityMultiplier = b => Mathf.Max(0.1f, 0.3f + b.Personality[Trait.Fearfulness] - 0.3f * b.Personality[Trait.Courage]),
                Enumerate = (b, now, o) =>
                {
                    if (b.State[StateVar.Awareness] > 0.3f && !b.AtSafety) o.Add(GoalOption.Of(GoalType.Hide));
                }
            }
            .Add("Terror", c => c.Brain.State[StateVar.Fear], ResponseCurve.Logistic(12f, 0.72f))
            .Add("Hiding worked before", c => c.Brain.Learning.Predict(StrategyKey.HideAway).Expected, ResponseCurve.Floor(0.3f))
            .Add("No way out", c => c.Brain.Routes.ExitRouteConfidence(c.Now), ResponseCurve.InverseFloor(0.3f))
            .Add("Here is not burning", c => c.Brain.DangerAt(c.Brain.CurrentRoom, c.Now), ResponseCurve.InverseFloor(0.1f)));

            // Information seeking: resolve doubt by looking (Verify) or by asking someone (AskForInfo).
            list.Add(new GoalDefinition
            {
                Type = GoalType.Verify, BaseWeight = 0.85f, CooldownOnSuccess = 25f, Needs = new[] { NeedType.Information },
                PersonalityMultiplier = b => (0.3f + 0.7f * b.Personality[Trait.Curiosity] + 0.6f * Mathf.Max(0f, b.Learning.Dispositions.InformationSeeking))
                                            * (1.2f - 0.6f * b.State[StateVar.Confidence]), // the confident don't bother checking
                Enumerate = EnumerateVerification
            }
            .Add("Doubt", c => PlaceDoubt(c.Brain, c.Option.Place, c.Now), ResponseCurve.Linear)
            .Add("Information need", c => c.Brain.Needs[NeedType.Information], ResponseCurve.Floor(0.3f))
            .Add("Place not dangerous", c => c.Brain.DangerAt(c.Option.Place, c.Now), ResponseCurve.InverseFloor(0.05f))
            .Add("Composure", c => c.Brain.State[StateVar.Fear], ResponseCurve.Logistic(10f, 0.6f, invert: true))
            .Add("Distance", c => Vector3.Distance(c.Brain.Body.Position, c.Brain.FloorPlan.RoomCenter(c.Option.Place)) / 40f, ResponseCurve.InverseFloor(0.3f)));

            list.Add(new GoalDefinition
            {
                Type = GoalType.AskForInfo, BaseWeight = 0.85f, CooldownOnSuccess = 30f, Needs = new[] { NeedType.Information, NeedType.Assistance },
                PersonalityMultiplier = b => 0.3f + 0.6f * (1f - b.Personality[Trait.Independence]) + 0.4f * (1f - b.State[StateVar.Confidence])
                                             + 0.4f * Mathf.Max(0f, b.Learning.Dispositions.SocialExpectation),
                Enumerate = EnumerateInformants
            }
            .Add("Uncertainty", c => c.Brain.Self.Uncertainty, ResponseCurve.Linear)
            .Add("Source quality", c => SourceQuality(c.Brain, c.Option.Target), ResponseCurve.Floor(0.2f))
            .Add("Expected answer", c => c.Brain.Learning.Predict(StrategyKey.Ask(c.Option.Target)).Expected, ResponseCurve.Floor(0.2f))
            .Add("Close", c => c.Brain.DistanceToBelieved(c.Option.Target, c.Now) / 12f, ResponseCurve.InverseFloor(0.3f))
            .Add("Composure", c => c.Brain.State[StateVar.Fear], ResponseCurve.Logistic(10f, 0.8f, invert: true)));

            return list;
        }

        // ---- helpers -------------------------------------------------------------------------------------------

        private static float Bravery(AgentBrain b) => 0.5f * b.Personality[Trait.Courage] + 0.5f * b.Personality[Trait.RiskTolerance] - 0.5f * b.Learning.Dispositions.RiskPerception;

        private static float Affection(AgentBrain b, EntityId t)
        {
            var r = b.Relationships.Peek(t);
            return r == null ? 0f : r.Affection;
        }

        private static float Bond(AgentBrain b, EntityId t)
        {
            var r = b.Relationships.Peek(t);
            float bond = 0f;
            if (r != null) bond = Mathf.Max(r.Affection, r.Respect * 0.5f);
            bond = Mathf.Max(bond, b.Memory.Impression(t) * 0.5f);
            return 0.45f + 0.55f * Mathf.Clamp01(bond); // strangers still matter; bonds matter more (recalibrated after the impression fix, see docs/cognition/AUDIT.md)
        }

        private static float LeaderTrust(AgentBrain b, EntityId t)
        {
            var r = b.Relationships.Get(t);
            return Mathf.Clamp01(0.6f * r.Trust + 0.4f * r.Respect - 0.3f * r.Suspicion + 0.2f * b.Memory.Impression(t));
        }

        private static float RespectFor(AgentBrain b, EntityId t)
        {
            var r = b.Relationships.Peek(t);
            return r == null ? 0f : r.Respect;
        }

        /// <summary>Uncertainty, being asked, and fear-driven herding all push toward following someone.</summary>
        private static float SocialPull(DecisionContext c)
        {
            var b = c.Brain;
            float ordered = b.Beliefs.Weighted(FactKey.Ordered(c.Option.Target), c.Now);
            float uncertainty = b.Self.Uncertainty;
            float herding = b.State[StateVar.Fear] * (1f - b.Personality[Trait.Independence]);
            return Mathf.Clamp01(0.35f + 0.4f * ordered + 0.35f * uncertainty + 0.3f * herding + 0.3f * b.Learning.Dispositions.SocialExpectation);
        }

        private static float TargetDanger(DecisionContext c) => c.Brain.DangerAt(c.Brain.BelievedRoomOf(c.Option.Target, c.Now), c.Now);

        /// <summary>Danger of getting there, not only of being there: the worst room on the believed route.</summary>
        private static float PathDanger(DecisionContext c) => c.Brain.RouteDanger(c.Brain.BelievedRoomOf(c.Option.Target, c.Now), c.Now);

        private static float TargetNeed(DecisionContext c)
            => Mathf.Max(c.Brain.Beliefs.Weighted(FactKey.Injured(c.Option.Target), c.Now), TargetDanger(c) * 0.8f);

        private static readonly List<EntityId> Scratch = new List<EntityId>();
        private static readonly List<Belief> BeliefScratch = new List<Belief>();

        private static void EnumerateHelpTargets(AgentBrain b, float now, List<GoalOption> o)
        {
            Scratch.Clear();
            b.KnownAgents(now, 0.2f, Scratch);
            foreach (var id in Scratch)
            {
                if (b.Beliefs.Weighted(FactKey.Safe(id), now) > 0.5f) continue;
                if (b.DistanceToBelieved(id, now) > 40f) continue;
                if (b.Groups.LeaderOf(id) == b.Id) continue; // already following me: leading them out IS the help
                float injured = b.Beliefs.Weighted(FactKey.Injured(id), now);
                float bond = Affection(b, id);
                float danger = b.DangerAt(b.BelievedRoomOf(id, now), now);
                if (injured > 0.4f || (bond > 0.5f && danger > 0.3f)) o.Add(GoalOption.WithTarget(GoalType.HelpOther, id));
            }
        }

        private static void EnumerateLeaders(AgentBrain b, float now, List<GoalOption> o)
        {
            if (b.AtSafety) return;
            Scratch.Clear();
            b.KnownAgents(now, 0.35f, Scratch);
            foreach (var id in Scratch)
            {
                if (b.DistanceToBelieved(id, now) > 20f) continue;
                float ordered = b.Beliefs.Weighted(FactKey.Ordered(id), now);
                float evacuating = b.Beliefs.Weighted(FactKey.Evacuating(id), now);
                if (ordered < 0.2f && evacuating < 0.4f) continue;
                if (b.Groups.FollowsTransitively(id, b.Id)) continue; // they already follow me (directly or not)
                if (b.Beliefs.Weighted(FactKey.Injured(id), now) > 0.5f) continue;
                o.Add(GoalOption.WithTarget(GoalType.Follow, id));
            }
        }

        private static void EnumerateInvestigation(AgentBrain b, float now, List<GoalOption> o)
        {
            if (b.AtSafety) return;
            BeliefScratch.Clear();
            b.Beliefs.Collect(FactType.NoiseInRoom, BeliefScratch);
            b.Beliefs.Collect(FactType.SmokeInRoom, BeliefScratch);
            foreach (var belief in BeliefScratch)
            {
                int room = belief.Key.Subject;
                if (room < 0 || b.FloorPlan.IsExterior(room)) continue;
                if (belief.Value * belief.EffectiveConfidence(now) < 0.12f) continue;
                if (belief.Key.Type == FactType.SmokeInRoom && b.Beliefs.Confidence(FactKey.Hazard(room), now) > 0.4f) continue;
                var opt = GoalOption.AtPlace(GoalType.Investigate, room);
                if (!o.Contains(opt)) o.Add(opt);
            }
        }

        private static void EnumerateWarnTargets(AgentBrain b, float now, List<GoalOption> o)
        {
            if (b.AtSafety) return; // shouting from outside is modelled by GatherGroup/Warn messages, not by walking back in
            if (b.KnownThreatLevel(now) < 0.2f && b.Beliefs.Weighted(FactKey.Alarm, now) < 0.5f) return;
            Scratch.Clear();
            b.KnownAgents(now, 0.4f, Scratch);
            foreach (var id in Scratch)
            {
                if (b.DistanceToBelieved(id, now) > 15f) continue;
                if (b.Beliefs.Weighted(FactKey.Aware(id), now) > 0.5f) continue;
                if (b.Beliefs.Weighted(FactKey.Safe(id), now) > 0.5f) continue;
                if (b.Beliefs.Weighted(FactKey.Evacuating(id), now) > 0.4f) continue;
                o.Add(GoalOption.WithTarget(GoalType.WarnOther, id));
            }
        }

        public static float SourceQuality(AgentBrain b, EntityId t)
        {
            var r = b.Relationships.Get(t);
            return Mathf.Clamp01(0.5f * r.Trust + 0.5f * r.Reliability - 0.5f * r.Suspicion);
        }

        /// <summary>Largest doubt about anything that can be checked from a room (doors of the room, fire in it).</summary>
        public static float PlaceDoubt(AgentBrain b, int room, float now)
        {
            float d = 0f;
            var doors = b.FloorPlan.DoorsOf(room);
            for (int i = 0; i < doors.Count; i++)
            {
                Belief belief;
                if (!b.Beliefs.TryGet(FactKey.Door(doors[i]), out belief)) continue;
                bool matters = b.IsDoorOnCurrentRoute(doors[i]) || b.FloorPlan.IsExitDoor(doors[i]);
                if (belief.HasAlternative || matters) d = Mathf.Max(d, belief.Doubt(now) * (belief.HasAlternative ? 1f : 0.6f) * (matters ? 1f : 0.5f));
            }
            Belief h;
            if (b.Beliefs.TryGet(FactKey.Hazard(room), out h) && h.HasAlternative) d = Mathf.Max(d, h.Doubt(now));
            return d;
        }

        private static void EnumerateVerification(AgentBrain b, float now, List<GoalOption> o)
        {
            if (!b.Cognition.InformationSeeking || b.AtSafety || b.State[StateVar.Awareness] < 0.25f) return;
            for (int r = 0; r < b.FloorPlan.RoomCount; r++)
            {
                if (b.FloorPlan.IsExterior(r)) continue;
                if (PlaceDoubt(b, r, now) < 0.35f) continue;
                if (!b.CanReachRoom(r)) continue;
                o.Add(GoalOption.AtPlace(GoalType.Verify, r));
            }
        }

        private static void EnumerateInformants(AgentBrain b, float now, List<GoalOption> o)
        {
            if (!b.Cognition.InformationSeeking || b.AtSafety || b.Self.Uncertainty < 0.35f) return;
            foreach (var id in b.Perception.VisibleAgents)
            {
                if (b.DistanceToBelieved(id, now) > 12f) continue;
                if (b.Beliefs.Weighted(FactKey.Injured(id), now) > 0.5f) continue;
                o.Add(GoalOption.WithTarget(GoalType.AskForInfo, id));
            }
        }

        private static void EnumerateFires(AgentBrain b, float now, List<GoalOption> o)
        {
            if (b.AtSafety) return;
            BeliefScratch.Clear();
            b.Beliefs.Collect(FactType.HazardInRoom, BeliefScratch);
            foreach (var belief in BeliefScratch)
            {
                int room = belief.Key.Subject;
                if (b.FloorPlan.IsExterior(room)) continue;
                if (belief.Value < 0.05f || belief.EffectiveConfidence(now) < 0.3f) continue;
                if (Vector3.Distance(b.Body.Position, belief.Position) > 30f) continue;
                o.Add(GoalOption.AtPlace(GoalType.FightFire, room));
            }
        }
    }

    /// <summary>
    /// Utility AI over goal options, with compensation, personality multipliers, need urgency, long-term bias,
    /// inertia and emotion masking. Produces a full DecisionTrace every time.
    /// </summary>
    public sealed class GoalSelector
    {
        private readonly AgentBrain _b;
        private readonly List<GoalDefinition> _definitions;
        private readonly Dictionary<GoalType, GoalDefinition> _byType = new Dictionary<GoalType, GoalDefinition>();
        private readonly Dictionary<GoalOption, float> _cooldownUntil = new Dictionary<GoalOption, float>();
        private readonly Dictionary<GoalOption, int> _failures = new Dictionary<GoalOption, int>();
        private readonly Dictionary<GoalOption, float> _abandonedAt = new Dictionary<GoalOption, float>();

        /// <summary>Goals dropped for another option are dampened for a few seconds to prevent dithering.</summary>
        public float AbandonDampingSeconds = 8f;
        public float AbandonDamping = 0.6f;
        private readonly List<GoalOption> _candidates = new List<GoalOption>();
        private readonly List<float> _scores = new List<float>();
        private readonly List<float> _consScores = new List<float>();
        private readonly List<float> _attrScratch = new List<float>();

        public float SelectionMargin = 0.15f;
        public float InertiaBonus = 0.25f;
        /// <summary>Below this score an option is noise, not a reason to act.</summary>
        public float MinimumScore = 0.03f;

        public GoalOption Current { get; private set; } = GoalOption.Of(GoalType.Routine);
        public bool HasGoal { get; private set; }
        public float CurrentSince { get; private set; }
        public float CurrentScore { get; private set; }

        public GoalSelector(AgentBrain brain, List<GoalDefinition> definitions)
        {
            _b = brain;
            _definitions = definitions;
            foreach (var d in definitions) _byType[d.Type] = d;
        }

        public IReadOnlyList<GoalDefinition> Definitions => _definitions;

        public DecisionTrace Decide(float now, string trigger)
        {
            var trace = new DecisionTrace { Time = now, Emotion = _b.Emotion.Current, Trigger = trigger };
            var mods = _b.Emotion.Modifiers;
            _scores.Clear();

            foreach (var def in _definitions)
            {
                // Waiting outside is always allowed, whatever the emotion.
                if (mods.Masks(def.Type) && !(def.Type == GoalType.Routine && _b.AtSafety)) continue;
                _candidates.Clear();
                def.Enumerate(_b, now, _candidates);
                foreach (var opt in _candidates)
                {
                    float until;
                    if (_cooldownUntil.TryGetValue(opt, out until) && now < until) continue;

                    var ctx = new DecisionContext { Brain = _b, Option = opt, Now = now };
                    var ot = new OptionTrace { Option = opt, Label = opt.Label(_b) };
                    _consScores.Clear();
                    foreach (var c in def.Considerations)
                    {
                        float raw;
                        float s = c.Evaluate(ctx, out raw);
                        _consScores.Add(s);
                        ot.Considerations.Add(new ConsiderationTrace { Name = c.Name, Input = raw, Score = s });
                    }
                    ot.Combined = UtilityMath.Combine(_consScores);
                    ot.PersonalityMultiplier = def.PersonalityMultiplier(_b);
                    ot.OptionMultiplier = def.OptionMultiplier != null ? def.OptionMultiplier(_b, opt) : 1f;
                    ot.NeedUrgency = NeedUrgency(def);
                    ot.LongTermBias = _b.LongTermBias(opt, now);
                    ot.InertiaBonus = HasGoal && opt.Equals(Current) ? 1f + InertiaBonus * Mathf.Clamp01(1f - (now - CurrentSince) / 30f) + 0.05f : 1f;
                    int fails;
                    _failures.TryGetValue(opt, out fails);
                    ot.FailurePenalty = 1f / (1f + 0.5f * fails);
                    float abandoned;
                    if (_abandonedAt.TryGetValue(opt, out abandoned) && now - abandoned < AbandonDampingSeconds) ot.FailurePenalty *= AbandonDamping;
                    ot.FinalScore = def.BaseWeight * ot.Combined * ot.PersonalityMultiplier * ot.OptionMultiplier * ot.NeedUrgency * ot.LongTermBias * ot.InertiaBonus * ot.FailurePenalty;
                    Attribution.Explain(ot, def.BaseWeight, _attrScratch);
                    trace.Options.Add(ot);
                    _scores.Add(ot.FinalScore);
                }
            }

            int chosen = UtilitySelector.Select(_scores, mods.SelectionTemperature, SelectionMargin, _b.Rng);
            if (chosen >= 0 && _scores[chosen] < MinimumScore) chosen = -1;
            GoalOption next;
            if (chosen < 0)
            {
                next = GoalOption.Of(GoalType.Routine);
                trace.ChosenReason = "no option scored above the minimum: fallback to routine";
            }
            else
            {
                next = trace.Options[chosen].Option;
                trace.ChosenIndex = chosen;
                int best = 0;
                for (int i = 1; i < _scores.Count; i++) if (_scores[i] > _scores[best]) best = i;
                trace.ChosenReason = chosen == best ? "highest score" : "within margin of best, picked by softmax (T=" + mods.SelectionTemperature.ToString("0.00") + ")";
                CurrentScore = _scores[chosen];
            }

            float top = 0f, second = 0f;
            foreach (var sc in _scores) { if (sc > top) { second = top; top = sc; } else if (sc > second) second = sc; }
            trace.DecisionConfidence = top <= 0f ? 0f : Mathf.Clamp01(0.5f * (top - second) / top + 0.5f * Mathf.Min(1f, top));
            trace.GoalChanged = !HasGoal || !next.Equals(Current);
            var self = _b.Self;
            trace.KnowledgeConfidence = self.KnowledgeConfidence;
            trace.PlanConfidence = self.PlanConfidence;
            trace.SocialConfidence = self.SocialConfidence;
            trace.Uncertainty = self.Uncertainty;
            trace.LastPredictionError = _b.Learning.LastPredictionError;
            trace.Dispositions = _b.Learning.Dispositions.ToString();
            if (trace.Chosen != null)
            {
                trace.Reason = Attribution.Reason(_b, trace.Chosen);
                var key = Attribution.StrategyFor(_b, trace.Chosen.Option);
                if (key.HasValue)
                {
                    trace.PredictedSuccess = _b.Learning.Predict(key.Value).Expected;
                    trace.PredictedStrategy = key.Value.Describe(_b);
                }
            }
            if (trace.GoalChanged)
            {
                if (HasGoal) _abandonedAt[Current] = now;
                Current = next;
                CurrentSince = now;
                HasGoal = true;
            }
            return trace;
        }

        private float NeedUrgency(GoalDefinition def)
        {
            if (def.Needs == null || def.Needs.Length == 0) return 1f;
            float max = 0f;
            foreach (var n in def.Needs) max = Mathf.Max(max, _b.Needs[n]);
            return 0.25f + 0.75f * max;
        }

        public void OnAchieved(float now)
        {
            GoalDefinition def;
            if (_byType.TryGetValue(Current.Type, out def) && def.CooldownOnSuccess > 0f)
                _cooldownUntil[Current] = now + def.CooldownOnSuccess;
            _failures.Remove(Current);
            HasGoal = false;
        }

        /// <summary>Returns true if the goal has failed repeatedly and was put on cooldown.</summary>
        public bool OnFailed(float now, string reason)
        {
            int fails;
            _failures.TryGetValue(Current, out fails);
            fails++;
            if (fails >= 3)
            {
                _failures.Remove(Current);
                _cooldownUntil[Current] = now + 8f;
                HasGoal = false;
                return true;
            }
            _failures[Current] = fails;
            return false;
        }

        public void SetCooldown(GoalOption option, float until) { _cooldownUntil[option] = until; }

        public void ForceReevaluate() { HasGoal = false; }
    }
}
