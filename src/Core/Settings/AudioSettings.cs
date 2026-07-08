namespace DotWhisper.Core.Settings;

public sealed class AudioSettings
{
    public string[] MicDevices { get; init; } = ["Remote Audio"];
    public double SilenceThreshold { get; init; } = 0.02;
    public int SilenceTimeoutMs { get; init; } = 1200;
    public int MaxRecordSeconds { get; init; } = 300;

    // Cap on any held-back pause, mid-recording or at final stop — kept from the start of the
    // pause (closest to the prior speech), so a quiet trailing word survives while a long
    // silence gap doesn't reach Whisper large enough to trigger a repeat-loop hallucination.
    //
    // Deliberately NOT derived from SilenceTimeoutMs, even though both are "silence" settings:
    // SilenceTimeoutMs answers "how patient to be before deciding you've stopped talking";
    // this answers "how much of that silence is safe to actually send." Equating them breaks
    // one of the two bugs this design fixes — keep every sub-timeout pause whole and a long
    // mid-recording pause reaches Whisper uncapped again, or discard the whole buffer once the
    // timeout is hit (which is every silence-triggered stop, by definition) and trailing-word
    // clipping becomes the default again. Also note: the primary defense against Whisper's
    // silence-hallucination behavior is the server-side VadFilter (Silero VAD, on by default —
    // see WhisperSettings.VadFilter) sent with every request. This is a secondary, local safety
    // margin sized around how long a trailing word takes to fade out, not a precisely-derived
    // Whisper-safety threshold — see README.md for the full writeup.
    public int TrailingPaddingMs { get; init; } = 500;
}
