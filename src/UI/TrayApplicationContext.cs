using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DotWhisper.Core.Api;
using DotWhisper.Core.Audio;
using DotWhisper.Core.Pipeline;
using DotWhisper.Core.Settings;

namespace DotWhisper.UI;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ITranscriptionPipeline _pipeline;
    private readonly ITranscriptionClient _transcriptionClient;
    private readonly IAudioCapture _audioCapture;
    private readonly WhisperSettings _whisperSettings;
    private readonly UiSettings _uiSettings;
    private readonly ILogger<TrayApplicationContext> _log;
    private readonly string _logFilePath;

    private readonly Icon _iconIdle;
    private readonly Icon _iconListening;
    private readonly Icon _iconProcessing;
    private readonly Icon _iconSuccess;
    private readonly Icon _iconError;

    private readonly List<string> _history = [];
    private readonly HotkeyManager _hotkey;

    private CancellationTokenSource? _cts;
    private AppState _state = AppState.Idle;
    private DateTime _lastActivityUtc = DateTime.UtcNow;

    private enum AppState { Idle, Listening, Processing }

    public TrayApplicationContext(
        ITranscriptionPipeline pipeline,
        ITranscriptionClient transcriptionClient,
        IAudioCapture audioCapture,
        IOptions<WhisperSettings> whisperSettings,
        IOptions<UiSettings> uiSettings,
        ILogger<TrayApplicationContext> log,
        string logFilePath)
    {
        _pipeline = pipeline;
        _transcriptionClient = transcriptionClient;
        _audioCapture = audioCapture;
        _whisperSettings = whisperSettings.Value;
        _uiSettings = uiSettings.Value;
        _log = log;
        _logFilePath = logFilePath;

        _iconIdle = LoadIcon("idle.ico");
        _iconListening = LoadIcon("listening.ico");
        _iconProcessing = LoadIcon("processing.ico");
        _iconSuccess = LoadIcon("success.ico");
        _iconError = LoadIcon("error.ico");

        _tray = new NotifyIcon
        {
            Icon = _iconIdle,
            Text = "DotWhisper — Idle",
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _hotkey = new HotkeyManager(_uiSettings.HotKey, OnHotkeyPressed);
        _log.LogInformation("TrayApplicationContext initialized, hotkey: {HotKey}", _uiSettings.HotKey);
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

        // Record/Stop toggle
        if (_state == AppState.Listening)
        {
            menu.Items.Add(new ToolStripMenuItem("Stop Recording", null, (_, _) => StopAndTranscribe()));
        }
        else
        {
            var recordItem = new ToolStripMenuItem("Start Recording", null, (_, _) => OnHotkeyPressed());
            recordItem.Enabled = _state != AppState.Processing;
            menu.Items.Add(recordItem);
        }

        menu.Items.Add(new ToolStripSeparator());

        // History
        if (_history.Count > 0)
        {
            foreach (var entry in _history)
            {
                var snippet = entry.Length > 60 ? entry[..57] + "..." : entry;
                var fullText = entry;
                menu.Items.Add(new ToolStripMenuItem(snippet, null, (_, _) =>
                {
                    ClipboardHelper.SetText(fullText);
                }));
            }

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Clear History", null, (_, _) =>
            {
                _history.Clear();
                RebuildMenuItems(menu);
            }));
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open Log File", null, (_, _) => OpenLogFile()));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()));
    }

    private void OnHotkeyPressed()
    {
        if (_state == AppState.Listening || _state == AppState.Processing)
        {
            // F22 during active work: cancel and discard via CancellationToken
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _log.LogInformation("Previous {State} cancelled via F22, starting fresh recording", _state);
        }

        _ = StartRecordingAsync();
    }

    private void StopAndTranscribe()
    {
        // Manual stop: gracefully stop recording so audio is sent for transcription
        // (unlike F22 which cancels/discards via CancellationToken)
        _audioCapture.RequestStop();
    }

    private async Task StartRecordingAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            // Warmup if idle too long
            var idleMinutes = (DateTime.UtcNow - _lastActivityUtc).TotalMinutes;
            if (idleMinutes >= _whisperSettings.ColdStartThresholdMinutes)
            {
                _log.LogInformation("Idle for {Minutes:F0} minutes, sending warmup", idleMinutes);
                SetState(AppState.Processing, "Warming up...");
                await _transcriptionClient.WarmupAsync(ct);
            }

            // Record
            SetState(AppState.Listening, "Listening...");
            using var audio = await _audioCapture.RecordAsync(ct);

            ct.ThrowIfCancellationRequested();

            // Transcribe + process
            SetState(AppState.Processing, "Processing...");
            var result = await _pipeline.TranscribeAndProcessAsync(audio, ct);

            if (result != null)
            {
                AddToHistory(result);
                SetState(AppState.Idle, Truncate(result, 128));
                FlashIcon(_iconSuccess);

                if (_uiSettings.AutoPaste)
                    ClipboardHelper.AutoPaste(result, _log);
                else
                    ClipboardHelper.SetText(result);
            }
            else
            {
                // Empty/whitespace result — silently return to idle
                SetState(AppState.Idle, "DotWhisper — Idle");
            }

            _lastActivityUtc = DateTime.UtcNow;
        }
        catch (OperationCanceledException)
        {
            // Expected when F22 restarts — don't treat as error
            _log.LogDebug("Recording/transcription cancelled");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Recording/transcription failed");
            SetState(AppState.Idle, Truncate(ex.Message, 128));
            FlashIcon(_iconError);
        }
    }

    private void SetState(AppState state, string tooltip)
    {
        _state = state;
        _tray.Icon = state switch
        {
            AppState.Idle => _iconIdle,
            AppState.Listening => _iconListening,
            AppState.Processing => _iconProcessing,
            _ => _iconIdle
        };
        _tray.Text = Truncate(tooltip, 128);
        RebuildMenuItems(_tray.ContextMenuStrip!);
    }

    private void FlashIcon(Icon icon)
    {
        var previous = _tray.Icon;
        _tray.Icon = icon;

        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) =>
        {
            _tray.Icon = _iconIdle;
            timer.Stop();
            timer.Dispose();
        };
        timer.Start();
    }

    private void AddToHistory(string text)
    {
        _history.Insert(0, text);
        if (_history.Count > _uiSettings.HistoryLimit)
            _history.RemoveAt(_history.Count - 1);
        RebuildMenuItems(_tray.ContextMenuStrip!);
    }

    private void OpenLogFile()
    {
        try
        {
            if (File.Exists(_logFilePath))
                Process.Start(new ProcessStartInfo(_logFilePath) { UseShellExecute = true });
            else
                _log.LogWarning("Log file not found: {Path}", _logFilePath);
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
        _hotkey.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }

    private static Icon LoadIcon(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Icons", name);
        return new Icon(path);
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }
}
