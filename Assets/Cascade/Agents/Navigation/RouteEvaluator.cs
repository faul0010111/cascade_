using System.Collections.Generic;
using Cascade.World;
using UnityEngine;

namespace Cascade.Agents
{
    /// <summary>
    /// Strategic routing on the room graph with PER-AGENT edge costs from beliefs and memory. NavMesh only moves the
    /// body between consecutive doors. Two agents with the same goal can pick different routes because they believe
    /// and remember different things.
    /// </summary>
    public sealed class RouteEvaluator
    {
        private readonly AgentBrain _b;
        private readonly List<int> _scratch = new List<int>();
        private float _cacheTime = -1f;
        private int _cacheRoom = -2;
        private bool _cachedHasExit;
        private float _cachedExitCost;

        public float DangerCostScale = 40f;
        private readonly List<int> _exitScratch = new List<int>();

        public RouteEvaluator(AgentBrain brain) { _b = brain; }

        private float Now => _b.Now;

        public bool IsDoorBelievedImpassable(int door)
        {
            Belief db;
            if (!_b.Beliefs.TryGet(FactKey.Door(door), out db)) return false;
            if (db.EffectiveConfidence(Now) < 0.3f) return false;
            return !DoorStates.IsPassable((DoorState)(int)db.Value);
        }

        public float EdgeCost(int door, int from, int to)
        {
            var plan = _b.FloorPlan;
            if (IsDoorBelievedImpassable(door)) return float.PositiveInfinity;
            // An exit you do not know about is just a wall to you.
            if (plan.IsExitDoor(door) && _b.Beliefs.Value(FactKey.Exit(door), Now) < 0.5f) return float.PositiveInfinity;

            var doorPos = plan.DoorPosition(door);
            // The first leg starts where the agent actually stands, not at the room centre.
            var start = from == _b.CurrentRoom ? MoveAlongRoute.Flat(_b.Body.Position) : plan.RoomCenter(from);
            float dist = Vector3.Distance(start, doorPos) + Vector3.Distance(doorPos, plan.RoomCenter(to));
            if (plan.IsExterior(to)) dist *= 0.5f;
            float danger = _b.DangerAt(to, Now);
            float aversion = Mathf.Max(0.2f, 1.6f - _b.Personality[Trait.RiskTolerance] - 0.4f * _b.Personality[Trait.Courage] + _b.Learning.Dispositions.RiskPerception);
            float cost = dist + danger * DangerCostScale * aversion;
            // Doubt is not ignored: if someone credible says this door is impassable, the route costs more in
            // proportion to how credible that is (cautious agents may detour, verify, or still risk it).
            Belief db;
            if (_b.Beliefs.TryGet(FactKey.Door(door), out db) && db.HasAlternative && !DoorStates.IsPassable((DoorState)(int)db.AltValue))
                cost += db.AltConfidence * 60f * aversion;
            // Learned: exits that failed me (in this kind of situation) look worse; exits that worked look better.
            if (plan.IsExitDoor(door) && _b.Cognition.LearnedRouteCosts) cost += (1f - _b.Learning.Predict(StrategyKey.Exit(door)).Expected) * _b.Cognition.LearnedExitWeight;
            return cost;
        }

        public bool FindExitRoute(List<int> doors, out float cost, out int targetRoom)
        {
            var plan = _b.FloorPlan;
            return RoomGraph.FindPath(plan, _b.CurrentRoom, plan.IsExterior, EdgeCost, doors, out cost, out targetRoom);
        }

        public bool FindRouteTo(int room, List<int> doors, out float cost)
        {
            int target;
            return RoomGraph.FindPath(_b.FloorPlan, _b.CurrentRoom, r => r == room, EdgeCost, doors, out cost, out target);
        }

        public bool HasKnownExitRoute(float now)
        {
            Refresh(now);
            return _cachedHasExit;
        }

        public float ExitRouteCost(float now)
        {
            Refresh(now);
            return _cachedExitCost;
        }

        /// <summary>0..1: how confident the agent is that it knows a reasonable way out.</summary>
        public float ExitRouteConfidence(float now)
        {
            Refresh(now);
            if (!_cachedHasExit) return 0f;
            return Mathf.Clamp01(1.2f - _cachedExitCost / 150f);
        }

        public void Invalidate() { _cacheTime = -1f; }

        /// <summary>The exit door the agent would currently use, or -1.</summary>
        public int PlannedExitDoor(float now)
        {
            float cost; int target;
            if (!FindExitRoute(_exitScratch, out cost, out target) || _exitScratch.Count == 0) return -1;
            return _exitScratch[_exitScratch.Count - 1];
        }

        private void Refresh(float now)
        {
            if (Mathf.Abs(now - _cacheTime) < 0.25f && _cacheRoom == _b.CurrentRoom) return;
            _cacheTime = now;
            _cacheRoom = _b.CurrentRoom;
            if (_b.AtSafety) { _cachedHasExit = true; _cachedExitCost = 0f; return; }
            int target;
            _cachedHasExit = FindExitRoute(_scratch, out _cachedExitCost, out target);
        }
    }
}
