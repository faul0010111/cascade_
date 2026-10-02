using System.Collections.Generic;

namespace Cascade.Core
{
    /// <summary>Allocates ids and stores display metadata. Holds no simulation state.</summary>
    public sealed class EntityRegistry
    {
        private int _next = 1;
        private readonly Dictionary<EntityId, string> _names = new Dictionary<EntityId, string>();
        private readonly Dictionary<EntityId, EntityKind> _kinds = new Dictionary<EntityId, EntityKind>();

        public EntityId Register(EntityKind kind, string name)
        {
            var id = new EntityId(_next++);
            _names[id] = name;
            _kinds[id] = kind;
            return id;
        }

        public void Unregister(EntityId id)
        {
            _names.Remove(id);
            _kinds.Remove(id);
        }

        public string NameOf(EntityId id)
        {
            string name;
            return _names.TryGetValue(id, out name) ? name : id.ToString();
        }

        public EntityKind KindOf(EntityId id)
        {
            EntityKind kind;
            return _kinds.TryGetValue(id, out kind) ? kind : EntityKind.Interactable;
        }

        public bool IsPlayer(EntityId id) => KindOf(id) == EntityKind.Player;

        public IEnumerable<EntityId> All => _names.Keys;
    }
}
