using Cascade.Runtime;
using Cascade.Simulation;
using Cascade.Simulation.Headless;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cascade.EditorTools
{
    public static class CascadeMenu
    {
        private const string ScenePath = "Assets/Cascade/Scenes/CascadeDemo.unity";

        [MenuItem("Cascade/Create Demo Scene", priority = 0)]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("Cascade").AddComponent<CascadeBootstrap>();
            System.IO.Directory.CreateDirectory("Assets/Cascade/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[Cascade] Demo scene created at " + ScenePath + ". Press Play.");
        }

        [MenuItem("Cascade/Run Headless Batch (10 seeds)", priority = 20)]
        public static void RunBatch()
        {
            var boot = Object.FindAnyObjectByType<CascadeBootstrap>();
            var cfg = boot != null ? boot.Scenario.Clone() : new ScenarioConfig();
            var results = ExperimentRunner.Run(cfg, 10, 240f);
            foreach (var r in results) Debug.Log("[Cascade] " + r.Summary());
            Debug.Log("[Cascade] " + ExperimentRunner.Summarize(results));
        }

        [MenuItem("Cascade/Open NPC Inspector", priority = 40)]
        public static void OpenInspector() { NpcInspectorWindow.Open(); }

        [MenuItem("Cascade/Claude Generator", priority = 41)]
        public static void OpenGenerator() { ClaudeGeneratorWindow.Open(); }
    }
}
