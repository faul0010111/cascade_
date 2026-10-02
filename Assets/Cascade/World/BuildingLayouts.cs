using UnityEngine;

namespace Cascade.World
{
    /// <summary>
    /// Hand-authored building used by the Building Fire scenario and the Simulation Lab.
    /// Single floor, 40 x 24 m, three exits, two interior shortcuts. Coordinates in meters, X right, Z forward.
    /// </summary>
    public static class BuildingLayouts
    {
        public const float WallHeight = 3f;

        public static RoomGraph CreateOfficeFloor()
        {
            var g = new RoomGraph();
            var corridor = g.AddRoom("Corridor", 0, 10, 40, 4);         // 0
            var officeA  = g.AddRoom("Office A", 0, 14, 10, 10);        // 1
            var officeB  = g.AddRoom("Office B", 10, 14, 10, 10);       // 2
            var meeting  = g.AddRoom("Meeting Room", 20, 14, 10, 10);   // 3
            var kitchen  = g.AddRoom("Kitchen", 30, 14, 10, 10);        // 4
            var lobby    = g.AddRoom("Lobby", 0, 0, 12, 10);            // 5
            var storage  = g.AddRoom("Storage", 12, 0, 8, 10);          // 6
            var server   = g.AddRoom("Server Room", 20, 0, 8, 10);      // 7
            var workshop = g.AddRoom("Workshop", 28, 0, 12, 10);        // 8
            var front    = g.AddRoom("Front Yard", 0, -10, 14, 10, exterior: true);  // 9
            var east     = g.AddRoom("East Yard", 40, 6, 10, 12, exterior: true);    // 10
            var west     = g.AddRoom("West Yard", -10, 14, 10, 10, exterior: true);  // 11

            g.AddDoor(corridor.Id, officeA.Id,  new Vector3(5, 0, 14));
            g.AddDoor(corridor.Id, officeB.Id,  new Vector3(15, 0, 14));
            g.AddDoor(corridor.Id, meeting.Id,  new Vector3(25, 0, 14));
            g.AddDoor(corridor.Id, kitchen.Id,  new Vector3(35, 0, 14));
            g.AddDoor(corridor.Id, lobby.Id,    new Vector3(6, 0, 10));
            g.AddDoor(corridor.Id, storage.Id,  new Vector3(16, 0, 10), DoorState.Closed);
            g.AddDoor(corridor.Id, server.Id,   new Vector3(24, 0, 10), DoorState.Closed);
            g.AddDoor(corridor.Id, workshop.Id, new Vector3(34, 0, 10));
            g.AddDoor(lobby.Id, front.Id,       new Vector3(6, 0, 0));      // main exit
            g.AddDoor(corridor.Id, east.Id,     new Vector3(40, 0, 12));    // east emergency exit
            g.AddDoor(officeA.Id, west.Id,      new Vector3(0, 0, 19));     // west emergency exit
            g.AddDoor(meeting.Id, kitchen.Id,   new Vector3(30, 0, 19));
            g.AddDoor(storage.Id, server.Id,    new Vector3(20, 0, 5), DoorState.Locked);
            return g;
        }

        public static void AddDefaultInteractables(BuildingModel model)
        {
            model.AddExtinguisher(new Vector3(20, 0, 10.6f));
            model.AddExtinguisher(new Vector3(39.3f, 0, 2));
            model.AddExtinguisher(new Vector3(0.7f, 0, 8));
            model.AddAlarm(new Vector3(10, 0, 13.4f));
            model.AddAlarm(new Vector3(30, 0, 13.4f));
            model.AddAlarm(new Vector3(6, 0, 9.4f));
        }
    }
}
