using System;
using System.Collections.Generic;
using System.Text;

namespace Cascade.ClaudeIntegration
{
    public struct TransportResponse
    {
        public bool NetworkOk;
        public long StatusCode;
        public string Body;
        public string Error;
    }

    /// <summary>HTTP abstraction: UnityWebRequest in the game, a fake in tests.</summary>
    public interface IClaudeTransport
    {
        void Post(string url, string body, IDictionary<string, string> headers, float timeoutSeconds, Action<TransportResponse> onDone);
    }

    public enum ClaudeTask { Dialogue, NpcGeneration, ScenarioGeneration, DevAssistant }

    public sealed class ClaudeRequest
    {
        public ClaudeTask Task;
        public string System;
        public string User;
        public string Model;
        public int MaxTokens;
        public bool Cacheable = true;
    }

    public enum ClaudeResultStatus { Ok, Disabled, RateLimited, NoCredentials, NetworkError, HttpError, EmptyResponse }

    public sealed class ClaudeResult
    {
        public ClaudeResultStatus Status;
        public string Text;
        public string Error;
        public bool FromCache;
        public float LatencySeconds;
        public bool Ok => Status == ClaudeResultStatus.Ok;
    }

    public struct GatewayLogEntry
    {
        public DateTime Time;
        public ClaudeTask Task;
        public ClaudeResultStatus Status;
        public bool FromCache;
        public float Latency;
        public string Note;
    }

    /// <summary>
    /// Single entry point to the Claude Messages API. Adds rate limiting, caching, a kill switch and a request log
    /// for the debugger. It never blocks: results arrive through callbacks, and callers must always have a fallback.
    /// </summary>
    public sealed class ClaudeGateway
    {
        private readonly ClaudeSettings _settings;
        private readonly IClaudeTransport _transport;
        private readonly Func<string> _apiKeyProvider;
        private readonly Func<double> _clock;
        private readonly Queue<double> _recentRequests = new Queue<double>();
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>();
        private readonly LinkedList<string> _cacheOrder = new LinkedList<string>();
        private readonly List<GatewayLogEntry> _log = new List<GatewayLogEntry>();

        /// <summary>Runtime kill switch (debug panel). Also trips automatically after repeated auth failures.</summary>
        public bool KillSwitch;
        public int ConsecutiveFailures { get; private set; }
        public int RequestsSent { get; private set; }
        public int CacheHits { get; private set; }
        public IReadOnlyList<GatewayLogEntry> Log => _log;

        public ClaudeGateway(ClaudeSettings settings, IClaudeTransport transport, Func<string> apiKeyProvider, Func<double> clockSeconds)
        {
            _settings = settings;
            _transport = transport;
            _apiKeyProvider = apiKeyProvider;
            _clock = clockSeconds;
        }

        public ClaudeSettings Settings => _settings;
        public bool IsAvailable => _settings.Enabled && !KillSwitch && _transport != null;

        public void Send(ClaudeRequest request, Action<ClaudeResult> onDone)
        {
            if (!IsAvailable) { Finish(request, onDone, new ClaudeResult { Status = ClaudeResultStatus.Disabled }); return; }

            string cacheKey = request.Cacheable ? CacheKey(request) : null;
            string cached;
            if (cacheKey != null && _cache.TryGetValue(cacheKey, out cached))
            {
                CacheHits++;
                Finish(request, onDone, new ClaudeResult { Status = ClaudeResultStatus.Ok, Text = cached, FromCache = true });
                return;
            }

            double now = _clock();
            while (_recentRequests.Count > 0 && now - _recentRequests.Peek() > 60.0) _recentRequests.Dequeue();
            if (_recentRequests.Count >= _settings.MaxRequestsPerMinute)
            {
                Finish(request, onDone, new ClaudeResult { Status = ClaudeResultStatus.RateLimited });
                return;
            }

            var headers = new Dictionary<string, string> { { "content-type", "application/json" } };
            if (!_settings.UsesProxy)
            {
                string key = _apiKeyProvider != null ? _apiKeyProvider() : null;
                if (string.IsNullOrEmpty(key)) { Finish(request, onDone, new ClaudeResult { Status = ClaudeResultStatus.NoCredentials }); return; }
                headers["x-api-key"] = key;
                headers["anthropic-version"] = _settings.ApiVersion;
            }

            _recentRequests.Enqueue(now);
            RequestsSent++;
            string body = BuildBody(request);
            _transport.Post(_settings.EffectiveEndpoint, body, headers, _settings.TimeoutSeconds, response =>
            {
                var result = Interpret(response);
                result.LatencySeconds = (float)(_clock() - now);
                if (result.Ok)
                {
                    ConsecutiveFailures = 0;
                    if (cacheKey != null) Store(cacheKey, result.Text);
                }
                else
                {
                    ConsecutiveFailures++;
                    if (response.StatusCode == 401 || response.StatusCode == 403) KillSwitch = true; // bad credentials: stop hammering
                }
                Finish(request, onDone, result);
            });
        }

        public string BuildBody(ClaudeRequest r)
        {
            var sb = new StringBuilder();
            sb.Append("{\"model\":").Append(MiniJson.Quote(r.Model))
              .Append(",\"max_tokens\":").Append(r.MaxTokens);
            if (!string.IsNullOrEmpty(r.System)) sb.Append(",\"system\":").Append(MiniJson.Quote(r.System));
            sb.Append(",\"messages\":[{\"role\":\"user\",\"content\":").Append(MiniJson.Quote(r.User)).Append("}]}");
            return sb.ToString();
        }

        /// <summary>Extracts the concatenated text blocks from a Messages API response.</summary>
        public static ClaudeResult Interpret(TransportResponse response)
        {
            if (!response.NetworkOk) return new ClaudeResult { Status = ClaudeResultStatus.NetworkError, Error = response.Error };
            if (response.StatusCode < 200 || response.StatusCode >= 300)
                return new ClaudeResult { Status = ClaudeResultStatus.HttpError, Error = "HTTP " + response.StatusCode + ": " + Truncate(response.Body, 300) };

            object parsed;
            string error;
            if (!MiniJson.TryParse(response.Body, out parsed, out error))
                return new ClaudeResult { Status = ClaudeResultStatus.EmptyResponse, Error = "Invalid JSON: " + error };
            var content = MiniJson.GetList(parsed as Dictionary<string, object>, "content");
            if (content == null) return new ClaudeResult { Status = ClaudeResultStatus.EmptyResponse, Error = "No content" };

            var text = new StringBuilder();
            foreach (var block in content)
            {
                var b = block as Dictionary<string, object>;
                if (MiniJson.GetString(b, "type") == "text") text.Append(MiniJson.GetString(b, "text", ""));
            }
            if (text.Length == 0) return new ClaudeResult { Status = ClaudeResultStatus.EmptyResponse, Error = "No text blocks" };
            return new ClaudeResult { Status = ClaudeResultStatus.Ok, Text = text.ToString() };
        }

        private void Finish(ClaudeRequest request, Action<ClaudeResult> onDone, ClaudeResult result)
        {
            _log.Add(new GatewayLogEntry
            {
                Time = DateTime.Now, Task = request.Task, Status = result.Status, FromCache = result.FromCache,
                Latency = result.LatencySeconds, Note = result.Error
            });
            if (_log.Count > 100) _log.RemoveAt(0);
            if (onDone != null) onDone(result);
        }

        private string CacheKey(ClaudeRequest r) => r.Model + "\u0001" + r.System + "\u0001" + r.User;

        private void Store(string key, string value)
        {
            if (_cache.ContainsKey(key)) return;
            _cache[key] = value;
            _cacheOrder.AddLast(key);
            while (_cacheOrder.Count > _settings.CacheCapacity)
            {
                _cache.Remove(_cacheOrder.First.Value);
                _cacheOrder.RemoveFirst();
            }
        }

        private static string Truncate(string s, int n) => s == null ? "" : (s.Length <= n ? s : s.Substring(0, n) + "...");
    }
}
