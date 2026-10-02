using System.Collections.Generic;
using System.Text;
using Cascade.Agents;
using Cascade.World;
using UnityEngine;

namespace Cascade.Simulation.Headless
{
    public struct RunResult
    {
        public int Seed;
        public int Npcs;
        public int Safe;
        public int Injured;
        public int Incapacitated;
        public float FirstEvacuation;
        public float LastEvacuation;
        public int HelpEvents;
        public int GroupsFormed;
        public int MessagesDelivered;
        public float SimulatedSeconds;
        public float FireStartTime;
        public Dictionary<GoalType, int> GoalChoices;

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append("seed ").Append(Seed).Append(": safe ").Append(Safe).Append('/').Append(Npcs)
              .Append(", injured ").Append(Injured).Append(", down ").Append(Incapacitated)
              .Append(", evac ").Append(FirstEvacuation.ToString("0")).Append("-").Append(LastEvacuation.ToString("0")).Append("s")
              .Append(", helps ").Append(HelpEvents).Append(", groups ").Append(GroupsFormed).Append(", msgs ").Append(MessagesDelivered);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Runs a full scenario with no Unity scene: the same brains, world and routing as the game, with kinematic
    /// bodies. Used by tests, batch experiments in the Simulation Lab and CI.
    /// </summary>
    public sealed class HeadlessSimulation
    {
        public readonly SimulationWorld World;
        public readonly ScenarioDirector Director;
        public readonly List<AgentBrain> Brains = new List<AgentBrain>();
        public readonly List<HeadlessBody> Bodies = new List<HeadlessBody>();
        private int _helpEvents;
        private int _maxGroups;
        private readonly Dictionary<GoalType, int> _goalChoices = new Dictionary<GoalType, int>();
        private readonly Dictionary<Core.EntityId, GoalOption> _lastGoal = new Dictionary<Core.EntityId, GoalOption>();
        private readonly Dictionary<Core.EntityId, List<Core.EntityId>> _groupScratch = new Dictionary<Core.EntityId, List<Core.EntityId>>();

        public HeadlessSimulation(ScenarioConfig config, RoomGraph graph = null, IList<AgentProfile> profiles = null)
        {
            graph = graph ?? BuildingLayouts.CreateOfficeFloor();
            World = new SimulationWorld(config.Seed, graph, new RoomGraphLineOfSight(graph));
            World.LodEnabled = false;
            World.AlarmWorks = config.AlarmWorks;
            World.Services.Cognition = config.Cognition ?? new Agents.CognitionSettings();
            World.FrameBudgetMs = 1000f;
            BuildingLayouts.AddDefaultInteractables(World.Building);
            Director = new ScenarioDirector(World, config);
            World.SocialActionPublished += e => { if (e.Kind == SocialActionKind.Helped) _helpEvents++; };

            Brains.AddRange(ScenarioBuilder.SpawnAgents(World, config, (id, profile, pos) =>
            {
                var body = new HeadlessBody(pos);
                Bodies.Add(body);
                return body;
            }, profiles));
        }

        public void Step(float dt)
        {
            Director.Update();
            World.Step(dt);
            foreach (var b in Bodies) b.Step(dt);
            foreach (var brain in Brains)
            {
                if (!brain.Goals.HasGoal) continue;
                GoalOption last;
                if (_lastGoal.TryGetValue(brain.Id, out last) && last.Equals(brain.Goals.Current)) continue;
                _lastGoal[brain.Id] = brain.Goals.Current;
                int n;
                _goalChoices.TryGetValue(brain.Goals.Current.Type, out n);
                _goalChoices[brain.Goals.Current.Type] = n + 1;
            }
            World.Groups.CollectGroups(_groupScratch);
            if (_groupScratch.Count > _maxGroups) _maxGroups = _groupScratch.Count;
        }

        public RunResult Run(float seconds, float dt = 0.1f, bool stopWhenResolved = true)
        {
            float end = World.Now + seconds;
            while (World.Now < end)
            {
                Step(dt);
                var s = World.Stats;
                if (stopWhenResolved && Director.FireStarted && s.Safe + s.Incapacitated == s.Agents) break;
            }
            return Result();
        }

        public RunResult Result()
        {
            var s = World.Stats;
            return new RunResult
            {
                Seed = World.Context.Seed,
                Npcs = s.Agents,
                Safe = s.Safe,
                Injured = s.Injured,
                Incapacitated = s.Incapacitated,
                FirstEvacuation = s.FirstEvacuationTime,
                LastEvacuation = s.LastEvacuationTime,
                HelpEvents = _helpEvents,
                GroupsFormed = _maxGroups,
                MessagesDelivered = s.MessagesDelivered,
                SimulatedSeconds = World.Now,
                GoalChoices = new Dictionary<GoalType, int>(_goalChoices)
            };
        }
    }

    /// <summary>Batch experiments: same scenario over many seeds, for comparing designs statistically.</summary>
    public static class ExperimentRunner
    {
        public static List<RunResult> Run(ScenarioConfig baseConfig, int runs, float seconds)
        {
            var results = new List<RunResult>();
            for (int i = 0; i < runs; i++)
            {
                var cfg = baseConfig.Clone();
                cfg.Seed = baseConfig.Seed + i * 7919;
                results.Add(new HeadlessSimulation(cfg).Run(seconds));
            }
            return results;
        }

        public static string Summarize(List<RunResult> results)
        {
            if (results.Count == 0) return "no runs";
            float safe = 0, down = 0, last = 0, helps = 0, groups = 0;
            foreach (var r in results)
            {
                safe += (float)r.Safe / Mathf.Max(1, r.Npcs);
                down += r.Incapacitated;
                last += r.LastEvacuation > 0 ? r.LastEvacuation : r.SimulatedSeconds;
                helps += r.HelpEvents;
                groups += r.GroupsFormed;
            }
            int n = results.Count;
            return "runs " + n + " | safe " + (safe / n * 100f).ToString("0") + "% | down/run " + (down / n).ToString("0.0") +
                   " | resolve time " + (last / n).ToString("0") + "s | helps/run " + (helps / n).ToString("0.0") + " | groups/run " + (groups / n).ToString("0.0");
        }
    }
}
