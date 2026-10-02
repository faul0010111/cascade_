using System.Collections.Generic;
using Cascade.Agents;
using Cascade.Simulation.Headless;
using Cascade.World;
using UnityEngine;

namespace Cascade.Simulation
{
    /// <summary>
    /// Scenarios defined ONLY by initial conditions (who knows what, who trusts whom, who is hurt). There is no
    /// scripted response anywhere: what happens emerges from perception, beliefs, memory, personality, emotion,
    /// relationships, needs, learning, goals and planning.
    /// </summary>
    public static class ScenarioLibrary
    {
        /// <summary>
        /// "Fire blocks the main exit." A knows a secondary exit. B trusts A. C distrusts A. D has an injured friend.
        /// E is very curious. F is very fearful. Everyone else is a visitor who only knows the main entrance.
        /// </summary>
        public static HeadlessSimulation BlockedMainExit(int seed, CognitionSettings cognition = null)
        {
            var cfg = new ScenarioConfig
            {
                Title = "Fire blocks main exit", Seed = seed, NpcCount = 12, FireRoom = "Lobby", FireDelaySeconds = 3f,
                InitialFireIntensity = 0.8f, FamilyPairs = 0, InjuredAtStart = 0, ColleagueLinkChance = 0f,
                Cognition = cognition ?? new CognitionSettings()
            };
            var profiles = new List<AgentProfile>
            {
                P("Alice", Archetypes.Organizer, employee: true),
                P("Bruno", Archetypes.Average),
                P("Caio", Archetypes.Loner),
                P("Dora", Archetypes.Protector),
                P("Enzo", Archetypes.Curious),
                P("Fabi", Archetypes.Anxious),
                P("Lia", Archetypes.Average),
            };
            profiles[1].Relationships.Add(new InitialRelationship { TargetName = "Alice", Kind = RelationshipKind.Colleague, Trust = 0.9f, Affection = 0.4f });
            profiles[2].Relationships.Add(new InitialRelationship { TargetName = "Alice", Kind = RelationshipKind.Acquaintance, Trust = 0.1f, Affection = 0f });
            profiles[3].Relationships.Add(new InitialRelationship { TargetName = "Lia", Kind = RelationshipKind.Friend, Trust = 0.9f, Affection = 0.9f });
            profiles[4].Personality[Trait.Curiosity] = 0.98f;
            profiles[5].Personality[Trait.Fearfulness] = 0.98f;
            var rng = new Core.Rng((ulong)(seed * 31 + 7));
            for (int i = profiles.Count; i < cfg.NpcCount; i++) profiles.Add(AgentFactory.CreateProfile(i, Archetypes.Average, rng));
            foreach (var p in profiles) if (p.Name != "Alice") p.KnowsAllExits = false;

            var sim = new HeadlessSimulation(cfg, null, profiles);
            var lia = sim.Brains.Find(b => b.Name == "Lia");
            if (lia != null) lia.State.SetValue(StateVar.Health, 0.3f);
            // The main exit itself is on fire.
            var g = sim.World.Building.Graph;
            int lobby = g.FindRoom("Lobby");
            sim.World.Building.Hazards.Ignite(new Vector3(6f, 0f, 1.2f), 0.9f);
            foreach (var d in g.Doors) if (g.IsExitDoor(d.Id) && d.Connects(lobby)) sim.World.Building.SetDoorState(d.Id, DoorState.Blocked);
            return sim;
        }

        private static AgentProfile P(string name, PersonalityArchetype a, bool employee = false)
        {
            var p = new Personality();
            for (int i = 0; i < Personality.Count; i++) p[(Trait)i] = a.Mean[i];
            return new AgentProfile { Name = name, Archetype = a.Name, Personality = p, KnowsAllExits = employee };
        }
    }
}
