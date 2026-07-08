🎙️ DotWhisper

The ultra-low-latency, local-first voice dictation client for developers.

DotWhisper is a minimalist .NET 10 task tray application that bridges your physical microphone to a local, containerized Whisper AI instance. Designed for power users with high-end GPUs (like the RTX 5090), it provides a near-instant "Talk-to-Type" experience that respects your privacy and out-performs standard browser-based dictation.
✨ Key Features

    Zero-Disk Pipeline: Audio is captured at 16kHz and streamed directly from RAM to your Whisper container. No transient audio files are ever written to your SSD.

    Predictive Warmup: DotWhisper intelligently "primes" your GPU VRAM the moment you trigger a recording, masking the "cold start" latency of local AI models.

    F22 Global Trigger: Designed to be bound to a hardware macro (like a Logitech G-Key), allowing for system-wide dictation into Slack, IDEs, or browsers without switching focus.

    Smart Device Matching: Automatically prioritizes your high-quality mics (like a Logitech camera) over "junk" inputs (like VR headsets or RDP virtual audio).

    Contextual History: Right-click the tray icon to access a history of your last 15 transcriptions for quick copy-pasting.

🚀 Use Cases

    Developer Dictation: Quickly dictate complex technical Slack messages, Jira tickets, or PR descriptions without the inaccuracies of built-in OS speech engines.

    Privacy-Conscious Workflow: Keep your voice data off the cloud. All processing happens on your local hardware.

    VM/RDP Support: Specifically architected to work within a Windows VM environment via RDP audio redirection.

    AI Orchestration: Use DotWhisper as the first stage in a local AI pipeline, with planned support for LLM-based "Refiners" to clean up grammar and filler words.

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

    If speech resumes shortly after (a normal mid-sentence pause), the held chunks are flushed into the file first, followed by the new speech — so natural pauses are preserved untouched.

    If instead the silence clock hits the timeout and recording stops, those held-but-unwritten chunks really were trailing dead air — they're dropped instead of being written, so Whisper never sees them.

The trade-off — trailing padding: dropping all held-back silence has a side effect. People's voice naturally quiets down right before they stop talking (a real phenomenon called declination), so the last word or two can dip under SilenceThreshold, get misclassified as silence, and be discarded along with the real dead air — clipping the end of your sentence. To avoid that, DotWhisper keeps a bounded slice of the held-back buffer — TrailingPaddingMs (default 500ms) — starting from the chunk closest to the last confirmed speech, and only discards whatever silence is left beyond that window.

This is a blunt, fixed-time assumption, not a re-detection of whether that audio is truly speech: if a trailing word takes longer to fade out than TrailingPaddingMs, the portion beyond that window still gets cut. Raising TrailingPaddingMs catches longer trail-offs at the cost of feeding a bit more real silence back to Whisper (some hallucination risk returns); lowering it cuts tighter but clips trailing words more often. If either symptom shows up — a missing last word, or a repeated last word — this is the setting to tune.

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