namespace DotWhisper.Core.Settings;

public sealed class RefinerSettings
{
    // IPv4 literal, not "localhost" — "localhost" resolves to both ::1 and 127.0.0.1 on Windows,
    // and if the IPv6 candidate stalls (common with Docker port publishing), connect hangs for
    // ~15s before falling back. An IP literal skips resolution entirely.
    public string BaseUrl { get; init; } = "http://127.0.0.1:11434";
    public string Model { get; init; } = "llama3.2:1b";

    public string SystemPrompt { get; init; } =
        "You are a professional text correction utility. Rewrite casual, spoken-voice transcripts into polished, " +
        "professional business English suitable for Slack or corporate email. Strip out all verbal fillers, " +
        "\"ums\", \"ahs\", repetitions, and casual slang. Maintain the original intent exactly, but correct grammar, " +
        "punctuation, and structural flow. Do not add introductory text, do not explain your changes, and do not " +
        "provide pleasantries. Output ONLY the finalized text.";

    public double Temperature { get; init; } = 0.1;

    // Context window size in tokens — restricts the model's scratchpad footprint to avoid cold-start VRAM delays.
    public int NumCtx { get; init; } = 2048;

    public int NumPredict { get; init; } = 150;
    public int TimeoutSeconds { get; init; } = 10;
}
