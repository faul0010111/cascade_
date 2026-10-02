using System;
using System.Collections.Generic;
using Cascade.Agents;
using Cascade.Core;
using Cascade.Simulation;

namespace Cascade.ClaudeIntegration
{
    public struct SpokenLine
    {
        public EntityId Speaker;
        public string Text;
        public float Time;
        public bool Generated;
    }

    /// <summary>
    /// Gives every message a template line immediately; for full-LOD speakers it may request a Claude line and swap
    /// it in if it arrives before the deadline. The message's mechanical effect has already happened either way
    /// (principle P6: real time never waits for Claude).
    /// </summary>
    public sealed class DialogueDirector
    {
        private readonly SimulationWorld _world;
        private readonly ClaudeGateway _gateway;
        private readonly Rng _rng;
        public readonly List<SpokenLine> Lines = new List<SpokenLine>();
        public event Action<SpokenLine> LineSpoken;
        public int GeneratedLines { get; private set; }
        public int TemplateLines { get; private set; }
        public int LateDiscarded { get; private set; }

        public DialogueDirector(SimulationWorld world, ClaudeGateway gateway)
        {
            _world = world;
            _gateway = gateway;
            _rng = world.Context.CreateRng("dialogue");
            world.MessageSent += OnMessageSent;
        }

        private void OnMessageSent(Message m)
        {
            var speaker = _world.GetAgent(m.Sender);
            if (speaker == null || speaker.IsPlayer) return;
            m.Text = DialogueTemplates.Pick(m.Kind, speaker.Brain.Emotion.Current, _rng);
            Emit(new SpokenLine { Speaker = m.Sender, Text = m.Text, Time = _world.Now });
            TemplateLines++;

            if (_gateway == null || !_gateway.IsAvailable || !_gateway.Settings.UseForDialogue) return;
            if (speaker.Lod != LodTier.Full) return;

            float issued = _world.Now;
            string addressee = m.IsBroadcast ? null : _world.Context.Entities.NameOf(m.Addressee);
            var request = new ClaudeRequest
            {
                Task = ClaudeTask.Dialogue,
                Model = _gateway.Settings.DialogueModel,
                MaxTokens = _gateway.Settings.MaxTokensDialogue,
                System = PromptBuilder.DialogueSystem,
                User = PromptBuilder.DialogueUser(speaker.Brain, m, addressee)
            };
            _gateway.Send(request, result =>
            {
                if (!result.Ok) return;
                if (_world.Now - issued > _gateway.Settings.DialogueDeadlineSeconds) { LateDiscarded++; return; }
                string line, tone;
                if (!DialogueValidator.TryParse(result.Text, out line, out tone)) return;
                m.Text = line;
                GeneratedLines++;
                Emit(new SpokenLine { Speaker = m.Sender, Text = line, Time = _world.Now, Generated = true });
            });
        }

        private void Emit(SpokenLine line)
        {
            Lines.Add(line);
            if (Lines.Count > 60) Lines.RemoveAt(0);
            if (LineSpoken != null) LineSpoken(line);
        }
    }

    /// <summary>Fallback lines. The game is fully playable with only these.</summary>
    public static class DialogueTemplates
    {
        private static readonly Dictionary<MessageKind, string[]> Calm = new Dictionary<MessageKind, string[]>
        {
            { MessageKind.Warn, new[] { "There's a fire, we need to get out.", "Heads up, fire! Head for the exits.", "Don't go that way, it's burning." } },
            { MessageKind.Order, new[] { "Everyone, follow me!", "Stay with me, I know the way.", "This way, all of you!" } },
            { MessageKind.Assist, new[] { "I've got you, lean on me.", "Come on, I'll help you out.", "You're not alone, let's go." } },
            { MessageKind.Ask, new[] { "Do you know the way out?", "Which exit is clear?", "Where do we go?" } },
            { MessageKind.Inform, new[] { "Here's what I know.", "The exit is that way.", "I saw it with my own eyes." } },
            { MessageKind.Reassure, new[] { "Stay calm, we'll make it.", "Breathe. We're getting out." } },
        };

        private static readonly Dictionary<MessageKind, string[]> Panicked = new Dictionary<MessageKind, string[]>
        {
            { MessageKind.Warn, new[] { "FIRE! Run!", "It's everywhere, get out!", "Move, move, MOVE!" } },
            { MessageKind.Order, new[] { "Follow me, NOW!", "Come on, come on!" } },
            { MessageKind.Assist, new[] { "Get up, please get up!", "Grab my arm!" } },
            { MessageKind.Ask, new[] { "How do we get out?!", "Where's the exit?!" } },
            { MessageKind.Inform, new[] { "That way! That way!" } },
            { MessageKind.Reassure, new[] { "It's... it's okay, it's okay!" } },
        };

        public static string Pick(MessageKind kind, EmotionState emotion, Rng rng)
        {
            var table = emotion == EmotionState.Panicked || emotion == EmotionState.Afraid ? Panicked : Calm;
            string[] options;
            if (!table.TryGetValue(kind, out options)) return "...";
            return options[rng.Range(0, options.Length)];
        }
    }
}
