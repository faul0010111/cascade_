using Cascade.Runtime;
using UnityEngine;

namespace Cascade.DebugTools
{
    using System.Collections.Generic;
    /// <summary>Adds the debugger and lab to any scene that contains a CascadeBootstrap. Remove this assembly to ship without them.</summary>
    public static class DebugInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var boot = Object.FindAnyObjectByType<CascadeBootstrap>();
            if (boot == null) return;
            var go = boot.gameObject;
            if (go.GetComponent<NpcDebugOverlay>() == null) go.AddComponent<NpcDebugOverlay>();
            if (go.GetComponent<SimulationLabPanel>() == null) go.AddComponent<SimulationLabPanel>();
            if (go.GetComponent<DebugGizmos>() == null) go.AddComponent<DebugGizmos>();
        }
    }

    /// <summary>Shared debug state (selection, toggles, tool mode).</summary>
    public static class DebugState
    {
        public static Runtime.NpcAgent Selected;
        public static bool ShowLabels = true;
        public static bool ShowPerception = true;
        public static bool ShowRoutes = true;
        public static bool ShowGroups = true;
        public static bool ShowMessages = true;
        public static bool ShowBeliefVsTruth = true;
        public static bool ShowSpeech = true;
        public static LabTool Tool = LabTool.Select;
        public static string SpawnArchetype = "Average";
    }

    public enum LabTool { Select, Ignite, Extinguish, ToggleDoor, SpawnNpc, Injure }

    /// <summary>IMGUI drawing of engine-independent BrainReport sections.</summary>
    public static class ReportDrawer
    {
        private static Texture2D _white;
        private static GUIStyle _small;

        public static GUIStyle Small
        {
            get
            {
                if (_small == null) _small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true, richText = true };
                return _small;
            }
        }

        public static void Section(Agents.ReportSection s, float width, System.Action<int> onRowClicked = null)
        {
            GUILayout.Label("<b>" + s.Title + "</b>", Small);
            for (int i = 0; i < s.Rows.Count; i++)
            {
                var r = s.Rows[i];
                GUILayout.BeginHorizontal();
                if (onRowClicked != null && GUILayout.Button(r.Label, Small, GUILayout.Width(width * 0.5f))) onRowClicked(i);
                else if (onRowClicked == null) GUILayout.Label(r.Label, Small, GUILayout.Width(width * 0.5f));
                if (r.Bar >= 0f)
                {
                    var rect = GUILayoutUtility.GetRect(width * 0.22f, 12f, GUILayout.Width(width * 0.22f));
                    rect.y += 3f;
                    Bar(rect, r.Bar);
                }
                GUILayout.Label(r.Text, Small);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6f);
        }

        /// <summary>Tiny line chart: one column per sample, height = value (0..1), optional marker at index.</summary>
        public static void Sparkline(Rect rect, IList<float> values, Color color, int marker = -1)
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(rect, _white);
            int n = values.Count;
            if (n > 0)
            {
                float w = rect.width / n;
                for (int i = 0; i < n; i++)
                {
                    float v = Mathf.Clamp01(values[i]);
                    GUI.color = i == marker ? Color.white : color;
                    GUI.DrawTexture(new Rect(rect.x + i * w, rect.y + rect.height * (1f - v), Mathf.Max(1f, w), Mathf.Max(1f, rect.height * v)), _white);
                }
            }
            GUI.color = old;
        }

        public static void Bar(Rect rect, float value)
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.4f);
            GUI.DrawTexture(rect, _white);
            GUI.color = Color.Lerp(new Color(0.3f, 0.8f, 0.4f), new Color(0.95f, 0.3f, 0.2f), value);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value), rect.height), _white);
            GUI.color = old;
        }
    }
}
