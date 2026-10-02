using System;
using System.Collections.Generic;
using Cascade.AI.BehaviorTrees;
using Cascade.AI.Planning;

namespace Cascade.Agents
{
    /// <summary>Symbols of the planning state. Built fresh from beliefs before every planning call.</summary>
    public enum Symbol
    {
        AtSafeZone, KnowsExitRoute, AtTarget, KnowsTargetLocation, TargetAssisted, TargetWarned, HasExtinguisher,
        KnowsExtinguisher, NearFire, FireSuppressed, IsFollowing, IsHidden, AtInvestigatePlace, GroupGathered, RoutineDone, InformationObtained
    }

    /// <summary>
    /// A plannable action (GOAP preconditions/effects + belief-based cost) whose execution is a small behavior tree.
    /// </summary>
    public sealed class AgentAction : IPlanAction<AgentBrain>
    {
        public string Name { get; }
        public Condition Preconditions { get; private set; } = Condition.None;
        public ulong SetBits { get; private set; }
        public ulong ClearBits { get; private set; }

        public Func<AgentBrain, bool> Applicable = b => true;
        public Func<AgentBrain, float> CostFn = b => 1f;
        public Func<AgentBrain, BtNode<AgentBrain>> Behavior;
        /// <summary>Called when the action ends for any reason (success, failure, abort). Used to release resources.</summary>
        public Action<AgentBrain> OnEnd;
        /// <summary>The learnable strategy this action tries (if any). Evaluated once when the action starts.</summary>
        public Func<AgentBrain, StrategyKey?> Strategy;
        /// <summary>When true, success is recorded elsewhere (e.g. reaching safety) because finishing the action is not the real outcome.</summary>
        public bool SuccessRecordedExternally;
        /// <summary>Outcome value on failure, per failure reason. Default 0. Negative = not the strategy's fault, do not learn.</summary>
        public Func<string, float> FailureOutcome = reason => 0f;

        public AgentAction(string name) { Name = name; }

        public AgentAction Requires(Symbol s, bool value = true)
        {
            Preconditions = Preconditions.And((int)s, value);
            return this;
        }

        public AgentAction Achieves(Symbol s)
        {
            SetBits |= 1UL << (int)s;
            return this;
        }

        public bool IsApplicable(AgentBrain context) => Applicable(context);
        public float Cost(AgentBrain context) => CostFn(context);
        public override string ToString() => Name;
    }

    /// <summary>Wraps the generic GOAP planner with agent-specific state building and goal conditions.</summary>
    public sealed class AgentPlanner
    {
        private readonly GoapPlanner<AgentBrain> _goap = new GoapPlanner<AgentBrain>();
        private readonly List<IPlanAction<AgentBrain>> _actions = new List<IPlanAction<AgentBrain>>();
        private readonly List<IPlanAction<AgentBrain>> _raw = new List<IPlanAction<AgentBrain>>();
        private readonly List<AgentAction> _result = new List<AgentAction>();

        public int BaseBudget = 200;
        public ulong LastStartState { get; private set; }
        public int LastExpansions => _goap.LastExpansions;
        public IReadOnlyList<IPlanAction<AgentBrain>> Actions => _actions;

        public AgentPlanner(IEnumerable<AgentAction> actions)
        {
            foreach (var a in actions) _actions.Add(a);
        }

        public static Condition GoalCondition(GoalType type)
        {
            switch (type)
            {
                case GoalType.Evacuate: return Condition.Require((int)Symbol.AtSafeZone);
                case GoalType.HelpOther: return Condition.Require((int)Symbol.TargetAssisted);
                case GoalType.SearchFor: return Condition.Require((int)Symbol.TargetAssisted);
                case GoalType.Follow: return Condition.Require((int)Symbol.IsFollowing);
                case GoalType.Investigate: return Condition.Require((int)Symbol.AtInvestigatePlace);
                case GoalType.WarnOther: return Condition.Require((int)Symbol.TargetWarned);
                case GoalType.GatherGroup: return Condition.Require((int)Symbol.GroupGathered);
                case GoalType.FightFire: return Condition.Require((int)Symbol.FireSuppressed);
                case GoalType.Hide: return Condition.Require((int)Symbol.IsHidden);
                case GoalType.Verify: return Condition.Require((int)Symbol.AtInvestigatePlace);
                case GoalType.AskForInfo: return Condition.Require((int)Symbol.InformationObtained);
                default: return Condition.Require((int)Symbol.RoutineDone);
            }
        }

        public ulong BuildState(AgentBrain b)
        {
            float now = b.Now;
            ulong s = 0;
            if (b.AtSafety) s |= Bit(Symbol.AtSafeZone);
            if (b.Routes.HasKnownExitRoute(now)) s |= Bit(Symbol.KnowsExitRoute);
            var goal = b.Goals.Current;
            if (goal.Target.IsValid)
            {
                if (b.Beliefs.Confidence(FactKey.AgentRoom(goal.Target), now) > 0.3f) s |= Bit(Symbol.KnowsTargetLocation);
                if (b.Perception.CanSee(goal.Target) && b.DistanceToBelieved(goal.Target, now) < 2.2f) s |= Bit(Symbol.AtTarget);
            }
            if (b.HeldExtinguisher >= 0) s |= Bit(Symbol.HasExtinguisher);
            if (b.HasKnownExtinguisher(now)) s |= Bit(Symbol.KnowsExtinguisher);
            if (goal.Place >= 0)
            {
                UnityEngine.Vector3 fire;
                if (b.Beliefs.TryGetPosition(FactKey.Hazard(goal.Place), now, 0.2f, out fire) && UnityEngine.Vector3.Distance(b.Body.Position, fire) < 3.5f)
                    s |= Bit(Symbol.NearFire);
            }
            return s;
        }

        public List<AgentAction> Plan(AgentBrain b, GoalOption goal, out bool complete)
        {
            LastStartState = BuildState(b);
            int budget = Math.Max(20, (int)(BaseBudget * b.Emotion.Modifiers.PlanningBudgetMul));
            complete = _goap.Plan(LastStartState, GoalCondition(goal.Type), _actions, b, budget, _raw);
            _result.Clear();
            foreach (var a in _raw) _result.Add((AgentAction)a);
            // Panic shortens the planning horizon: act on the next step or two, then reconsider.
            int maxSteps = b.Emotion.Current == EmotionState.Panicked ? 2 : 8;
            if (_result.Count > maxSteps)
            {
                _result.RemoveRange(maxSteps, _result.Count - maxSteps);
                complete = false;
            }
            return _result;
        }

        public static ulong Bit(Symbol s) => 1UL << (int)s;
    }

    public enum ExecResult { Idle, Running, StepSucceeded, PlanSucceeded, PartialPlanEnded, Failed }

    /// <summary>Runs the current plan one behavior tree at a time and reports structured failure reasons.</summary>
    public sealed class ActionExecutor
    {
        private readonly List<AgentAction> _plan = new List<AgentAction>();
        private int _index;
        private BtNode<AgentBrain> _tree;
        private bool _complete;

        public string FailureReason { get; private set; }
        public StrategyKey? ActiveStrategy { get; private set; }
        public string LastFailedAction { get; private set; }
        public AgentAction CurrentAction => _index < _plan.Count ? _plan[_index] : null;
        public bool HasPlan => _index < _plan.Count;
        public IReadOnlyList<AgentAction> Plan => _plan;
        public int StepIndex => _index;
        public bool PlanIsComplete => _complete;

        public void Fail(string reason)
        {
            if (FailureReason == null) FailureReason = reason;
        }

        public void SetPlan(List<AgentAction> plan, bool complete)
        {
            _plan.Clear();
            _plan.AddRange(plan);
            _index = 0;
            _tree = null;
            _complete = complete;
            FailureReason = null;
        }

        public void Abort(AgentBrain b)
        {
            var current = CurrentAction;
            if (current != null && _tree != null && current.OnEnd != null) current.OnEnd(b);
            if (ActiveStrategy.HasValue && !(current != null && current.SuccessRecordedExternally)) b.Learning.CancelExpectation(ActiveStrategy.Value);
            ActiveStrategy = null;
            _plan.Clear();
            _index = 0;
            _tree = null;
        }

        public ExecResult Tick(AgentBrain b, float dt)
        {
            if (!HasPlan) return ExecResult.Idle;
            var action = _plan[_index];
            if (_tree == null)
            {
                FailureReason = null;
                ActiveStrategy = action.Strategy != null ? action.Strategy(b) : null;
                if (ActiveStrategy.HasValue) b.Learning.Expect(ActiveStrategy.Value);
                _tree = action.Behavior(b);
            }
            var status = _tree.Tick(b, dt);
            if (status == BtStatus.Running) return ExecResult.Running;

            if (action.OnEnd != null) action.OnEnd(b);
            _tree = null;
            var strategy = ActiveStrategy;
            ActiveStrategy = null;
            if (status == BtStatus.Failure)
            {
                float failOutcome = strategy.HasValue ? action.FailureOutcome(FailureReason ?? "Failed") : -1f;
                if (failOutcome >= 0f) b.Learning.Outcome(strategy.Value, failOutcome, ExperienceSource.Direct, FailureReason ?? "failed");
                else if (strategy.HasValue) b.Learning.CancelExpectation(strategy.Value);
                if (FailureReason == null) FailureReason = "Failed";
                LastFailedAction = action.Name;
                _plan.Clear();
                _index = 0;
                return ExecResult.Failed;
            }
            if (strategy.HasValue && !action.SuccessRecordedExternally) b.Learning.Outcome(strategy.Value, 1f, ExperienceSource.Direct, "worked");
            _index++;
            if (_index < _plan.Count) return ExecResult.StepSucceeded;
            _plan.Clear();
            _index = 0;
            return _complete ? ExecResult.PlanSucceeded : ExecResult.PartialPlanEnded;
        }

        public string Describe()
        {
            var a = CurrentAction;
            if (a == null) return "idle";
            return a.Name + (_tree != null ? " > " + _tree.Describe() : "");
        }
    }
}
