using System.Collections.Generic;
using System.Threading.Tasks;
using Cascade.Agents;
using Cascade.Runtime;
using Cascade.Simulation;
using Cascade.Simulation.Headless;
using Cascade.World;
using UnityEngine;

namespace Cascade.DebugTools
{
    /// <summary>
    /// Simulation Lab: time control, scenario parameters, click tools (ignite, extinguish, lock doors, spawn, injure),
    /// gizmo toggles, LOD and performance stats, Claude status, trace export and headless batch experiments.
    /// </summary>
    public sealed class SimulationLabPanel : MonoBehaviour
    {
        public const float Width = 270f;
        private CascadeBootstrap _boot;
        private Vector2 _scroll;
        private bool _visible = true;
        private string _seedText;
        private int _batchRuns = 10;
        private Task<string> _batch;
        private string _batchResult = "";
        private string _status = "";
        private int _spawnIndex = 1000;
        private NpcAgent _compare;
        private int _traitIndex;
        private int _expSeeds = 10;
        private Task<string> _experiment;
        private string _experimentResult = "";
        private List<LabCommand> _lastLog;

        private void Awake() { _boot = GetComponent<CascadeBootstrap>(); }

        private void Update()
        {
            if (_boot == null || _boot.World == null) return;
            var clock = _boot.World.Context.Clock;
            if (CascadeInput.Pressed(InputKey.Space)) clock.Paused = !clock.Paused;
            if (CascadeInput.Pressed(InputKey.Period)) clock.StepOnce();
            if (CascadeInput.Pressed(InputKey.R)) _boot.Rebuild();
            if (DebugState.Tool != LabTool.Select && CascadeInput.LeftClick && CascadeInput.GuiMousePosition.x > Width + 10f) ApplyTool();
            if (_experiment != null && _experiment.IsCompleted)
            {
                _experimentResult = _experiment.IsFaulted ? "Experiment failed: " + _experiment.Exception.GetBaseException().Message : _experiment.Result;
                _experiment = null;
            }
            if (_batch != null && _batch.IsCompleted)
            {
                _batchResult = _batch.IsFaulted ? "Batch failed: " + _batch.Exception.GetBaseException().Message : _batch.Result;
                _batch = null;
            }
        }

        private void ApplyTool()
        {
            Vector3 p;
            if (!_boot.TryGroundPointUnderMouse(out p)) return;
            var world = _boot.World;
            var graph = world.Building.Graph;
            switch (DebugState.Tool)
            {
                case LabTool.Ignite:
                    _status = world.Building.Hazards.Ignite(p, 0.5f) ? "Fire started in " + graph.RoomName(graph.RoomAt(p)) : "Nothing flammable there";
                    break;
                case LabTool.Extinguish:
                    world.Building.Hazards.Extinguish(p, 3f, 1f);
                    _status = "Extinguished area";
                    break;
                case LabTool.ToggleDoor:
                    Door nearest = null;
                    float best = 3f;
                    foreach (var d in graph.Doors)
                    {
                        float dist = Vector3.Distance(d.Position, new Vector3(p.x, 0f, p.z));
                        if (dist < best) { best = dist; nearest = d; }
                    }
                    if (nearest == null) { _status = "No door near click"; break; }
                    var next = nearest.State == DoorState.Open ? DoorState.Closed : nearest.State == DoorState.Closed ? DoorState.Locked : DoorState.Open;
                    world.Building.SetDoorState(nearest.Id, next);
                    _status = "Door " + nearest.Id + " -> " + next;
                    break;
                case LabTool.SpawnNpc:
                    SpawnAt(p);
                    break;
                case LabTool.Injure:
                    foreach (var a in world.Agents)
                        if (!a.IsPlayer && Vector3.Distance(a.Body.Position, p) < 1.5f) { a.Brain.ApplyDamage(0.65f, world.Now); _status = a.Brain.Name + " injured"; break; }
                    break;
            }
        }

        private void SpawnAt(Vector3 p)
        {
            var world = _boot.World;
            var rng = world.Context.CreateRng("lab-spawn:" + _spawnIndex);
            var profile = AgentFactory.CreateProfile(_spawnIndex++, Archetypes.Find(DebugState.SpawnArchetype), rng);
            var id = world.Context.Entities.Register(Core.EntityKind.Npc, profile.Name);
            var npc = NpcAgent.Create(profile.Name, p, _boot.Materials, _boot.transform);
            var brain = new AgentBrain(id, profile, world.Services, npc);
            AgentFactory.ApplyPriors(brain, world.Building.Graph, ScenarioBuilder.FindMainExit(world.Building.Graph, _boot.Scenario.MainExitRoom));
            npc.Bind(brain);
            world.Register(brain, npc, false);
            _boot.Npcs.Add(npc);
            _status = "Spawned " + profile.Name + " (" + profile.Archetype + ")";
        }

        private LabCommand ForSelected(LabCommandKind kind)
        {
            var sel = DebugState.Selected;
            return new LabCommand { Kind = kind, Agent = sel != null && sel.Brain != null ? sel.Brain.Id.Value : 0 };
        }

        private int NearestExitDoor(Vector3 p)
        {
            var g = _boot.World.Building.Graph;
            int best = -1;
            float bestD = float.MaxValue;
            foreach (var d in g.Doors)
            {
                if (!g.IsExitDoor(d.Id)) continue;
                float dist = Vector3.Distance(d.Position, p);
                if (dist < bestD) { bestD = dist; best = d.Id; }
            }
            return best;
        }

        /// <summary>Cognitive experiments on the selected NPC. Every action goes through LabController (logged, replayable).</summary>
        private void DrawCognitiveLab(GUIStyle s)
        {
            var lab = _boot.Lab;
            GUILayout.Label("<b>Cognitive lab</b>", s);
            var sel = DebugState.Selected;
            if (sel == null || sel.Brain == null) GUILayout.Label("(select an NPC to experiment on it)", s);
            else
            {
                var agent = _boot.World.GetAgent(sel.Brain.Id);
                GUILayout.Label("Subject: " + sel.Brain.Name, s);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(agent != null && agent.Frozen ? "Unfreeze" : "Freeze")) lab.Execute(ForSelected(agent != null && agent.Frozen ? LabCommandKind.Unfreeze : LabCommandKind.Freeze));
                if (GUILayout.Button("Clone")) { var c = ForSelected(LabCommandKind.Clone); c.Value = 0f; lab.Execute(c); }
                if (GUILayout.Button("Clone+mind")) { var c = ForSelected(LabCommandKind.Clone); c.Value = 1f; lab.Execute(c); }
                if (GUILayout.Button("Reset mind")) lab.Execute(ForSelected(LabCommandKind.ResetMind));
                GUILayout.EndHorizontal();

                _traitIndex = GUILayout.SelectionGrid(_traitIndex, System.Enum.GetNames(typeof(Trait)), 3);
                var trait = (Trait)_traitIndex;
                float v = sel.Brain.Personality[trait];
                GUILayout.Label(trait + " " + v.ToString("0.00"), s);
                float nv = GUILayout.HorizontalSlider(v, 0f, 1f);
                if (Mathf.Abs(nv - v) > 0.01f) { var c = ForSelected(LabCommandKind.SetTrait); c.Other = _traitIndex; c.Value = nv; lab.Execute(c); }

                int exit = NearestExitDoor(sel.transform.position);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Tell: exit " + exit + " blocked"))
                {
                    var c = ForSelected(LabCommandKind.InjectBelief);
                    c.Int = (int)FactType.DoorState; c.Subject = exit; c.Value = (float)(int)DoorState.Blocked; c.Value2 = 0.8f;
                    lab.Execute(c);
                }
                if (GUILayout.Button("Tell: exit " + exit + " open"))
                {
                    var c = ForSelected(LabCommandKind.InjectBelief);
                    c.Int = (int)FactType.DoorState; c.Subject = exit; c.Value = (float)(int)DoorState.Open; c.Value2 = 0.8f;
                    lab.Execute(c);
                }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Rumor nearby: exit " + exit + " blocked (may be false)"))
                    lab.Execute(new LabCommand { Kind = LabCommandKind.InjectRumor, Int = (int)FactType.DoorState, Subject = exit, Value = (float)(int)DoorState.Blocked, Value2 = 12f, Position = sel.transform.position });

                SimAgent nearest = null;
                float nd = 6f;
                foreach (var a in _boot.World.Agents)
                {
                    if (a.IsPlayer || a.Id == sel.Brain.Id) continue;
                    float d = Vector3.Distance(a.Body.Position, sel.transform.position);
                    if (d < nd) { nd = d; nearest = a; }
                }
                if (nearest != null)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Helps " + nearest.Brain.Name)) { var c = ForSelected(LabCommandKind.SocialAct); c.Other = nearest.Id.Value; c.Int = (int)SocialActionKind.Helped; lab.Execute(c); }
                    if (GUILayout.Button("Pushes " + nearest.Brain.Name)) { var c = ForSelected(LabCommandKind.SocialAct); c.Other = nearest.Id.Value; c.Int = (int)SocialActionKind.Pushed; lab.Execute(c); }
                    GUILayout.EndHorizontal();
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Injure")) { var c = ForSelected(LabCommandKind.Injure); c.Value = 0.65f; lab.Execute(c); }
                if (GUILayout.Button(_compare == sel ? "Unpin" : "Pin to compare")) _compare = _compare == sel ? null : sel;
                GUILayout.EndHorizontal();
                if (_compare != null && _compare != sel && _compare.Brain != null) DrawComparison(_compare.Brain, sel.Brain, s);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Trigger alarm")) lab.Execute(new LabCommand { Kind = LabCommandKind.TriggerAlarm });
            if (GUILayout.Button("Replay this run")) { _lastLog = new List<LabCommand>(lab.Log); _boot.Rebuild(null, _lastLog); }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(lab.LastResult)) GUILayout.Label(lab.LastResult, s);
            GUILayout.Label("Lab log: " + lab.Log.Count + " commands (replay = same seed + same commands)", s);

            GUILayout.Label("<b>Cognitive experiment</b> (headless, background)", s);
            GUILayout.Label("Seeds " + _expSeeds, s);
            _expSeeds = Mathf.RoundToInt(GUILayout.HorizontalSlider(_expSeeds, 2, 100));
            GUI.enabled = _experiment == null;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Per-archetype metrics"))
            {
                var cfg = _boot.Scenario.Clone();
                int seeds = _expSeeds;
                _experiment = Task.Run(() => CognitiveExperiment.Report(CognitiveExperiment.Run(cfg, seeds, 180f)));
            }
            if (GUILayout.Button("Fire drills x4 (learning on/off)"))
            {
                int seeds = _expSeeds;
                _experiment = Task.Run(() => Drills(seeds));
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            if (_experiment != null) GUILayout.Label("Running...", s);
            if (!string.IsNullOrEmpty(_experimentResult)) GUILayout.TextArea(_experimentResult);
        }

        private static string Drills(int seeds)
        {
            var sb = new System.Text.StringBuilder("Blocked-main-exit drills, survival %\n");
            foreach (var learn in new[] { true, false })
                foreach (var eps in new[] { 1, 4 })
                {
                    var cog = new CognitionSettings { Learning = learn, SocialLearning = learn, LearnedRouteCosts = learn };
                    var g = CognitiveExperiment.Run(new ScenarioConfig { Seed = 2000, Cognition = cog }, seeds, 180f, eps, c => ScenarioLibrary.BlockedMainExit(c.Seed, c.Cognition));
                    int agents = 0, surv = 0;
                    foreach (var m in g.Values) { agents += m.Agents; surv += m.Survived; }
                    sb.Append("learning ").Append(learn ? "on " : "off").Append(" episode ").Append(eps).Append(": ").Append((surv * 100f / Mathf.Max(1, agents)).ToString("0")).Append("%\n");
                }
            return sb.ToString();
        }

        private static void DrawComparison(AgentBrain a, AgentBrain b, GUIStyle s)
        {
            GUILayout.Label("<b>" + a.Name + "  vs  " + b.Name + "</b>", s);
            Row("Goal", a.Goals.Current.Label(a), b.Goals.Current.Label(b), s);
            Row("Emotion", a.Emotion.Current.ToString(), b.Emotion.Current.ToString(), s);
            Row("Knowledge conf.", P(a.Self.KnowledgeConfidence), P(b.Self.KnowledgeConfidence), s);
            Row("Decision conf.", P(a.Self.DecisionConfidence), P(b.Self.DecisionConfidence), s);
            Row("Learning events", a.Learning.TotalEvents.ToString(), b.Learning.TotalEvents.ToString(), s);
            Row("Risk perception", a.Learning.Dispositions.RiskPerception.ToString("+0.00;-0.00"), b.Learning.Dispositions.RiskPerception.ToString("+0.00;-0.00"), s);
            Row("Reason", a.Log.Latest != null ? a.Log.Latest.Reason : "-", b.Log.Latest != null ? b.Log.Latest.Reason : "-", s);
        }

        private static string P(float v) => Mathf.RoundToInt(v * 100) + "%";

        private static void Row(string label, string a, string b, GUIStyle s)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, s, GUILayout.Width(80f));
            GUILayout.Label(a, s, GUILayout.Width(80f));
            GUILayout.Label(b, s);
            GUILayout.EndHorizontal();
        }

        private void OnGUI()
        {
            if (_boot == null || _boot.World == null) return;
            if (GUI.Button(new Rect(10f, 10f, 24f, 20f), _visible ? "<" : ">")) _visible = !_visible;
            if (!_visible) return;
            GUILayout.BeginArea(new Rect(40f, 10f, Width - 30f, Screen.height - 50f), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            var world = _boot.World;
            var clock = world.Context.Clock;
            var s = ReportDrawer.Small;

            GUILayout.Label("<b>SIMULATION LAB</b>  t=" + world.Now.ToString("0.0") + "s", s);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(clock.Paused ? "Play" : "Pause")) clock.Paused = !clock.Paused;
            if (GUILayout.Button("Step")) clock.StepOnce();
            if (GUILayout.Button("Restart")) _boot.Rebuild();
            GUILayout.EndHorizontal();
            GUILayout.Label("Time scale x" + clock.TimeScale.ToString("0.0"), s);
            clock.TimeScale = GUILayout.HorizontalSlider(clock.TimeScale, 0.1f, 8f);

            var st = world.Stats;
            GUILayout.Label("<b>Status</b>  safe " + st.Safe + "/" + st.Agents + "  injured " + st.Injured + "  down " + st.Incapacitated, s);
            GUILayout.Label("Ticks/frame " + st.TicksThisFrame + " (deferred " + st.DeferredThisFrame + ")  avg " + st.AvgTickMs.ToString("0.00") + "ms  max " + st.MaxTickMs.ToString("0.00") + "ms", s);
            GUILayout.Label("Messages " + st.MessagesDelivered + "  fire " + world.Building.Hazards.TotalFire().ToString("0") + (_boot.Director.FireStarted ? "" : "  (fire at " + _boot.Scenario.FireDelaySeconds + "s)"), s);

            GUILayout.Label("<b>Scenario</b> (applies on Restart)", s);
            var cfg = _boot.Scenario;
            if (_seedText == null) _seedText = cfg.Seed.ToString();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Seed", s, GUILayout.Width(40f));
            _seedText = GUILayout.TextField(_seedText);
            int seed;
            if (int.TryParse(_seedText, out seed)) cfg.Seed = seed;
            if (GUILayout.Button("Random", GUILayout.Width(60f))) { cfg.Seed = Random.Range(1, 999999); _seedText = cfg.Seed.ToString(); }
            GUILayout.EndHorizontal();
            GUILayout.Label("NPCs " + cfg.NpcCount, s);
            cfg.NpcCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(cfg.NpcCount, 1, 200));
            GUILayout.Label("Fire delay " + cfg.FireDelaySeconds.ToString("0") + "s", s);
            cfg.FireDelaySeconds = GUILayout.HorizontalSlider(cfg.FireDelaySeconds, 0f, 120f);
            GUILayout.Label("Fire room: " + cfg.FireRoom, s);
            GUILayout.BeginHorizontal();
            foreach (var room in new[] { "Kitchen", "Storage", "Server Room", "Lobby", "Office A" })
                if (GUILayout.Button(room.Split(' ')[0], GUILayout.Width(44f))) cfg.FireRoom = room;
            GUILayout.EndHorizontal();
            cfg.AlarmWorks = GUILayout.Toggle(cfg.AlarmWorks, "Alarm works");
            GUILayout.Label("Family pairs " + cfg.FamilyPairs + "   injured at start " + cfg.InjuredAtStart, s);
            cfg.FamilyPairs = Mathf.RoundToInt(GUILayout.HorizontalSlider(cfg.FamilyPairs, 0, 10));
            cfg.InjuredAtStart = Mathf.RoundToInt(GUILayout.HorizontalSlider(cfg.InjuredAtStart, 0, 10));

            GUILayout.Label("<b>Click tool</b>", s);
            DebugState.Tool = (LabTool)GUILayout.SelectionGrid((int)DebugState.Tool, System.Enum.GetNames(typeof(LabTool)), 3);
            if (DebugState.Tool == LabTool.SpawnNpc)
            {
                GUILayout.BeginHorizontal();
                foreach (var a in Archetypes.All)
                    if (GUILayout.Toggle(DebugState.SpawnArchetype == a.Name, a.Name.Substring(0, 3), "Button")) DebugState.SpawnArchetype = a.Name;
                GUILayout.EndHorizontal();
            }
            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status, s);

            DrawCognitiveLab(s);

            GUILayout.Label("<b>Visualization</b>", s);
            DebugState.ShowLabels = GUILayout.Toggle(DebugState.ShowLabels, "Names and goals");
            DebugState.ShowSpeech = GUILayout.Toggle(DebugState.ShowSpeech, "Speech");
            DebugState.ShowPerception = GUILayout.Toggle(DebugState.ShowPerception, "Perception (selected)");
            DebugState.ShowRoutes = GUILayout.Toggle(DebugState.ShowRoutes, "Routes");
            DebugState.ShowGroups = GUILayout.Toggle(DebugState.ShowGroups, "Follow links");
            DebugState.ShowMessages = GUILayout.Toggle(DebugState.ShowMessages, "Messages");
            DebugState.ShowBeliefVsTruth = GUILayout.Toggle(DebugState.ShowBeliefVsTruth, "Believed fire vs truth (selected)");
            world.LodEnabled = GUILayout.Toggle(world.LodEnabled, "Behavioral LOD");
            GUILayout.Label("Frame budget " + world.FrameBudgetMs.ToString("0.0") + "ms", s);
            world.FrameBudgetMs = GUILayout.HorizontalSlider(world.FrameBudgetMs, 0.5f, 16f);

            GUILayout.Label("<b>Claude</b>", s);
            var gw = _boot.Gateway;
            GUILayout.Label(gw.IsAvailable ? "available" : (gw.Settings.Enabled ? "kill switch / unavailable" : "disabled (templates only)"), s);
            gw.KillSwitch = GUILayout.Toggle(gw.KillSwitch, "Kill switch");
            GUILayout.Label("requests " + gw.RequestsSent + "  cache hits " + gw.CacheHits + "  generated lines " + _boot.Dialogue.GeneratedLines + "  late " + _boot.Dialogue.LateDiscarded, s);

            GUILayout.Label("<b>Experiments</b>", s);
            if (GUILayout.Button("Export decision traces (JSONL)"))
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath, "cascade_traces_" + cfg.Seed + ".jsonl");
                int n = TraceExporter.Export(world, path);
                _status = n + " traces -> " + path;
            }
            GUILayout.Label("Headless batch runs: " + _batchRuns, s);
            _batchRuns = Mathf.RoundToInt(GUILayout.HorizontalSlider(_batchRuns, 1, 50));
            GUI.enabled = _batch == null;
            if (GUILayout.Button(_batch == null ? "Run batch in background" : "Running..."))
            {
                var baseCfg = cfg.Clone();
                int runs = _batchRuns;
                _batch = Task.Run(() => ExperimentRunner.Summarize(ExperimentRunner.Run(baseCfg, runs, 240f)));
            }
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(_batchResult)) GUILayout.Label(_batchResult, s);
            GUILayout.Label("Space pause | . step | R restart", s);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
