using System;
using System.Collections.Generic;
using Cascade.AI.BehaviorTrees;
using Cascade.Core;
using UnityEngine;

namespace Cascade.Agents
{
    public struct RouteRequest
    {
        public bool ToExit;
        public int Room;
        public bool HasFinal;
        public Vector3 Final;
        public float FinalTolerance;
    }

    /// <summary>
    /// Follows a room-graph route door by door. Re-validates the remaining doors against beliefs every tick, so a door
    /// learned to be blocked makes the node fail with "PathBlocked" and the brain replans. Replans periodically to
    /// take new danger information into account, and detects being stuck.
    /// </summary>
    public sealed class MoveAlongRoute : BtNode<AgentBrain>
    {
        private readonly Func<AgentBrain, RouteRequest> _request;
        private readonly List<int> _doors = new List<int>();
        private RouteRequest _req;
        private int _index;
        private int _targetRoom;
        private bool _planned;
        private float _replanTimer;
        private float _stuckTime;
        private Vector3 _lastPos;

        public MoveAlongRoute(string name, Func<AgentBrain, RouteRequest> request)
        {
            Name = name;
            _request = request;
        }

        protected override BtStatus OnTick(AgentBrain b, float dt)
        {
            if (!_planned)
            {
                _req = _request(b);
                if (!Plan(b)) { b.Executor.Fail("NoRoute"); return BtStatus.Failure; }
            }

            for (int i = _index; i < _doors.Count; i++)
                if (b.Routes.IsDoorBelievedImpassable(_doors[i])) { b.Executor.Fail("PathBlocked"); return BtStatus.Failure; }

            _replanTimer += dt;
            if (_replanTimer > 3f)
            {
                _replanTimer = 0f;
                if (!Plan(b)) { b.Executor.Fail("NoRoute"); return BtStatus.Failure; }
            }

            var pos = Flat(b.Body.Position);
            Vector3 target;
            if (_index < _doors.Count)
            {
                target = b.FloorPlan.DoorPosition(_doors[_index]);
                if (Vector3.Distance(pos, target) < 0.9f)
                {
                    _index++;
                    return BtStatus.Running;
                }
            }
            else if (_req.HasFinal)
            {
                target = _req.Final;
                if (Vector3.Distance(pos, Flat(target)) <= _req.FinalTolerance) { b.Body.Stop(); return BtStatus.Success; }
            }
            else
            {
                if (b.CurrentRoom == _targetRoom) return BtStatus.Success;
                target = b.FloorPlan.RoomCenter(_targetRoom);
            }

            b.MoveBody(target);

            if ((pos - _lastPos).magnitude < 0.01f) _stuckTime += dt; else _stuckTime = 0f;
            _lastPos = pos;
            if (_stuckTime > 6f) { b.Executor.Fail("Stuck"); return BtStatus.Failure; }
            return BtStatus.Running;
        }

        private bool Plan(AgentBrain b)
        {
            _planned = true;
            _index = 0;
            float cost;
            if (_req.ToExit)
            {
                int target;
                if (!b.Routes.FindExitRoute(_doors, out cost, out target)) return false;
                if (_targetRoom != target || !_req.HasFinal)
                {
                    _targetRoom = target;
                    var plan = b.FloorPlan;
                    Vector3 doorPos = _doors.Count > 0 ? plan.DoorPosition(_doors[_doors.Count - 1]) : b.Body.Position;
                    var outward = (plan.RoomCenter(target) - doorPos).normalized;
                    var jitter = new Vector3(b.Rng.Range(-2f, 2f), 0f, b.Rng.Range(-2f, 2f));
                    _req.Final = plan.ClampToRoom(target, doorPos + outward * 5f + jitter, 0.8f);
                    _req.HasFinal = true;
                    _req.FinalTolerance = 1.5f;
                }
            }
            else
            {
                _targetRoom = _req.Room;
                if (_targetRoom < 0) return false;
                if (_targetRoom == b.CurrentRoom) _doors.Clear();
                else if (!b.Routes.FindRouteTo(_targetRoom, _doors, out cost)) return false;
            }
            b.CurrentRoute.Clear();
            b.CurrentRoute.AddRange(_doors);
            return true;
        }

        public override void Reset()
        {
            _planned = false;
            _index = 0;
            _stuckTime = 0f;
            _replanTimer = 0f;
        }

        public override string Describe()
            => Name + (_index < _doors.Count ? " (door " + (_index + 1) + "/" + _doors.Count + ")" : " (final leg)");

        public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }

    /// <summary>Moves to a point chosen when the node starts. Used inside a room.</summary>
    public sealed class MoveToPoint : BtNode<AgentBrain>
    {
        private readonly Func<AgentBrain, Vector3> _point;
        private readonly float _tolerance;
        private Vector3 _target;
        private bool _started;
        private float _elapsed;

        public MoveToPoint(string name, Func<AgentBrain, Vector3> point, float tolerance)
        {
            Name = name; _point = point; _tolerance = tolerance;
        }

        protected override BtStatus OnTick(AgentBrain b, float dt)
        {
            if (!_started) { _started = true; _target = _point(b); _elapsed = 0f; }
            _elapsed += dt;
            if (Vector3.Distance(MoveAlongRoute.Flat(b.Body.Position), MoveAlongRoute.Flat(_target)) <= _tolerance)
            {
                b.Body.Stop();
                _started = false;
                return BtStatus.Success;
            }
            if (_elapsed > 20f) { b.Executor.Fail("Stuck"); _started = false; return BtStatus.Failure; }
            b.MoveBody(_target);
            return BtStatus.Running;
        }

        public override void Reset() { _started = false; }
    }

    /// <summary>
    /// Approaches another agent using only the BELIEVED position. Crosses rooms with a route when the target is
    /// believed elsewhere. In follow mode it never succeeds and keeps the agent near the leader.
    /// </summary>
    public sealed class ApproachAgent : BtNode<AgentBrain>
    {
        private readonly Func<AgentBrain, EntityId> _target;
        private readonly float _tolerance;
        private readonly bool _follow;
        private MoveAlongRoute _sub;
        private int _subRoom = -1;
        private float _unseenTime;

        public ApproachAgent(string name, Func<AgentBrain, EntityId> target, float tolerance, bool follow = false)
        {
            Name = name; _target = target; _tolerance = tolerance; _follow = follow;
        }

        protected override BtStatus OnTick(AgentBrain b, float dt)
        {
            var t = _target(b);
            float now = b.Now;
            Vector3 p;
            if (!t.IsValid || !b.Beliefs.TryGetPosition(FactKey.AgentRoom(t), now, 0.1f, out p))
            {
                b.Executor.Fail("TargetLost");
                return BtStatus.Failure;
            }

            bool visible = b.Perception.CanSee(t);
            _unseenTime = visible ? 0f : _unseenTime + dt;
            float dist = Vector3.Distance(MoveAlongRoute.Flat(b.Body.Position), MoveAlongRoute.Flat(p));
            if (!visible && dist < 1.5f) _unseenTime += dt; // at the remembered spot and nobody is there
            if (_unseenTime > (_follow ? 8f : 10f))
            {
                b.Executor.Fail("TargetLost");
                return BtStatus.Failure;
            }

            if (visible && dist <= _tolerance)
            {
                b.Body.Stop();
                b.Body.FaceTowards(p);
                return _follow ? BtStatus.Running : BtStatus.Success;
            }

            int theirRoom = (int)b.Beliefs.Value(FactKey.AgentRoom(t), now, -1f, 0.1f);
            if (!visible && theirRoom >= 0 && theirRoom != b.CurrentRoom)
            {
                if (_sub == null || _subRoom != theirRoom)
                {
                    int room = theirRoom;
                    _sub = new MoveAlongRoute("Route to " + b.NameOf(t), bb => new RouteRequest { Room = room });
                    _subRoom = theirRoom;
                }
                var s = _sub.Tick(b, dt);
                if (s == BtStatus.Failure) return BtStatus.Failure;
                if (s == BtStatus.Success) { _sub = null; _subRoom = -1; }
                return BtStatus.Running;
            }

            b.MoveBody(p);
            return BtStatus.Running;
        }

        public override void Reset()
        {
            _sub = null;
            _subRoom = -1;
            _unseenTime = 0f;
        }
    }

    /// <summary>Turns in place to scan the surroundings (lets perception cover the whole room).</summary>
    public sealed class LookAround : BtNode<AgentBrain>
    {
        private readonly float _duration;
        private float _elapsed;
        private float _nextTurn;

        public LookAround(float seconds) { _duration = seconds; Name = "Look around"; }

        protected override BtStatus OnTick(AgentBrain b, float dt)
        {
            if (_elapsed == 0f) b.Body.Stop();
            _elapsed += dt;
            if (_elapsed >= _nextTurn)
            {
                _nextTurn = _elapsed + 0.6f;
                float angle = b.Rng.Range(0f, Mathf.PI * 2f);
                b.Body.FaceTowards(b.Body.Position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 3f);
            }
            if (_elapsed < _duration) return BtStatus.Running;
            Reset();
            return BtStatus.Success;
        }

        public override void Reset() { _elapsed = 0f; _nextTurn = 0f; }
    }

    /// <summary>
    /// Visits rooms, preferring rarely visited and not-believed-dangerous ones, until a condition holds.
    /// Exploration choices depend on memory (visit counts, remembered danger).
    /// </summary>
    public sealed class Explore : BtNode<AgentBrain>
    {
        private readonly Func<AgentBrain, bool> _done;
        private readonly Func<AgentBrain, int> _preferred;
        private readonly int _maxRooms;
        private readonly List<int> _scratch = new List<int>();
        private readonly HashSet<int> _tried = new HashSet<int>();
        private MoveAlongRoute _move;
        private LookAround _look;
        private int _targetRoom = -1;
        private int _visited;
        private bool _looking;

        public Explore(string name, Func<AgentBrain, bool> done, int maxRooms, Func<AgentBrain, int> preferred = null)
        {
            Name = name; _done = done; _maxRooms = maxRooms; _preferred = preferred;
        }

        protected override BtStatus OnTick(AgentBrain b, float dt)
        {
            if (_done(b)) return BtStatus.Success;
            if (_move == null)
            {
                _targetRoom = PickRoom(b);
                if (_targetRoom < 0) { b.Executor.Fail("NothingLeftToExplore"); return BtStatus.Failure; }
                _tried.Add(_targetRoom);
                int room = _targetRoom;
                _move = new MoveAlongRoute("Explore " + b.FloorPlan.RoomName(room), bb => new RouteRequest { Room = room });
                _look = new LookAround(1.2f);
                _looking = false;
            }

            if (!_looking)
            {
                var s = _move.Tick(b, dt);
                if (s == BtStatus.Running) return BtStatus.Running;
                if (s == BtStatus.Success) { _looking = true; return BtStatus.Running; }
                _move = null; // unreachable room: try another
                return ++_visited >= _maxRooms ? Fail(b) : BtStatus.Running;
            }

            if (_look.Tick(b, dt) == BtStatus.Running) return BtStatus.Running;
            _move = null;
            return ++_visited >= _maxRooms ? Fail(b) : BtStatus.Running;
        }

        private BtStatus Fail(AgentBrain b)
        {
            b.Executor.Fail("ExploredWithoutSuccess");
            return BtStatus.Failure;
        }

        private int PickRoom(AgentBrain b)
        {
            float now = b.Now;
            if (_preferred != null)
            {
                int p = _preferred(b);
                if (p >= 0 && p != b.CurrentRoom && !_tried.Contains(p)) return p;
            }
            int best = -1;
            float bestScore = float.MaxValue;
            for (int r = 0; r < b.FloorPlan.RoomCount; r++)
            {
                if (r == b.CurrentRoom || _tried.Contains(r) || b.FloorPlan.IsExterior(r)) continue;
                float cost;
                if (!b.Routes.FindRouteTo(r, _scratch, out cost)) continue;
                float score = b.Memory.Visits(r) * 12f + cost * 0.15f + b.DangerAt(r, now) * 60f;
                if (score < bestScore) { bestScore = score; best = r; }
            }
            return best;
        }

        public override void Reset()
        {
            _move = null;
            _looking = false;
            _visited = 0;
            _tried.Clear();
        }

        public override string Describe() => _move != null ? _move.Describe() : Name;
    }
}
