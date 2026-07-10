namespace DotWhisper.Core.Settings;

public sealed class WhisperSettings
{
    public string BaseUrl { get; init; } = "http://192.168.1.50:8000/v1";
    public string Model { get; init; } = "whisper-1";
    public string Language { get; init; } = "en";
    public double Temperature { get; init; }

    // Server-side Silero VAD: skips silence before decoding, a second layer against
    // silence-triggered repeat-loop hallucinations beyond our own client-side trimming.
    public bool VadFilter { get; init; } = true;

    public int ColdStartThresholdMinutes { get; init; } = 30;
}
