using Cascade.Agents;
using UnityEngine;

namespace Cascade.Simulation.Headless
{
    /// <summary>Kinematic body for headless runs: moves in straight lines. Routes go door to door and rooms are convex, so no navmesh is needed.</summary>
    public sealed class HeadlessBody : IAgentBody
    {
        public Vector3 Position { get; set; }
        public Vector3 Forward { get; private set; } = Vector3.forward;
        public float BaseSpeed = 3.2f;
        public Posture Posture { get; private set; }
        private Vector3 _target;
        private bool _moving;
        private float _speedMul = 1f;

        public HeadlessBody(Vector3 position) { Position = position; _target = position; }

        public void MoveTo(Vector3 destination, float speedMultiplier)
        {
            _target = new Vector3(destination.x, 0f, destination.z);
            _speedMul = speedMultiplier;
            _moving = true;
        }

        public void Stop() { _moving = false; _target = Position; }

        public bool HasArrived(float tolerance) => Vector3.Distance(Position, _target) <= tolerance;

        public void FaceTowards(Vector3 point)
        {
            var d = point - Position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.0001f) Forward = d.normalized;
        }

        public void SetPosture(Posture posture) { Posture = posture; }

        public void Teleport(Vector3 position) { Position = new Vector3(position.x, 0f, position.z); }

        public void Step(float dt)
        {
            if (!_moving || Posture == Posture.Down) return;
            float speed = BaseSpeed * _speedMul * (Posture == Posture.Crouching ? 0.5f : 1f);
            var next = Vector3.MoveTowards(Position, _target, speed * dt);
            var delta = next - Position;
            if (delta.sqrMagnitude > 0.000001f) Forward = delta.normalized;
            Position = next;
            if (Vector3.Distance(Position, _target) < 0.01f) _moving = false;
        }
    }

    /// <summary>Line of sight from the room graph: same room, or through a door that is not closed.</summary>
    public sealed class RoomGraphLineOfSight : ILineOfSight
    {
        private readonly World.RoomGraph _graph;
        public RoomGraphLineOfSight(World.RoomGraph graph) { _graph = graph; }

        public bool Check(Vector3 from, Vector3 to)
        {
            int a = _graph.RoomAt(from), b = _graph.RoomAt(to);
            if (a == b) return true;
            foreach (var d in _graph.Doors)
            {
                if (!d.Connects(a) || !d.Connects(b)) continue;
                if (d.State == World.DoorState.Closed || d.State == World.DoorState.Locked) continue;
                if (DistanceToSegment(d.Position, from, to) < 1.0f) return true;
            }
            // Also true when looking AT a door from inside the room (perception checks door positions).
            foreach (var d in _graph.Doors)
                if (d.Connects(a) && Vector3.Distance(new Vector3(to.x, 0, to.z), d.Position) < 0.8f) return true;
            return false;
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = a.y = b.y = 0f;
            var ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }
    }
}
