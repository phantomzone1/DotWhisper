namespace DotWhisper.Core.Pipeline;

public interface ITranscriptionPipeline
{
    /// <summary>
    /// Orchestrates the full flow: capture audio → transcribe → process text → clipboard.
    /// </summary>
    Task<string?> ExecuteAsync(CancellationToken ct = default);
}
