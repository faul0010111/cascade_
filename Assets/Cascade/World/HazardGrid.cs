using System.Collections.Generic;
using Cascade.Core;
using UnityEngine;

namespace Cascade.World
{
    public struct HazardSample
    {
        public float Fire;   // max fire intensity within ~1.5 m
        public float Smoke;  // smoke density of the room
        public float Heat;   // 0..1, from nearby fire

        public float Danger => Mathf.Clamp01(Mathf.Max(Fire, Heat * 0.8f) + Smoke * 0.5f);
    }

    /// <summary>
    /// Coarse fire / smoke model (the "influence map"). Fire lives on 1 m cells; smoke is tracked per room and flows
    /// through open doors. Deliberately simple and cheap: it exists to stress agents, not to be physically accurate.
    /// </summary>
    public sealed class HazardGrid
    {
        public readonly float CellSize;
        public readonly Vector3 Origin;
        public readonly int Width, Height;

        private readonly float[] _fire;
        private readonly float[] _fuel;
        private readonly int[] _room;
        private readonly float[] _roomSmoke;
        private readonly RoomGraph _graph;
        private readonly EventBus _bus;
        private readonly Rng _rng;

        public float SpreadChancePerSecond = 0.35f;
        public float GrowthPerSecond = 0.08f;
        public float BurnPerSecond = 0.012f;
        public float SmokePerFirePerSecond = 1.4f;
        public float SmokeFlowPerSecond = 0.25f;
        public float SmokeVentPerSecond = 0.01f;
        public float DoorBlockThreshold = 0.55f;

        public HazardGrid(RoomGraph graph, EventBus bus, Rng rng, float cellSize = 1f)
        {
            _graph = graph;
            _bus = bus;
            _rng = rng;
            CellSize = cellSize;

            Vector3 min = new Vector3(float.MaxValue, 0, float.MaxValue), max = new Vector3(float.MinValue, 0, float.MinValue);
            foreach (var r in graph.Rooms)
            {
                if (r.IsExterior) continue;
                min = new Vector3(Mathf.Min(min.x, r.Min.x), 0, Mathf.Min(min.z, r.Min.z));
                max = new Vector3(Mathf.Max(max.x, r.Max.x), 0, Mathf.Max(max.z, r.Max.z));
            }
            Origin = min;
            Width = Mathf.CeilToInt((max.x - min.x) / cellSize);
            Height = Mathf.CeilToInt((max.z - min.z) / cellSize);
            _fire = new float[Width * Height];
            _fuel = new float[Width * Height];
            _room = new int[Width * Height];
            _roomSmoke = new float[graph.Rooms.Count];

            for (int z = 0; z < Height; z++)
            for (int x = 0; x < Width; x++)
            {
                int i = z * Width + x;
                var center = CellCenter(x, z);
                _room[i] = -1;
                foreach (var r in graph.Rooms)
                    if (!r.IsExterior && r.Contains(center)) { _room[i] = r.Id; break; }
                _fuel[i] = _room[i] >= 0 && graph.Rooms[_room[i]].Flammable ? rng.Range(0.6f, 1f) : 0f;
            }
        }

        public int CellCount => _fire.Length;
        public float FireAt(int cell) => _fire[cell];
        public float RoomSmoke(int room) => room >= 0 && room < _roomSmoke.Length ? _roomSmoke[room] : 0f;
        public int CellRoom(int cell) => _room[cell];

        public Vector3 CellCenter(int x, int z) => Origin + new Vector3((x + 0.5f) * CellSize, 0f, (z + 0.5f) * CellSize);
        public Vector3 CellCenter(int cell) => CellCenter(cell % Width, cell / Width);

        public int CellIndex(Vector3 p)
        {
            int x = Mathf.FloorToInt((p.x - Origin.x) / CellSize);
            int z = Mathf.FloorToInt((p.z - Origin.z) / CellSize);
            if (x < 0 || z < 0 || x >= Width || z >= Height) return -1;
            return z * Width + x;
        }

        public bool Ignite(Vector3 position, float intensity = 0.3f)
        {
            int c = CellIndex(position);
            if (c < 0 || _fuel[c] <= 0f) return false;
            bool wasBurning = _fire[c] > 0f;
            _fire[c] = Mathf.Max(_fire[c], intensity);
            if (!wasBurning) _bus.Enqueue(new FireIgnitedEvent { Position = CellCenter(c), Room = _room[c] });
            return true;
        }

        /// <summary>Reduces fire in a radius. Returns the remaining max intensity in that radius.</summary>
        public float Extinguish(Vector3 position, float radius, float amount)
        {
            float remaining = 0f;
            ForCellsInRadius(position, radius, c =>
            {
                _fire[c] = Mathf.Max(0f, _fire[c] - amount);
                if (_fire[c] < 0.05f) _fire[c] = 0f;
                remaining = Mathf.Max(remaining, _fire[c]);
            });
            return remaining;
        }

        public void Clear()
        {
            for (int i = 0; i < _fire.Length; i++) _fire[i] = 0f;
            for (int i = 0; i < _roomSmoke.Length; i++) _roomSmoke[i] = 0f;
        }

        public HazardSample Sample(Vector3 position)
        {
            var s = new HazardSample();
            float heat = 0f;
            ForCellsInRadius(position, 4f, c =>
            {
                if (_fire[c] <= 0f) return;
                float d = Vector3.Distance(CellCenter(c), new Vector3(position.x, 0f, position.z));
                if (d <= 1.5f) s.Fire = Mathf.Max(s.Fire, _fire[c]);
                heat = Mathf.Max(heat, _fire[c] * Mathf.Clamp01(1f - d / 4f));
            });
            s.Heat = heat;
            s.Smoke = RoomSmoke(_graph.RoomAt(position));
            return s;
        }

        /// <summary>Max fire intensity in a room and the position of the strongest cell.</summary>
        public float RoomFire(int room, out Vector3 hottest)
        {
            float best = 0f;
            hottest = Vector3.zero;
            for (int i = 0; i < _fire.Length; i++)
            {
                if (_room[i] != room || _fire[i] <= best) continue;
                best = _fire[i];
                hottest = CellCenter(i);
            }
            return best;
        }

        public float TotalFire()
        {
            float sum = 0f;
            for (int i = 0; i < _fire.Length; i++) sum += _fire[i];
            return sum;
        }

        public void Step(float dt)
        {
            if (dt <= 0f) return;
            var newlyIgnited = new List<int>();
            var roomFire = new float[_roomSmoke.Length];

            for (int i = 0; i < _fire.Length; i++)
            {
                float f = _fire[i];
                if (f <= 0f) continue;
                roomFire[_room[i]] += f;
                if (_fuel[i] > 0f)
                {
                    _fire[i] = Mathf.Min(1f, f + GrowthPerSecond * dt);
                    _fuel[i] -= BurnPerSecond * dt * f;
                }
                else
                {
                    _fire[i] = Mathf.Max(0f, f - 0.05f * dt);
                }

                int x = i % Width, z = i / Width;
                TrySpread(i, x + 1, z, f, dt, newlyIgnited);
                TrySpread(i, x - 1, z, f, dt, newlyIgnited);
                TrySpread(i, x, z + 1, f, dt, newlyIgnited);
                TrySpread(i, x, z - 1, f, dt, newlyIgnited);
            }
            foreach (var c in newlyIgnited) _fire[c] = Mathf.Max(_fire[c], 0.15f);

            SpreadThroughDoors(dt);
            UpdateSmoke(dt, roomFire);
            UpdateDoors();
        }

        private void TrySpread(int from, int x, int z, float intensity, float dt, List<int> ignited)
        {
            if (x < 0 || z < 0 || x >= Width || z >= Height) return;
            int to = z * Width + x;
            if (_fire[to] > 0f || _fuel[to] <= 0f) return;
            if (_room[to] != _room[from]) return; // walls: cross-room spread only through doors
            if (_rng.Chance(SpreadChancePerSecond * intensity * dt)) ignited.Add(to);
        }

        private void SpreadThroughDoors(float dt)
        {
            foreach (var door in _graph.Doors)
            {
                if (door.State == DoorState.Locked || door.State == DoorState.Closed) continue;
                SpreadAcross(door, door.RoomA, door.RoomB, dt);
                SpreadAcross(door, door.RoomB, door.RoomA, dt);
            }
        }

        private void SpreadAcross(Door door, int fromRoom, int toRoom, float dt)
        {
            if (_graph.Rooms[toRoom].IsExterior) return;
            var fromSide = door.Position + (_graph.Rooms[fromRoom].Center - door.Position).normalized * CellSize;
            var toSide = door.Position + (_graph.Rooms[toRoom].Center - door.Position).normalized * CellSize;
            int cf = CellIndex(fromSide), ct = CellIndex(toSide);
            if (cf < 0 || ct < 0 || _fire[cf] < 0.5f || _fire[ct] > 0f) return;
            if (_rng.Chance(SpreadChancePerSecond * 0.5f * _fire[cf] * dt)) Ignite(toSide, 0.15f);
        }

        private void UpdateSmoke(float dt, float[] roomFire)
        {
            for (int r = 0; r < _roomSmoke.Length; r++)
            {
                var room = _graph.Rooms[r];
                if (room.IsExterior) { _roomSmoke[r] = 0f; continue; }
                _roomSmoke[r] += roomFire[r] * SmokePerFirePerSecond * dt / Mathf.Max(10f, room.Area);
                _roomSmoke[r] -= SmokeVentPerSecond * dt;
            }
            foreach (var door in _graph.Doors)
            {
                if (!DoorStates.LetsSmokeThrough(door.State)) continue;
                float a = _roomSmoke[door.RoomA], b = _roomSmoke[door.RoomB];
                float flow = (a - b) * SmokeFlowPerSecond * dt;
                if (!_graph.Rooms[door.RoomA].IsExterior) _roomSmoke[door.RoomA] -= flow;
                if (!_graph.Rooms[door.RoomB].IsExterior) _roomSmoke[door.RoomB] += flow;
            }
            for (int r = 0; r < _roomSmoke.Length; r++) _roomSmoke[r] = Mathf.Clamp01(_roomSmoke[r]);
        }

        private void UpdateDoors()
        {
            foreach (var door in _graph.Doors)
            {
                if (door.State == DoorState.Blocked) continue;
                int c = CellIndex(door.Position + (_graph.Rooms[door.RoomA].Center - door.Position).normalized * 0.5f);
                int c2 = CellIndex(door.Position + (_graph.Rooms[door.RoomB].Center - door.Position).normalized * 0.5f);
                float f = Mathf.Max(c >= 0 ? _fire[c] : 0f, c2 >= 0 ? _fire[c2] : 0f);
                if (f < DoorBlockThreshold) continue;
                var old = door.State;
                door.State = DoorState.Blocked;
                _bus.Enqueue(new DoorStateChangedEvent { Door = door.Id, OldState = old, NewState = DoorState.Blocked });
            }
        }

        private void ForCellsInRadius(Vector3 p, float radius, System.Action<int> action)
        {
            int minX = Mathf.FloorToInt((p.x - radius - Origin.x) / CellSize), maxX = Mathf.FloorToInt((p.x + radius - Origin.x) / CellSize);
            int minZ = Mathf.FloorToInt((p.z - radius - Origin.z) / CellSize), maxZ = Mathf.FloorToInt((p.z + radius - Origin.z) / CellSize);
            for (int z = Mathf.Max(0, minZ); z <= Mathf.Min(Height - 1, maxZ); z++)
            for (int x = Mathf.Max(0, minX); x <= Mathf.Min(Width - 1, maxX); x++)
                action(z * Width + x);
        }
    }
}
