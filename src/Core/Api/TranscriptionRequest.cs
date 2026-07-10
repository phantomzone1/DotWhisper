namespace DotWhisper.Core.Api;

public sealed class TranscriptionRequest
{
    public required string Model { get; init; }
    public required string Language { get; init; }
    public double Temperature { get; init; }
    public bool VadFilter { get; init; }
}
