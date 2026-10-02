using Cascade.Agents;
using UnityEngine;
using UnityEngine.AI;

namespace Cascade.Runtime
{
    /// <summary>
    /// Thin adapter between an AgentBrain and Unity (principle P2): NavMesh movement, posture and visuals.
    /// Holds no decision logic. The brain is ticked by the SimulationWorld scheduler, not by this component.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class NpcAgent : MonoBehaviour, IAgentBody
    {
        public AgentBrain Brain { get; private set; }
        public float BaseSpeed = 3.4f;

        private NavMeshAgent _nav;
        private Renderer _renderer;
        private Material _material;
        private Transform _visual;
        private Posture _posture = Posture.Standing;
        private Vector3 _lastDestination;
        private bool _hasDestination;
        private Vector3 _faceTarget;
        private bool _hasFaceTarget;

        public static NpcAgent Create(string name, Vector3 position, MaterialLibrary materials, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.layer = 2; // Ignore Raycast: agents do not block line of sight

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Body";
            visual.layer = 2;
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = new Vector3(0f, 1f, 0f);
            visual.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Facing";
            nose.layer = 2;
            Object.Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(visual.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.55f, 0.45f);
            nose.transform.localScale = new Vector3(0.3f, 0.12f, 0.3f);
            nose.GetComponent<Renderer>().sharedMaterial = materials.Opaque(new Color(0.15f, 0.15f, 0.15f));

            var nav = go.AddComponent<NavMeshAgent>();
            nav.radius = 0.35f;
            nav.height = 1.8f;
            nav.angularSpeed = 540f;
            nav.acceleration = 14f;
            nav.stoppingDistance = 0.2f;
            nav.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;

            var agent = go.AddComponent<NpcAgent>();
            agent._visual = visual.transform;
            agent._renderer = visual.GetComponent<Renderer>();
            agent._material = materials.Instance(Color.white);
            agent._renderer.sharedMaterial = agent._material;
            return agent;
        }

        private void Awake() { _nav = GetComponent<NavMeshAgent>(); }

        public void Bind(AgentBrain brain) { Brain = brain; }

        public Vector3 Position => transform.position;
        public Vector3 Forward => transform.forward;

        public void MoveTo(Vector3 destination, float speedMultiplier)
        {
            if (_posture == Posture.Down || _nav == null || !_nav.isOnNavMesh) return;
            _nav.speed = BaseSpeed * speedMultiplier * (_posture == Posture.Crouching ? 0.5f : 1f);
            _hasFaceTarget = false;
            if (_hasDestination && (destination - _lastDestination).sqrMagnitude < 0.04f && !_nav.isStopped) return;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(destination, out hit, 2f, NavMesh.AllAreas)) destination = hit.position;
            _nav.isStopped = false;
            _nav.SetDestination(destination);
            _lastDestination = destination;
            _hasDestination = true;
        }

        public void Stop()
        {
            _hasDestination = false;
            if (_nav != null && _nav.isOnNavMesh)
            {
                _nav.isStopped = true;
                _nav.ResetPath();
            }
        }

        public bool HasArrived(float tolerance)
            => _nav == null || !_nav.isOnNavMesh || (!_nav.pathPending && _nav.remainingDistance <= tolerance);

        public void FaceTowards(Vector3 point)
        {
            _faceTarget = point;
            _hasFaceTarget = true;
        }

        public void SetPosture(Posture posture)
        {
            if (_posture == posture) return;
            _posture = posture;
            if (posture == Posture.Down) Stop();
            if (_visual == null) return;
            switch (posture)
            {
                case Posture.Crouching:
                    _visual.localPosition = new Vector3(0f, 0.6f, 0f);
                    _visual.localScale = new Vector3(0.6f, 0.55f, 0.6f);
                    _visual.localRotation = Quaternion.identity;
                    break;
                case Posture.Down:
                    _visual.localPosition = new Vector3(0f, 0.3f, 0f);
                    _visual.localScale = new Vector3(0.6f, 0.9f, 0.6f);
                    _visual.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    break;
                default:
                    _visual.localPosition = new Vector3(0f, 1f, 0f);
                    _visual.localScale = new Vector3(0.6f, 0.9f, 0.6f);
                    _visual.localRotation = Quaternion.identity;
                    break;
            }
        }

        public void Teleport(Vector3 position)
        {
            if (_nav != null && _nav.isOnNavMesh) _nav.Warp(position);
            else transform.position = position;
        }

        private void Update()
        {
            if (Brain == null) return;
            if (_hasFaceTarget && _posture != Posture.Down)
            {
                var dir = _faceTarget - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 360f * Time.deltaTime);
            }
            var color = Palette.ForEmotion(Brain.Emotion.Current);
            if (Brain.IsInjured) color = Color.Lerp(color, Color.black, 0.35f);
            if (Brain.Incapacitated) color = new Color(0.25f, 0.25f, 0.25f);
            _material.color = color;
        }
    }
}
