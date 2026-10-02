using System;
using System.Collections.Generic;
using Cascade.Agents;
using Cascade.Simulation;
using Cascade.World;
using UnityEngine;

namespace Cascade.ClaudeIntegration
{
    /// <summary>
    /// Turns untrusted model output into engine data. Unknown enum values are rejected, numbers are clamped, strings
    /// are length-limited, and references must exist in an allowlist. Anything the validator cannot vouch for is
    /// dropped: a partially valid result is better than a crash, and an invalid one falls back to defaults.
    /// </summary>
    public static class NpcProfileValidator
    {
        public const int MaxNameLength = 32;
        public const int MaxTextLength = 400;

        public static bool TryParseList(string json, List<AgentProfile> into, List<string> errors)
        {
            object root;
            string parseError;
            if (!MiniJson.TryParse(MiniJson.ExtractJsonObject(json), out root, out parseError))
            {
                errors.Add("Not valid JSON: " + parseError);
                return false;
            }
            var list = MiniJson.GetList(root as Dictionary<string, object>, "npcs");
            if (list == null) { errors.Add("Missing 'npcs' array"); return false; }
            int before = into.Count;
            for (int i = 0; i < list.Count && i < 200; i++)
            {
                AgentProfile p;
                if (TryConvert(list[i] as Dictionary<string, object>, errors, "npcs[" + i + "]", out p)) into.Add(p);
            }
            return into.Count > before;
        }

        public static bool TryConvert(Dictionary<string, object> d, List<string> errors, string path, out AgentProfile profile)
        {
            profile = null;
            if (d == null) { errors.Add(path + ": not an object"); return false; }
            string name = Clean(MiniJson.GetString(d, "name"), MaxNameLength);
            if (string.IsNullOrEmpty(name)) { errors.Add(path + ": missing name"); return false; }

            string archetypeName = MiniJson.GetString(d, "archetype", "Average");
            var archetype = Archetypes.Find(archetypeName);
            if (!string.Equals(archetype.Name, archetypeName, StringComparison.OrdinalIgnoreCase))
                errors.Add(path + ": unknown archetype '" + archetypeName + "', using Average");

            // Start from the archetype mean, then apply explicit traits.
            var personality = new Personality();
            for (int t = 0; t < Personality.Count; t++) personality[(Trait)t] = archetype.Mean[t];
            var traits = MiniJson.GetObject(d, "traits");
            if (traits != null)
            {
                foreach (var kv in traits)
                {
                    Trait trait;
                    if (!Enum.TryParse(kv.Key, true, out trait) || !Enum.IsDefined(typeof(Trait), trait)) { errors.Add(path + ": unknown trait '" + kv.Key + "' ignored"); continue; }
                    if (!(kv.Value is double)) { errors.Add(path + ": trait '" + kv.Key + "' is not a number"); continue; }
                    float v = (float)(double)kv.Value;
                    if (v < 0f || v > 1f) errors.Add(path + ": trait '" + kv.Key + "' clamped to [0,1]");
                    personality[trait] = Mathf.Clamp01(v);
                }
            }

            profile = new AgentProfile
            {
                Name = name,
                Archetype = archetype.Name,
                Personality = personality,
                Background = Clean(MiniJson.GetString(d, "background", ""), MaxTextLength),
                SpeechStyle = Clean(MiniJson.GetString(d, "speechStyle", ""), 120),
                KnowsAllExits = MiniJson.GetString(d, "role", "visitor") == "employee"
            };

            var goal = MiniJson.GetObject(d, "longTermGoal");
            if (goal != null)
            {
                string type = MiniJson.GetString(goal, "type", "None");
                if (type == "FindPerson")
                {
                    profile.LongTermGoal = LongTermGoalType.FindPerson;
                    profile.LongTermTargetName = Clean(MiniJson.GetString(goal, "target"), MaxNameLength);
                }
                else if (type != "None") errors.Add(path + ": unsupported long-term goal '" + type + "' ignored");
            }

            var rels = MiniJson.GetList(d, "relationships");
            if (rels != null)
            {
                foreach (var item in rels)
                {
                    var r = item as Dictionary<string, object>;
                    string target = Clean(MiniJson.GetString(r, "name"), MaxNameLength);
                    RelationshipKind kind;
                    if (string.IsNullOrEmpty(target) || !Enum.TryParse(MiniJson.GetString(r, "kind", ""), true, out kind))
                    {
                        errors.Add(path + ": invalid relationship ignored");
                        continue;
                    }
                    float trust, affection;
                    MiniJson.TryGetNumber(r, "trust", out trust);
                    MiniJson.TryGetNumber(r, "affection", out affection);
                    profile.Relationships.Add(new InitialRelationship { TargetName = target, Kind = kind, Trust = Mathf.Clamp01(trust), Affection = Mathf.Clamp01(affection) });
                }
            }
            return true;
        }

        public static string Clean(string s, int maxLength)
        {
            if (s == null) return null;
            s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return s.Length > maxLength ? s.Substring(0, maxLength) : s;
        }

        /// <summary>After loading a batch, references to names that do not exist are removed (no dangling ids).</summary>
        public static void RemoveDanglingReferences(List<AgentProfile> profiles, List<string> warnings)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in profiles) names.Add(p.Name);
            foreach (var p in profiles)
            {
                p.Relationships.RemoveAll(r =>
                {
                    bool bad = !names.Contains(r.TargetName) || string.Equals(r.TargetName, p.Name, StringComparison.OrdinalIgnoreCase);
                    if (bad) warnings.Add(p.Name + ": relationship to unknown '" + r.TargetName + "' removed");
                    return bad;
                });
                if (p.LongTermGoal == LongTermGoalType.FindPerson && !names.Contains(p.LongTermTargetName ?? ""))
                {
                    warnings.Add(p.Name + ": long-term target '" + p.LongTermTargetName + "' does not exist, goal removed");
                    p.LongTermGoal = LongTermGoalType.None;
                    p.LongTermTargetName = null;
                }
            }
        }
    }

    public static class ScenarioValidator
    {
        public static bool TryParse(string json, RoomGraph graph, ScenarioConfig baseConfig, out ScenarioConfig config, List<string> errors)
        {
            config = baseConfig.Clone();
            config.Archetypes = new List<ArchetypeWeight>();
            config.LockedDoorsBetween = new List<string>();
            object root;
            string parseError;
            if (!MiniJson.TryParse(MiniJson.ExtractJsonObject(json), out root, out parseError))
            {
                errors.Add("Not valid JSON: " + parseError);
                return false;
            }
            var d = root as Dictionary<string, object>;
            if (d == null) { errors.Add("Root is not an object"); return false; }

            config.Title = NpcProfileValidator.Clean(MiniJson.GetString(d, "title", "Generated scenario"), 80);
            string fireRoom = MiniJson.GetString(d, "fireRoom");
            int room = fireRoom != null ? graph.FindRoom(fireRoom) : -1;
            if (room < 0 || graph.Rooms[room].IsExterior) errors.Add("fireRoom '" + fireRoom + "' is not an interior room, keeping '" + config.FireRoom + "'");
            else config.FireRoom = graph.Rooms[room].Name;

            float v;
            if (MiniJson.TryGetNumber(d, "npcCount", out v)) config.NpcCount = Mathf.Clamp(Mathf.RoundToInt(v), 1, 200);
            if (MiniJson.TryGetNumber(d, "fireDelaySeconds", out v)) config.FireDelaySeconds = Mathf.Clamp(v, 0f, 300f);
            if (MiniJson.TryGetNumber(d, "familyPairs", out v)) config.FamilyPairs = Mathf.Clamp(Mathf.RoundToInt(v), 0, 20);
            if (MiniJson.TryGetNumber(d, "injuredAtStart", out v)) config.InjuredAtStart = Mathf.Clamp(Mathf.RoundToInt(v), 0, 10);
            object alarm;
            if (d.TryGetValue("alarmWorks", out alarm) && alarm is bool) config.AlarmWorks = (bool)alarm;

            var mix = MiniJson.GetObject(d, "archetypeMix");
            if (mix != null)
                foreach (var kv in mix)
                {
                    var a = Archetypes.Find(kv.Key);
                    if (!string.Equals(a.Name, kv.Key, StringComparison.OrdinalIgnoreCase) || !(kv.Value is double)) { errors.Add("archetypeMix: '" + kv.Key + "' ignored"); continue; }
                    config.Archetypes.Add(new ArchetypeWeight { Archetype = a.Name, Weight = Mathf.Clamp((float)(double)kv.Value, 0f, 100f) });
                }

            var locked = MiniJson.GetList(d, "lockedDoors");
            if (locked != null)
                foreach (var item in locked)
                {
                    var pair = item as List<object>;
                    if (pair == null || pair.Count != 2) { errors.Add("lockedDoors entry must be [roomA, roomB]"); continue; }
                    int a = graph.FindRoom(pair[0] as string), b = graph.FindRoom(pair[1] as string);
                    bool exists = false;
                    foreach (var door in graph.Doors) if (door.Connects(a) && door.Connects(b) && a != b) exists = true;
                    if (!exists) { errors.Add("No door between '" + pair[0] + "' and '" + pair[1] + "'"); continue; }
                    config.LockedDoorsBetween.Add(graph.Rooms[a].Name + "|" + graph.Rooms[b].Name);
                }
            return true;
        }
    }

    public static class DialogueValidator
    {
        public const int MaxLength = 140;
        private static readonly string[] AllowedTones = { "calm", "urgent", "scared", "panicked", "reassuring", "angry", "confused", "relieved" };

        public static bool TryParse(string text, out string line, out string tone)
        {
            line = null;
            tone = "calm";
            object root;
            string err;
            if (!MiniJson.TryParse(MiniJson.ExtractJsonObject(text), out root, out err)) return false;
            var d = root as Dictionary<string, object>;
            line = NpcProfileValidator.Clean(MiniJson.GetString(d, "line"), MaxLength);
            string t = MiniJson.GetString(d, "tone", "calm");
            if (Array.IndexOf(AllowedTones, t) >= 0) tone = t;
            if (string.IsNullOrEmpty(line)) return false;
            if (line.Contains("{") || line.Contains("<")) return false; // leaked markup or template syntax
            return true;
        }
    }
}
