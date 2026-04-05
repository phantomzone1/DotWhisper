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