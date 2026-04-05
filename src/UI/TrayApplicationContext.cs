using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using DotWhisper.Core.Audio;
using DotWhisper.Core.Pipeline;
using DotWhisper.Core.Settings;

namespace DotWhisper.UI;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ITranscriptionPipeline _pipeline;
    private readonly IAudioCapture _audioCapture;
    private readonly ILogger<TrayApplicationContext> _log;
    private readonly float _successSoundVolume;
    private readonly string _logDirectory;

    private readonly Icon _iconIdle;
    private readonly Icon _iconListening;
    private readonly Icon _iconProcessing;
    private readonly Icon _iconSuccess;
    private readonly Icon _iconError;

    private readonly HotkeyManager? _hotkey;

    private CancellationTokenSource? _cts;
    private AppState _state = AppState.Idle;

    private enum AppState { Idle, Listening, Processing, Error }

    public TrayApplicationContext(
        ITranscriptionPipeline pipeline,
        IAudioCapture audioCapture,
        IOptions<UiSettings> uiSettings,
        ILogger<TrayApplicationContext> log,
        string logDirectory)
    {
        _pipeline = pipeline;
        _audioCapture = audioCapture;
        _log = log;
        _logDirectory = logDirectory;

        var uiConfig = uiSettings.Value;
        _successSoundVolume = uiConfig.SuccessSoundVolume;

        _iconIdle = LoadIcon("idle.ico");
        _iconListening = LoadIcon("listening.ico");
        _iconProcessing = LoadIcon("processing.ico");
        _iconSuccess = LoadIcon("success.ico");
        _iconError = LoadIcon("error.ico");

        _tray = new NotifyIcon
        {
            Icon = _iconIdle,
            Text = "DotWhisper",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        try
        {
            _hotkey = new HotkeyManager(uiConfig.HotKey, OnHotkeyPressed);
            _log.LogInformation("TrayApplicationContext initialized, hotkey: {HotKey}", uiConfig.HotKey);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Hotkey {HotKey} registration failed", uiConfig.HotKey);
            // Hotkey unavailable — menu-only mode
        }

        // Validate mic device at startup
        try
        {
            _audioCapture.ValidateMicDevice();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Mic validation failed at startup");
            SetError(ex.Message);
        }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        RebuildMenuItems(menu);
        return menu;
    }

    private void RebuildMenuItems(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        if (_state == AppState.Listening)
        {
            menu.Items.Add(new ToolStripMenuItem("Stop Recording", null, (_, _) => StopAndTranscribe()));
        }
        else
        {
            var recordItem = new ToolStripMenuItem("Start Recording", null, (_, _) => OnHotkeyPressed());
            recordItem.Enabled = _state == AppState.Idle;
            menu.Items.Add(recordItem);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open Log File", null, (_, _) => OpenLogFile()));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()));
    }

    private void OnHotkeyPressed()
    {
        if (_state == AppState.Error)
        {
            try
            {
                _audioCapture.ValidateMicDevice();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Mic still not available");
                SetError(ex.Message);
                return;
            }
        }

        if (_state == AppState.Listening || _state == AppState.Processing)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _log.LogInformation("Previous {State} cancelled via hotkey, starting fresh recording", _state);
        }

        _ = StartRecordingAsync();
    }

    private void StopAndTranscribe()
    {
        _audioCapture.RequestStop();
    }

    private async Task StartRecordingAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var sw = Stopwatch.StartNew();

            SetState(AppState.Listening);
            using var audio = await _audioCapture.RecordAsync(ct);
            _log.LogInformation("[TIMING] Recording: {ElapsedMs}ms", sw.ElapsedMilliseconds);

            ct.ThrowIfCancellationRequested();

            sw.Restart();
            SetState(AppState.Processing);
            var result = await _pipeline.TranscribeAndProcessAsync(audio, ct);
            _log.LogInformation("[TIMING] Transcribe+Process: {ElapsedMs}ms", sw.ElapsedMilliseconds);

            if (result != null)
            {
                ClipboardHelper.SetText(result);
                SetState(AppState.Idle);
                FlashSuccess();
                PlaySuccessSound();
            }
            else
            {
                SetState(AppState.Idle);
            }
        }
        catch (OperationCanceledException)
        {
            _log.LogDebug("Recording/transcription cancelled");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Recording/transcription failed");
            SetError(ex.Message);
        }
    }

    private void SetError(string message)
    {
        _state = AppState.Error;
        _tray.Icon = _iconError;
        _log.LogError("Error: {Message}", message);
        RebuildMenuItems(_tray.ContextMenuStrip!);
    }

    private void SetState(AppState state)
    {
        _state = state;
        _tray.Icon = state switch
        {
            AppState.Idle => _iconIdle,
            AppState.Listening => _iconListening,
            AppState.Processing => _iconProcessing,
            AppState.Error => _iconError,
            _ => _iconIdle
        };
        RebuildMenuItems(_tray.ContextMenuStrip!);
    }

    private void FlashSuccess()
    {
        _tray.Icon = _iconSuccess;

        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) =>
        {
            _tray.Icon = _iconIdle;
            timer.Stop();
            timer.Dispose();
        };
        timer.Start();
    }

    private void OpenLogFile()
    {
        try
        {
            if (Directory.Exists(_logDirectory))
            {
                var latestLog = Directory.GetFiles(_logDirectory, "*.log")
                    .OrderByDescending(File.GetLastWriteTime)
                    .FirstOrDefault();

                var target = latestLog ?? _logDirectory;
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            else
            {
                _log.LogWarning("Log directory not found: {Path}", _logDirectory);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to open log file");
        }
    }

    private void ExitApplication()
    {
        _log.LogInformation("Application exiting");
        _cts?.Cancel();
        _cts?.Dispose();
        _hotkey?.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }

    private void PlaySuccessSound()
    {
        try
        {
            const int sampleRate = 44100;
            const float duration = 0.03f; // 30ms click
            const float volume = 0.3f;
            int samples = (int)(sampleRate * duration);

            var buffer = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = 1f - (float)i / samples;
                buffer[i] = volume * envelope * MathF.Sin(2 * MathF.PI * 800f * t);
            }

            var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1))
            {
                BufferLength = buffer.Length * 4 + 4096,
                ReadFully = false
            };
            var bytes = new byte[buffer.Length * 4];
            Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);
            provider.AddSamples(bytes, 0, bytes.Length);

            var wo = new WaveOutEvent { Volume = _successSoundVolume };
            wo.Init(provider);
            wo.PlaybackStopped += (_, _) => wo.Dispose();
            wo.Play();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to play success sound");
        }
    }

    private static Icon LoadIcon(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Icons", name);
        return new Icon(path);
    }

}
