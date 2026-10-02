using System;
using System.Collections.Generic;
using Cascade.Agents;
using Cascade.ClaudeIntegration;
using Cascade.Simulation;
using Cascade.World;
using NUnit.Framework;

namespace Cascade.Tests
{
    public class ClaudeIntegrationTests
    {
        private sealed class FakeTransport : IClaudeTransport
        {
            public int Calls;
            public TransportResponse Next;
            public string LastBody;
            public IDictionary<string, string> LastHeaders;
            public void Post(string url, string body, IDictionary<string, string> headers, float timeout, Action<TransportResponse> onDone)
            {
                Calls++;
                LastBody = body;
                LastHeaders = headers;
                onDone(Next);
            }
        }

        private static TransportResponse Ok(string text)
            => new TransportResponse { NetworkOk = true, StatusCode = 200, Body = "{\"content\":[{\"type\":\"text\",\"text\":" + MiniJson.Quote(text) + "}]}" };

        [Test]
        public void Gateway_Disabled_NeverCallsNetwork()
        {
            var t = new FakeTransport();
            var gw = new ClaudeGateway(new ClaudeSettings { Enabled = false }, t, () => "k", () => 0);
            ClaudeResult r = null;
            gw.Send(new ClaudeRequest { User = "hi", Model = "m", MaxTokens = 10 }, x => r = x);
            Assert.AreEqual(ClaudeResultStatus.Disabled, r.Status);
            Assert.AreEqual(0, t.Calls);
        }

        [Test]
        public void Gateway_SendsHeaders_Caches_RateLimits_AndTripsOnAuthFailure()
        {
            var t = new FakeTransport { Next = Ok("hello") };
            double clock = 0;
            var gw = new ClaudeGateway(new ClaudeSettings { Enabled = true, MaxRequestsPerMinute = 2 }, t, () => "secret", () => clock);
            ClaudeResult r = null;
            gw.Send(new ClaudeRequest { User = "a", Model = "m", MaxTokens = 10 }, x => r = x);
            Assert.AreEqual("hello", r.Text);
            Assert.AreEqual("secret", t.LastHeaders["x-api-key"]);
            Assert.AreEqual("2023-06-01", t.LastHeaders["anthropic-version"]);
            StringAssert.Contains("\"messages\":[{\"role\":\"user\"", t.LastBody);

            gw.Send(new ClaudeRequest { User = "a", Model = "m", MaxTokens = 10 }, x => r = x);
            Assert.IsTrue(r.FromCache);
            Assert.AreEqual(1, t.Calls);

            gw.Send(new ClaudeRequest { User = "b", Model = "m", MaxTokens = 10 }, x => r = x);
            gw.Send(new ClaudeRequest { User = "c", Model = "m", MaxTokens = 10 }, x => r = x);
            Assert.AreEqual(ClaudeResultStatus.RateLimited, r.Status);

            clock = 120;
            t.Next = new TransportResponse { NetworkOk = true, StatusCode = 401, Body = "{}" };
            gw.Send(new ClaudeRequest { User = "d", Model = "m", MaxTokens = 10 }, x => r = x);
            Assert.IsTrue(gw.KillSwitch);
        }

        [Test]
        public void Gateway_ProxyMode_NeverSendsKey()
        {
            var t = new FakeTransport { Next = Ok("x") };
            var gw = new ClaudeGateway(new ClaudeSettings { Enabled = true, ProxyEndpoint = "https://my.proxy/claude" }, t, () => "secret", () => 0);
            gw.Send(new ClaudeRequest { User = "a", Model = "m", MaxTokens = 10 }, x => { });
            Assert.IsFalse(t.LastHeaders.ContainsKey("x-api-key"));
        }

        [Test]
        public void ProfileValidator_ClampsRejectsAndRemovesDanglingReferences()
        {
            string json = "Here you go:\n```json\n{\"npcs\":[" +
                "{\"name\":\"Ana\",\"archetype\":\"Protector\",\"role\":\"employee\",\"traits\":{\"Courage\":1.7,\"Telepathy\":0.9}," +
                "\"longTermGoal\":{\"type\":\"FindPerson\",\"target\":\"Beto\"},\"relationships\":[{\"name\":\"Beto\",\"kind\":\"Family\",\"trust\":0.9,\"affection\":0.9},{\"name\":\"Ghost\",\"kind\":\"Friend\"}]}," +
                "{\"name\":\"Beto\",\"archetype\":\"Wizard\"}," +
                "{\"archetype\":\"Loner\"}]}\n```";
            var profiles = new List<AgentProfile>();
            var errors = new List<string>();
            Assert.IsTrue(NpcProfileValidator.TryParseList(json, profiles, errors));
            NpcProfileValidator.RemoveDanglingReferences(profiles, errors);
            Assert.AreEqual(2, profiles.Count, "the nameless entry is rejected");
            Assert.AreEqual(1f, profiles[0].Personality[Trait.Courage], 1e-5f);
            Assert.IsTrue(profiles[0].KnowsAllExits);
            Assert.AreEqual(1, profiles[0].Relationships.Count, "relationship to 'Ghost' removed");
            Assert.AreEqual("Average", profiles[1].Archetype);
            Assert.IsTrue(errors.Exists(e => e.Contains("Telepathy")));
        }

        [Test]
        public void ScenarioValidator_OnlyAcceptsKnownRoomsAndDoors()
        {
            var graph = BuildingLayouts.CreateOfficeFloor();
            string json = "{\"title\":\"t\",\"fireRoom\":\"Moon Base\",\"npcCount\":9999,\"lockedDoors\":[[\"Storage\",\"Server Room\"],[\"Lobby\",\"Kitchen\"]],\"archetypeMix\":{\"Anxious\":3,\"Robot\":1}}";
            ScenarioConfig cfg;
            var errors = new List<string>();
            Assert.IsTrue(ScenarioValidator.TryParse(json, graph, new ScenarioConfig(), out cfg, errors));
            Assert.AreEqual("Kitchen", cfg.FireRoom);
            Assert.AreEqual(200, cfg.NpcCount);
            Assert.AreEqual(1, cfg.LockedDoorsBetween.Count);
            Assert.AreEqual(1, cfg.Archetypes.Count);
        }

        [Test]
        public void Dialogue_FallsBackToTemplates_AndDiscardsLateLines()
        {
            var world = Fixtures.World();
            var t = new FakeTransport { Next = Ok("{\"line\":\"Fire in the kitchen, go left!\",\"tone\":\"urgent\"}") };
            var director = new DialogueDirector(world, new ClaudeGateway(new ClaudeSettings { Enabled = false }, t, () => "k", () => 0));
            var bodies = new List<Simulation.Headless.HeadlessBody>();
            var a = Fixtures.Spawn(world, "A", Fixtures.Traits(), "Office A", bodies: bodies);
            a.Comms.Send(MessageKind.Warn, Core.EntityId.None, 8f);
            world.Step(0.1f);
            Assert.AreEqual(1, director.TemplateLines);
            Assert.AreEqual(0, t.Calls);
            string line, tone;
            Assert.IsTrue(DialogueValidator.TryParse("{\"line\":\"Run!\",\"tone\":\"panicked\"}", out line, out tone));
            Assert.IsFalse(DialogueValidator.TryParse("{\"line\":\"<script>\"}", out line, out tone));
        }
    }
}
