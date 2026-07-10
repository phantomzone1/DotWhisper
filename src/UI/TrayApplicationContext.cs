using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using DotWhisper.Core.Api;
using DotWhisper.Core.Audio;
using DotWhisper.Core.Pipeline;
using DotWhisper.Core.Settings;

namespace DotWhisper.UI;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ITranscriptionPipeline _pipeline;
    private readonly IAudioCapture _audioCapture;
    private readonly IRefinerClient _refiner;
    private readonly ILogger<TrayApplicationContext> _log;
    private readonly float _successSoundVolume;
    private readonly string _logDirectory;

    private readonly Icon _iconIdle;
    private readonly Icon _iconListening1;
    private readonly Icon _iconListening2;
    private readonly Icon _iconProcessing1;
    private readonly Icon _iconProcessing2;
    private readonly Icon _iconRefining1;
    private readonly Icon _iconRefining2;
    private readonly Icon _iconError;

    private readonly System.Windows.Forms.Timer _pulseTimer;
    private bool _pulseFrameToggle;

    private readonly HotkeyManager? _hotkey;
    private readonly HotkeyManager? _refineHotkey;

    private CancellationTokenSource? _cts;
    private AppState _state = AppState.Idle;

    private enum AppState { Idle, Listening, Processing, Refining, Error }

    public TrayApplicationContext(
        ITranscriptionPipeline pipeline,
        IAudioCapture audioCapture,
        IRefinerClient refiner,
        IOptions<UiSettings> uiSettings,
        ILogger<TrayApplicationContext> log,
        string logDirectory)
    {
        _pipeline = pipeline;
        _audioCapture = audioCapture;
        _refiner = refiner;
        _log = log;
        _logDirectory = logDirectory;

        var uiConfig = uiSettings.Value;
        _successSoundVolume = uiConfig.SuccessSoundVolume;

        _iconIdle = LoadIcon("idle.ico");
        _iconListening1 = LoadIcon("listening_1.ico");
        _iconListening2 = LoadIcon("listening_2.ico");
        _iconProcessing1 = LoadIcon("processing_1.ico");
        _iconProcessing2 = LoadIcon("processing_2.ico");
        _iconRefining1 = LoadIcon("refining_1.ico");
        _iconRefining2 = LoadIcon("refining_2.ico");
        _iconError = LoadIcon("error.ico");

        _pulseTimer = new System.Windows.Forms.Timer { Interval = Math.Max(50, uiConfig.IconPulseIntervalMs) };
        _pulseTimer.Tick += (_, _) => TogglePulseFrame();

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

        try
        {
            _refineHotkey = new HotkeyManager(uiConfig.RefineHotKey, OnRefineHotkeyPressed);
            _log.LogInformation("Refine hotkey registered: {RefineHotKey}", uiConfig.RefineHotKey);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Refine hotkey {RefineHotKey} registration failed", uiConfig.RefineHotKey);
            // Refine hotkey unavailable — raw-transcription hotkey still works
        }

        _ = WarmupRefinerAsync();

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

    private void OnHotkeyPressed() => HandleHotkeyPress(refine: false);

    private void OnRefineHotkeyPressed() => HandleHotkeyPress(refine: true);

    private void HandleHotkeyPress(bool refine)
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

        if (_state == AppState.Listening)
        {
            // Second press while recording: force-stop and send what's been captured so far,
            // same as the "Stop Recording" menu item — a manual override for when background
            // noise keeps the auto silence-detection from ever firing on its own.
            StopAndTranscribe();
            return;
        }

        if (_state is AppState.Processing or AppState.Refining)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _log.LogInformation("Previous {State} cancelled via hotkey, starting fresh recording", _state);
        }

        _ = StartRecordingAsync(refine);
    }

    private void StopAndTranscribe()
    {
        _log.LogInformation("Recording stopped manually, sending for transcription");
        _audioCapture.RequestStop();
    }

    private async Task StartRecordingAsync(bool refine = false)
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
                if (refine)
                {
                    SetState(AppState.Refining);
                    sw.Restart();
                    try
                    {
                        result = await _refiner.RefineAsync(result, ct);
                        _log.LogInformation("[TIMING] Refine: {ElapsedMs}ms", sw.ElapsedMilliseconds);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        // Refiner-side timeout, not a user cancellation — fall back to raw text below.
                        _log.LogWarning("Refiner call timed out, falling back to raw transcription");
                    }
                    catch (Exception ex)
                    {
                        // Don't lose the words already said over a cleanup-container hiccup.
                        _log.LogWarning(ex, "Refiner call failed, falling back to raw transcription");
                    }
                }

                ClipboardHelper.SetText(result);
                SetState(AppState.Idle);
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

    private async Task WarmupRefinerAsync()
    {
        try
        {
            await _refiner.WarmupAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Refiner warmup failed; first refine request will eat the cold-start cost");
        }
    }

    private void SetError(string message)
    {
        _state = AppState.Error;
        _pulseTimer.Stop();
        _tray.Icon = _iconError;
        _log.LogError("Error: {Message}", message);
        RebuildMenuItems(_tray.ContextMenuStrip!);
    }

    private void SetState(AppState state)
    {
        _state = state;

        if (state is AppState.Listening or AppState.Processing or AppState.Refining)
        {
            _pulseFrameToggle = false;
            _tray.Icon = state switch
            {
                AppState.Listening => _iconListening1,
                AppState.Refining => _iconRefining1,
                _ => _iconProcessing1
            };
            _pulseTimer.Start();
        }
        else
        {
            _pulseTimer.Stop();
            _tray.Icon = state == AppState.Error ? _iconError : _iconIdle;
        }

        RebuildMenuItems(_tray.ContextMenuStrip!);
    }

    private void TogglePulseFrame()
    {
        _pulseFrameToggle = !_pulseFrameToggle;
        _tray.Icon = _state switch
        {
            AppState.Listening => _pulseFrameToggle ? _iconListening2 : _iconListening1,
            AppState.Processing => _pulseFrameToggle ? _iconProcessing2 : _iconProcessing1,
            AppState.Refining => _pulseFrameToggle ? _iconRefining2 : _iconRefining1,
            _ => _tray.Icon
        };
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
        _refineHotkey?.Dispose();
        _pulseTimer.Stop();
        _pulseTimer.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }

    private void PlaySuccessSound()
    {
        try
        {
            const int sampleRate = 44100;
            const float duration = 0.4f; // 400ms — long enough to read as a "ding", not a click
            const float volume = 0.35f;
            const float fundamental = 1200f; // bright, bell-like pitch (vs. the old 800Hz boop)
            const float decayRate = 6f; // exponential decay — a natural chime tail, not a hard cutoff
            int samples = (int)(sampleRate * duration);

            var buffer = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = MathF.Exp(-decayRate * t);
                float tone = MathF.Sin(2 * MathF.PI * fundamental * t)
                    + 0.4f * MathF.Sin(2 * MathF.PI * fundamental * 2f * t); // overtone for a bell-like timbre
                buffer[i] = volume * envelope * tone;
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
