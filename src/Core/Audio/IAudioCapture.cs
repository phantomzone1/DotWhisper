namespace DotWhisper.Core.Audio;

public interface IAudioCapture
{
    /// <summary>
    /// Starts recording from the configured mic device.
    /// Returns a MemoryStream containing WAV audio when recording stops
    /// (via VAD silence detection, max duration, manual stop, or cancellation).
    /// Cancellation via the token discards audio (throws OperationCanceledException).
    /// </summary>
    Task<MemoryStream> RecordAsync(CancellationToken ct = default);

    /// <summary>
    /// Gracefully stops the current recording and returns the captured audio.
    /// Used by Manual Record/Stop menu — unlike CancellationToken which discards.
    /// </summary>
    void RequestStop();
}
