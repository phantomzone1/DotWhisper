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

        var tcs = new TaskCompletionSource<bool>();
        _activeTcs = tcs;

        capture.DataAvailable += (_, e) =>
        {
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetCanceled(ct);
                return;
            }

            writer.Write(e.Buffer, 0, e.BytesRecorded);

            var chunkDuration = TimeSpan.FromSeconds((double)e.BytesRecorded / waveFormat.AverageBytesPerSecond);
            totalDuration += chunkDuration;

            var rms = CalculateRms(e.Buffer, e.BytesRecorded);

            if (rms >= _settings.SilenceThreshold)
            {
                speechDetected = true;
                silenceDuration = TimeSpan.Zero;
            }
            else if (speechDetected)
            {
                silenceDuration += chunkDuration;
            }

            if (totalDuration >= maxDuration)
            {
                _log.LogInformation("Max recording duration reached ({MaxSeconds}s)", _settings.MaxRecordSeconds);
                tcs.TrySetResult(true);
            }
            else if (speechDetected && silenceDuration >= silenceTimeout)
            {
                _log.LogInformation("Silence detected after {TotalMs}ms of recording", totalDuration.TotalMilliseconds);
                tcs.TrySetResult(true);
            }
        };

        capture.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null)
                tcs.TrySetException(e.Exception);
            else
                tcs.TrySetResult(true);
        };

        _log.LogInformation("Recording started on device {DeviceNumber}", deviceNumber);
        capture.StartRecording();

        try
        {
            await tcs.Task;
        }
        finally
        {
            _activeTcs = null;
            capture.StopRecording();
            await writer.FlushAsync();
        }

        audioStream.Position = 0;
        _log.LogInformation("Recording complete: {TotalMs}ms, {Bytes} bytes",
            totalDuration.TotalMilliseconds, audioStream.Length);

        return audioStream;
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
