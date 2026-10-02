using Cascade.Core;
using Cascade.World;
using UnityEngine;

namespace Cascade.Agents
{
    public enum Posture { Standing, Crouching, Down }

    /// <summary>What the brain needs from the physical body. Implemented by the NpcAgent MonoBehaviour, or a fake in tests.</summary>
    public interface IAgentBody
    {
        Vector3 Position { get; }
        Vector3 Forward { get; }
        void MoveTo(Vector3 destination, float speedMultiplier);
        void Stop();
        bool HasArrived(float tolerance);
        void FaceTowards(Vector3 point);
        void SetPosture(Posture posture);
        /// <summary>Instant reposition (used when another agent carries this one).</summary>
        void Teleport(Vector3 position);
    }

    /// <summary>
    /// Physical interactions with the world. Results are facts the agent then learns (e.g. an extinguisher was gone),
    /// they are not a back door to world knowledge.
    /// </summary>
    public interface IAgentWorldActions
    {
        bool TryTakeExtinguisher(int extinguisherId, EntityId agent);
        void DropExtinguisher(int extinguisherId, EntityId agent, Vector3 position);
        /// <summary>Sprays for dt seconds. Returns the remaining max fire intensity near the target.</summary>
        float UseExtinguisher(int extinguisherId, Vector3 target, float dt);
        float ExtinguisherCharge(int extinguisherId);
        void PublishSocialAction(SocialActionEvent evt);
        void PublishSound(SoundEvent evt);
        /// <summary>Starts carrying an incapacitated agent within reach. The carried body then follows the carrier.</summary>
        bool TryStartCarry(EntityId carrier, EntityId target);
        void StopCarry(EntityId carrier);
        bool IsCarrying(EntityId carrier);
    }

    public struct AgentObservation
    {
        public EntityId Id;
        public Vector3 Position;
        public bool Injured;
        public bool Incapacitated;
        public bool Evacuating;
        public bool IsPlayer;
    }

    public struct ExtinguisherObservation
    {
        public int Id;
        public Vector3 Position;
        public bool Available;
    }

    /// <summary>
    /// The ONLY path from world truth into an agent. Held exclusively by PerceptionSystem, which decides what is
    /// visible before querying anything (principle P1).
    /// </summary>
    public interface IPerceptionSource
    {
        void QueryAgents(Vector3 center, float radius, System.Collections.Generic.List<EntityId> results);
        bool TryObserveAgent(EntityId id, out AgentObservation observation);
        bool HasLineOfSight(Vector3 from, Vector3 to);
        HazardSample SampleHazards(Vector3 position);
        float ObserveRoomFire(int room, Vector3 viewer, float range, out Vector3 firePosition);
        float ObserveRoomSmoke(int room);
        DoorState ObserveDoor(int door);
        void QueryExtinguishers(int room, System.Collections.Generic.List<ExtinguisherObservation> results);
    }

    public sealed class AgentServices
    {
        public ISimulationContext Sim;
        public IFloorPlan FloorPlan;
        public IPerceptionSource Perception;
        public IAgentWorldActions WorldActions;
        public GroupRegistry Groups;
        public CognitionSettings Cognition = new CognitionSettings();
    }
}
