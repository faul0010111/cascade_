using System.Collections.Generic;
using Cascade.Agents;
using Cascade.ClaudeIntegration;
using Cascade.Runtime;
using UnityEngine;

namespace Cascade.DebugTools
{
    /// <summary>
    /// NPC Debugger (in game): click an NPC to inspect personality, state, emotion, needs, the full utility breakdown
    /// of the last decision, plan, memories, relationships, beliefs and event log. Also draws name labels and speech.
    /// </summary>
    public sealed class NpcDebugOverlay : MonoBehaviour
    {
        private enum Tab { Overview, Decision, Learning, Timeline, Mind, Memory, Social, Beliefs, Log }

        private CascadeBootstrap _boot;
        private Tab _tab;
        private Vector2 _scroll;
        private int _breakdownIndex = -1;
        private readonly List<SpokenLine> _bubbles = new List<SpokenLine>();
        private DialogueDirector _subscribed;
        private const float PanelWidth = 420f;
        private float _timelineCursor = 1f; // 0..1 across the recorded history
        private readonly List<float> _series = new List<float>();

        private void Awake() { _boot = GetComponent<CascadeBootstrap>(); }

        private void Update()
        {
            if (_boot == null || _boot.World == null) return;
            if (_subscribed != _boot.Dialogue)
            {
                _subscribed = _boot.Dialogue;
                _bubbles.Clear();
                if (_subscribed != null) _subscribed.LineSpoken += l => _bubbles.Add(l);
            }
            _bubbles.RemoveAll(l => _boot.World.Now - l.Time > 3.5f);

            if (DebugState.Tool == LabTool.Select && CascadeInput.LeftClick && !MouseOverPanels()) Pick();
            if (DebugState.Selected != null) _boot.World.SelectedAgent = DebugState.Selected.Brain.Id;
        }

        private bool MouseOverPanels()
        {
            var m = CascadeInput.GuiMousePosition;
            return m.x > Screen.width - PanelWidth - 10f || m.x < SimulationLabPanel.Width + 10f;
        }

        private void Pick()
        {
            var ray = _boot.CameraRig.Camera.ScreenPointToRay(CascadeInput.MousePosition);
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, 500f, ~0, QueryTriggerInteraction.Ignore))
            {
                var npc = hit.collider.GetComponentInParent<NpcAgent>();
                DebugState.Selected = npc;
                _breakdownIndex = -1;
            }
        }

        private void OnGUI()
        {
            if (_boot == null || _boot.World == null) return;
            var cam = _boot.CameraRig != null ? _boot.CameraRig.Camera : null;
            if (cam != null)
            {
                if (DebugState.ShowLabels) DrawLabels(cam);
                if (DebugState.ShowSpeech) DrawSpeech(cam);
            }
            DrawPlayerHint();
            if (DebugState.Selected != null && DebugState.Selected.Brain != null) DrawInspector(DebugState.Selected.Brain);
        }

        private void DrawLabels(Camera cam)
        {
            foreach (var npc in _boot.Npcs)
            {
                if (npc == null || npc.Brain == null) continue;
                var p = cam.WorldToScreenPoint(npc.transform.position + Vector3.up * 2.3f);
                if (p.z < 0f) continue;
                var b = npc.Brain;
                string goal = b.Goals.HasGoal ? b.Goals.Current.Type.ToString() : "";
                string text = (npc == DebugState.Selected ? "<b>" + b.Name + "</b>" : b.Name) + "\n<size=9>" + goal + "</size>";
                var style = ReportDrawer.Small;
                var old = GUI.color;
                GUI.color = Palette.ForEmotion(b.Emotion.Current);
                GUI.Label(new Rect(p.x - 50f, Screen.height - p.y - 14f, 100f, 32f), text, style);
                GUI.color = old;
            }
        }

        private void DrawSpeech(Camera cam)
        {
            foreach (var line in _bubbles)
            {
                var a = _boot.World.GetAgent(line.Speaker);
                if (a == null) continue;
                var p = cam.WorldToScreenPoint(a.Body.Position + Vector3.up * 3.1f);
                if (p.z < 0f) continue;
                GUI.Box(new Rect(p.x - 90f, Screen.height - p.y - 22f, 180f, 36f), (line.Generated ? "* " : "") + line.Text, ReportDrawer.Small);
            }
        }

        private void DrawPlayerHint()
        {
            var player = _boot.Player;
            if (player == null) return;
            string last = Time.time - player.LastActionTime < 2.5f ? "   >> " + player.LastAction : "";
            GUI.Label(new Rect(SimulationLabPanel.Width + 20f, Screen.height - 28f, 900f, 24f),
                "WASD move | E follow me | Q warn | F help/carry | G push | Tab free camera | click NPC to inspect" + last, ReportDrawer.Small);
        }

        /// <summary>Cognitive timelines with a cursor: pick a moment and see why the NPC acted as it did then.</summary>
        private void DrawTimeline(AgentBrain b, float w)
        {
            var samples = b.History.Samples;
            if (samples.Count == 0) { GUILayout.Label("No history yet.", ReportDrawer.Small); return; }
            int marker = Mathf.Clamp(Mathf.RoundToInt(_timelineCursor * (samples.Count - 1)), 0, samples.Count - 1);
            Series(b, "Fear", s => s.Fear, new Color(1f, 0.4f, 0.2f), w, marker);
            Series(b, "Knowledge confidence", s => s.KnowledgeConfidence, new Color(0.3f, 0.8f, 1f), w, marker);
            Series(b, "Decision confidence", s => s.DecisionConfidence, new Color(0.6f, 1f, 0.5f), w, marker);
            Series(b, "|Prediction error|", s => Mathf.Abs(s.PredictionError), new Color(1f, 0.9f, 0.3f), w, marker);
            Series(b, "Highest trust", s => s.TopTrust, new Color(0.8f, 0.6f, 1f), w, marker);
            GUILayout.Label("Goals: " + GoalStrip(samples), ReportDrawer.Small);
            _timelineCursor = GUILayout.HorizontalSlider(_timelineCursor, 0f, 1f);
            float t = samples[marker].Time;
            ReportDrawer.Section(BrainReport.WhyAt(b, t), w);
        }

        private void Series(AgentBrain b, string label, System.Func<CognitiveSample, float> f, Color c, float w, int marker)
        {
            _series.Clear();
            foreach (var s in b.History.Samples) _series.Add(f(s));
            GUILayout.Label(label, ReportDrawer.Small);
            var rect = GUILayoutUtility.GetRect(w, 26f, GUILayout.Width(w));
            ReportDrawer.Sparkline(rect, _series, c, marker);
        }

        private static string GoalStrip(IReadOnlyList<CognitiveSample> samples)
        {
            var sb = new System.Text.StringBuilder();
            GoalType last = (GoalType)(-1);
            foreach (var s in samples)
            {
                if (s.Goal == last) continue;
                last = s.Goal;
                sb.Append(s.Time.ToString("0")).Append("s ").Append(s.Goal).Append("  ");
            }
            return sb.ToString();
        }

        private void DrawInspector(AgentBrain b)
        {
            var rect = new Rect(Screen.width - PanelWidth - 10f, 10f, PanelWidth, Screen.height - 50f);
            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>NPC DEBUGGER</b>", ReportDrawer.Small);
            if (GUILayout.Button("x", GUILayout.Width(22f))) DebugState.Selected = null;
            GUILayout.EndHorizontal();
            _tab = (Tab)GUILayout.Toolbar((int)_tab, System.Enum.GetNames(typeof(Tab)), GUILayout.Height(20f));
            _scroll = GUILayout.BeginScrollView(_scroll);
            float w = PanelWidth - 30f;
            switch (_tab)
            {
                case Tab.Overview:
                    ReportDrawer.Section(BrainReport.Overview(b), w);
                    ReportDrawer.Section(BrainReport.InternalState(b), w);
                    break;
                case Tab.Decision:
                    var latest = b.Log.Latest;
                    ReportDrawer.Section(BrainReport.Decision(b), w, i => _breakdownIndex = i);
                    if (latest != null)
                    {
                        var sorted = new List<OptionTrace>(latest.Options);
                        sorted.Sort((x, y) => y.FinalScore.CompareTo(x.FinalScore));
                        var chosen = _breakdownIndex >= 0 && _breakdownIndex < sorted.Count ? sorted[_breakdownIndex] : latest.Chosen;
                        ReportDrawer.Section(BrainReport.Breakdown(chosen), w);
                        GUILayout.Label("(click an option to see its breakdown)", ReportDrawer.Small);
                    }
                    break;
                case Tab.Learning:
                    ReportDrawer.Section(BrainReport.SelfAssessment(b), w);
                    ReportDrawer.Section(BrainReport.Learning(b), w);
                    ReportDrawer.Section(BrainReport.Experiences(b), w);
                    ReportDrawer.Section(BrainReport.LearningEvents(b), w);
                    break;
                case Tab.Timeline:
                    DrawTimeline(b, w);
                    break;
                case Tab.Mind:
                    ReportDrawer.Section(BrainReport.Personality(b), w);
                    ReportDrawer.Section(BrainReport.Needs(b), w);
                    break;
                case Tab.Memory:
                    ReportDrawer.Section(BrainReport.Memories(b, 16), w);
                    break;
                case Tab.Social:
                    ReportDrawer.Section(BrainReport.Relationships(b, 16), w);
                    break;
                case Tab.Beliefs:
                    ReportDrawer.Section(BrainReport.Beliefs(b), w);
                    break;
                case Tab.Log:
                    for (int i = b.Log.Events.Count - 1; i >= 0; i--) GUILayout.Label(b.Log.Events[i], ReportDrawer.Small);
                    break;
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("Copy full report to clipboard")) GUIUtility.systemCopyBuffer = BrainReport.ToPlainText(b);
            GUILayout.EndArea();
        }
    }
}
