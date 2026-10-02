using System;

namespace Cascade.ClaudeIntegration
{
    /// <summary>
    /// Configuration of the Claude layer. The game must work with Enabled = false: every feature has a fallback.
    /// API keys never live here. In the Editor the key comes from EditorPrefs or the ANTHROPIC_API_KEY environment
    /// variable; player builds must go through your own proxy (ProxyEndpoint) that holds the key server-side.
    /// </summary>
    [Serializable]
    public sealed class ClaudeSettings
    {
        public bool Enabled = false;
        public string Endpoint = "https://api.anthropic.com/v1/messages";
        public string ProxyEndpoint = "";
        public string ApiVersion = "2023-06-01";
        public string DialogueModel = "claude-haiku-4-5-20251001";
        public string GenerationModel = "claude-sonnet-5";
        public int MaxTokensDialogue = 120;
        public int MaxTokensGeneration = 4000;
        public float TimeoutSeconds = 20f;
        public float DialogueDeadlineSeconds = 2.5f;
        public int MaxRequestsPerMinute = 30;
        public int CacheCapacity = 256;
        public bool UseForDialogue = true;

        public bool UsesProxy => !string.IsNullOrEmpty(ProxyEndpoint);
        public string EffectiveEndpoint => UsesProxy ? ProxyEndpoint : Endpoint;
    }
}
