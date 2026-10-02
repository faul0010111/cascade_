using Cascade.Agents;
using Cascade.Core;
using Cascade.Simulation;
using Cascade.World;
using UnityEngine;

namespace Cascade.Runtime
{
    /// <summary>
    /// The player is an ordinary entity to NPCs: they perceive, remember and judge what the player does. The player
    /// has a player-controlled brain (perception + beliefs, no decisions), so "Warn" shares what the player has seen.
    /// Controls: WASD move, E "follow me!", Q warn about what you saw, F help / carry the nearest hurt person,
    /// G push the nearest person.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour, IAgentBody
    {
        public float Speed = 5f;
        public AgentBrain Brain { get; private set; }
        public string LastAction { get; private set; }
        public float LastActionTime { get; private set; } = -99f;

        private CharacterController _controller;
        private SimulationWorld _world;
        private Transform _cameraTransform;
        private EntityId _carrying;

        public static PlayerController Create(Vector3 position, MaterialLibrary materials)
        {
            var go = new GameObject("Player");
            go.layer = 2;
            go.transform.position = position + Vector3.up * 0.05f;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.layer = 2;
            Object.Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            visual.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            visual.GetComponent<Renderer>().sharedMaterial = materials.Opaque(Palette.Player);
            return go.AddComponent<PlayerController>();
        }

        public void Bind(AgentBrain brain, SimulationWorld world, Transform cameraTransform)
        {
            Brain = brain;
            _world = world;
            _cameraTransform = cameraTransform;
            _controller = GetComponent<CharacterController>();
        }

        // IAgentBody: the player's body is driven by input, never by the brain.
        public Vector3 Position => transform.position;
        public Vector3 Forward => transform.forward;
        public void MoveTo(Vector3 destination, float speedMultiplier) { }
        public void Stop() { }
        public bool HasArrived(float tolerance) => true;
        public void FaceTowards(Vector3 point) { }
        public void SetPosture(Posture posture) { }

        public void Teleport(Vector3 position)
        {
            if (_controller != null) _controller.enabled = false;
            transform.position = position;
            if (_controller != null) _controller.enabled = true;
        }

        private void Update()
        {
            if (Brain == null || _world == null) return;
            Move();
            if (CascadeInput.Pressed(InputKey.E)) CallToFollow();
            if (CascadeInput.Pressed(InputKey.Q)) Warn();
            if (CascadeInput.Pressed(InputKey.F)) Help();
            if (CascadeInput.Pressed(InputKey.G)) Push();
        }

        private void Move()
        {
            var input = Vector3.zero;
            if (CascadeInput.Held(InputKey.W)) input.z += 1f;
            if (CascadeInput.Held(InputKey.S)) input.z -= 1f;
            if (CascadeInput.Held(InputKey.D)) input.x += 1f;
            if (CascadeInput.Held(InputKey.A)) input.x -= 1f;
            if (_cameraTransform != null)
            {
                var fwd = _cameraTransform.forward; fwd.y = 0f; fwd.Normalize();
                var right = _cameraTransform.right; right.y = 0f; right.Normalize();
                input = fwd * input.z + right * input.x;
            }
            if (input.sqrMagnitude > 1f) input.Normalize();
            float speed = Speed * (_carrying.IsValid ? 0.55f : 1f) * (CascadeInput.Held(InputKey.LeftShift) ? 1.4f : 1f);
            _controller.SimpleMove(input * speed);
            if (input.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(input);

            // What NPCs can see: are you heading for the nearest exit?
            _world.PlayerLooksEvacuating = input.sqrMagnitude > 0.01f && HeadingToExit(input);
        }

        private bool HeadingToExit(Vector3 direction)
        {
            var g = _world.Building.Graph;
            float best = float.MaxValue;
            Vector3 exit = Vector3.zero;
            foreach (var d in g.Doors)
            {
                if (!g.IsExitDoor(d.Id)) continue;
                float dist = Vector3.Distance(d.Position, transform.position);
                if (dist < best) { best = dist; exit = d.Position; }
            }
            var to = exit - transform.position;
            to.y = 0f;
            return Vector3.Dot(to.normalized, direction.normalized) > 0.6f;
        }

        private void CallToFollow()
        {
            Brain.PerformGatherCall();
            Say("Everyone, follow me!");
        }

        private void Warn()
        {
            var m = Brain.Comms.SendRelevant(MessageKind.Warn, EntityId.None, 12f);
            _world.PublishSound(new SoundEvent { Kind = SoundKind.Shout, Position = transform.position, Loudness = 12f, Source = Brain.Id });
            Say(m.Payload.Count > 0 ? "Watch out! (" + m.Payload.Count + " things I know)" : "Watch out!");
        }

        private void Help()
        {
            if (_carrying.IsValid)
            {
                _world.StopCarry(Brain.Id);
                Say("Put " + _world.Context.Entities.NameOf(_carrying) + " down");
                _carrying = EntityId.None;
                return;
            }
            var target = Nearest(2.2f, a => a.Brain.IsInjured);
            if (target == null) { Say("Nobody hurt nearby"); return; }
            if (target.Brain.Incapacitated && _world.TryStartCarry(Brain.Id, target.Id))
            {
                _carrying = target.Id;
                Say("Carrying " + target.Brain.Name);
            }
            else Say("Helping " + target.Brain.Name);
            Brain.PerformAssist(target.Id);
        }

        private void Push()
        {
            var target = Nearest(1.6f, a => true);
            if (target == null) return;
            _world.PublishSocialAction(new Agents.SocialActionEvent { Actor = Brain.Id, Target = target.Id, Kind = SocialActionKind.Pushed, Position = transform.position });
            var dir = (target.Body.Position - transform.position).normalized;
            target.Body.Teleport(target.Body.Position + dir * 0.8f);
            Say("Pushed " + target.Brain.Name);
        }

        private SimAgent Nearest(float radius, System.Func<SimAgent, bool> filter)
        {
            SimAgent best = null;
            float bestD = radius;
            foreach (var a in _world.Agents)
            {
                if (a.IsPlayer || !filter(a)) continue;
                float d = Vector3.Distance(a.Body.Position, transform.position);
                if (d < bestD) { bestD = d; best = a; }
            }
            return best;
        }

        private void Say(string text)
        {
            LastAction = text;
            LastActionTime = Time.time;
        }
    }
}
