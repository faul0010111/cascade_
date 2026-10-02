using System;
using System.Collections.Generic;
using Cascade.Agents;
using Cascade.Core;
using Cascade.World;
using UnityEngine;

namespace Cascade.Simulation
{
    /// <summary>
    /// Spawns agents for a scenario. Shared by the Unity bootstrap and the headless runner so both produce the
    /// same population for the same seed. The caller supplies the body factory (NavMesh agent or kinematic body).
    /// </summary>
    public static class ScenarioBuilder
    {
        public delegate IAgentBody BodyFactory(EntityId id, AgentProfile profile, Vector3 position);

        public static List<AgentBrain> SpawnAgents(SimulationWorld world, ScenarioConfig config, BodyFactory createBody,
                                                   IList<AgentProfile> authoredProfiles = null)
        {
            var graph = world.Building.Graph;
            var rng = world.Context.CreateRng("spawn");
            int mainExit = FindMainExit(graph, config.MainExitRoom);
            var spawnRooms = new List<int>();
            foreach (var r in graph.Rooms) if (!r.IsExterior) spawnRooms.Add(r.Id);

            var brains = new List<AgentBrain>();
            for (int i = 0; i < config.NpcCount; i++)
            {
                AgentProfile profile;
                if (authoredProfiles != null && i < authoredProfiles.Count) profile = authoredProfiles[i];
                else profile = AgentFactory.CreateProfile(i, AgentFactory.PickArchetype(config.Archetypes, rng), rng);

                var room = graph.Rooms[spawnRooms[rng.Range(0, spawnRooms.Count)]];
                var pos = room.Clamp(room.Center + new Vector3(rng.Range(-4f, 4f), 0f, rng.Range(-4f, 4f)), 1f);
                var id = world.Context.Entities.Register(EntityKind.Npc, profile.Name);
                var body = createBody(id, profile, pos);
                var brain = new AgentBrain(id, profile, world.Services, body);
                AgentFactory.ApplyPriors(brain, graph, mainExit);
                brains.Add(brain);
                world.Register(brain, body, false);
            }
            AgentFactory.ResolveRelationships(brains, rng, config.FamilyPairs, config.ColleagueLinkChance);
            for (int i = 0; i < config.InjuredAtStart && i < brains.Count; i++)
                brains[i].State.SetValue(StateVar.Health, 0.3f);
            return brains;
        }

        /// <summary>The player is an entity with a player-controlled brain: it perceives, so what it says is what it saw.</summary>
        public static AgentBrain SpawnPlayer(SimulationWorld world, IAgentBody body)
        {
            var id = world.Context.Entities.Register(EntityKind.Player, "Player");
            var profile = new AgentProfile { Name = "Player", Archetype = "Player", KnowsAllExits = true };
            var brain = new AgentBrain(id, profile, world.Services, body, playerControlled: true);
            AgentFactory.ApplyPriors(brain, world.Building.Graph, -1);
            world.Register(brain, body, true);
            return brain;
        }

        public static int FindMainExit(RoomGraph graph, string exteriorRoomName)
        {
            int room = graph.FindRoom(exteriorRoomName);
            foreach (var d in graph.Doors)
                if (graph.IsExitDoor(d.Id) && (room < 0 || d.Connects(room))) return d.Id;
            return -1;
        }
    }
}
