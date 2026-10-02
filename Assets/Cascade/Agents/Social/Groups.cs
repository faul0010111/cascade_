using System.Collections.Generic;
using Cascade.Core;
using UnityEngine;

namespace Cascade.Agents
{
    /// <summary>Visible social acts. Endangered / Led are outcomes of following someone, visible to bystanders.</summary>
    public enum SocialActionKind { Helped, Pushed, Abandoned, Endangered, Led }

    /// <summary>A visible social act. The runtime routes it to agents who can see the actor.</summary>
    public struct SocialActionEvent
    {
        public EntityId Actor;
        public EntityId Target;
        public SocialActionKind Kind;
        public Vector3 Position;
    }

    /// <summary>
    /// Groups have no brain of their own: this only records who is following whom so that leaders emerge from
    /// followers' individual decisions (ARCHITECTURE.md 7.14).
    /// </summary>
    public sealed class GroupRegistry
    {
        private readonly Dictionary<EntityId, EntityId> _leaderOf = new Dictionary<EntityId, EntityId>();

        public bool TrySetFollow(EntityId follower, EntityId leader)
        {
            if (!leader.IsValid || follower == leader) return false;
            // Reject cycles: walk up from the leader.
            var cursor = leader;
            for (int guard = 0; guard < 64; guard++)
            {
                if (cursor == follower) return false;
                EntityId up;
                if (!_leaderOf.TryGetValue(cursor, out up)) break;
                cursor = up;
            }
            _leaderOf[follower] = leader;
            return true;
        }

        public void ClearFollow(EntityId follower) { _leaderOf.Remove(follower); }

        public EntityId LeaderOf(EntityId follower)
        {
            EntityId l;
            return _leaderOf.TryGetValue(follower, out l) ? l : EntityId.None;
        }

        public EntityId RootLeader(EntityId agent)
        {
            var cursor = agent;
            for (int guard = 0; guard < 64; guard++)
            {
                EntityId up;
                if (!_leaderOf.TryGetValue(cursor, out up)) return cursor;
                cursor = up;
            }
            return cursor;
        }

        /// <summary>True if 'follower' is somewhere below 'leader' in a follow chain.</summary>
        public bool FollowsTransitively(EntityId follower, EntityId leader)
        {
            var cursor = follower;
            for (int guard = 0; guard < 64; guard++)
            {
                EntityId up;
                if (!_leaderOf.TryGetValue(cursor, out up)) return false;
                if (up == leader) return true;
                cursor = up;
            }
            return false;
        }

        public bool IsInGroupOf(EntityId agent, EntityId leader) => agent != leader && RootLeader(agent) == RootLeader(leader);

        public int FollowerCount(EntityId leader)
        {
            int n = 0;
            foreach (var kv in _leaderOf)
                if (kv.Key != leader && RootLeader(kv.Key) == leader) n++;
            return n;
        }

        /// <summary>Root leader → members (excluding the leader).</summary>
        public void CollectGroups(Dictionary<EntityId, List<EntityId>> into)
        {
            into.Clear();
            foreach (var kv in _leaderOf)
            {
                var root = RootLeader(kv.Key);
                List<EntityId> list;
                if (!into.TryGetValue(root, out list)) { list = new List<EntityId>(); into[root] = list; }
                list.Add(kv.Key);
            }
        }

        public IEnumerable<KeyValuePair<EntityId, EntityId>> Links => _leaderOf;

        public void Remove(EntityId agent)
        {
            _leaderOf.Remove(agent);
            var orphans = new List<EntityId>();
            foreach (var kv in _leaderOf) if (kv.Value == agent) orphans.Add(kv.Key);
            foreach (var o in orphans) _leaderOf.Remove(o);
        }

        public void Clear() { _leaderOf.Clear(); }
    }
}
