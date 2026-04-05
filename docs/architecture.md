🏗️ DotWhisper: Final Technical Specification (V5.0)

Project Goal: A high-performance, minimalist .NET 10 task tray application providing "always-on" local voice-to-text dictation by bridging local audio capture to a containerized Whisper API on an RTX 5090.
1. Repository & Directory Structure

Adhering to the "No-Noise" naming convention and standard .NET GitHub layouts.
Plaintext

DotWhisper/
├── .github/                # CI/CD Workflows (GitHub Actions)
├── artifacts/              # Static assets
│   └── icons/              # Tray icon images (Idle, Listening, Processing, Success, Error)
├── docs/                   # Architecture docs and diagrams
├── src/                    # Production Source Code
│   ├── UI/                 # DotWhisper.UI (WinForms, Tray, Hotkeys)
│   └── Core/               # DotWhisper.Core (Audio Engine, API Client)
├── tests/                  # Testing Projects
│   └── UnitTests/          # DotWhisper.Tests (xUnit, NSubstitute)
├── DotWhisper.sln          # Solution file at root
├── Directory.Build.props   # Global build settings (AOT, versions)
├── Directory.Packages.props# Central Package Management (CPM)
├── packages.lock.json      # Dependency supply chain lock
├── .gitignore              # .NET specific exclusions
└── README.md

2. UI & Context Menu Logic

The NotifyIcon serves as the primary interaction point. The context menu is dynamically updated to provide history and manual controls.

    Manual Record/Stop: A top-level menu item that toggles between starting and stopping a recording. When no recording is active, it triggers the Listening state. When a recording is in progress, it stops the recording and sends the audio for transcription.

    Error State: If no configured mic is found or the API returns an error, the tray icon turns red, the exception's .Message (truncated to 128 characters with ellipsis) is shown as the tooltip on hover, and the full exception is written to the log file. The error state auto-clears on the next successful transcription (icon returns to Idle, tooltip reverts to the latest snippet) or updates with the newest error if the next recording also fails. No retry logic — the server is local and transient failures are not expected.

    Transcription History: * Displays the last 15 entries (configurable).

        Entries are read-only text snippets.

        Action: Clicking an entry copies the full string to the clipboard.

    Clear History: Flushes the memory-resident history list. History is intentionally not persisted to disk — it resets on app restart.

    Open Log File: Opens the Serilog log file in the default text editor via Process.Start. Always visible in the context menu (not just during errors) for general troubleshooting.

    Exit: Disposes the NotifyIcon, cancels any in-flight recording or API call, and calls Application.Exit() to shut down cleanly.

    Status Tooltip: Hovering over the tray icon displays the most recent transcription snippet.

    AutoPaste: When enabled, the transcription result is copied to the clipboard and automatically pasted into the foreground application via SendInput. The clipboard buffer is preserved so the user can re-paste or correct elsewhere. If the foreground window changes during transcription, the paste may target the wrong app — this is acceptable; the result remains in the clipboard for manual pasting. If clipboard access fails (another process holds the lock), the error is logged and the operation is skipped — no retry.

3. Audio & Pipeline Specifications

Optimized for the RTX 5090 and Whisper's native inference engine.

    Format: 16 kHz, 16-bit PCM, Mono (WAV). 

    In-Memory Strategy: Audio is captured via NAudio directly into a MemoryStream. No disk writes are permitted for transient audio data. (quality configurable in settings)

    Mic Binding: The MicDevices array is a priority-ordered fallback list. The app binds to the first device whose name contains a matching substring (case-insensitive) and ignores all others. If no device from the list is found, the tray icon turns red and an error is logged. If the mic is disconnected mid-recording (NAudio throws), the partial audio is discarded and the app enters the standard error state.

    Single Instance: Enforced via a named Mutex. The .exe is file-locked by Windows while running, so it cannot be replaced. If a second instance is launched, it flashes the existing tray icon and exits.

    Predictive Warmup: Upon F22/Manual trigger, if the app has been idle for > 30 minutes, a warmup call is sent to the API to prime the GPU VRAM (empty/silent audio clip). The warmup must complete before transcription begins. The warmup result is not copied to the clipboard. If warmup fails, the app enters the standard error state (red icon, tooltip, log).

    F22/Hotkey Behavior: Pressing F22 always starts a fresh recording. If a recording is already in progress, it is discarded (not sent for transcription) and a new recording begins immediately. If the app is in the Processing state (API call in-flight), the pending request is cancelled via CancellationToken, its response is discarded, and a new recording starts. This acts as a sliding window — only the latest recording matters.

    VAD (Silence Detection): RMS-based monitoring. For each NAudio DataAvailable chunk, calculate RMS of the PCM samples. If RMS < SilenceThreshold, increment a silence duration counter; if RMS >= SilenceThreshold, reset the counter to zero. Recording stops automatically when the counter reaches SilenceTimeoutMs. The silence timer only begins after the first chunk that exceeds the threshold (i.e., the app waits indefinitely for the user to start speaking, then cuts off after sustained silence). Threshold and timeout values are tunable in config — initial values are best guesses pending real-world testing. VAD and F22 interact as follows: VAD can auto-stop a recording, but pressing F22 during an active recording discards it and restarts. (timeout configurable in settings)

    MaxRecordSeconds: Hard cap on recording duration. When hit, recording stops, audio is sent for transcription, and the icon state updates. The event is logged.

4. API Interface

The client uses a minimalist, typed HttpClient designed for Native AOT compatibility.

Endpoint: POST /v1/audio/transcriptions (FastAPI / OpenAI Schema)
C#

namespace DotWhisper.Core.Api;

// Provider-agnostic transcription contract. All implementations (Whisper, etc.) must satisfy this interface.
public interface ITranscriptionClient
{
    Task WarmupAsync(CancellationToken ct = default);

    // On error: tray icon turns red, error is logged. No retry logic.
    Task<string> TranscribeAsync(Stream audioStream, TranscriptionRequest request, CancellationToken ct = default);
}

// Current implementation: WhisperTranscriptionClient (HttpClient-based, targets OpenAI-compatible /v1/audio/transcriptions endpoint)

5. Sample Configuration (config.json)

This file resides in the application root and is bound via IOptions in the Core layer. Configuration is read at startup only — changes require an app restart to take effect.
JSON

{
  "Whisper": {
    "BaseUrl": "http://192.168.1.50:8000/v1",
    "Model": "whisper-1",
    "Language": "en",
    "Temperature": 0,
    "ColdStartThresholdMinutes": 30
  },
  "Audio": {
    "MicDevices": [ "Remote Audio", "Logitech" ],
    "SilenceThreshold": 0.02,
    "SilenceTimeoutMs": 1200,
    "MaxRecordSeconds": 300
  },
  "UI": {
    "HotKey": "F22",           // F22 chosen to avoid conflicts; mapped from a Logitech G-key
    "AutoPaste": true,
    "HistoryLimit": 15,
    "ShowGhostOverlay": true    // Future feature: floating tooltip near cursor showing transcription text
  },
  "Logging": {
    "MinimumLevel": "Information",
    "LogToFile": true
  }
}

6. Dependency Injection

All services are registered via Microsoft.Extensions.DependencyInjection so they can be mocked and tested with NSubstitute.

Core registrations:
    - ITranscriptionClient → WhisperTranscriptionClient (HttpClient-based, OpenAI-compatible endpoint)
    - IAudioCapture → AudioCapture (NAudio mic binding and recording)
    - ITranscriptionPipeline → TranscriptionPipeline (orchestrates capture → transcribe → clipboard)
    - IEnumerable<ITextProcessor> → ordered post-transcription pipeline steps
    - IOptions<WhisperSettings>, IOptions<AudioSettings>, IOptions<UiSettings> (bound from config.json)
    - ILogger (Serilog)

The UI project owns the composition root and builds the service provider at startup. The Core project depends only on abstractions (interfaces), never on concrete implementations.

7. Build & Security Standards

    Supply Chain: Central Package Management (CPM) with exact version pinning [x.y.z].

    Performance: Native AOT publishing is a nice-to-have (Self-contained, single EXE, minimal RAM footprint). If a required library is not AOT-compatible, evaluate alternatives or drop AOT.

    Dependencies: Prefer built-in .NET APIs over third-party packages. When a third-party package is necessary, use only well-known, established libraries to minimize supply chain risk.

    Logging: Serilog structured logging for telemetry on API latency and mic device matching.
	

8. Post-Transcription Pipeline

After transcription, the result is trimmed (leading/trailing whitespace removed). If the trimmed result is empty or whitespace-only, it is silently discarded — no clipboard write, no AutoPaste, no error state. Non-empty results pass through a configurable pipeline of ITextProcessor steps before being sent to the clipboard. This allows text cleanup, formatting, or transformation without coupling logic to the transcription client.

Pipeline steps are registered via DI and executed in order. Examples of future processors:
    - Hallucination filter (strip common Whisper artifacts like leading "Thank you.")
    - Find/replace rules for custom vocabulary
    - Punctuation or capitalization normalization

9. Future Features (Out of Scope for V1)

    Sound Feedback: Audible beep/click on recording start and stop via System.Media.SoundPlayer, so the user gets confirmation without looking at the tray icon.

    Startup with Windows: Context menu toggle to register/unregister the app in the Windows startup sequence (Run key or Startup folder shortcut).

    GhostOverlay: Floating tooltip near mouse cursor showing transcription text.

10. Tray Icon has the following states:

Idle: ⚪ (Dot)

Listening: 👂 (Ear)

Processing: sound waves (Pulsing)

Success/Error: Brief color shift (Green/Red) and hover-text update.