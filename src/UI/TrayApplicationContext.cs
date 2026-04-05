using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DotWhisper.Core.Audio;
using DotWhisper.Core.Pipeline;
using DotWhisper.Core.Settings;

namespace DotWhisper.UI;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ITranscriptionPipeline _pipeline;
    private readonly IAudioCapture _audioCapture;
    private readonly UiSettings _uiSettings;
    private readonly ILogger<TrayApplicationContext> _log;
    private readonly string _logDirectory;

    private readonly Icon _iconIdle;
    private readonly Icon _iconListening;
    private readonly Icon _iconProcessing;
    private readonly Icon _iconSuccess;
    private readonly Icon _iconError;

    private readonly List<string> _history = [];
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
        _uiSettings = uiSettings.Value;
        _log = log;
        _logDirectory = logDirectory;

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

        try
        {
            _hotkey = new HotkeyManager(_uiSettings.HotKey, OnHotkeyPressed);
            _log.LogInformation("TrayApplicationContext initialized, hotkey: {HotKey}", _uiSettings.HotKey);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Hotkey {HotKey} registration failed — use context menu to record", _uiSettings.HotKey);
            _tray.Text = Truncate("Hotkey " + _uiSettings.HotKey + " unavailable - use right-click menu", 63);
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

        // Record/Stop toggle — disabled during Processing or Error
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
        if (_state == AppState.Error)
        {
            // Re-validate mic before allowing recording from error state
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
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Record
            SetState(AppState.Listening, "Listening...");
            using var audio = await _audioCapture.RecordAsync(ct);
            _log.LogInformation("[TIMING] Recording: {ElapsedMs}ms", sw.ElapsedMilliseconds);

            ct.ThrowIfCancellationRequested();

            // Transcribe + process
            sw.Restart();
            SetState(AppState.Processing, "Processing...");
            var result = await _pipeline.TranscribeAndProcessAsync(audio, ct);
            _log.LogInformation("[TIMING] Transcribe+Process: {ElapsedMs}ms", sw.ElapsedMilliseconds);

            if (result != null)
            {
                AddToHistory(result);
                SetState(AppState.Idle, Truncate(result, 63));
                FlashSuccess();

                if (_uiSettings.AutoPaste)
                    ClipboardHelper.AutoPaste(result, _log);
                else
                    ClipboardHelper.SetText(result);
            }
            else
            {
                SetState(AppState.Idle, "DotWhisper — Idle");
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
        _tray.Text = Truncate(message, 63);
        RebuildMenuItems(_tray.ContextMenuStrip!);
    }

    private void SetState(AppState state, string tooltip)
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
        _tray.Text = Truncate(tooltip, 63);
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
            if (Directory.Exists(_logDirectory))
            {
                // Open the most recent log file, or the directory if none exist
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
