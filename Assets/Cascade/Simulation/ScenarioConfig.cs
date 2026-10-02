using System;
using System.Collections.Generic;
using Cascade.Core;
using Cascade.World;
using UnityEngine;

namespace Cascade.Simulation
{
    /// <summary>Everything needed to reproduce a run. Serializable so the Simulation Lab and Claude scenarios can edit it.</summary>
    [Serializable]
    public sealed class ScenarioConfig
    {
        public string Title = "Office fire";
        public int Seed = 1234;
        [UnityEngine.Range(1, 400)] public int NpcCount = 24;
        public List<ArchetypeWeight> Archetypes = new List<ArchetypeWeight>();
        public string FireRoom = "Kitchen";
        public float FireDelaySeconds = 20f;
        public float InitialFireIntensity = 0.35f;
        public List<string> LockedDoorsBetween = new List<string>();   // "Storage|Server Room"
        public int FamilyPairs = 2;
        [UnityEngine.Range(0f, 1f)] public float ColleagueLinkChance = 0.15f;
        public int InjuredAtStart = 2;
        public bool AlarmWorks = true;
        public Agents.CognitionSettings Cognition = new Agents.CognitionSettings();
        public string MainExitRoom = "Front Yard";

        public ScenarioConfig Clone()
        {
            var c = (ScenarioConfig)MemberwiseClone();
            c.Cognition = Cognition != null ? Cognition.Clone() : new Agents.CognitionSettings();
            return c;
        }
    }

    /// <summary>Scripted scenario events (fire start, locked doors). Agents are never told about these directly.</summary>
    public sealed class ScenarioDirector
    {
        private readonly SimulationWorld _world;
        private readonly ScenarioConfig _config;
        private readonly Rng _rng;
        public bool FireStarted { get; private set; }
        public Vector3 FireOrigin { get; private set; }

        public ScenarioDirector(SimulationWorld world, ScenarioConfig config)
        {
            _world = world;
            _config = config;
            _rng = world.Context.CreateRng("director");
            ApplyDoors();
        }

        private void ApplyDoors()
        {
            var g = _world.Building.Graph;
            foreach (var pair in _config.LockedDoorsBetween)
            {
                var parts = pair.Split('|');
                if (parts.Length != 2) continue;
                int a = g.FindRoom(parts[0].Trim()), b = g.FindRoom(parts[1].Trim());
                foreach (var d in g.Doors)
                    if ((d.RoomA == a && d.RoomB == b) || (d.RoomA == b && d.RoomB == a)) _world.Building.SetDoorState(d.Id, DoorState.Locked);
            }
        }

        public void Update()
        {
            if (FireStarted || _world.Now < _config.FireDelaySeconds) return;
            var g = _world.Building.Graph;
            int room = g.FindRoom(_config.FireRoom);
            if (room < 0 || g.Rooms[room].IsExterior)
            {
                do room = _rng.Range(0, g.Rooms.Count); while (g.Rooms[room].IsExterior);
            }
            var r = g.Rooms[room];
            var p = r.Clamp(r.Center + new Vector3(_rng.Range(-3f, 3f), 0f, _rng.Range(-3f, 3f)), 1.5f);
            StartFire(p);
        }

        public void StartFire(Vector3 position)
        {
            if (_world.Building.Hazards.Ignite(position, _config.InitialFireIntensity))
            {
                FireStarted = true;
                FireOrigin = position;
            }
        }
    }
}
