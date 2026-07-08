namespace DotWhisper.Core.Settings;

public sealed class AudioSettings
{
    public string[] MicDevices { get; init; } = ["Remote Audio"];
    public double SilenceThreshold { get; init; } = 0.02;
    public int SilenceTimeoutMs { get; init; } = 1200;
    public int MaxRecordSeconds { get; init; } = 300;
}
