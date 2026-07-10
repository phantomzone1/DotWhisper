🎙️ DotWhisper

The ultra-low-latency, local-first voice dictation client for developers.

DotWhisper is a minimalist .NET 10 task tray application that bridges your physical microphone to a local, containerized Whisper AI instance, with an optional local LLM pass to clean up the raw dictation before it hits your clipboard. Designed for power users with high-end GPUs (like the RTX 5090), it provides a near-instant "Talk-to-Type" experience that stays fully local, respects your privacy, and out-performs standard browser-based dictation. It's meant to live in the background all day, bound to spare keys on a Logitech keyboard, as a fast supplement to AI assistants like Claude — dictate instead of typing, then let the assistant take it from there.
✨ Key Features

    Zero-Disk Pipeline: Audio is captured at 16kHz and streamed directly from RAM to your Whisper container. No transient audio files are ever written to your SSD.

    F22 Raw Trigger: Designed to be bound to a hardware macro (like a Logitech G-Key), allowing for system-wide dictation into Slack, IDEs, or browsers without switching focus. Record → transcribe → copy raw text to clipboard.

    F23 Refiner Trigger: A second hotkey that runs the same recording through a local LLM before copying — see 🧹 The Refiner Pipeline below.

    Startup Warm-Up: Fires a non-blocking warm-up call to the Ollama refiner container as soon as the tray app launches, so the first F23 press doesn't eat a cold-model-load penalty. (Whisper's transcription client has the same warm-up plumbing, but it isn't wired up to anything yet, so a Whisper container that's been idle still pays its own cold-start cost on the next request.)

    Smart Device Matching: Automatically prioritizes your high-quality mics (like a Logitech camera) over "junk" inputs (like VR headsets or RDP virtual audio), via a priority-ordered, case-insensitive substring match (Audio.MicDevices in config.json).

    Clipboard, Not Auto-Type: Every result — raw or refined — is copied to the clipboard for you to paste (Ctrl+V) wherever you need it. Nothing is auto-typed into the foreground window and no transcription history is persisted; the tray's right-click menu only has Start/Stop Recording, Open Log File, and Exit.

    Launches with Windows: Silently registers itself in the current user's registry Run key on every startup so it's always ready in the tray (no menu toggle yet — remove the DotWhisper value under HKCU\...\Run manually if you don't want that).

🚀 Use Cases

    Developer Dictation: Quickly dictate complex technical Slack messages, Jira tickets, or PR descriptions without the inaccuracies of built-in OS speech engines.

    Privacy-Conscious Workflow: Keep your voice data off the cloud. All processing happens on your local hardware.

    VM/RDP Support: Specifically architected to work within a Windows VM environment via RDP audio redirection.

    AI Orchestration: Use DotWhisper as the first stage in a local pipeline that feeds into other AI tools — an LLM-based "Refiner" (F23) cleans up grammar and filler words locally before you paste the result into Claude, Slack, or email — see 🧹 The Refiner Pipeline below.

🛠️ Technical Architecture

    Framework: .NET 10 (net10.0-windows) WinForms tray app, Windows-only. Native AOT is not currently enabled (WinForms AOT publishing isn't there yet) — this is a regular framework-dependent/self-contained build, not an ahead-of-time-compiled one.

    Audio Engine: NAudio (WaveInEvent / WASAPI) with integrated RMS-based Silence Detection (VAD) — see 🎚️ below.

    Transcription API: OpenAI-compatible /v1/audio/transcriptions bridge. Currently fedirz/faster-whisper-server (CUDA) running deepdml/faster-whisper-large-v3-turbo-ct2.

    Refiner API: Ollama's native /api/chat endpoint (stream: false). Currently qwen2.5:1.5b — see 🧹 below.

    Security: Central Package Management (CPM) with deterministic dependency locking via a packages.lock.json per project.

🎚️ How Audio Capture & Silence Trimming Works

Recording doesn't arrive as one finished file. NAudio delivers the microphone in small ~100ms chunks as you talk (see AudioCapture.RecordAsync in src/Core/Audio/AudioCapture.cs), and each chunk is classified and handled independently, in real time, as it comes in.

    Loudness per chunk (RMS): every chunk's raw 16-bit samples are normalized to -1.0..1.0 and combined into a single "Root Mean Square" number — a standard way to measure how much energy is in a chunk of audio. No frequency/FFT analysis, just "how loud was this ~100ms slice."

    Speech vs. silence: that RMS number is compared against SilenceThreshold (config.json, Audio section). Above the threshold counts as speech, below counts as silence. This is the "RMS-based Silence Detection (VAD)" mentioned under Technical Architecture above.

    A running silence clock: each chunk also represents a known slice of time, so a silenceDuration counter accumulates every time a "silence" chunk arrives and resets to zero the instant a "speech" chunk arrives. Once that counter reaches SilenceTimeoutMs, DotWhisper decides you've stopped talking and ends the recording automatically.

Why silence has to be stripped before sending to Whisper: Whisper (and faster-whisper) can hallucinate on trailing silence — instead of cleanly ending, the decoder tends to get stuck repeating the last word or phrase over and over. Every auto-stopped recording naturally has up to SilenceTimeoutMs of dead air baked into its tail (that's literally what triggered the stop), so that silence has to be removed from the audio actually sent to the API, or the repeat-loop hallucination reliably reappears.

How the trimming works — buffer, don't write, until you know: rather than writing every chunk to the output WAV as it arrives, "silence" chunks are held in memory (pendingSilence) instead of being committed immediately.

    If speech resumes shortly after (a normal mid-sentence pause), only a bounded slice of the held-back pause — the part closest to your last word — is flushed into the file, followed by the new speech. Short pauses are preserved in full; longer ones (e.g. trailing off mid-thought) get trimmed, for the same hallucination-avoidance reason as the final-stop case below.

    If instead the silence clock hits the timeout and recording stops, the same bounded flush applies: the trailing dead air is discarded, and only a small slice closest to your last word survives.

The trade-off — trailing padding: dropping all held-back silence has a side effect. People's voice naturally quiets down right before they stop talking (a real phenomenon called declination), so the last word or two can dip under SilenceThreshold, get misclassified as silence, and be discarded along with the real dead air — clipping the end of your sentence. To avoid that, DotWhisper keeps a bounded slice of the held-back buffer — TrailingPaddingMs (default 500ms) — starting from the chunk closest to the last confirmed speech, and only discards whatever silence is left beyond that window. This same cap applies every time a pause is flushed, whether speech resumes mid-recording or the recording ends — an uncapped mid-recording pause (e.g. a long "train of thought" gap) can trigger the same repeat-loop hallucination as an uncapped trailing one, so both cases are trimmed identically.

TrailingPaddingMs is deliberately independent of SilenceTimeoutMs, even though both are "silence" settings — they answer different questions. SilenceTimeoutMs is a patience setting: how long to wait before deciding you've stopped talking. TrailingPaddingMs is a safety-margin setting: how much of that silence is actually safe to send. There's no reason these should share a value, and setting them equal would undo one of the two fixes above: keep every sub-timeout pause in full and a long mid-recording pause reaches Whisper uncapped again, or discard the whole buffer once the timeout is hit — which is *every* silence-triggered stop, by definition — and trailing-word clipping becomes the default behavior again, not an edge case. Worth noting too: the primary defense against Whisper's silence-hallucination behavior is actually the server-side `vad_filter` (Silero VAD, on by default — see `WhisperSettings.VadFilter`) sent with every request. TrailingPaddingMs is a secondary, local safety margin sized around how long a trailing word takes to fade out, not a precisely-derived Whisper-safety threshold.

This is a blunt, fixed-time assumption, not a re-detection of whether that audio is truly speech: if a trailing word takes longer to fade out than TrailingPaddingMs, the portion beyond that window still gets cut. Raising TrailingPaddingMs catches longer trail-offs at the cost of feeding a bit more real silence back to Whisper (some hallucination risk returns); lowering it cuts tighter but clips trailing words more often. If either symptom shows up — a missing last word, or a repeated last word — this is the setting to tune.

🧹 The Refiner Pipeline

Status: implemented.

The problem: raw dictation is conversational — filler words, run-on sentences, no punctuation discipline — which is fine for chatting with an AI assistant but not for pasting straight into an email or a Slack message to a colleague. The fix is a second, optional pipeline stage: feed the raw transcription into a small local LLM with instructions to clean it up, before it hits the clipboard.

How it's triggered: a second global hotkey (F23, alongside the existing F22) selects the pipeline for that recording. (F21 was the original choice; switched to F23 because F21 is already claimed by default bindings in some browsers/editors.)

    F22 (unchanged): record → transcribe → copy raw text.

    F23: record → transcribe → refine via local LLM → copy refined text.

Same recording, silence-detection, and transcription pipeline for both — the only difference is one extra step at the end, chosen by which key you pressed. It is not implemented as an ITextProcessor (src/Core/Pipeline/ITextProcessor.cs), because that chain runs unconditionally on every transcription; refinement is opt-in per recording, so it's a distinct pipeline stage instead.

The model: qwen2.5:1.5b, running locally in an Ollama container, talked to via Ollama's native /api/chat endpoint (stream: false) — a separate API bridge from the OpenAI-compatible one DotWhisper uses for Whisper. Model, endpoint, temperature, context window, and output token cap are all config-driven (RefinerSettings / the "Refiner" section of config.json), so swapping models or tuning any of this is a config change, not a code change.

Components (mirroring the existing Whisper client/settings pattern):

    Core/Settings/RefinerSettings.cs: BaseUrl (IP literal, not "localhost" — see note below), Model, SystemPrompt, Temperature (default 0.1 — deterministic rewrite, not creative generation), NumCtx (default 2048 — keeps the KV-cache allocation small and fast rather than a much larger default some models ship with), NumPredict (default 150 — output truncation guard), TimeoutSeconds.

    Core/Api/IRefinerClient.cs + RefinerClient.cs: WarmupAsync() and RefineAsync(string rawText, CancellationToken ct), same shape as ITranscriptionClient. Posts model + stream:false + options.temperature/num_ctx/num_predict + messages (system prompt, then the raw transcription as the user message), reads message.content back out of the single non-streamed JSON response. Registered via AddHttpClient<IRefinerClient, RefinerClient>() with the same SocketsHttpHandler/ConnectCallback timing instrumentation wired up for the Whisper client (see Program.cs).

localhost vs. 127.0.0.1: RefinerSettings.BaseUrl uses the IPv4 literal 127.0.0.1, not the hostname "localhost". "localhost" resolves to both ::1 and 127.0.0.1 on Windows, and if the IPv6 candidate doesn't get answered (common with Docker port publishing), the custom ConnectCallback's raw socket connect stalls for ~15 seconds before giving up — even though curl against the same "localhost" URL returns instantly. This cost real debugging time; don't revert it.

Interface boundary: TrayApplicationContext depends only on IRefinerClient, never on RefinerClient directly, so the hotkey handling, state machine, warm-up-on-startup, and fallback-to-raw-text-on-failure logic are fully insulated from which model or container sits behind it.

Hotkey wiring: HotkeyManager's RegisterHotKey id only needs to be unique per window handle, not process-wide, and each HotkeyManager instance owns its own hidden NativeWindow — so a second instance (UiSettings.RefineHotKey, default "F23") works with zero changes to that class. TrayApplicationContext constructs two: the existing one for F22 → OnHotkeyPressed, and one for F23 → OnRefineHotkeyPressed, both routed through a shared HandleHotkeyPress(bool refine) that mirrors the existing start/force-stop/cancel-and-restart state machine and threads the flag into StartRecordingAsync(bool refine). After the transcription pipeline returns, if refine is set, RefineAsync(result, ct) runs before ClipboardHelper.SetText(...).

Failure handling: if the refiner call fails or times out, StartRecordingAsync falls back to copying the raw transcription rather than nothing. A refiner timeout is distinguished from a genuine user cancellation (via the linked CancellationToken's own IsCancellationRequested) so cancelling a recording still cancels cleanly instead of silently "succeeding" with raw text.

Tray feedback: a distinct two-frame "refining" icon (magic wand + twinkling sparkle — tools/generate-icons.ps1's Draw-Wand/Draw-Star) pulses during the LLM call, mirroring the existing idle/listening/processing icon pattern. The completion beep fires after the refine call resolves (success or fallback), not immediately after transcription, so it always marks the true end of whichever pipeline you triggered. A separate, shorter/higher-pitched click plays the moment a recording starts listening on either hotkey.

Warm-up: RefinerClient.WarmupAsync() fires non-blocking on startup (errors logged and swallowed) so the Ollama container isn't cold on the first real F23 press. (WhisperTranscriptionClient.WarmupAsync() still isn't wired up anywhere — that gap predates the refiner work and remains open.)

Bottleneck diagnosis logging: RefineAsync logs headers-received vs. body-read time for its own HTTP round trip, plus Ollama's own self-reported breakdown (total/load/prompt-eval/eval durations, converted from nanoseconds) pulled straight out of its response — this tells you directly whether a slow call is cold model load, prompt processing, or generation, not just "it was slow." Gated behind `_log.IsEnabled(LogLevel.Information)` so the extra parsing doesn't run when logging is turned down; WhisperTranscriptionClient got the same headers/body split for consistency.

Ollama-specific note: OLLAMA_KEEP_ALIVE on the container controls how long the model stays resident in VRAM between requests (1h) — same lever as WHISPER__TTL on the Whisper container. Since DotWhisper is single-user/single-request by design, the Ollama container also sets OLLAMA_NUM_PARALLEL=1 and OLLAMA_MAX_LOADED_MODELS=1 to avoid reserving KV-cache slots for concurrency that will never happen.

📦 Project Structure
Plaintext

DotWhisper/
├── .github/            # CI workflow folder (empty — no workflows defined yet)
├── artifacts/
│   └── icons/          # Tray icon frames (idle, listening, processing, refining, error) consumed by src/UI
├── container/
│   └── docker-compose.yml  # Whisper + Ollama (+ optional open-webui) containers
├── docs/
│   └── architecture.md     # Original design spec (V5.0)
├── src/
│   ├── UI/             # WinForms Tray App, hotkeys, clipboard, tray icon/menu
│   └── Core/            # Audio Engine (VAD), Whisper/Refiner API clients, transcription pipeline
├── tests/
│   └── UnitTests/       # xUnit project scaffold — no tests committed yet
├── tools/
│   └── generate-icons.ps1  # Regenerates artifacts/icons/*.ico
├── config.json          # Runtime configuration (copied next to the built exe)
└── DotWhisper.slnx

🚦 Getting Started

    Bring up the containers: docker compose -f container/docker-compose.yml up -d starts Whisper and Ollama (and, if you use it, open-webui) with GPU passthrough. Point config.json's Whisper.BaseUrl / Refiner.BaseUrl at those containers if they're not on localhost.

    Hardware Bind: Map keyboard keys or mouse buttons (a Logitech G-Key macro, in this setup) to F22 for raw dictation and F23 for dictation-plus-refine — or change UI.HotKey / UI.RefineHotKey in config.json to whatever's convenient.

    Speak: Hold the key, talk, and pause. DotWhisper auto-stops on silence, transcribes, (optionally refines,) and copies the result to your clipboard — paste it with Ctrl+V wherever you need it.

🛡️ Supply Chain & Security

DotWhisper is built with a minimalist mindset. We use exact version pinning for all NuGet dependencies to protect against supply chain attacks, ensuring the code you build is exactly the code you've audited.
