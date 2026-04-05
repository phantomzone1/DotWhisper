namespace DotWhisper.Core.Audio;

public interface IAudioCapture
{
    /// <summary>
    /// Starts recording from the configured mic device.
    /// Returns a MemoryStream containing WAV audio when recording stops
    /// (via VAD silence detection, max duration, or cancellation).
    /// </summary>
    Task<MemoryStream> RecordAsync(CancellationToken ct = default);
}
