using System;

namespace Cascade.Core
{
    /// <summary>Stable identifier for anything agents can perceive or reason about (NPCs, the player, interactables).</summary>
    [Serializable]
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public readonly int Value;

        public EntityId(int value) { Value = value; }

        public static readonly EntityId None = new EntityId(0);

        public bool IsValid => Value != 0;

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && other.Value == Value;
        public override int GetHashCode() => Value;
        public static bool operator ==(EntityId a, EntityId b) => a.Value == b.Value;
        public static bool operator !=(EntityId a, EntityId b) => a.Value != b.Value;
        public override string ToString() => Value == 0 ? "none" : "#" + Value.ToString("000");
    }

    public enum EntityKind { Npc, Player, Interactable }
}
