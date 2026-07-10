using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DotWhisper.Core.Settings;

namespace DotWhisper.Core.Api;

public sealed class RefinerClient : IRefinerClient
{
    private readonly HttpClient _http;
    private readonly RefinerSettings _settings;
    private readonly ILogger<RefinerClient> _log;

    public RefinerClient(
        HttpClient http,
        IOptions<RefinerSettings> settings,
        ILogger<RefinerClient> log)
    {
        _http = http;
        _settings = settings.Value;
        _log = log;

        _http.BaseAddress ??= new Uri(_settings.BaseUrl);
    }

    public async Task WarmupAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Sending refiner warmup request to {BaseUrl}", _settings.BaseUrl);
        await RefineAsync("Warmup ping, ignore.", ct);
        _log.LogInformation("Refiner warmup complete");
    }

    public async Task<string> RefineAsync(string rawText, CancellationToken ct = default)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var request = new RefinerChatRequest
        {
            Model = _settings.Model,
            Stream = false,
            Options = new RefinerChatOptions
            {
                Temperature = _settings.Temperature,
                NumCtx = _settings.NumCtx,
                NumPredict = _settings.NumPredict
            },
            Messages = new[]
            {
                new RefinerChatMessage { Role = "system", Content = _settings.SystemPrompt },
                new RefinerChatMessage { Role = "user", Content = rawText }
            }
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await _http.PostAsJsonAsync("/api/chat", request, linkedCts.Token);
        var headersMs = sw.ElapsedMilliseconds;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(linkedCts.Token);
        sw.Stop();

        // Bottleneck diagnosis: split our own network/HTTP round-trip from what Ollama itself
        // reports (nanoseconds — model load vs prompt eval vs generation). Only computed when
        // Information logging is on, since parsing these extra fields is pure overhead otherwise.
        if (_log.IsEnabled(LogLevel.Information))
        {
            var bodyMs = sw.ElapsedMilliseconds - headersMs;
            var ollamaTotalMs = GetDurationMs(json, "total_duration");
            var loadMs = GetDurationMs(json, "load_duration");
            var promptEvalMs = GetDurationMs(json, "prompt_eval_duration");
            var evalMs = GetDurationMs(json, "eval_duration");
            var promptTokens = GetCount(json, "prompt_eval_count");
            var evalTokens = GetCount(json, "eval_count");

            _log.LogInformation(
                "[TIMING] Refine round-trip {TotalMs}ms (headers {HeadersMs}ms, body-read {BodyMs}ms) | " +
                "Ollama server-side: total={OllamaTotalMs}ms load={LoadMs}ms prompt_eval={PromptEvalMs}ms " +
                "({PromptTokens} tok) eval={EvalMs}ms ({EvalTokens} tok)",
                sw.ElapsedMilliseconds, headersMs, bodyMs, ollamaTotalMs, loadMs, promptEvalMs, promptTokens, evalMs, evalTokens);
        }

        return json.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private static long? GetDurationMs(JsonElement json, string propertyName) =>
        json.TryGetProperty(propertyName, out var el) ? el.GetInt64() / 1_000_000 : null;

    private static long? GetCount(JsonElement json, string propertyName) =>
        json.TryGetProperty(propertyName, out var el) ? el.GetInt64() : null;

    private sealed class RefinerChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("options")]
        public required RefinerChatOptions Options { get; init; }

        [JsonPropertyName("messages")]
        public required RefinerChatMessage[] Messages { get; init; }
    }

    private sealed class RefinerChatOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; init; }

        [JsonPropertyName("num_ctx")]
        public int NumCtx { get; init; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; init; }
    }

    private sealed class RefinerChatMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }
}
