using System.Collections.Generic;
using Cascade.World;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace Cascade.Runtime
{
    /// <summary>
    /// Builds the 3D building from the RoomGraph at runtime: ground, floors, walls with door gaps, door panels and
    /// props, then bakes a NavMesh. The room graph stays the single source of truth for layout.
    /// </summary>
    public static class BuildingBuilder
    {
        public const float DoorWidth = 1.8f;
        public const float WallThickness = 0.2f;

        public sealed class Result
        {
            public NavMeshSurface Surface;
            public readonly Dictionary<int, DoorView> Doors = new Dictionary<int, DoorView>();
            public readonly List<Transform> Extinguishers = new List<Transform>();
        }

        public static Result Build(RoomGraph graph, Transform root, MaterialLibrary mats)
        {
            var result = new Result();

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(root, false);
            ground.transform.localScale = new Vector3(90f, 0.2f, 70f);
            ground.transform.position = new Vector3(20f, -0.1f, 7f);
            ground.GetComponent<Renderer>().sharedMaterial = mats.Opaque(Palette.Ground);

            var built = new HashSet<string>();
            foreach (var room in graph.Rooms)
            {
                if (room.IsExterior) continue;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
                floor.name = "Floor " + room.Name;
                Object.Destroy(floor.GetComponent<Collider>());
                floor.transform.SetParent(root, false);
                floor.transform.position = new Vector3(room.Center.x, 0.01f, room.Center.z);
                floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                floor.transform.localScale = new Vector3(room.Max.x - room.Min.x, room.Max.z - room.Min.z, 1f);
                float tint = 0.9f + 0.1f * ((room.Id * 37) % 5) / 4f;
                floor.GetComponent<Renderer>().sharedMaterial = mats.Opaque(Palette.Floor * tint);

                var a = new Vector3(room.Min.x, 0f, room.Min.z);
                var b = new Vector3(room.Max.x, 0f, room.Min.z);
                var c = new Vector3(room.Max.x, 0f, room.Max.z);
                var d = new Vector3(room.Min.x, 0f, room.Max.z);
                BuildEdge(graph, a, b, root, mats, built);
                BuildEdge(graph, b, c, root, mats, built);
                BuildEdge(graph, d, c, root, mats, built);
                BuildEdge(graph, a, d, root, mats, built);
            }

            // Bake before door panels exist: closed doors are swing doors (walkable); locked/blocked doors carve at runtime.
            result.Surface = root.gameObject.AddComponent<NavMeshSurface>();
            result.Surface.collectObjects = CollectObjects.Children;
            result.Surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            result.Surface.BuildNavMesh();

            foreach (var door in graph.Doors) result.Doors[door.Id] = DoorView.Create(door, graph, root, mats);
            return result;
        }

        public static void BuildProps(BuildingModel model, Transform root, MaterialLibrary mats, Result result)
        {
            foreach (var e in model.Extinguishers)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "Extinguisher " + e.Id;
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(root, false);
                go.transform.localScale = new Vector3(0.25f, 0.35f, 0.25f);
                go.GetComponent<Renderer>().sharedMaterial = mats.Opaque(Palette.Extinguisher);
                result.Extinguishers.Add(go.transform);
            }
            foreach (var alarm in model.Alarms)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Alarm " + alarm.Id;
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(root, false);
                go.transform.position = alarm.Position + Vector3.up * 2.2f;
                go.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                go.GetComponent<Renderer>().sharedMaterial = mats.Opaque(new Color(1f, 0.2f, 0.2f));
            }
        }

        /// <summary>Builds one wall edge with gaps where doors sit. Shared edges are built once.</summary>
        private static void BuildEdge(RoomGraph graph, Vector3 from, Vector3 to, Transform root, MaterialLibrary mats, HashSet<string> built)
        {
            string key = Key(from, to);
            if (!built.Add(key)) return;

            var dir = (to - from).normalized;
            float length = Vector3.Distance(from, to);
            var cuts = new List<float>();
            foreach (var door in graph.Doors)
            {
                var rel = door.Position - from;
                float along = Vector3.Dot(rel, dir);
                float off = (rel - dir * along).magnitude;
                if (off < 0.3f && along > 0f && along < length) cuts.Add(along);
            }
            cuts.Sort();

            float start = 0f;
            foreach (var cut in cuts)
            {
                float gapStart = cut - DoorWidth * 0.5f;
                if (gapStart > start + 0.05f) Segment(from + dir * start, from + dir * gapStart, root, mats);
                start = cut + DoorWidth * 0.5f;
            }
            if (length > start + 0.05f) Segment(from + dir * start, to, root, mats);
        }

        private static void Segment(Vector3 a, Vector3 b, Transform root, MaterialLibrary mats)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.SetParent(root, false);
            var mid = (a + b) * 0.5f;
            float len = Vector3.Distance(a, b);
            bool alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.z - a.z);
            wall.transform.position = new Vector3(mid.x, BuildingLayouts.WallHeight * 0.5f, mid.z);
            wall.transform.localScale = alongX
                ? new Vector3(len + WallThickness, BuildingLayouts.WallHeight, WallThickness)
                : new Vector3(WallThickness, BuildingLayouts.WallHeight, len + WallThickness);
            wall.GetComponent<Renderer>().sharedMaterial = mats.Opaque(Palette.Wall);
        }

        private static string Key(Vector3 a, Vector3 b)
        {
            if (a.x > b.x || (Mathf.Approximately(a.x, b.x) && a.z > b.z)) { var t = a; a = b; b = t; }
            return Mathf.RoundToInt(a.x * 10) + "," + Mathf.RoundToInt(a.z * 10) + ":" + Mathf.RoundToInt(b.x * 10) + "," + Mathf.RoundToInt(b.z * 10);
        }
    }

    /// <summary>Door visual driven by world truth: panel when not open, NavMesh carving when impassable.</summary>
    public sealed class DoorView : MonoBehaviour
    {
        public Door Door;
        private GameObject _panel;
        private Material _material;
        private NavMeshObstacle _obstacle;
        private DoorState _shown = (DoorState)(-1);

        public static DoorView Create(Door door, RoomGraph graph, Transform root, MaterialLibrary mats)
        {
            var go = new GameObject("Door " + door.Id + " (" + graph.RoomName(door.RoomA) + " - " + graph.RoomName(door.RoomB) + ")");
            go.transform.SetParent(root, false);
            go.transform.position = door.Position;
            var ra = graph.Rooms[door.RoomA];
            bool wallAlongX = Mathf.Abs(door.Position.z - ra.Min.z) < 0.3f || Mathf.Abs(door.Position.z - ra.Max.z) < 0.3f;

            var view = go.AddComponent<DoorView>();
            view.Door = door;
            view._panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            view._panel.name = "Panel";
            view._panel.transform.SetParent(go.transform, false);
            view._panel.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            view._panel.transform.localScale = wallAlongX ? new Vector3(BuildingBuilder.DoorWidth, 2.2f, 0.08f) : new Vector3(0.08f, 2.2f, BuildingBuilder.DoorWidth);
            view._panel.GetComponent<Collider>().isTrigger = true; // blocks sight (linecast hits triggers) but not movement
            view._material = mats.Instance(Palette.DoorClosed);
            view._panel.GetComponent<Renderer>().sharedMaterial = view._material;

            view._obstacle = go.AddComponent<NavMeshObstacle>();
            view._obstacle.shape = NavMeshObstacleShape.Box;
            view._obstacle.size = wallAlongX ? new Vector3(BuildingBuilder.DoorWidth, 2f, 0.6f) : new Vector3(0.6f, 2f, BuildingBuilder.DoorWidth);
            view._obstacle.center = new Vector3(0f, 1f, 0f);
            view._obstacle.carving = true;
            view._obstacle.enabled = false;

            if (graph.IsExitDoor(door.Id))
            {
                var sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
                sign.name = "Exit sign";
                Object.Destroy(sign.GetComponent<Collider>());
                sign.transform.SetParent(go.transform, false);
                sign.transform.localPosition = new Vector3(0f, 2.7f, 0f);
                sign.transform.localScale = new Vector3(0.6f, 0.25f, 0.6f);
                sign.GetComponent<Renderer>().sharedMaterial = mats.Opaque(Palette.Exit);
            }
            return view;
        }

        private void Update()
        {
            if (Door == null || Door.State == _shown) return;
            _shown = Door.State;
            _panel.SetActive(Door.State != DoorState.Open);
            _obstacle.enabled = !DoorStates.IsPassable(Door.State);
            _material.color = Door.State == DoorState.Locked ? Palette.DoorLocked
                            : Door.State == DoorState.Blocked ? Palette.DoorBlocked
                            : Palette.DoorClosed;
        }
    }
}
