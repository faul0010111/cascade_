using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cascade.World
{
    public enum DoorState { Open, Closed, Locked, Blocked }

    public static class DoorStates
    {
        /// <summary>Closed doors are swing doors: they slow agents slightly but are passable. Locked and Blocked are not.</summary>
        public static bool IsPassable(DoorState s) => s == DoorState.Open || s == DoorState.Closed;
        public static bool LetsSmokeThrough(DoorState s) => s == DoorState.Open || s == DoorState.Blocked;
    }

    public sealed class Room
    {
        public int Id;
        public string Name;
        public Vector3 Min;       // XZ rectangle, y ignored
        public Vector3 Max;
        public bool IsExterior;   // safe zone outside the building
        public bool Flammable = true;
        public readonly List<int> Doors = new List<int>();

        public Vector3 Center => new Vector3((Min.x + Max.x) * 0.5f, 0f, (Min.z + Max.z) * 0.5f);
        public float Area => (Max.x - Min.x) * (Max.z - Min.z);

        public bool Contains(Vector3 p) => p.x >= Min.x && p.x <= Max.x && p.z >= Min.z && p.z <= Max.z;

        public Vector3 Clamp(Vector3 p, float margin)
            => new Vector3(Mathf.Clamp(p.x, Min.x + margin, Max.x - margin), 0f, Mathf.Clamp(p.z, Min.z + margin, Max.z - margin));
    }

    public sealed class Door
    {
        public int Id;
        public int RoomA;
        public int RoomB;
        public Vector3 Position;
        public DoorState State;  // world truth: agents never read this directly

        public int Other(int room) => room == RoomA ? RoomB : RoomA;
        public bool Connects(int room) => room == RoomA || room == RoomB;
    }

    /// <summary>
    /// Static topology an agent is allowed to know (room layout, door positions, which doors lead outside).
    /// Door STATES are intentionally not exposed: agents learn them only through perception or communication.
    /// </summary>
    public interface IFloorPlan
    {
        int RoomCount { get; }
        int DoorCount { get; }
        int RoomAt(Vector3 position);
        string RoomName(int room);
        Vector3 RoomCenter(int room);
        Vector3 ClampToRoom(int room, Vector3 position, float margin);
        bool IsExterior(int room);
        IReadOnlyList<int> DoorsOf(int room);
        int DoorOtherSide(int door, int fromRoom);
        Vector3 DoorPosition(int door);
        bool IsExitDoor(int door);
    }

    public delegate float EdgeCostFn(int door, int fromRoom, int toRoom);
    public delegate bool RoomPredicate(int room);

    public sealed class RoomGraph : IFloorPlan
    {
        public readonly List<Room> Rooms = new List<Room>();
        public readonly List<Door> Doors = new List<Door>();

        public int RoomCount => Rooms.Count;
        public int DoorCount => Doors.Count;

        public Room AddRoom(string name, float minX, float minZ, float width, float depth, bool exterior = false)
        {
            var room = new Room
            {
                Id = Rooms.Count,
                Name = name,
                Min = new Vector3(minX, 0f, minZ),
                Max = new Vector3(minX + width, 0f, minZ + depth),
                IsExterior = exterior,
                Flammable = !exterior
            };
            Rooms.Add(room);
            return room;
        }

        public Door AddDoor(int roomA, int roomB, Vector3 position, DoorState state = DoorState.Open)
        {
            var door = new Door { Id = Doors.Count, RoomA = roomA, RoomB = roomB, Position = position, State = state };
            Doors.Add(door);
            Rooms[roomA].Doors.Add(door.Id);
            Rooms[roomB].Doors.Add(door.Id);
            return door;
        }

        public int RoomAt(Vector3 p)
        {
            for (int i = 0; i < Rooms.Count; i++)
                if (!Rooms[i].IsExterior && Rooms[i].Contains(p)) return i;
            // Outside the building: nearest exterior zone.
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < Rooms.Count; i++)
            {
                if (!Rooms[i].IsExterior) continue;
                if (Rooms[i].Contains(p)) return i;
                float d = (Rooms[i].Center - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public string RoomName(int room) => room >= 0 && room < Rooms.Count ? Rooms[room].Name : "?";
        public Vector3 RoomCenter(int room) => Rooms[room].Center;
        public Vector3 ClampToRoom(int room, Vector3 p, float margin) => Rooms[room].Clamp(p, margin);
        public bool IsExterior(int room) => room >= 0 && Rooms[room].IsExterior;
        public IReadOnlyList<int> DoorsOf(int room) => Rooms[room].Doors;
        public int DoorOtherSide(int door, int fromRoom) => Doors[door].Other(fromRoom);
        public Vector3 DoorPosition(int door) => Doors[door].Position;
        public bool IsExitDoor(int door) => Rooms[Doors[door].RoomA].IsExterior != Rooms[Doors[door].RoomB].IsExterior;

        public int FindRoom(string name)
        {
            for (int i = 0; i < Rooms.Count; i++)
                if (string.Equals(Rooms[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>
        /// Dijkstra over rooms. Edge costs come from the caller (per-agent beliefs), which is why this lives on the
        /// graph but is parameterized: truth never leaks into the cost unless the caller puts it there.
        /// Cost function returns float.PositiveInfinity for impassable edges.
        /// </summary>
        public static bool FindPath(IFloorPlan plan, int start, RoomPredicate isTarget, EdgeCostFn cost,
                                    List<int> outDoors, out float totalCost, out int targetRoom)
        {
            outDoors.Clear();
            totalCost = float.PositiveInfinity;
            targetRoom = -1;
            if (start < 0) return false;
            int n = plan.RoomCount;
            var dist = new float[n];
            var viaDoor = new int[n];
            var prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.PositiveInfinity; viaDoor[i] = -1; prev[i] = -1; }
            dist[start] = 0f;

            while (true)
            {
                int u = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                    if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                if (u < 0) return false;
                if (isTarget(u))
                {
                    targetRoom = u;
                    totalCost = dist[u];
                    for (int r = u; r != start; r = prev[r]) outDoors.Add(viaDoor[r]);
                    outDoors.Reverse();
                    return true;
                }
                done[u] = true;
                var doors = plan.DoorsOf(u);
                for (int k = 0; k < doors.Count; k++)
                {
                    int d = doors[k];
                    int v = plan.DoorOtherSide(d, u);
                    if (done[v]) continue;
                    float c = cost(d, u, v);
                    if (float.IsInfinity(c)) continue;
                    float alt = dist[u] + c;
                    if (alt < dist[v]) { dist[v] = alt; prev[v] = u; viaDoor[v] = d; }
                }
            }
        }
    }
}
