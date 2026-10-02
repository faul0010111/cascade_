using System;
using System.Collections.Generic;
using Cascade.Agents;
using Cascade.ClaudeIntegration;
using Cascade.Simulation;
using Cascade.World;
using UnityEngine;

namespace Cascade.Runtime
{
    /// <summary>
    /// The only component a scene needs. Builds the building, the simulation, the agents, the player, views and
    /// the Claude layer at runtime, then drives the SimulationWorld every frame. Rebuild() restarts a run.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class CascadeBootstrap : MonoBehaviour
    {
        [Header("Scenario")]
        public ScenarioConfig Scenario = new ScenarioConfig();
        [Tooltip("Optional JSON produced by Cascade > Claude Generator (validated again at load).")]
        public TextAsset GeneratedProfiles;
        public bool SpawnPlayer = true;
        public float NpcBaseSpeed = 3.4f;

        [Header("Claude (optional, the game works without it)")]
        public ClaudeSettings Claude = new ClaudeSettings();

        public static CascadeBootstrap Instance { get; private set; }

        public SimulationWorld World { get; private set; }
        public ScenarioDirector Director { get; private set; }
        public ClaudeGateway Gateway { get; private set; }
        public DialogueDirector Dialogue { get; private set; }
        public LabController Lab { get; private set; }
        public PlayerController Player { get; private set; }
        public CameraRig CameraRig { get; private set; }
        public MaterialLibrary Materials { get; } = new MaterialLibrary();
        public readonly List<NpcAgent> Npcs = new List<NpcAgent>();
        public readonly List<string> LoadWarnings = new List<string>();
        public event Action Rebuilt;

        private Transform _root;

        private void Awake()
        {
            Instance = this;
            if (Scenario.Archetypes.Count == 0)
                foreach (var a in Archetypes.All) Scenario.Archetypes.Add(new ArchetypeWeight { Archetype = a.Name, Weight = 1f });
        }

        private void Start()
        {
            if (FindAnyObjectByType<Light>() == null)
            {
                var light = new GameObject("Sun").AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(55f, 30f, 0f);
            }
            CameraRig = CameraRig.Create();
            Build();
        }

        public void Rebuild(ScenarioConfig config = null, IEnumerable<LabCommand> replay = null)
        {
            if (config != null) Scenario = config;
            Teardown();
            Build();
            if (replay != null) Lab.ScheduleReplay(replay);
        }

        private void Build()
        {
            var graph = BuildingLayouts.CreateOfficeFloor();
            _root = new GameObject("Cascade World").transform;
            var build = BuildingBuilder.Build(graph, _root, Materials);

            World = new SimulationWorld(Scenario.Seed, graph, new UnityLineOfSight());
            World.AlarmWorks = Scenario.AlarmWorks;
            BuildingLayouts.AddDefaultInteractables(World.Building);
            BuildingBuilder.BuildProps(World.Building, _root, Materials, build);
            _extinguisherViews = build.Extinguishers;
            Director = new ScenarioDirector(World, Scenario);

            Gateway = new ClaudeGateway(Claude, new UnityWebRequestTransport(), ApiKey, () => Time.realtimeSinceStartupAsDouble);
            Dialogue = new DialogueDirector(World, Gateway);

            var agentsRoot = new GameObject("Agents").transform;
            agentsRoot.SetParent(_root, false);
            Npcs.Clear();
            var brains = ScenarioBuilder.SpawnAgents(World, Scenario, (id, profile, pos) =>
            {
                var npc = NpcAgent.Create(profile.Name, pos, Materials, agentsRoot);
                npc.BaseSpeed = NpcBaseSpeed;
                Npcs.Add(npc);
                return npc;
            }, LoadProfiles());
            for (int i = 0; i < brains.Count; i++) Npcs[i].Bind(brains[i]);

            if (SpawnPlayer)
            {
                var lobby = graph.Rooms[graph.FindRoom("Lobby")];
                Player = PlayerController.Create(lobby.Center, Materials);
                Player.transform.SetParent(_root, true);
                var brain = ScenarioBuilder.SpawnPlayer(World, Player);
                Player.Bind(brain, World, CameraRig.transform);
                CameraRig.Follow = Player.transform;
            }
            else CameraRig.Follow = null;

            Lab = new LabController(World, (id, profile, pos) =>
            {
                var npc = NpcAgent.Create(profile.Name, pos, Materials, agentsRoot);
                npc.BaseSpeed = NpcBaseSpeed;
                return npc;
            });
            Lab.AgentSpawned += (brain, body) =>
            {
                var npc = body as NpcAgent;
                if (npc == null) return;
                npc.Bind(brain);
                Npcs.Add(npc);
            };
            World.Services.Cognition = Scenario.Cognition ?? new CognitionSettings();

            HazardView.Create(World.Building, Materials, _root);
            if (Rebuilt != null) Rebuilt();
        }

        private List<Transform> _extinguisherViews;

        private void Teardown()
        {
            if (_root != null) Destroy(_root.gameObject);
            foreach (var npc in Npcs) if (npc != null) Destroy(npc.gameObject); // includes NPCs spawned by the Lab
            Npcs.Clear();
            Player = null;
            World = null;
        }

        private List<AgentProfile> LoadProfiles()
        {
            LoadWarnings.Clear();
            if (GeneratedProfiles == null) return null;
            var profiles = new List<AgentProfile>();
            NpcProfileValidator.TryParseList(GeneratedProfiles.text, profiles, LoadWarnings);
            NpcProfileValidator.RemoveDanglingReferences(profiles, LoadWarnings);
            foreach (var w in LoadWarnings) Debug.LogWarning("[Cascade] " + w);
            return profiles;
        }

        private static string ApiKey()
        {
#if UNITY_EDITOR
            var key = UnityEditor.EditorPrefs.GetString("Cascade.AnthropicApiKey", "");
            if (!string.IsNullOrEmpty(key)) return key;
#endif
            return Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        }

        private void Update()
        {
            if (World == null) return;
            Director.Update();
            Lab.Update();
            World.FocusPoints.Clear();
            if (CameraRig != null) World.FocusPoints.Add(CameraRig.Focus);
            if (Player != null) World.FocusPoints.Add(Player.transform.position);
            World.Step(Time.deltaTime);
            SyncExtinguishers();
        }

        private void SyncExtinguishers()
        {
            if (_extinguisherViews == null) return;
            var list = World.Building.Extinguishers;
            for (int i = 0; i < list.Count && i < _extinguisherViews.Count; i++)
            {
                var e = list[i];
                Vector3 pos = e.Position + Vector3.up * 0.35f;
                if (e.HeldBy.IsValid)
                {
                    var holder = World.GetAgent(e.HeldBy);
                    if (holder != null) pos = holder.Body.Position + holder.Body.Forward * 0.4f + Vector3.up * 1f;
                }
                _extinguisherViews[i].position = pos;
            }
        }

        /// <summary>Screen-space helpers used by debug tools.</summary>
        public bool TryGroundPointUnderMouse(out Vector3 point)
        {
            point = Vector3.zero;
            if (CameraRig == null) return false;
            var ray = CameraRig.Camera.ScreenPointToRay(CascadeInput.MousePosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            float enter;
            if (!plane.Raycast(ray, out enter)) return false;
            point = ray.GetPoint(enter);
            return true;
        }
    }
}
