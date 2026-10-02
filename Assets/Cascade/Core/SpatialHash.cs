using System.Collections.Generic;
using UnityEngine;

namespace Cascade.Core
{
    /// <summary>Uniform grid on the XZ plane for neighbour queries. O(1) updates, cost of a query proportional to local density.</summary>
    public sealed class SpatialHash<T>
    {
        private readonly float _cellSize;
        private readonly Dictionary<long, List<T>> _cells = new Dictionary<long, List<T>>();
        private readonly Dictionary<T, long> _where = new Dictionary<T, long>();
        private readonly Dictionary<T, Vector3> _positions = new Dictionary<T, Vector3>();

        public SpatialHash(float cellSize) { _cellSize = cellSize; }

        public int Count => _where.Count;

        private long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        private long KeyOf(Vector3 p) => Key(Mathf.FloorToInt(p.x / _cellSize), Mathf.FloorToInt(p.z / _cellSize));

        public void Update(T item, Vector3 position)
        {
            long key = KeyOf(position);
            _positions[item] = position;
            long old;
            if (_where.TryGetValue(item, out old))
            {
                if (old == key) return;
                _cells[old].Remove(item);
            }
            List<T> list;
            if (!_cells.TryGetValue(key, out list))
            {
                list = new List<T>();
                _cells[key] = list;
            }
            list.Add(item);
            _where[item] = key;
        }

        public void Remove(T item)
        {
            long key;
            if (!_where.TryGetValue(item, out key)) return;
            _cells[key].Remove(item);
            _where.Remove(item);
            _positions.Remove(item);
        }

        public bool TryGetPosition(T item, out Vector3 position) => _positions.TryGetValue(item, out position);

        public void Query(Vector3 center, float radius, List<T> results)
        {
            int minX = Mathf.FloorToInt((center.x - radius) / _cellSize);
            int maxX = Mathf.FloorToInt((center.x + radius) / _cellSize);
            int minZ = Mathf.FloorToInt((center.z - radius) / _cellSize);
            int maxZ = Mathf.FloorToInt((center.z + radius) / _cellSize);
            float r2 = radius * radius;
            for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
            {
                List<T> list;
                if (!_cells.TryGetValue(Key(x, z), out list)) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = _positions[list[i]];
                    float dx = p.x - center.x, dz = p.z - center.z;
                    if (dx * dx + dz * dz <= r2) results.Add(list[i]);
                }
            }
        }

        public void Clear()
        {
            _cells.Clear();
            _where.Clear();
            _positions.Clear();
        }
    }
}
