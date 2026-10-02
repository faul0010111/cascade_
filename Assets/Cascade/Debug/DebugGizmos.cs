using System.Collections.Generic;
using Cascade.Agents;
using Cascade.ClaudeIntegration;
using Cascade.Core;
using Cascade.Runtime;
using UnityEngine;

namespace Cascade.DebugTools
{
    /// <summary>
    /// Runtime lines (visible in the Game view and in builds): perception cone and route of the selected NPC, follow
    /// links, message flashes, and the selected NPC's believed fire positions next to the real ones.
    /// </summary>
    public sealed class DebugGizmos : MonoBehaviour
    {
        private CascadeBootstrap _boot;
        private readonly List<LineRenderer> _pool = new List<LineRenderer>();
        private int _used;
        private Material _material;
        private readonly List<KeyValuePair<float, Message>> _recentMessages = new List<KeyValuePair<float, Message>>();
        private Simulation.SimulationWorld _subscribed;

        private void Awake() { _boot = GetComponent<CascadeBootstrap>(); }

        private void LateUpdate()
        {
            _used = 0;
            if (_boot == null || _boot.World == null) { Finish(); return; }
            var world = _boot.World;
            if (_subscribed != world)
            {
                _subscribed = world;
                _recentMessages.Clear();
                world.MessageSent += m => _recentMessages.Add(new KeyValuePair<float, Message>(world.Now, m));
            }
            _recentMessages.RemoveAll(kv => world.Now - kv.Key > 1.2f);

            if (DebugState.ShowGroups)
                foreach (var link in world.Groups.Links)
                {
                    var f = world.GetAgent(link.Key);
                    var l = world.GetAgent(link.Value);
                    if (f != null && l != null) Line(f.Body.Position + Vector3.up * 0.2f, l.Body.Position + Vector3.up * 0.2f, new Color(0.2f, 0.6f, 1f), 0.08f);
                }

            if (DebugState.ShowMessages)
                foreach (var kv in _recentMessages)
                {
                    var m = kv.Value;
                    var color = m.Kind == MessageKind.Warn ? new Color(1f, 0.5f, 0f) : m.Kind == MessageKind.Assist ? Color.green : Color.white;
                    if (m.IsBroadcast) Circle(m.SenderPosition + Vector3.up * 0.1f, m.Loudness * (world.Now - kv.Key) / 1.2f, color);
                    else
                    {
                        var to = world.GetAgent(m.Addressee);
                        if (to != null) Line(m.SenderPosition + Vector3.up * 1.8f, to.Body.Position + Vector3.up * 1.8f, color, 0.05f);
                    }
                }

            var sel = DebugState.Selected;
            if (sel != null && sel.Brain != null)
            {
                var b = sel.Brain;
                if (DebugState.ShowPerception) Cone(sel.transform.position + Vector3.up * 0.1f, sel.transform.forward, b.Perception.EffectiveRange, b.Perception.EffectiveFov);
                if (DebugState.ShowRoutes && b.CurrentRoute.Count > 0)
                {
                    var prev = sel.transform.position + Vector3.up * 0.15f;
                    foreach (var d in b.CurrentRoute)
                    {
                        var p = b.FloorPlan.DoorPosition(d) + Vector3.up * 0.15f;
                        Line(prev, p, Color.cyan, 0.1f);
                        prev = p;
                    }
                }
                if (DebugState.ShowBeliefVsTruth)
                {
                    foreach (var belief in b.Beliefs.OfType(FactType.HazardInRoom))
                    {
                        if (belief.Value <= 0.05f) continue;
                        float conf = belief.EffectiveConfidence(b.Now);
                        Circle(belief.Position + Vector3.up * 0.2f, 1f + belief.Value, new Color(1f, 0f, 1f, Mathf.Clamp01(conf + 0.2f)));
                    }
                    foreach (var room in world.Building.Graph.Rooms)
                    {
                        Vector3 hot;
                        if (room.IsExterior || world.Building.Hazards.RoomFire(room.Id, out hot) <= 0.05f) continue;
                        Circle(hot + Vector3.up * 0.25f, 0.6f, Color.yellow);
                    }
                }
                Circle(sel.transform.position + Vector3.up * 0.05f, 0.7f, Color.white);
            }
            Finish();
        }

        private void Finish()
        {
            for (int i = _used; i < _pool.Count; i++) _pool[i].enabled = false;
        }

        private LineRenderer Next(int points, Color color, float width)
        {
            if (_material == null) _material = new Material(Shader.Find("Sprites/Default"));
            if (_used >= _pool.Count)
            {
                var go = new GameObject("DebugLine");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = _material;
                lr.useWorldSpace = true;
                _pool.Add(lr);
            }
            var r = _pool[_used++];
            r.enabled = true;
            r.positionCount = points;
            r.startColor = r.endColor = color;
            r.startWidth = r.endWidth = width;
            return r;
        }

        private void Line(Vector3 a, Vector3 b, Color color, float width)
        {
            var r = Next(2, color, width);
            r.SetPosition(0, a);
            r.SetPosition(1, b);
        }

        private void Circle(Vector3 center, float radius, Color color)
        {
            const int n = 24;
            var r = Next(n + 1, color, 0.05f);
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                r.SetPosition(i, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
            }
        }

        private void Cone(Vector3 origin, Vector3 forward, float range, float fov)
        {
            const int n = 16;
            var r = Next(n + 3, new Color(1f, 1f, 0.3f, 0.8f), 0.05f);
            float baseAngle = Mathf.Atan2(forward.z, forward.x);
            float half = fov * 0.5f * Mathf.Deg2Rad;
            r.SetPosition(0, origin);
            for (int i = 0; i <= n; i++)
            {
                float a = baseAngle - half + 2f * half * i / n;
                r.SetPosition(i + 1, origin + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * range);
            }
            r.SetPosition(n + 2, origin);
        }
    }
}
