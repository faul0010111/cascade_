using System.Collections.Generic;
using System.IO;
using Cascade.Agents;
using Cascade.ClaudeIntegration;
using Cascade.Simulation;
using Cascade.World;
using UnityEditor;
using UnityEngine;

namespace Cascade.EditorTools
{
    /// <summary>
    /// Edit-time content generation with Claude: NPC profiles and test scenarios. Output is validated, the
    /// validation report is shown, and nothing enters the project until you save it as a reviewable JSON asset.
    /// The API key is stored in EditorPrefs (per machine, never in the repository).
    /// </summary>
    public sealed class ClaudeGeneratorWindow : EditorWindow
    {
        private const string KeyPref = "Cascade.AnthropicApiKey";
        private readonly ClaudeSettings _settings = new ClaudeSettings { Enabled = true };
        private UnityWebRequestTransport _transport;
        private ClaudeGateway _gateway;
        private string _apiKey;
        private int _count = 12;
        private string _theme = "a busy Monday with visiting clients";
        private string _raw = "";
        private readonly List<string> _report = new List<string>();
        private List<AgentProfile> _profiles;
        private ScenarioConfig _scenario;
        private Vector2 _scroll;
        private bool _busy;

        public static void Open() { GetWindow<ClaudeGeneratorWindow>("Claude Generator"); }

        private void OnEnable()
        {
            _apiKey = EditorPrefs.GetString(KeyPref, "");
            _transport = new UnityWebRequestTransport { PollMode = true };
            _gateway = new ClaudeGateway(_settings, _transport, () => _apiKey, () => EditorApplication.timeSinceStartup);
            EditorApplication.update += Poll;
        }

        private void OnDisable() { EditorApplication.update -= Poll; }

        private void Poll()
        {
            _transport.Tick();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Generates content at edit time. The game never needs Claude at runtime; generated files are validated again when loaded.", MessageType.None);
            var key = EditorGUILayout.PasswordField("Anthropic API key", _apiKey);
            if (key != _apiKey) { _apiKey = key; EditorPrefs.SetString(KeyPref, key); }
            _settings.GenerationModel = EditorGUILayout.TextField("Model", _settings.GenerationModel);
            _count = EditorGUILayout.IntSlider("NPC count", _count, 1, 40);
            _theme = EditorGUILayout.TextField("Theme", _theme);

            GUI.enabled = !_busy && !string.IsNullOrEmpty(_apiKey);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate NPC profiles")) GenerateProfiles();
            if (GUILayout.Button("Generate scenario")) GenerateScenario();
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;
            if (_busy) EditorGUILayout.LabelField("Waiting for Claude...");

            EditorGUILayout.LabelField("Validation report", EditorStyles.boldLabel);
            foreach (var line in _report) EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(220f));
            EditorGUILayout.TextArea(_raw, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            GUI.enabled = (_profiles != null && _profiles.Count > 0) || _scenario != null;
            if (GUILayout.Button("Save to Assets/Cascade/Data/Generated")) Save();
            GUI.enabled = true;
        }

        private void GenerateProfiles()
        {
            _busy = true;
            _report.Clear();
            _scenario = null;
            _gateway.Send(new ClaudeRequest
            {
                Task = ClaudeTask.NpcGeneration, Model = _settings.GenerationModel, MaxTokens = _settings.MaxTokensGeneration,
                System = PromptBuilder.ProfileSystem, User = PromptBuilder.ProfileUser(_count, _theme), Cacheable = false
            }, result =>
            {
                _busy = false;
                if (!result.Ok) { _report.Add("Request failed: " + result.Status + " " + result.Error); Repaint(); return; }
                _raw = result.Text;
                _profiles = new List<AgentProfile>();
                NpcProfileValidator.TryParseList(result.Text, _profiles, _report);
                NpcProfileValidator.RemoveDanglingReferences(_profiles, _report);
                _report.Insert(0, _profiles.Count + " valid profiles, " + _report.Count + " issues");
                Repaint();
            });
        }

        private void GenerateScenario()
        {
            _busy = true;
            _report.Clear();
            _profiles = null;
            var graph = BuildingLayouts.CreateOfficeFloor();
            _gateway.Send(new ClaudeRequest
            {
                Task = ClaudeTask.ScenarioGeneration, Model = _settings.GenerationModel, MaxTokens = 800,
                System = PromptBuilder.ScenarioSystem(graph), User = "Design a challenging scenario. Theme: " + _theme, Cacheable = false
            }, result =>
            {
                _busy = false;
                if (!result.Ok) { _report.Add("Request failed: " + result.Status + " " + result.Error); Repaint(); return; }
                _raw = result.Text;
                ScenarioConfig cfg;
                if (ScenarioValidator.TryParse(result.Text, graph, new ScenarioConfig(), out cfg, _report)) _scenario = cfg;
                _report.Insert(0, _scenario != null ? "Scenario '" + _scenario.Title + "' valid" : "Scenario rejected");
                Repaint();
            });
        }

        private void Save()
        {
            const string dir = "Assets/Cascade/Data/Generated";
            Directory.CreateDirectory(dir);
            string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = dir + "/" + (_scenario != null ? "scenario_" : "npcs_") + stamp + ".json";
            File.WriteAllText(path, MiniJson.ExtractJsonObject(_raw));
            if (_scenario != null) File.WriteAllText(path.Replace(".json", ".scenario.txt"), JsonUtility.ToJson(_scenario, true));
            AssetDatabase.Refresh();
            Debug.Log("[Cascade] Saved " + path + ". Assign NPC files to CascadeBootstrap.GeneratedProfiles.");
        }
    }
}
