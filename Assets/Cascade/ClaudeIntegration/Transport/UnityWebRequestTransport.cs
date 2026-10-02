using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Cascade.ClaudeIntegration
{
    /// <summary>
    /// HTTP transport over UnityWebRequest. Non-blocking: completion arrives on the main thread via the async
    /// operation's completed event (play mode) or by polling Tick() (edit mode tools).
    /// </summary>
    public sealed class UnityWebRequestTransport : IClaudeTransport
    {
        private readonly List<KeyValuePair<UnityWebRequest, Action<TransportResponse>>> _pending = new List<KeyValuePair<UnityWebRequest, Action<TransportResponse>>>();
        public bool PollMode;

        public void Post(string url, string body, IDictionary<string, string> headers, float timeoutSeconds, Action<TransportResponse> onDone)
        {
            var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = Mathf.CeilToInt(timeoutSeconds)
            };
            foreach (var h in headers) req.SetRequestHeader(h.Key, h.Value);
            var op = req.SendWebRequest();
            if (PollMode) { _pending.Add(new KeyValuePair<UnityWebRequest, Action<TransportResponse>>(req, onDone)); return; }
            op.completed += _ => Complete(req, onDone);
        }

        /// <summary>Call every editor update when PollMode is true.</summary>
        public void Tick()
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (!_pending[i].Key.isDone) continue;
                var p = _pending[i];
                _pending.RemoveAt(i);
                Complete(p.Key, p.Value);
            }
        }

        public bool Busy => _pending.Count > 0;

        private static void Complete(UnityWebRequest req, Action<TransportResponse> onDone)
        {
            var response = new TransportResponse
            {
                NetworkOk = req.result != UnityWebRequest.Result.ConnectionError && req.result != UnityWebRequest.Result.DataProcessingError,
                StatusCode = req.responseCode,
                Body = req.downloadHandler != null ? req.downloadHandler.text : null,
                Error = req.error
            };
            req.Dispose();
            onDone(response);
        }
    }
}
