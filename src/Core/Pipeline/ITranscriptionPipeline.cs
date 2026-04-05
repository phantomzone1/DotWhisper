namespace DotWhisper.Core.Pipeline;

public interface ITranscriptionPipeline
{
    /// <summary>
    /// Transcribes audio from the provided stream and runs the result through text processors.
    /// Returns null if the result is empty/whitespace after processing.
    /// </summary>
    Task<string?> TranscribeAndProcessAsync(Stream audioStream, CancellationToken ct = default);
}
