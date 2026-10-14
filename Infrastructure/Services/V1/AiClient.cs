using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Application.DTOs.Platform;
using Application.Interfaces.V1;
using Domain.Entities.AppSettings;
using Domain.Entities.Store;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.V1
{
    /// <summary>
    /// server.py anthropic_call / insights_call / _post_json. Uses the named HttpClient
    /// <see cref="HttpClientName"/> (90 s timeout, outbound_proxy honoured — see InfrastructureRegistration).
    /// </summary>
    public sealed class AiClient : IAiClient
    {
        public const string HttpClientName = "pogov-ai";
        public const string AnthropicUrl = "https://api.anthropic.com/v1/messages";
        public const string GroqUrl = "https://api.groq.com/openai/v1/chat/completions";

        private readonly IHttpClientFactory _http;
        private readonly PoGovSettings _cfg;

        public AiClient(IHttpClientFactory http, IOptions<PoGovSettings> cfg)
        {
            _http = http;
            _cfg = cfg.Value;
        }

        /// <summary>POSTs JSON; returns (status, body). Network failures → 502 with the v1.1.4 message.</summary>
        private async Task<AiResult> PostJsonAsync(string url, IDictionary<string, string> headers, JsonNode body, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
                };
                foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
                using var resp = await _http.CreateClient(HttpClientName).SendAsync(req, ct);
                var text = await resp.Content.ReadAsStringAsync(ct);
                var status = (int)resp.StatusCode;
                try
                {
                    return new AiResult(status, JsonNode.Parse(text) ?? new JsonObject());
                }
                catch (Exception) when (!resp.IsSuccessStatusCode)
                {
                    return new AiResult(status, new JsonObject { ["error"] = new JsonObject { ["message"] = $"HTTP Error {status}: {resp.ReasonPhrase}" } });
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or WebException && !ct.IsCancellationRequested)
            {
                return new AiResult(502, new JsonObject { ["error"] = new JsonObject { ["message"] = "The server could not reach the AI service: " + e.Message } });
            }
        }

        public Task<AiResult> AnthropicCallAsync(string system, JsonNode messages, int maxTokens, CancellationToken ct = default) =>
            PostJsonAsync(AnthropicUrl,
                new Dictionary<string, string> { ["x-api-key"] = _cfg.AnthropicApiKey, ["anthropic-version"] = "2023-06-01" },
                new JsonObject
                {
                    ["model"] = string.IsNullOrEmpty(_cfg.AnthropicModel) ? "claude-sonnet-5" : _cfg.AnthropicModel,
                    ["max_tokens"] = maxTokens,
                    ["system"] = system,
                    ["messages"] = messages.Parent == null ? messages : messages.DeepClone()
                }, ct);

        public async Task<AiResult> InsightsCallAsync(string prompt, CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(_cfg.GroqApiKey))
            {
                var hdr = new Dictionary<string, string> { ["Authorization"] = "Bearer " + _cfg.GroqApiKey };
                JsonObject Req(bool jsonMode)
                {
                    var r = new JsonObject
                    {
                        ["model"] = string.IsNullOrEmpty(_cfg.GroqModel) ? "openai/gpt-oss-120b" : _cfg.GroqModel,
                        ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt }),
                        ["temperature"] = 0.5,
                    };
                    if (jsonMode) r["response_format"] = new JsonObject { ["type"] = "json_object" };
                    return r;
                }
                var res = await PostJsonAsync(GroqUrl, hdr, Req(true), ct);
                if (res.Status == 400 && res.Data.ToJsonString().Contains("response_format"))
                    res = await PostJsonAsync(GroqUrl, hdr, Req(false), ct);   // model without JSON mode — the page parses plain text too
                if (res.Data is JsonObject d) d["_provider"] = "groq";
                return res;
            }
            var a = await AnthropicCallAsync(
                "You are a retail PO-governance analyst. Reply with a single JSON object only — no prose, no code fences.",
                new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt }), 2000, ct);
            if (a.Status != 200)
            {
                if (a.Data is JsonObject d) d["_provider"] = "anthropic";
                return a;
            }
            var text = new StringBuilder();
            if ((a.Data as JsonObject).Get("content") is JsonArray content)
                foreach (var b in content.OfType<JsonObject>())
                    if (b.GetStr("type") == "text") text.Append(b.GetStr("text") ?? "");
            return new AiResult(200, new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text.ToString() } }),
                ["_provider"] = "anthropic"
            });
        }
    }
}
