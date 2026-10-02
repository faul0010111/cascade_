using Cascade.Core;
using UnityEngine;

namespace Cascade.World
{
    public enum SoundKind { Alarm, Shout, Scream, Crash }

    /// <summary>Anything audible. Delivered by the runtime hearing router to agents in range.</summary>
    public struct SoundEvent
    {
        public SoundKind Kind;
        public Vector3 Position;
        public float Loudness;      // audible radius in meters before door attenuation
        public EntityId Source;
    }

    public struct FireIgnitedEvent { public Vector3 Position; public int Room; }
    public struct DoorStateChangedEvent { public int Door; public DoorState OldState; public DoorState NewState; }
    public struct AlarmTriggeredEvent { public int Alarm; public Vector3 Position; }
}
