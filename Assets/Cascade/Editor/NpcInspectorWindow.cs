using Cascade.Agents;
using Cascade.Runtime;
using UnityEditor;
using UnityEngine;

namespace Cascade.EditorTools
{
    /// <summary>Editor-side NPC Debugger: shows the same BrainReport as the in-game overlay for the selected NPC.</summary>
    public sealed class NpcInspectorWindow : EditorWindow
    {
        private Vector2 _scroll;
        private int _option = -1;
        private float _whyAt = -1f;

        public static void Open() { GetWindow<NpcInspectorWindow>("NPC Inspector"); }

        private void OnInspectorUpdate() { Repaint(); }

        private void OnGUI()
        {
            if (!Application.isPlaying) { EditorGUILayout.HelpBox("Enter Play mode and select an NPC (in the Hierarchy or by clicking it in the Game view).", MessageType.Info); return; }
            NpcAgent npc = null;
            if (Selection.activeGameObject != null) npc = Selection.activeGameObject.GetComponentInParent<NpcAgent>();
            if (npc == null) npc = DebugTools.DebugState.Selected;
            if (npc == null || npc.Brain == null) { EditorGUILayout.HelpBox("No NPC selected.", MessageType.Info); return; }

            var b = npc.Brain;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            Draw(BrainReport.Overview(b));
            Draw(BrainReport.Decision(b), true);
            var latest = b.Log.Latest;
            if (latest != null)
            {
                var sorted = new System.Collections.Generic.List<OptionTrace>(latest.Options);
                sorted.Sort((x, y) => y.FinalScore.CompareTo(x.FinalScore));
                Draw(BrainReport.Breakdown(_option >= 0 && _option < sorted.Count ? sorted[_option] : latest.Chosen));
            }
            Draw(BrainReport.SelfAssessment(b));
            Draw(BrainReport.Learning(b));
            Draw(BrainReport.Experiences(b));
            Draw(BrainReport.LearningEvents(b));
            Draw(BrainReport.WhyAt(b, _whyAt < 0f ? b.Now : _whyAt));
            _whyAt = EditorGUILayout.Slider("Why at t =", _whyAt < 0f ? b.Now : _whyAt, 0f, Mathf.Max(0.1f, b.Now));
            Draw(BrainReport.InternalState(b));
            Draw(BrainReport.Needs(b));
            Draw(BrainReport.Personality(b));
            Draw(BrainReport.Memories(b));
            Draw(BrainReport.Relationships(b));
            Draw(BrainReport.Beliefs(b));
            EditorGUILayout.LabelField("Recent events", EditorStyles.boldLabel);
            for (int i = b.Log.Events.Count - 1; i >= 0; i--) EditorGUILayout.LabelField(b.Log.Events[i]);
            EditorGUILayout.EndScrollView();
        }

        private void Draw(ReportSection s, bool clickable = false)
        {
            EditorGUILayout.LabelField(s.Title, EditorStyles.boldLabel);
            for (int i = 0; i < s.Rows.Count; i++)
            {
                var r = s.Rows[i];
                EditorGUILayout.BeginHorizontal();
                if (clickable) { if (GUILayout.Button(r.Label, EditorStyles.label, GUILayout.Width(200f))) _option = i; }
                else EditorGUILayout.LabelField(r.Label, GUILayout.Width(200f));
                if (r.Bar >= 0f)
                {
                    var rect = GUILayoutUtility.GetRect(100f, 16f, GUILayout.Width(100f));
                    EditorGUI.ProgressBar(rect, r.Bar, "");
                }
                EditorGUILayout.LabelField(r.Text);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space();
        }
    }
}
