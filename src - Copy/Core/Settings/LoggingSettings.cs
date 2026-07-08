namespace DotWhisper.Core.Settings;

public sealed class LoggingSettings
{
    public string MinimumLevel { get; init; } = "Information";
    public bool LogToFile { get; init; } = true;
}
