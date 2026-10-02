using System.Collections.Generic;
using Cascade.AI.BehaviorTrees;
using Cascade.Core;
using UnityEngine;

namespace Cascade.Agents
{
    /// <summary>
    /// The default action set. Each action: GOAP metadata + belief-based cost + a small behavior tree.
    /// Costs read the agent's own beliefs and personality, so the same goal yields different plans per agent.
    /// </summary>
    public static class ActionLibrary
    {
        public static List<AgentAction> CreateDefault()
        {
            var list = new List<AgentAction>();

            list.Add(new AgentAction("TravelToSafety")
            {
                CostFn = b => 2f + b.Routes.ExitRouteCost(b.Now) / 10f,
                Strategy = b => b.Routes.PlannedExitDoor(b.Now) >= 0 ? StrategyKey.Exit(b.Routes.PlannedExitDoor(b.Now)) : (StrategyKey?)null,
                SuccessRecordedExternally = true,
                Behavior = b => new MoveAlongRoute("Evacuate", bb => new RouteRequest { ToExit = true })
            }.Requires(Symbol.KnowsExitRoute).Achieves(Symbol.AtSafeZone));

            list.Add(new AgentAction("SearchForExit")
            {
                CostFn = b => 8f / Mathf.Max(0.15f, b.Learning.Predict(StrategyKey.Explore).Expected / 0.6f),
                Strategy = b => StrategyKey.Explore,
                Behavior = b => new Explore("Search for an exit", bb => bb.Routes.HasKnownExitRoute(bb.Now), 5)
            }.Achieves(Symbol.KnowsExitRoute));

            list.Add(new AgentAction("AskForDirections")
            {
                Applicable = b => b.BestPersonToAsk(b.Now).IsValid,
                CostFn = b => (3f + 5f * b.Personality[Trait.Independence]) / Mathf.Max(0.15f, b.Learning.Predict(StrategyKey.Ask(b.BestPersonToAsk(b.Now))).Expected / 0.6f),
                Strategy = b => StrategyKey.Ask(b.BestPersonToAsk(b.Now)),
                Behavior = b => AskBehavior(b, b.BestPersonToAsk(b.Now), bb => bb.Routes.HasKnownExitRoute(bb.Now))
            }.Achieves(Symbol.KnowsExitRoute));

            list.Add(new AgentAction("AskTarget")
            {
                Applicable = b => b.Goals.Current.Target.IsValid,
                CostFn = b => 1f + b.DistanceToBelieved(b.Goals.Current.Target, b.Now) / 8f,
                Strategy = b => StrategyKey.Ask(b.Goals.Current.Target),
                Behavior = b => AskBehavior(b, b.Goals.Current.Target, bb => bb.AnsweredSince(bb.Goals.Current.Target, bb.Now - 3.5f))
            }.Achieves(Symbol.InformationObtained));

            list.Add(new AgentAction("GoToTarget")
            {
                CostFn = b => 1f + b.DistanceToBelieved(b.Goals.Current.Target, b.Now) / 8f,
                // Going to someone to help them is the risky part of helping: getting hurt on the way is learned.
                Strategy = b => b.Goals.Current.Type == GoalType.HelpOther || b.Goals.Current.Type == GoalType.SearchFor ? StrategyKey.Help : (StrategyKey?)null,
                FailureOutcome = reason => reason == "TargetLost" ? -1f : 0.3f,
                Behavior = b => new ApproachAgent("Go to " + b.NameOf(b.Goals.Current.Target), bb => bb.Goals.Current.Target, 2f)
            }.Requires(Symbol.KnowsTargetLocation).Achieves(Symbol.AtTarget));

            list.Add(new AgentAction("SearchForTarget")
            {
                Applicable = b => b.Goals.Current.Target.IsValid,
                CostFn = b => 9f,
                Behavior = b => new Explore("Search for " + b.NameOf(b.Goals.Current.Target),
                    bb => bb.Beliefs.Confidence(FactKey.AgentRoom(bb.Goals.Current.Target), bb.Now) > 0.5f, 6,
                    bb => (int)bb.Beliefs.Value(FactKey.AgentRoom(bb.Goals.Current.Target), bb.Now, -1f, 0f))
            }.Achieves(Symbol.KnowsTargetLocation));

            list.Add(new AgentAction("CarryToSafety")
            {
                Applicable = b => b.Beliefs.Weighted(FactKey.Injured(b.Goals.Current.Target), b.Now) > 0.4f && b.TargetLooksIncapacitated(b.Goals.Current.Target),
                CostFn = b => (3f + b.Routes.ExitRouteCost(b.Now) / 6f) / Mathf.Max(0.2f, b.Learning.Predict(StrategyKey.Help).Expected / 0.6f),
                Strategy = b => StrategyKey.Help,
                Behavior = b =>
                {
                    var who = b.Goals.Current.Target;
                    return new Sequence<AgentBrain>("Carry " + b.NameOf(who),
                        new Do<AgentBrain>("Lift", (bb, dt) =>
                        {
                            if (!bb.World.TryStartCarry(bb.Id, who)) { bb.Executor.Fail("CannotLift"); return BtStatus.Failure; }
                            bb.PerformAssist(who);
                            bb.Log.Event(bb.Now, "Carrying " + bb.NameOf(who));
                            return BtStatus.Success;
                        }),
                        new MoveAlongRoute("Carry to safety", bb => new RouteRequest { ToExit = true }));
                },
                OnEnd = b => b.World.StopCarry(b.Id)
            }.Requires(Symbol.AtTarget).Requires(Symbol.KnowsExitRoute).Achieves(Symbol.TargetAssisted));

            list.Add(new AgentAction("AssistTarget")
            {
                Applicable = b => !b.TargetLooksIncapacitated(b.Goals.Current.Target),
                CostFn = b => 1f,
                Behavior = b => new Do<AgentBrain>("Assist", (bb, dt) =>
                {
                    bb.PerformAssist(bb.Goals.Current.Target);
                    return BtStatus.Success;
                })
            }.Requires(Symbol.AtTarget).Achieves(Symbol.TargetAssisted));

            list.Add(new AgentAction("WarnTarget")
            {
                Applicable = b => b.Goals.Current.Target.IsValid,
                CostFn = b => 1f + b.DistanceToBelieved(b.Goals.Current.Target, b.Now) / 10f,
                Behavior = b => new Sequence<AgentBrain>("Warn",
                    new ApproachAgent("Get within earshot", bb => bb.Goals.Current.Target, 5.5f),
                    new Do<AgentBrain>("Shout warning", (bb, dt) =>
                    {
                        bb.PerformWarn(bb.Goals.Current.Target);
                        return BtStatus.Success;
                    }))
            }.Achieves(Symbol.TargetWarned));

            list.Add(new AgentAction("FetchExtinguisher")
            {
                Applicable = b =>
                {
                    int id; Vector3 pos;
                    return b.BestKnownExtinguisher(b.Now, out id, out pos) && b.CanReachRoom(b.FloorPlan.RoomAt(pos));
                },
                CostFn = b =>
                {
                    int id; Vector3 pos;
                    return b.BestKnownExtinguisher(b.Now, out id, out pos) ? 2f + Vector3.Distance(b.Body.Position, pos) / 6f : 99f;
                },
                Behavior = b =>
                {
                    int id; Vector3 pos;
                    b.BestKnownExtinguisher(b.Now, out id, out pos);
                    int room = b.FloorPlan.RoomAt(pos);
                    return new Sequence<AgentBrain>("Fetch extinguisher",
                        new MoveAlongRoute("Go to extinguisher", bb => new RouteRequest { Room = room, HasFinal = true, Final = pos, FinalTolerance = 1.2f }),
                        new Do<AgentBrain>("Take it", (bb, dt) =>
                        {
                            if (bb.World.TryTakeExtinguisher(id, bb.Id))
                            {
                                bb.HeldExtinguisher = id;
                                bb.Beliefs.Observe(FactKey.Extinguisher(id), 0f, 1f, bb.Now, BeliefSource.Perceived, pos);
                                bb.Log.Event(bb.Now, "Took an extinguisher");
                                return BtStatus.Success;
                            }
                            bb.Beliefs.Observe(FactKey.Extinguisher(id), 0f, 1f, bb.Now, BeliefSource.Perceived, pos);
                            bb.Executor.Fail("ItemGone");
                            return BtStatus.Failure;
                        }));
                }
            }.Requires(Symbol.KnowsExtinguisher).Achieves(Symbol.HasExtinguisher));

            list.Add(new AgentAction("GoToFire")
            {
                Applicable = b => b.Goals.Current.Place >= 0 && b.Beliefs.Confidence(FactKey.Hazard(b.Goals.Current.Place), b.Now) > 0.2f
                                  && b.CanReachRoom(b.Goals.Current.Place),
                CostFn = b => 1f + Vector3.Distance(b.Body.Position, b.FloorPlan.RoomCenter(b.Goals.Current.Place)) / 8f,
                Behavior = b => new MoveAlongRoute("Approach the fire", bb =>
                {
                    int room = bb.Goals.Current.Place;
                    Vector3 fire;
                    bb.Beliefs.TryGetPosition(FactKey.Hazard(room), bb.Now, 0f, out fire);
                    var away = (MoveAlongRoute.Flat(bb.Body.Position) - fire).normalized;
                    return new RouteRequest { Room = room, HasFinal = true, Final = bb.FloorPlan.ClampToRoom(room, fire + away * 2.5f, 0.5f), FinalTolerance = 1.2f };
                })
            }.Achieves(Symbol.NearFire));

            list.Add(new AgentAction("ExtinguishFire")
            {
                CostFn = b => 2f / Mathf.Max(0.15f, b.Learning.Predict(StrategyKey.Firefight).Expected / 0.6f),
                Strategy = b => StrategyKey.Firefight,
                Behavior = b =>
                {
                    float elapsed = 0f;
                    return new Do<AgentBrain>("Spray extinguisher", (bb, dt) =>
                    {
                        int room = bb.Goals.Current.Place;
                        Vector3 fire;
                        if (!bb.Beliefs.TryGetPosition(FactKey.Hazard(room), bb.Now, 0f, out fire)) { bb.Executor.Fail("FireNotFound"); return BtStatus.Failure; }
                        bb.Body.Stop();
                        bb.Body.FaceTowards(fire);
                        float remaining = bb.World.UseExtinguisher(bb.HeldExtinguisher, fire, dt);
                        elapsed += dt;
                        if (remaining < 0.05f)
                        {
                            bb.Beliefs.Observe(FactKey.Hazard(room), 0f, 0.9f, bb.Now, BeliefSource.Perceived);
                            bb.Log.Event(bb.Now, "Put out the fire in " + bb.FloorPlan.RoomName(room));
                            bb.Appraise(AppraisalEvent.HelpedSomeone, 1f);
                            return BtStatus.Success;
                        }
                        if (bb.World.ExtinguisherCharge(bb.HeldExtinguisher) <= 0f) { bb.DropExtinguisher(); bb.Executor.Fail("OutOfCharge"); return BtStatus.Failure; }
                        if (elapsed > 20f) { bb.Executor.Fail("FireTooBig"); return BtStatus.Failure; }
                        return BtStatus.Running;
                    });
                }
            }.Requires(Symbol.HasExtinguisher).Requires(Symbol.NearFire).Achieves(Symbol.FireSuppressed));

            list.Add(new AgentAction("FollowLeader")
            {
                Applicable = b => b.Goals.Current.Target.IsValid,
                CostFn = b => 1f,
                Strategy = b => StrategyKey.Follow(b.Goals.Current.Target),
                SuccessRecordedExternally = true,
                FailureOutcome = reason => reason == "TargetLost" ? 0.3f : reason == "CannotFollow" ? -1f : 0f,
                Behavior = b => new Sequence<AgentBrain>("Follow",
                    new Do<AgentBrain>("Join", (bb, dt) =>
                    {
                        var leader = bb.Goals.Current.Target;
                        if (!bb.Groups.TrySetFollow(bb.Id, leader)) { bb.Executor.Fail("CannotFollow"); return BtStatus.Failure; }
                        bb.Appraise(AppraisalEvent.JoinedGroup, 1f);
                        bb.Log.Event(bb.Now, "Following " + bb.NameOf(leader));
                        return BtStatus.Success;
                    }),
                    new ApproachAgent("Stay close", bb => bb.Goals.Current.Target, 2.6f, follow: true)),
                OnEnd = b => b.StopFollowing()
            }.Achieves(Symbol.IsFollowing));

            list.Add(new AgentAction("HideInPlace")
            {
                CostFn = b => 1.5f,
                Strategy = b => StrategyKey.HideAway,
                SuccessRecordedExternally = true,
                Behavior = b => new Sequence<AgentBrain>("Hide",
                    new MoveToPoint("Go to a corner", bb => bb.FindHidingSpot(), 0.8f),
                    new Do<AgentBrain>("Crouch and freeze", (bb, dt) =>
                    {
                        bb.Body.SetPosture(Posture.Crouching);
                        return BtStatus.Running;
                    })),
                OnEnd = b => b.Body.SetPosture(Posture.Standing)
            }.Achieves(Symbol.IsHidden));

            list.Add(new AgentAction("InvestigatePlace")
            {
                Applicable = b => b.Goals.Current.Place >= 0 && b.CanReachRoom(b.Goals.Current.Place),
                CostFn = b => 1f + Vector3.Distance(b.Body.Position, b.FloorPlan.RoomCenter(b.Goals.Current.Place)) / 8f,
                Behavior = b => new Sequence<AgentBrain>(b.Goals.Current.Type == GoalType.Verify ? "Verify" : "Investigate",
                    new MoveAlongRoute("Go and see", bb => new RouteRequest { Room = bb.Goals.Current.Place }),
                    new LookAround(2f),
                    new Do<AgentBrain>("Done looking", (bb, dt) =>
                    {
                        bb.Beliefs.Observe(FactKey.Noise(bb.Goals.Current.Place), 0f, 1f, bb.Now, BeliefSource.Perceived);
                        return BtStatus.Success;
                    }))
            }.Achieves(Symbol.AtInvestigatePlace));

            list.Add(new AgentAction("GatherNearby")
            {
                CostFn = b => 1f,
                Behavior = b => new Sequence<AgentBrain>("Gather people",
                    new Do<AgentBrain>("Call everyone", (bb, dt) =>
                    {
                        bb.PerformGatherCall();
                        return BtStatus.Success;
                    }),
                    new Wait<AgentBrain>(2.5f))
            }.Achieves(Symbol.GroupGathered));

            list.Add(new AgentAction("Routine")
            {
                CostFn = b => 1f,
                Behavior = b => new Sequence<AgentBrain>("Routine",
                    new MoveToPoint("Wander", bb => bb.RandomPointInCurrentRoom(), 0.7f),
                    new Wait<AgentBrain>(b.Rng.Range(2f, 5f)))
            }.Achieves(Symbol.RoutineDone));

            return list;
        }

        /// <summary>Approach, ask, wait for an answer. No answer is an outcome: the person is less worth asking.</summary>
        public static BtNode<AgentBrain> AskBehavior(AgentBrain b, EntityId who, System.Func<AgentBrain, bool> answered)
        {
            float waited = 0f;
            return new Sequence<AgentBrain>("Ask " + b.NameOf(who),
                new ApproachAgent("Approach " + b.NameOf(who), bb => who, 2.5f),
                new Do<AgentBrain>("Ask", (bb, dt) =>
                {
                    bb.Comms.Send(MessageKind.Ask, who, 4f);
                    bb.Log.Event(bb.Now, "Asked " + bb.NameOf(who) + " what they know");
                    return BtStatus.Success;
                }),
                new Do<AgentBrain>("Wait for answer", (bb, dt) =>
                {
                    if (answered(bb)) return BtStatus.Success;
                    waited += dt;
                    if (waited < 3f) return BtStatus.Running;
                    bb.Relationships.Apply(who, SocialEventKind.IgnoredMyRequest, bb.Now);
                    bb.Memory.Record(MemoryKind.IgnoredBy, who, bb.CurrentRoom, bb.Now, -0.4f, 0.5f);
                    bb.Executor.Fail("NoAnswer");
                    return BtStatus.Failure;
                }));
        }
    }
}
