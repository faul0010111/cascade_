using System.Collections.Generic;
using Cascade.World;
using UnityEngine;

namespace Cascade.Runtime
{
    /// <summary>Renders fire cells and per-room smoke from world truth. Pooled quads, refreshed a few times per second.</summary>
    public sealed class HazardView : MonoBehaviour
    {
        private BuildingModel _model;
        private readonly List<Renderer> _firePool = new List<Renderer>();
        private readonly Dictionary<int, Renderer> _smoke = new Dictionary<int, Renderer>();
        private readonly List<Material> _fireMaterials = new List<Material>();
        private float _next;
        public float RefreshInterval = 0.2f;

        public static HazardView Create(BuildingModel model, MaterialLibrary mats, Transform parent)
        {
            var go = new GameObject("Hazards");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<HazardView>();
            view._model = model;
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                view._fireMaterials.Add(mats.Transparent(new Color(1f, Mathf.Lerp(0.8f, 0.15f, t), 0.05f, Mathf.Lerp(0.55f, 0.95f, t))));
            }
            foreach (var room in model.Graph.Rooms)
            {
                if (room.IsExterior) continue;
                var q = Quad(go.transform, "Smoke " + room.Name);
                q.transform.position = new Vector3(room.Center.x, 2.4f, room.Center.z);
                q.transform.localScale = new Vector3(room.Max.x - room.Min.x, room.Max.z - room.Min.z, 1f);
                q.sharedMaterial = mats.Instance(new Color(0.2f, 0.2f, 0.2f, 0f), true);
                view._smoke[room.Id] = q;
            }
            return view;
        }

        private static Renderer Quad(Transform parent, string name)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return q.GetComponent<Renderer>();
        }

        private void Update()
        {
            if (_model == null || Time.time < _next) return;
            _next = Time.time + RefreshInterval;
            var grid = _model.Hazards;
            int used = 0;
            for (int c = 0; c < grid.CellCount; c++)
            {
                float f = grid.FireAt(c);
                if (f <= 0.01f) continue;
                if (used >= _firePool.Count) _firePool.Add(Quad(transform, "Fire"));
                var r = _firePool[used++];
                r.gameObject.SetActive(true);
                r.transform.position = grid.CellCenter(c) + Vector3.up * (0.05f + f * 0.1f);
                r.transform.localScale = Vector3.one * grid.CellSize * (0.6f + 0.4f * f);
                r.sharedMaterial = _fireMaterials[Mathf.Clamp(Mathf.FloorToInt(f * 5f), 0, 4)];
            }
            for (int i = used; i < _firePool.Count; i++) _firePool[i].gameObject.SetActive(false);

            foreach (var kv in _smoke)
            {
                float s = grid.RoomSmoke(kv.Key);
                kv.Value.sharedMaterial.color = new Color(0.2f, 0.2f, 0.2f, Mathf.Clamp01(s) * 0.65f);
            }
        }
    }
}
