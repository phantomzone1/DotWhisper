namespace DotWhisper.Core.Api;

/// <summary>
/// Provider-agnostic transcription contract.
/// </summary>
public interface ITranscriptionClient
{
    Task WarmupAsync(CancellationToken ct = default);
    Task<string> TranscribeAsync(Stream audioStream, TranscriptionRequest request, CancellationToken ct = default);
}
