namespace DotWhisper.Core.Settings;

public sealed class UiSettings
{
    public string HotKey { get; init; } = "F22";
    public string RefineHotKey { get; init; } = "F23";
    public float SuccessSoundVolume { get; init; } = 0.5f;
    public int IconPulseIntervalMs { get; init; } = 500;
}
