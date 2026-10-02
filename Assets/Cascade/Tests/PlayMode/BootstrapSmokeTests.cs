using System.Collections;
using Cascade.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cascade.Tests.PlayMode
{
    /// <summary>Smoke test of the Unity layer: the scene builds, NavMesh agents move, the simulation runs without errors.</summary>
    public class BootstrapSmokeTests
    {
        [UnityTest]
        public IEnumerator Bootstrap_BuildsWorld_AndAgentsMove()
        {
            var go = new GameObject("Cascade test");
            var boot = go.AddComponent<CascadeBootstrap>();
            boot.Scenario.NpcCount = 10;
            boot.Scenario.FireDelaySeconds = 1f;
            boot.SpawnPlayer = false;
            yield return null;
            Assert.IsNotNull(boot.World);
            Assert.AreEqual(10, boot.Npcs.Count);
            var start = boot.Npcs[0].transform.position;
            boot.World.Context.Clock.TimeScale = 4f;
            float until = Time.time + 6f;
            while (Time.time < until) yield return null;
            Assert.Greater(boot.World.Now, 5f);
            Assert.IsTrue(boot.Npcs.Exists(n => (n.transform.position - start).sqrMagnitude > 0.5f) || boot.World.Stats.Safe > 0);
            Object.Destroy(go);
        }
    }
}
