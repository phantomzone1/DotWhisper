namespace DotWhisper.Core.Settings;

public sealed class WhisperSettings
{
    public string BaseUrl { get; init; } = "http://192.168.1.50:8000/v1";
    public string Model { get; init; } = "whisper-1";
    public string Language { get; init; } = "en";
    public int Temperature { get; init; }
    public int ColdStartThresholdMinutes { get; init; } = 30;
}
