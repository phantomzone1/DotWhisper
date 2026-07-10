namespace DotWhisper.Core.Api;

/// <summary>
/// Refines raw dictated text into polished, professional prose via a local LLM.
/// </summary>
public interface IRefinerClient
{
    Task WarmupAsync(CancellationToken ct = default);
    Task<string> RefineAsync(string rawText, CancellationToken ct = default);
}
