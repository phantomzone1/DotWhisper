using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DotWhisper.Core.Api;
using DotWhisper.Core.Settings;

namespace DotWhisper.Core.Pipeline;

public sealed class TranscriptionPipeline : ITranscriptionPipeline
{
    private readonly ITranscriptionClient _client;
    private readonly WhisperSettings _whisperSettings;
    private readonly IEnumerable<ITextProcessor> _processors;
    private readonly ILogger<TranscriptionPipeline> _log;

    public TranscriptionPipeline(
        ITranscriptionClient client,
        IOptions<WhisperSettings> whisperSettings,
        IEnumerable<ITextProcessor> processors,
        ILogger<TranscriptionPipeline> log)
    {
        _client = client;
        _whisperSettings = whisperSettings.Value;
        _processors = processors;
        _log = log;
    }

    public async Task<string?> TranscribeAndProcessAsync(Stream audioStream, CancellationToken ct = default)
    {
        var request = new TranscriptionRequest
        {
            Model = _whisperSettings.Model,
            Language = _whisperSettings.Language,
            Temperature = _whisperSettings.Temperature
        };

        _log.LogInformation("Sending {Bytes} bytes for transcription", audioStream.Length);
        var text = await _client.TranscribeAsync(audioStream, request, ct);

        // Trim and check for empty
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _log.LogInformation("Transcription result was empty or whitespace, discarding");
            return null;
        }

        // Run through text processors
        foreach (var processor in _processors)
        {
            text = processor.Process(text);
        }

        // Final trim after processors
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _log.LogInformation("Text was empty after processing pipeline, discarding");
            return null;
        }

        _log.LogInformation("Transcription complete: {Length} chars", text.Length);
        return text;
    }
}
