using System.Collections.Generic;
using System.Text;
using Cascade.Agents;
using Cascade.World;

namespace Cascade.ClaudeIntegration
{
    /// <summary>
    /// Builds prompts. Dialogue context is made ONLY from the speaker's beliefs, emotion and memories, so the model
    /// cannot leak world truth the NPC does not have. Mechanics never depend on the generated wording.
    /// </summary>
    public static class PromptBuilder
    {
        public const string DialogueSystem =
            "You write ONE short line of spoken dialogue for a non-player character in a building-fire simulation. " +
            "Use only the facts given; never invent locations, people or events. Match the emotion and speech style. " +
            "At most 20 words. Reply with JSON only: {\"line\": string, \"tone\": one of calm|urgent|scared|panicked|reassuring|angry|confused|relieved}.";

        public static string DialogueUser(AgentBrain speaker, Message message, string addresseeName)
        {
            float now = speaker.Now;
            var plan = speaker.FloorPlan;
            var sb = new StringBuilder();
            sb.Append("Speaker: ").Append(speaker.Name).Append(" (").Append(speaker.Profile.Archetype).Append(")\n");
            if (!string.IsNullOrEmpty(speaker.Profile.SpeechStyle)) sb.Append("Speech style: ").Append(speaker.Profile.SpeechStyle).Append('\n');
            sb.Append("Emotion: ").Append(speaker.Emotion.Current).Append('\n');
            sb.Append("Intent: ").Append(Intent(message.Kind)).Append('\n');
            sb.Append("Talking to: ").Append(string.IsNullOrEmpty(addresseeName) ? "everyone nearby" : addresseeName).Append('\n');
            sb.Append("Facts the speaker is sharing:\n");
            if (message.Payload.Count == 0) sb.Append("- (none)\n");
            foreach (var b in message.Payload) sb.Append("- ").Append(DescribeBelief(b, plan, speaker)).Append('\n');
            var memories = new List<EpisodicMemory>();
            speaker.Memory.TopRelevant(now, 2, memories, subject: message.Addressee);
            if (memories.Count > 0)
            {
                sb.Append("Relevant memories:\n");
                foreach (var m in memories) sb.Append("- ").Append(m.Describe(speaker.NameOf, plan.RoomName)).Append('\n');
            }
            return sb.ToString();
        }

        public static string Intent(MessageKind kind)
        {
            switch (kind)
            {
                case MessageKind.Warn: return "warn about danger";
                case MessageKind.Order: return "tell people to follow the speaker";
                case MessageKind.Assist: return "offer help to someone hurt";
                case MessageKind.Ask: return "ask for the way out";
                case MessageKind.Reassure: return "calm someone down";
                default: return "share information";
            }
        }

        public static string DescribeBelief(Belief b, IFloorPlan plan, AgentBrain speaker)
        {
            switch (b.Key.Type)
            {
                case FactType.HazardInRoom: return "fire in " + plan.RoomName(b.Key.Subject) + (b.Value > 0.6f ? " (big)" : "");
                case FactType.DoorState: return "a door is " + ((DoorState)(int)b.Value).ToString().ToLowerInvariant();
                case FactType.ExitKnown: return "there is an exit to " + plan.RoomName(ExitSide(plan, b.Key.Subject));
                case FactType.AlarmActive: return "the fire alarm is ringing";
                case FactType.AgentInjured: return speaker.NameOf(new Core.EntityId(b.Key.Subject)) + " is hurt";
                case FactType.AgentRoom: return speaker.NameOf(new Core.EntityId(b.Key.Subject)) + " is in " + plan.RoomName((int)b.Value);
                default: return b.Key.ToString();
            }
        }

        private static int ExitSide(IFloorPlan plan, int door)
        {
            for (int r = 0; r < plan.RoomCount; r++)
            {
                if (!plan.IsExterior(r)) continue;
                var doors = plan.DoorsOf(r);
                for (int i = 0; i < doors.Count; i++) if (doors[i] == door) return r;
            }
            return -1;
        }

        public const string ProfileSystem =
            "You design non-player characters for an AI simulation. Reply with JSON only, no prose. Schema: " +
            "{\"npcs\":[{\"name\":string,\"archetype\":\"Protector|Anxious|Organizer|Loner|Curious|Average\",\"role\":\"employee|visitor\"," +
            "\"traits\":{\"Courage\":0-1,\"Fearfulness\":0-1,\"Leadership\":0-1,\"Empathy\":0-1,\"Curiosity\":0-1,\"Aggression\":0-1,\"Trust\":0-1," +
            "\"RiskTolerance\":0-1,\"Patience\":0-1,\"Independence\":0-1},\"background\":string,\"speechStyle\":string," +
            "\"longTermGoal\":{\"type\":\"None|FindPerson\",\"target\":name},\"relationships\":[{\"name\":string," +
            "\"kind\":\"Acquaintance|Colleague|Friend|Family\",\"trust\":0-1,\"affection\":0-1}]}]}. " +
            "Relationships and targets must reference names inside the same list. Personalities must be varied and internally consistent.";

        public static string ProfileUser(int count, string theme)
            => "Create " + count + " characters present in a small office building. Theme: " + (string.IsNullOrEmpty(theme) ? "ordinary workday" : theme) + ".";

        public static string ScenarioSystem(RoomGraph graph)
        {
            var rooms = new StringBuilder();
            foreach (var r in graph.Rooms) if (!r.IsExterior) rooms.Append(rooms.Length > 0 ? ", " : "").Append(r.Name);
            return "You design test scenarios for a building-fire NPC simulation. Reply with JSON only. Schema: " +
                   "{\"title\":string,\"fireRoom\":room,\"fireDelaySeconds\":number,\"npcCount\":1-200,\"familyPairs\":number," +
                   "\"injuredAtStart\":number,\"alarmWorks\":bool,\"archetypeMix\":{\"Protector|Anxious|Organizer|Loner|Curious|Average\":weight}," +
                   "\"lockedDoors\":[[roomA,roomB]]}. Valid rooms: " + rooms + ".";
        }
    }
}
