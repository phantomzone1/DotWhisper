using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using DotWhisper.Core.Settings;

namespace DotWhisper.Core.Audio;

public sealed class AudioCapture : IAudioCapture
{
    private readonly AudioSettings _settings;
    private readonly ILogger<AudioCapture> _log;
    private TaskCompletionSource<bool>? _activeTcs;

    public AudioCapture(IOptions<AudioSettings> settings, ILogger<AudioCapture> log)
    {
        _settings = settings.Value;
        _log = log;
    }

    public void RequestStop()
    {
        _activeTcs?.TrySetResult(true);
    }

    public void ValidateMicDevice()
    {
        FindMicDevice();
    }

	public async Task<MemoryStream> RecordAsync(CancellationToken ct = default)
	{
		var deviceNumber = FindMicDevice();
		var waveFormat = new WaveFormat(rate: 16000, bits: 16, channels: 1);
		using var capture = new WaveInEvent
		{
			DeviceNumber = deviceNumber,
			WaveFormat = waveFormat,
			BufferMilliseconds = 100
		};

		var audioStream = new MemoryStream();
		var writer = new WaveFileWriter(audioStream, waveFormat);

		var speechDetected = false;
		var silenceDuration = TimeSpan.Zero;
		var totalDuration = TimeSpan.Zero;
		var maxDuration = TimeSpan.FromSeconds(_settings.MaxRecordSeconds);
		var silenceTimeout = TimeSpan.FromMilliseconds(_settings.SilenceTimeoutMs);
		var pendingSilence = new List<byte[]>();

		// Keeps only the portion of held-back silence closest to the last confirmed speech (up to
		// TrailingPaddingMs), then drops the rest. Applied both when speech resumes mid-recording
		// (a long mid-sentence pause is capped, not sent to Whisper in full) and at final stop
		// (a quiet trailing word survives, the real dead air after it doesn't) — either an uncapped
		// mid-recording pause or an untrimmed trailing one is enough to trigger Whisper's
		// silence-triggered repeat-loop hallucination.
		void FlushBoundedSilence()
		{
			var cap = TimeSpan.FromMilliseconds(_settings.TrailingPaddingMs);
			var written = TimeSpan.Zero;

			foreach (var buffered in pendingSilence)
			{
				if (written >= cap)
					break;

				writer.Write(buffered, 0, buffered.Length);
				written += TimeSpan.FromSeconds((double)buffered.Length / waveFormat.AverageBytesPerSecond);
			}

			pendingSilence.Clear();
		}

		var stopRequestedTcs = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);

		var recordingStoppedTcs = new TaskCompletionSource<bool>(
			TaskCreationOptions.RunContinuationsAsynchronously);

		_activeTcs = stopRequestedTcs;

		using var _ = ct.Register(() => stopRequestedTcs.TrySetCanceled(ct));

		capture.DataAvailable += (_, e) =>
		{
			if (ct.IsCancellationRequested)
			{
				stopRequestedTcs.TrySetCanceled(ct);
				return;
			}

			var chunkDuration = TimeSpan.FromSeconds(
				(double)e.BytesRecorded / waveFormat.AverageBytesPerSecond);

			totalDuration += chunkDuration;

			var rms = CalculateRms(e.Buffer, e.BytesRecorded);

			if ((int)(totalDuration.TotalMilliseconds / 100) % 10 == 0)
			{
				_log.LogDebug(
					"RMS: {Rms:F4} | Threshold: {Threshold} | Speech: {Speech} | Silence: {SilenceMs}ms",
					rms, _settings.SilenceThreshold, speechDetected, silenceDuration.TotalMilliseconds);
			}

			if (rms >= _settings.SilenceThreshold)
			{
				// Speech resumed: keep only a bounded slice of the pause (closest to the prior
				// speech) instead of the whole thing — long enough for a natural breath, short
				// enough to not hand Whisper a silence gap large enough to trigger a mid-transcript
				// repeat-loop hallucination.
				FlushBoundedSilence();

				writer.Write(e.Buffer, 0, e.BytesRecorded);
				speechDetected = true;
				silenceDuration = TimeSpan.Zero;
			}
			else if (speechDetected)
			{
				// Hold silence instead of writing it — if this turns out to be trailing silence
				// (recording stops before speech resumes), it never reaches the file Whisper sees,
				// which avoids the repeated-last-word hallucination Whisper produces on silent tails.
				pendingSilence.Add(e.Buffer[..e.BytesRecorded]);
				silenceDuration += chunkDuration;
			}

			if (totalDuration >= maxDuration)
			{
				_log.LogInformation("Max recording duration reached ({MaxSeconds}s)",
					_settings.MaxRecordSeconds);
				stopRequestedTcs.TrySetResult(true);
			}
			else if (speechDetected && silenceDuration >= silenceTimeout)
			{
				_log.LogInformation("Silence detected after {TotalMs}ms of recording",
					totalDuration.TotalMilliseconds);
				stopRequestedTcs.TrySetResult(true);
			}
		};

		capture.RecordingStopped += (_, e) =>
		{
			if (e.Exception != null)
			{
				stopRequestedTcs.TrySetException(e.Exception);
				recordingStoppedTcs.TrySetException(e.Exception);
			}
			else
			{
				recordingStoppedTcs.TrySetResult(true);
			}
		};

		_log.LogInformation("Recording started on device {DeviceNumber}", deviceNumber);
		capture.StartRecording();

		try
		{
			await stopRequestedTcs.Task.ConfigureAwait(false);
		}
		finally
		{
			_activeTcs = null;
			capture.StopRecording();

			try
			{
				await recordingStoppedTcs.Task
					.WaitAsync(TimeSpan.FromSeconds(5))
					.ConfigureAwait(false);
			}
			catch (TimeoutException)
			{
				_log.LogWarning("Timed out waiting for RecordingStopped — proceeding anyway");
			}

			// Same bounded flush as mid-recording: keeps a quiet trailing word, drops the rest.
			FlushBoundedSilence();

			writer.Dispose(); // NAudio is fully stopped — WAV header is now safe to finalize
		}

		var wavBytes = audioStream.ToArray();

		_log.LogInformation(
			"Recording complete: {TotalMs}ms, {Bytes} bytes",
			totalDuration.TotalMilliseconds,
			wavBytes.Length);

		return new MemoryStream(wavBytes, writable: false);
	}

    private int FindMicDevice()
    {
        var deviceCount = WaveInEvent.DeviceCount;

        for (int i = 0; i < deviceCount; i++)
        {
            var capabilities = WaveInEvent.GetCapabilities(i);

            foreach (var target in _settings.MicDevices)
            {
                if (capabilities.ProductName.Contains(target, StringComparison.OrdinalIgnoreCase))
                {
                    _log.LogInformation("Matched mic device: {DeviceName} (index {Index}) for target '{Target}'",
                        capabilities.ProductName, i, target);
                    return i;
                }
            }
        }

        throw new InvalidOperationException(
            $"No matching mic device found. Searched for: [{string.Join(", ", _settings.MicDevices)}]. " +
            $"Available devices: [{string.Join(", ", GetDeviceNames())}]");
    }

    private static IEnumerable<string> GetDeviceNames()
    {
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            yield return WaveInEvent.GetCapabilities(i).ProductName;
    }

    private static double CalculateRms(byte[] buffer, int bytesRecorded)
    {
        int sampleCount = bytesRecorded / 2;
        double sumOfSquares = 0;

        for (int i = 0; i < bytesRecorded; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            double normalized = sample / 32768.0;
            sumOfSquares += normalized * normalized;
        }

        return Math.Sqrt(sumOfSquares / sampleCount);
    }
}
