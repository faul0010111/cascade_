using System.Collections.Generic;
using Cascade.Core;
using UnityEngine;

namespace Cascade.World
{
    public sealed class Extinguisher
    {
        public int Id;
        public int Room;
        public Vector3 Position;
        public EntityId HeldBy;   // None when on the wall
        public float Charge = 1f;
        public bool Available => !HeldBy.IsValid && Charge > 0f;
    }

    public sealed class Alarm
    {
        public int Id;
        public int Room;
        public Vector3 Position;
        public bool Active;
    }

    /// <summary>World truth for the building: topology, door states, hazards and interactables.</summary>
    public sealed class BuildingModel
    {
        public readonly RoomGraph Graph;
        public readonly HazardGrid Hazards;
        public readonly List<Extinguisher> Extinguishers = new List<Extinguisher>();
        public readonly List<Alarm> Alarms = new List<Alarm>();
        private readonly EventBus _bus;

        public float AlarmSmokeThreshold = 0.12f;

        public BuildingModel(RoomGraph graph, EventBus bus, Rng rng)
        {
            Graph = graph;
            _bus = bus;
            Hazards = new HazardGrid(graph, bus, rng);
        }

        public Extinguisher AddExtinguisher(Vector3 pos)
        {
            var e = new Extinguisher { Id = Extinguishers.Count, Position = pos, Room = Graph.RoomAt(pos) };
            Extinguishers.Add(e);
            return e;
        }

        public Alarm AddAlarm(Vector3 pos)
        {
            var a = new Alarm { Id = Alarms.Count, Position = pos, Room = Graph.RoomAt(pos) };
            Alarms.Add(a);
            return a;
        }

        public void SetDoorState(int doorId, DoorState state)
        {
            var door = Graph.Doors[doorId];
            if (door.State == state) return;
            var old = door.State;
            door.State = state;
            _bus.Enqueue(new DoorStateChangedEvent { Door = doorId, OldState = old, NewState = state });
        }

        public void TriggerAlarm(int alarmId)
        {
            var alarm = Alarms[alarmId];
            if (alarm.Active) return;
            foreach (var a in Alarms) a.Active = true; // building-wide system
            _bus.Enqueue(new AlarmTriggeredEvent { Alarm = alarmId, Position = alarm.Position });
        }

        public bool AnyAlarmActive
        {
            get { foreach (var a in Alarms) if (a.Active) return true; return false; }
        }

        public void Step(float dt)
        {
            Hazards.Step(dt);
            foreach (var a in Alarms)
                if (!a.Active && Hazards.RoomSmoke(a.Room) > AlarmSmokeThreshold) { TriggerAlarm(a.Id); break; }
        }
    }
}
