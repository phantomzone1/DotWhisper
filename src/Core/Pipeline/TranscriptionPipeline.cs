using Microsoft.Extensions.Logging;
using DotWhisper.Core.Api;
using DotWhisper.Core.Audio;
using DotWhisper.Core.Settings;
using Microsoft.Extensions.Options;

namespace DotWhisper.Core.Pipeline;

public sealed class TranscriptionPipeline : ITranscriptionPipeline
{
    private readonly IAudioCapture _capture;
    private readonly ITranscriptionClient _client;
    private readonly WhisperSettings _whisperSettings;
    private readonly IEnumerable<ITextProcessor> _processors;
    private readonly ILogger<TranscriptionPipeline> _log;

    public TranscriptionPipeline(
        IAudioCapture capture,
        ITranscriptionClient client,
        IOptions<WhisperSettings> whisperSettings,
        IEnumerable<ITextProcessor> processors,
        ILogger<TranscriptionPipeline> log)
    {
        _capture = capture;
        _client = client;
        _whisperSettings = whisperSettings.Value;
        _processors = processors;
        _log = log;
    }

    public async Task<string?> ExecuteAsync(CancellationToken ct = default)
    {
        // TODO: Full implementation in Phase 4
        _log.LogInformation("Pipeline stub — not yet implemented");
        await Task.CompletedTask;
        return null;
    }
}
