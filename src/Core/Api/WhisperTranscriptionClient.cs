using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DotWhisper.Core.Settings;

namespace DotWhisper.Core.Api;

public sealed class WhisperTranscriptionClient : ITranscriptionClient
{
    private readonly HttpClient _http;
    private readonly WhisperSettings _settings;
    private readonly ILogger<WhisperTranscriptionClient> _log;

    public WhisperTranscriptionClient(
        HttpClient http,
        IOptions<WhisperSettings> settings,
        ILogger<WhisperTranscriptionClient> log)
    {
        _http = http;
        _settings = settings.Value;
        _log = log;

        _http.BaseAddress ??= new Uri(_settings.BaseUrl);
    }

    public async Task WarmupAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Sending warmup request to {BaseUrl}", _settings.BaseUrl);

        using var silence = GenerateSilentWav(durationSeconds: 1);
        var request = new TranscriptionRequest
        {
            Model = _settings.Model,
            Language = _settings.Language,
            Temperature = _settings.Temperature,
            VadFilter = _settings.VadFilter
        };

        await TranscribeAsync(silence, request, ct);
        _log.LogInformation("Warmup complete");
    }

    public async Task<string> TranscribeAsync(Stream audioStream, TranscriptionRequest request, CancellationToken ct = default)
    {
        // Read stream into byte array so we send Content-Length (not chunked encoding)
        byte[] audioBytes;
        if (audioStream is MemoryStream ms)
        {
            audioBytes = ms.ToArray();
        }
        else
        {
            using var temp = new MemoryStream();
            await audioStream.CopyToAsync(temp, ct);
            audioBytes = temp.ToArray();
        }

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(audioBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", "audio.wav");
        content.Add(new StringContent(request.Model), "model");
        content.Add(new StringContent(request.Language), "language");
        content.Add(new StringContent(request.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture)), "temperature");
        content.Add(new StringContent(request.VadFilter ? "true" : "false"), "vad_filter");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await _http.PostAsync("/v1/audio/transcriptions", content, ct);
        var headersMs = sw.ElapsedMilliseconds;

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        sw.Stop();

        // Bottleneck diagnosis: headers-received time (network + server processing before it starts
        // streaming the response) vs body-read time (large JSON payload / slow deserialize). Only
        // computed when Information logging is on.
        if (_log.IsEnabled(LogLevel.Information))
        {
            var bodyMs = sw.ElapsedMilliseconds - headersMs;
            _log.LogInformation(
                "[TIMING] Transcription round-trip {TotalMs}ms (headers {HeadersMs}ms, body-read {BodyMs}ms) with status {StatusCode}",
                sw.ElapsedMilliseconds, headersMs, bodyMs, response.StatusCode);
        }

        return json.GetProperty("text").GetString() ?? string.Empty;
    }

    private static MemoryStream GenerateSilentWav(int durationSeconds)
    {
        const int sampleRate = 16000;
        const int bitsPerSample = 16;
        const int channels = 1;
        int totalSamples = sampleRate * durationSeconds;
        int dataSize = totalSamples * (bitsPerSample / 8) * channels;

        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        // WAV header
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16); // subchunk size
        writer.Write((short)1); // PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write((short)bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataSize);

        // Silent samples
        writer.Write(new byte[dataSize]);
        writer.Flush();

        stream.Position = 0;
        return stream;
    }
}
