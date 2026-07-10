🎙️ DotWhisper

The ultra-low-latency, local-first voice dictation client for developers.

DotWhisper is a minimalist .NET 10 task tray application that bridges your physical microphone to a local, containerized Whisper AI instance. Designed for power users with high-end GPUs (like the RTX 5090), it provides a near-instant "Talk-to-Type" experience that respects your privacy and out-performs standard browser-based dictation.
✨ Key Features

    Zero-Disk Pipeline: Audio is captured at 16kHz and streamed directly from RAM to your Whisper container. No transient audio files are ever written to your SSD.

    Predictive Warmup: DotWhisper intelligently "primes" your GPU VRAM the moment you trigger a recording, masking the "cold start" latency of local AI models.

    F22 Global Trigger: Designed to be bound to a hardware macro (like a Logitech G-Key), allowing for system-wide dictation into Slack, IDEs, or browsers without switching focus.

    F23 Refiner Trigger: A second hotkey that runs the same recording through a local LLM before pasting — see 🧹 The Refiner Pipeline below.

    Smart Device Matching: Automatically prioritizes your high-quality mics (like a Logitech camera) over "junk" inputs (like VR headsets or RDP virtual audio).

    Contextual History: Right-click the tray icon to access a history of your last 15 transcriptions for quick copy-pasting.

🚀 Use Cases

    Developer Dictation: Quickly dictate complex technical Slack messages, Jira tickets, or PR descriptions without the inaccuracies of built-in OS speech engines.

    Privacy-Conscious Workflow: Keep your voice data off the cloud. All processing happens on your local hardware.

    VM/RDP Support: Specifically architected to work within a Windows VM environment via RDP audio redirection.

    AI Orchestration: Use DotWhisper as the first stage in a local AI pipeline — an LLM-based "Refiner" (F23) cleans up grammar and filler words before pasting, see 🧹 The Refiner Pipeline below.

🛠️ Technical Architecture

    Framework: .NET 10 (Targeting Native AOT for instant startup and <20MB RAM footprint).

    Audio Engine: NAudio (WASAPI) with integrated RMS-based Silence Detection (VAD).

    API: OpenAI-compatible FastAPI/Whisper container bridge.

    Security: Central Package Management (CPM) with deterministic dependency locking via packages.lock.json.

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

    F22 (unchanged): record → transcribe → paste raw text.

    F20: record → transcribe → refine via local LLM → paste refined text.

Same recording, silence-detection, and transcription pipeline for both — the only difference is one extra step at the end, chosen by which key you pressed. It is not implemented as an ITextProcessor (src/Core/Pipeline/ITextProcessor.cs), because that chain runs unconditionally on every transcription; refinement is opt-in per recording, so it's a distinct pipeline stage instead.

The model: llama3.2:1b, running locally in an Ollama container, talked to via Ollama's native /api/chat endpoint (stream: false) — a separate API bridge from the OpenAI-compatible one DotWhisper uses for Whisper. Model, endpoint, temperature, context window, and output token cap are all config-driven (RefinerSettings / the "Refiner" section of config.json), so swapping models or tuning any of this is a config change, not a code change.

Components (mirroring the existing Whisper client/settings pattern):

    Core/Settings/RefinerSettings.cs: BaseUrl (IP literal, not "localhost" — see note below), Model ("llama3.2:1b"), SystemPrompt, Temperature (default 0.1 — deterministic rewrite, not creative generation), NumCtx (default 2048 — keeps the KV-cache allocation small and fast rather than a much larger default some models ship with), NumPredict (default 150 — output truncation guard), TimeoutSeconds.

    Core/Api/IRefinerClient.cs + RefinerClient.cs: WarmupAsync() and RefineAsync(string rawText, CancellationToken ct), same shape as ITranscriptionClient. Posts model + stream:false + options.temperature/num_ctx/num_predict + messages (system prompt, then the raw transcription as the user message), reads message.content back out of the single non-streamed JSON response. Registered via AddHttpClient<IRefinerClient, RefinerClient>() with the same SocketsHttpHandler/ConnectCallback timing instrumentation wired up for the Whisper client (see Program.cs).

localhost vs. 127.0.0.1: RefinerSettings.BaseUrl uses the IPv4 literal 127.0.0.1, not the hostname "localhost". "localhost" resolves to both ::1 and 127.0.0.1 on Windows, and if the IPv6 candidate doesn't get answered (common with Docker port publishing), the custom ConnectCallback's raw socket connect stalls for ~15 seconds before giving up — even though curl against the same "localhost" URL returns instantly. This cost real debugging time; don't revert it.

Interface boundary: TrayApplicationContext depends only on IRefinerClient, never on RefinerClient directly, so the hotkey handling, state machine, warm-up-on-startup, and fallback-to-raw-text-on-failure logic are fully insulated from which model or container sits behind it.

Hotkey wiring: HotkeyManager's RegisterHotKey id only needs to be unique per window handle, not process-wide, and each HotkeyManager instance owns its own hidden NativeWindow — so a second instance (UiSettings.RefineHotKey, default "F23") works with zero changes to that class. TrayApplicationContext constructs two: the existing one for F22 → OnHotkeyPressed, and one for F23 → OnRefineHotkeyPressed, both routed through a shared HandleHotkeyPress(bool refine) that mirrors the existing start/force-stop/cancel-and-restart state machine and threads the flag into StartRecordingAsync(bool refine). After the transcription pipeline returns, if refine is set, RefineAsync(result, ct) runs before ClipboardHelper.SetText(...).

Failure handling: if the refiner call fails or times out, StartRecordingAsync falls back to pasting the raw transcription rather than nothing. A refiner timeout is distinguished from a genuine user cancellation (via the linked CancellationToken's own IsCancellationRequested) so cancelling a recording still cancels cleanly instead of silently "succeeding" with raw text.

Tray feedback: a distinct two-frame "refining" icon (magic wand + twinkling sparkle — tools/generate-icons.ps1's Draw-Wand/Draw-Star) pulses during the LLM call, mirroring the existing idle/listening/processing icon pattern. The completion beep fires after the refine call resolves (success or fallback), not immediately after transcription, so it always marks the true end of whichever pipeline you triggered. A separate, shorter/higher-pitched click plays the moment a recording starts listening on either hotkey.

Warm-up: RefinerClient.WarmupAsync() fires non-blocking on startup (errors logged and swallowed) so the Ollama container isn't cold on the first real F23 press. (WhisperTranscriptionClient.WarmupAsync() still isn't wired up anywhere — that gap predates the refiner work and remains open.)

Bottleneck diagnosis logging: RefineAsync logs headers-received vs. body-read time for its own HTTP round trip, plus Ollama's own self-reported breakdown (total/load/prompt-eval/eval durations, converted from nanoseconds) pulled straight out of its response — this tells you directly whether a slow call is cold model load, prompt processing, or generation, not just "it was slow." Gated behind `_log.IsEnabled(LogLevel.Information)` so the extra parsing doesn't run when logging is turned down; WhisperTranscriptionClient got the same headers/body split for consistency.

Ollama-specific note: OLLAMA_KEEP_ALIVE on the container controls how long the model stays resident in VRAM between requests (60m) — same lever as WHISPER__TTL on the Whisper container. Since DotWhisper is single-user/single-request by design, the Ollama container also sets OLLAMA_NUM_PARALLEL=1 and OLLAMA_MAX_LOADED_MODELS=1 to avoid reserving KV-cache slots for concurrency that will never happen.

📦 Project Structure
Plaintext

DotWhisper/
├── src/
│   ├── UI/            # WinForms Tray App & Native Input Injection
│   └── Core/          # Audio Engine & In-Memory API Pipeline
├── tests/
│   └── UnitTests/     # xUnit test suite for VAD & Device Matching
└── DotWhisper.sln

🚦 Getting Started

    Configure your Container: Point the config.json to your local Whisper API (e.g., http://192.168.1.50:8000/v1).

    Hardware Bind: Map a keyboard key or mouse button to F22 (or whatever you want it).

    Speak: Hold the key, talk, and watch the text appear at your cursor.

🛡️ Supply Chain & Security

DotWhisper is built with a minimalist mindset. We use exact version pinning for all NuGet dependencies to protect against supply chain attacks, ensuring the code you build is exactly the code you've audited.