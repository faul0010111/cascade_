using System.Collections.Generic;
using UnityEngine;

namespace Cascade.Runtime
{
    /// <summary>
    /// Creates materials at runtime so the project needs no hand-made assets. Works with URP (default for Unity 6)
    /// and falls back to built-in shaders. For player builds add the used shaders to "Always Included Shaders".
    /// </summary>
    public sealed class MaterialLibrary
    {
        private readonly Dictionary<Color, Material> _opaque = new Dictionary<Color, Material>();
        private readonly Dictionary<Color, Material> _transparent = new Dictionary<Color, Material>();
        private Shader _lit, _unlitTransparent;

        public Material Opaque(Color color)
        {
            Material m;
            if (_opaque.TryGetValue(color, out m)) return m;
            // Pick the lit shader of the ACTIVE pipeline: a URP shader under the built-in renderer draws magenta.
            if (_lit == null)
                _lit = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null
                    ? Find("Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Unlit/Color")
                    : Find("Standard", "Unlit/Color");
            m = new Material(_lit) { color = color };
            _opaque[color] = m;
            return m;
        }

        public Material Transparent(Color color)
        {
            Material m;
            if (_transparent.TryGetValue(color, out m)) return m;
            if (_unlitTransparent == null) _unlitTransparent = Find("Sprites/Default", "Universal Render Pipeline/2D/Sprite-Unlit-Default", "Unlit/Transparent");
            m = new Material(_unlitTransparent) { color = color };
            _transparent[color] = m;
            return m;
        }

        /// <summary>A private material instance whose color can be changed per object without affecting others.</summary>
        public Material Instance(Color color, bool transparent = false) => new Material(transparent ? Transparent(color) : Opaque(color));

        private static Shader Find(params string[] names)
        {
            foreach (var n in names)
            {
                var s = Shader.Find(n);
                if (s != null) return s;
            }
            return Shader.Find("Hidden/InternalErrorShader");
        }
    }

    public static class Palette
    {
        public static readonly Color Floor = new Color(0.82f, 0.80f, 0.76f);
        public static readonly Color Ground = new Color(0.45f, 0.55f, 0.42f);
        public static readonly Color Wall = new Color(0.93f, 0.93f, 0.95f);
        public static readonly Color DoorClosed = new Color(0.55f, 0.38f, 0.22f);
        public static readonly Color DoorLocked = new Color(0.45f, 0.1f, 0.1f);
        public static readonly Color DoorBlocked = new Color(1f, 0.45f, 0.05f);
        public static readonly Color Exit = new Color(0.1f, 0.8f, 0.3f);
        public static readonly Color Extinguisher = new Color(0.85f, 0.05f, 0.05f);
        public static readonly Color Player = new Color(0.1f, 0.45f, 1f);

        public static Color ForEmotion(Agents.EmotionState e)
        {
            switch (e)
            {
                case Agents.EmotionState.Calm: return new Color(0.55f, 0.75f, 0.55f);
                case Agents.EmotionState.Nervous: return new Color(0.95f, 0.85f, 0.35f);
                case Agents.EmotionState.Afraid: return new Color(1f, 0.55f, 0.2f);
                case Agents.EmotionState.Panicked: return new Color(0.9f, 0.1f, 0.1f);
                case Agents.EmotionState.Confident: return new Color(0.25f, 0.6f, 0.95f);
                case Agents.EmotionState.Angry: return new Color(0.6f, 0.05f, 0.35f);
                case Agents.EmotionState.Confused: return new Color(0.65f, 0.45f, 0.85f);
                default: return new Color(0.6f, 0.95f, 0.9f); // Relieved
            }
        }
    }
}
