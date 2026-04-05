namespace DotWhisper.Core.Settings;

public sealed class UiSettings
{
    public string HotKey { get; init; } = "F22";
    public bool AutoPaste { get; init; } = true;
    public int HistoryLimit { get; init; } = 15;
}
