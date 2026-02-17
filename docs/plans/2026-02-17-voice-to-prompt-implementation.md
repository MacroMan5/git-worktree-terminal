# Voice-to-Prompt Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add Push-to-Talk voice-to-prompt capability to tmuxlike — speak messy instructions, get clean English prompts injected into the active terminal pane.

**Architecture:** Two-process sidecar model. Python backend (`voice-bridge`) handles audio capture, Whisper transcription, and LLM refinement. C# WPF frontend handles PTT key detection, status bar, confirmation overlay, and text injection via WebSocket (JSON) on `ws://localhost:5005`.

**Tech Stack:** C# .NET 8 WPF, Python 3.10+, websockets, faster-whisper, sounddevice, Ollama (Qwen2.5-Coder-7B), System.Net.WebSockets.ClientWebSocket

**Design Doc:** `docs/plans/2026-02-17-voice-to-prompt-design.md`

**Repo:** `https://github.com/MacroMan5/git-worktree-terminal.git`

**Base Branch:** `feature/voice-to-prompt` (worktree at `C:/Users/therouxe/source/repos/feature-voice-to-prompt`)

---

## Git Workflow Strategy

Each phase gets its own feature branch created from `feature/voice-to-prompt`. After each phase is complete and tested, merge it back into `feature/voice-to-prompt`. When all phases are done, `feature/voice-to-prompt` gets a PR into `main`.

```
main
 └── feature/voice-to-prompt (base worktree)
      ├── feature/vtp-phase1-python-backend
      ├── feature/vtp-phase2-csharp-service
      ├── feature/vtp-phase3-overlay
      └── feature/vtp-phase4-polish
```

Each phase branch is created, worked on, merged back, then the worktree is removed.

---

## Phase 1: Python Backend (`voice-bridge/`)

**Branch:** `feature/vtp-phase1-python-backend`
**Deliverable:** Standalone Python WebSocket server that captures audio, transcribes with Whisper, refines with Ollama, and returns clean prompts over WebSocket.

---

### Task 1.0: Set up Phase 1 branch

**Step 1: Create the phase branch**

```bash
cd C:/Users/therouxe/source/repos/feature-voice-to-prompt
git checkout -b feature/vtp-phase1-python-backend
```

No worktree needed — work directly in the existing worktree since this phase only adds new files in `voice-bridge/` with no C# changes.

---

### Task 1.1: Create project scaffold

**Files:**
- Create: `voice-bridge/requirements.txt`
- Create: `voice-bridge/voice_bridge.py`

**Step 1: Create `voice-bridge/requirements.txt`**

```
websockets>=12.0
faster-whisper>=1.0
sounddevice>=0.4
numpy>=1.24
requests>=2.31
```

**Step 2: Create `voice-bridge/voice_bridge.py` with imports and constants**

```python
"""Voice-to-Prompt bridge server.

WebSocket server that captures audio, transcribes with Whisper,
refines with Ollama, and returns clean prompts.
"""

import asyncio
import json
import logging
import threading

import numpy as np
import requests
import sounddevice as sd
import websockets
from faster_whisper import WhisperModel

HOST = "localhost"
PORT = 5005
SAMPLE_RATE = 16000
OLLAMA_URL = "http://localhost:11434/api/generate"
OLLAMA_MODEL = "qwen2.5-coder:7b"
WHISPER_MODEL = "base.en"
MAX_RECORD_SECONDS = 60
REFINE_TIMEOUT = 10

SYSTEM_PROMPT = """You are a voice-to-text cleanup assistant for a software developer.

Your ONLY job is to take messy spoken transcripts and return a clean,
well-structured version in English. Rules:
- ALWAYS output in English, regardless of input language
- If the input is in another language (e.g., French), translate it to English
- Fix grammar, remove filler words (um, uh, like, you know, euh, genre, donc)
- Organize rambling into clear sentences or bullet points
- Expand common abbreviations (auth → authentication, repo → repository,
  env → environment, config → configuration)
- Preserve the developer's original meaning exactly
- Use imperative tone for instructions
- NEVER invent commands, file paths, or technical details not in the input
- NEVER add explanations or commentary
- Return ONLY the cleaned text, nothing else"""

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
log = logging.getLogger("voice-bridge")
```

**Step 3: Commit**

```bash
git add voice-bridge/requirements.txt voice-bridge/voice_bridge.py
git commit -m "feat(voice-bridge): add project scaffold with dependencies and constants"
```

---

### Task 1.2: Implement AudioRecorder class

**Files:**
- Modify: `voice-bridge/voice_bridge.py`

**Step 1: Add AudioRecorder class after the constants**

```python
class AudioRecorder:
    """Records audio from the default microphone into a numpy buffer."""

    def __init__(self):
        self._buffer: list[np.ndarray] = []
        self._stream: sd.InputStream | None = None
        self._recording = False

    def start(self) -> None:
        """Open the mic stream and begin recording."""
        if self._recording:
            return
        self._buffer.clear()
        self._recording = True
        self._stream = sd.InputStream(
            samplerate=SAMPLE_RATE,
            channels=1,
            dtype="float32",
            callback=self._audio_callback,
        )
        self._stream.start()
        log.info("Recording started")

    def stop(self) -> np.ndarray:
        """Stop recording and return the audio buffer as a numpy array."""
        self._recording = False
        if self._stream is not None:
            self._stream.stop()
            self._stream.close()
            self._stream = None
        if not self._buffer:
            return np.array([], dtype="float32")
        audio = np.concatenate(self._buffer)
        self._buffer.clear()
        log.info("Recording stopped — %d samples (%.1fs)", len(audio), len(audio) / SAMPLE_RATE)
        return audio

    def _audio_callback(self, indata: np.ndarray, frames: int, time_info, status) -> None:
        if status:
            log.warning("Audio status: %s", status)
        if self._recording:
            self._buffer.append(indata[:, 0].copy())
```

**Step 2: Commit**

```bash
git add voice-bridge/voice_bridge.py
git commit -m "feat(voice-bridge): implement AudioRecorder class with sounddevice"
```

---

### Task 1.3: Implement Transcriber class

**Files:**
- Modify: `voice-bridge/voice_bridge.py`

**Step 1: Add Transcriber class after AudioRecorder**

```python
class Transcriber:
    """Transcribes audio using faster-whisper."""

    def __init__(self):
        log.info("Loading Whisper model '%s' (first run may download)...", WHISPER_MODEL)
        self._model = WhisperModel(WHISPER_MODEL, compute_type="int8")
        log.info("Whisper model loaded")

    def transcribe(self, audio: np.ndarray) -> str:
        """Transcribe an audio buffer to text. Returns empty string on failure."""
        if len(audio) == 0:
            return ""
        segments, _info = self._model.transcribe(audio, beam_size=5)
        text = " ".join(seg.text.strip() for seg in segments).strip()
        log.info("Transcript: %s", text)
        return text
```

**Step 2: Commit**

```bash
git add voice-bridge/voice_bridge.py
git commit -m "feat(voice-bridge): implement Transcriber class with faster-whisper"
```

---

### Task 1.4: Implement Refiner class

**Files:**
- Modify: `voice-bridge/voice_bridge.py`

**Step 1: Add Refiner class after Transcriber**

```python
class Refiner:
    """Refines raw transcripts using Ollama (local LLM)."""

    def refine(self, transcript: str) -> str:
        """Send transcript to Ollama for cleanup. Returns original on failure."""
        if not transcript:
            return ""
        try:
            resp = requests.post(
                OLLAMA_URL,
                json={
                    "model": OLLAMA_MODEL,
                    "system": SYSTEM_PROMPT,
                    "prompt": transcript,
                    "stream": False,
                },
                timeout=REFINE_TIMEOUT,
            )
            resp.raise_for_status()
            result = resp.json().get("response", transcript).strip()
            log.info("Refined: %s", result)
            return result
        except requests.Timeout:
            log.error("Ollama timeout after %ds", REFINE_TIMEOUT)
            raise
        except requests.ConnectionError:
            log.error("Cannot connect to Ollama at %s", OLLAMA_URL)
            raise
        except Exception as e:
            log.error("Refinement failed: %s", e)
            raise
```

**Step 2: Commit**

```bash
git add voice-bridge/voice_bridge.py
git commit -m "feat(voice-bridge): implement Refiner class with Ollama API"
```

---

### Task 1.5: Implement WebSocket server and main loop

**Files:**
- Modify: `voice-bridge/voice_bridge.py`

**Step 1: Add the server handler and main entry point after Refiner**

```python
class VoiceBridge:
    """WebSocket server orchestrating record → transcribe → refine pipeline."""

    def __init__(self):
        self._recorder = AudioRecorder()
        self._transcriber = Transcriber()
        self._refiner = Refiner()
        self._recording = False
        self._cancel = False

    async def handler(self, websocket):
        """Handle a single WebSocket client connection."""
        log.info("Client connected: %s", websocket.remote_address)
        try:
            async for raw in websocket:
                try:
                    msg = json.loads(raw)
                except json.JSONDecodeError:
                    log.warning("Invalid JSON: %s", raw)
                    continue

                action = msg.get("action")
                log.info("Received: %s", action)

                if action == "PTT_DOWN":
                    await self._handle_ptt_down(websocket)
                elif action == "PTT_UP":
                    await self._handle_ptt_up(websocket)
                elif action == "CANCEL":
                    await self._handle_cancel(websocket)
                else:
                    log.warning("Unknown action: %s", action)
        except websockets.ConnectionClosed:
            log.info("Client disconnected")
        finally:
            # Stop recording if client disconnects mid-recording
            if self._recording:
                self._recorder.stop()
                self._recording = False

    async def _handle_ptt_down(self, websocket):
        """Start recording audio."""
        self._cancel = False
        try:
            self._recorder.start()
            self._recording = True
            await self._send(websocket, {"event": "LISTENING"})
        except Exception as e:
            await self._send(websocket, {"event": "ERROR", "message": f"Mic error: {e}"})

    async def _handle_ptt_up(self, websocket):
        """Stop recording, transcribe, refine, and send result."""
        if not self._recording:
            return
        self._recording = False
        audio = self._recorder.stop()

        if self._cancel:
            return

        await self._send(websocket, {"event": "PROCESSING"})

        # Run transcription and refinement in a thread to avoid blocking the event loop
        try:
            loop = asyncio.get_running_loop()
            transcript = await loop.run_in_executor(None, self._transcriber.transcribe, audio)

            if self._cancel:
                return
            if not transcript:
                await self._send(websocket, {"event": "ERROR", "message": "No speech detected"})
                return

            refined = await loop.run_in_executor(None, self._refiner.refine, transcript)

            if self._cancel:
                return

            await self._send(websocket, {"event": "FINAL_PROMPT", "text": refined})
        except requests.Timeout:
            await self._send(websocket, {"event": "ERROR", "message": "LLM refinement timed out"})
        except requests.ConnectionError:
            await self._send(websocket, {"event": "ERROR", "message": "Ollama not running — start with: ollama serve"})
        except Exception as e:
            await self._send(websocket, {"event": "ERROR", "message": str(e)})

    async def _handle_cancel(self, websocket):
        """Cancel any in-progress recording or processing."""
        self._cancel = True
        if self._recording:
            self._recorder.stop()
            self._recording = False
        log.info("Operation cancelled")

    async def _send(self, websocket, data: dict):
        """Send a JSON event to the client."""
        await websocket.send(json.dumps(data))
        log.info("Sent: %s", data.get("event"))


async def main():
    bridge = VoiceBridge()
    log.info("Starting Voice Bridge on ws://%s:%d", HOST, PORT)
    async with websockets.serve(bridge.handler, HOST, PORT):
        await asyncio.Future()  # run forever


if __name__ == "__main__":
    asyncio.run(main())
```

**Step 2: Commit**

```bash
git add voice-bridge/voice_bridge.py
git commit -m "feat(voice-bridge): implement WebSocket server and main event loop"
```

---

### Task 1.6: Manual integration test

**Step 1: Install dependencies**

```bash
cd C:/Users/therouxe/source/repos/feature-voice-to-prompt/voice-bridge
pip install -r requirements.txt
```

**Step 2: Ensure Ollama is running with the model**

```bash
ollama list
# Should show qwen2.5-coder:7b. If not:
# ollama pull qwen2.5-coder:7b
```

**Step 3: Start the server**

```bash
python voice_bridge.py
# Expected: "Starting Voice Bridge on ws://localhost:5005"
```

**Step 4: Test with a WebSocket client (new terminal)**

Install websocat if needed: `winget install websocat` or use Python:

```bash
python -c "
import asyncio, websockets, json
async def test():
    async with websockets.connect('ws://localhost:5005') as ws:
        await ws.send(json.dumps({'action': 'PTT_DOWN'}))
        print(await ws.recv())  # should be LISTENING
        import time; time.sleep(3)  # speak for 3 seconds
        await ws.send(json.dumps({'action': 'PTT_UP'}))
        print(await ws.recv())  # should be PROCESSING
        print(await ws.recv())  # should be FINAL_PROMPT
asyncio.run(test())
"
```

**Expected output:**

```
{"event": "LISTENING"}
{"event": "PROCESSING"}
{"event": "FINAL_PROMPT", "text": "...cleaned prompt..."}
```

**Step 5: Test CANCEL**

```bash
python -c "
import asyncio, websockets, json
async def test():
    async with websockets.connect('ws://localhost:5005') as ws:
        await ws.send(json.dumps({'action': 'PTT_DOWN'}))
        print(await ws.recv())  # LISTENING
        await ws.send(json.dumps({'action': 'CANCEL'}))
        import time; time.sleep(1)
        await ws.send(json.dumps({'action': 'PTT_UP'}))
        # Should not get FINAL_PROMPT
        print('Cancel test passed')
asyncio.run(test())
"
```

**Step 6: Test error — Ollama offline**

Stop Ollama, then repeat Step 4. Should receive:
```
{"event": "ERROR", "message": "Ollama not running — start with: ollama serve"}
```

---

### Task 1.7: Merge Phase 1

**Step 1: Merge back to feature/voice-to-prompt**

```bash
cd C:/Users/therouxe/source/repos/feature-voice-to-prompt
git checkout feature/voice-to-prompt
git merge feature/vtp-phase1-python-backend --no-ff -m "Merge Phase 1: Python voice-bridge backend"
git branch -d feature/vtp-phase1-python-backend
```

---

## Phase 2: C# Service Layer

**Branch:** `feature/vtp-phase2-csharp-service`
**Deliverable:** `VoiceService.cs` with process spawning, WebSocket client, PTT state machine, and status bar integration. Pressing `Ctrl+Shift+V` toggles recording and updates the status bar. Received prompts are logged to Debug output (no overlay yet).

---

### Task 2.0: Set up Phase 2 branch

**Step 1: Create the phase branch**

```bash
cd C:/Users/therouxe/source/repos/feature-voice-to-prompt
git checkout -b feature/vtp-phase2-csharp-service
```

---

### Task 2.1: Create VoiceState enum and VoiceService skeleton

**Files:**
- Create: `tmuxlike/Services/VoiceService.cs`

**Step 1: Create `VoiceService.cs` with the enum and class skeleton**

```csharp
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace tmuxlike.Services;

public enum VoiceState
{
    Disconnected,
    Idle,
    Recording,
    Processing
}

public class VoiceService : IDisposable
{
    private const string Host = "localhost";
    private const int Port = 5005;
    private const int ReconnectDelayMs = 5000;
    private const int ConnectTimeoutMs = 3000;

    private readonly Dispatcher _dispatcher;
    private ClientWebSocket? _ws;
    private Process? _bridgeProcess;
    private CancellationTokenSource? _cts;
    private VoiceState _state = VoiceState.Disconnected;

    public event Action<VoiceState>? StateChanged;
    public event Action<string>? PromptReady;
    public event Action<string>? ErrorOccurred;

    public VoiceState State => _state;

    public VoiceService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Dispose()
    {
        StopBridge();
        _ws?.Dispose();
        _cts?.Dispose();
    }
}
```

**Step 2: Commit**

```bash
git add tmuxlike/Services/VoiceService.cs
git commit -m "feat(voice): add VoiceState enum and VoiceService skeleton"
```

---

### Task 2.2: Implement Python process management

**Files:**
- Modify: `tmuxlike/Services/VoiceService.cs`

**Step 1: Add StartBridge and StopBridge methods to VoiceService**

```csharp
    public void StartBridge()
    {
        var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "voice-bridge", "voice_bridge.py");
        if (!File.Exists(scriptPath))
        {
            scriptPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "voice-bridge", "voice_bridge.py"));
        }

        try
        {
            _bridgeProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            });

            if (_bridgeProcess != null)
            {
                _bridgeProcess.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.WriteLine($"[voice-bridge] {e.Data}");
                };
                _bridgeProcess.BeginErrorReadLine();
            }

            _cts = new CancellationTokenSource();
            _ = ConnectLoop(_cts.Token);
        }
        catch (Exception ex)
        {
            SetState(VoiceState.Disconnected);
            _dispatcher.Invoke(() => ErrorOccurred?.Invoke($"Python not found: {ex.Message}"));
        }
    }

    public void StopBridge()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_ws is { State: WebSocketState.Open })
        {
            try
            {
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch { }
        }
        _ws?.Dispose();
        _ws = null;

        if (_bridgeProcess is { HasExited: false })
        {
            try { _bridgeProcess.Kill(entireProcessTree: true); } catch { }
        }
        _bridgeProcess?.Dispose();
        _bridgeProcess = null;

        SetState(VoiceState.Disconnected);
    }
```

**Step 2: Add the SetState helper**

```csharp
    private void SetState(VoiceState state)
    {
        if (_state == state) return;
        _state = state;
        _dispatcher.Invoke(() => StateChanged?.Invoke(state));
    }
```

**Step 3: Commit**

```bash
git add tmuxlike/Services/VoiceService.cs
git commit -m "feat(voice): implement Python sidecar process management"
```

---

### Task 2.3: Implement WebSocket connect loop and receive loop

**Files:**
- Modify: `tmuxlike/Services/VoiceService.cs`

**Step 1: Add ConnectLoop and ReceiveLoop methods**

```csharp
    private async Task ConnectLoop(CancellationToken ct)
    {
        // Give the Python process time to start its WebSocket server
        await Task.Delay(2000, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(ConnectTimeoutMs);

                await _ws.ConnectAsync(new Uri($"ws://{Host}:{Port}"), connectCts.Token);
                SetState(VoiceState.Idle);
                await ReceiveLoop(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                SetState(VoiceState.Disconnected);
            }

            if (!ct.IsCancellationRequested)
                await Task.Delay(ReconnectDelayMs, ct).ConfigureAwait(false);
        }
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (_ws is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _ws.ReceiveAsync(buffer, ct);
            }
            catch
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                break;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            HandleEvent(json);
        }

        SetState(VoiceState.Disconnected);
    }
```

**Step 2: Add HandleEvent method**

```csharp
    private void HandleEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var evt = root.GetProperty("event").GetString();

            switch (evt)
            {
                case "LISTENING":
                    SetState(VoiceState.Recording);
                    break;
                case "PROCESSING":
                    SetState(VoiceState.Processing);
                    break;
                case "FINAL_PROMPT":
                    var text = root.GetProperty("text").GetString() ?? "";
                    SetState(VoiceState.Idle);
                    _dispatcher.Invoke(() => PromptReady?.Invoke(text));
                    break;
                case "ERROR":
                    var msg = root.GetProperty("message").GetString() ?? "Unknown error";
                    SetState(VoiceState.Idle);
                    _dispatcher.Invoke(() => ErrorOccurred?.Invoke(msg));
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoiceService] Bad event: {ex.Message}");
        }
    }
```

**Step 3: Commit**

```bash
git add tmuxlike/Services/VoiceService.cs
git commit -m "feat(voice): implement WebSocket connect/receive loop with auto-reconnect"
```

---

### Task 2.4: Implement PTT toggle

**Files:**
- Modify: `tmuxlike/Services/VoiceService.cs`

**Step 1: Add Toggle and SendAction methods**

```csharp
    public void Toggle()
    {
        switch (_state)
        {
            case VoiceState.Idle:
                _ = SendAction("PTT_DOWN");
                break;
            case VoiceState.Recording:
                _ = SendAction("PTT_UP");
                break;
            case VoiceState.Processing:
                _ = SendAction("CANCEL");
                break;
            // Disconnected — do nothing
        }
    }

    private async Task SendAction(string action)
    {
        if (_ws is not { State: WebSocketState.Open }) return;
        var json = JsonSerializer.Serialize(new { action });
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VoiceService] Send failed: {ex.Message}");
        }
    }
```

**Step 2: Commit**

```bash
git add tmuxlike/Services/VoiceService.cs
git commit -m "feat(voice): implement PTT toggle state machine"
```

---

### Task 2.5: Wire VoiceService into MainWindow — keybinding and status bar

**Files:**
- Modify: `tmuxlike/MainWindow.xaml`
- Modify: `tmuxlike/MainWindow.xaml.cs`

**Step 1: Add VoiceToggleCommand and keybinding to `MainWindow.xaml`**

In `MainWindow.xaml`, add the keybinding inside `<Window.InputBindings>`:

```xml
<KeyBinding Key="V" Modifiers="Ctrl+Shift" Command="{x:Static local:MainWindow.VoiceToggleCommand}" />
```

Update the status bar `<Border>` (the `DockPanel.Dock="Bottom"` element) to include a right-aligned voice state indicator:

Replace the existing status bar `Border` with:

```xml
<Border DockPanel.Dock="Bottom" Background="#2d2d2d" Padding="12,6" BorderBrush="#3c3c3c" BorderThickness="0,1,0,0">
    <DockPanel>
        <TextBlock DockPanel.Dock="Right" x:Name="VoiceStatusText"
                   Foreground="#888" FontSize="11" Margin="12,0"
                   HorizontalAlignment="Right" Text="Voice Offline" />
        <TextBlock Foreground="#aaa" FontSize="11"
                   Text="Ctrl+N: New | F5: Refresh | Del: Remove | Ctrl+E: Files | Ctrl+O: VS Code | Ctrl+T: Split | Ctrl+W: Close | Ctrl+Tab: Panes | Alt+&#x2191;&#x2193;: Worktrees" />
    </DockPanel>
</Border>
```

**Step 2: Add command, field, and wiring to `MainWindow.xaml.cs`**

Add the command declaration alongside the existing ones:

```csharp
public static readonly RoutedCommand VoiceToggleCommand = new();
```

Add the field alongside existing fields:

```csharp
private VoiceService? _voiceService;
```

In the `MainWindow()` constructor, after the existing `CommandBindings.Add(...)` lines, add:

```csharp
CommandBindings.Add(new CommandBinding(VoiceToggleCommand, (_, _) => _voiceService?.Toggle()));
```

After `ContentRendered += MainWindow_ContentRendered;`, add:

```csharp
_voiceService = new VoiceService(Dispatcher);
_voiceService.StateChanged += OnVoiceStateChanged;
_voiceService.PromptReady += OnVoicePromptReady;
_voiceService.ErrorOccurred += OnVoiceError;
_voiceService.StartBridge();
```

Add the event handler methods:

```csharp
// ── Voice service ───────────────────────────────────────────

private void OnVoiceStateChanged(VoiceState state)
{
    VoiceStatusText.Text = state switch
    {
        VoiceState.Disconnected => "\u26a0\ufe0f Voice Offline",
        VoiceState.Idle => "\U0001f3a4 Voice Ready",
        VoiceState.Recording => "\U0001f534 Recording... (Ctrl+Shift+V to stop)",
        VoiceState.Processing => "\u23f3 Refining prompt...",
        _ => ""
    };

    VoiceStatusText.Foreground = state switch
    {
        VoiceState.Recording => new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xff, 0x44, 0x44)),
        VoiceState.Processing => new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xff, 0xcc, 0x00)),
        _ => new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88))
    };
}

private void OnVoicePromptReady(string text)
{
    // Phase 2: log to debug output. Phase 3 will show the overlay.
    Debug.WriteLine($"[Voice] Prompt ready: {text}");
}

private void OnVoiceError(string message)
{
    VoiceStatusText.Text = $"\u274c {message}";
    VoiceStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
        System.Windows.Media.Color.FromRgb(0xff, 0x44, 0x44));

    // Auto-clear after 5 seconds
    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
    timer.Tick += (_, _) =>
    {
        timer.Stop();
        if (_voiceService != null)
            OnVoiceStateChanged(_voiceService.State);
    };
    timer.Start();
}
```

Update `Window_Closing` to dispose the voice service:

In the existing `Window_Closing` method, add at the beginning:

```csharp
_voiceService?.Dispose();
```

**Step 3: Commit**

```bash
git add tmuxlike/MainWindow.xaml tmuxlike/MainWindow.xaml.cs tmuxlike/Services/VoiceService.cs
git commit -m "feat(voice): wire VoiceService into MainWindow with keybinding and status bar"
```

---

### Task 2.6: Build and smoke test

**Step 1: Build the project**

```bash
cd C:/Users/therouxe/source/repos/feature-voice-to-prompt
dotnet build tmuxlike/tmuxlike.csproj
```

Expected: Build succeeded with 0 errors.

**Step 2: Run and verify**

1. Start the app: `dotnet run --project tmuxlike/tmuxlike.csproj`
2. Check status bar — should show "Voice Offline" initially, then "Voice Ready" after ~2s if `voice_bridge.py` starts
3. Press `Ctrl+Shift+V` — status bar should change to "Recording..."
4. Press `Ctrl+Shift+V` again — status bar should change to "Refining prompt..."
5. After processing, check Visual Studio Debug Output for `[Voice] Prompt ready: ...`

**Step 3: Fix any build or runtime issues, then commit fixes if needed**

---

### Task 2.7: Merge Phase 2

**Step 1: Merge back to feature/voice-to-prompt**

```bash
cd C:/Users/therouxe/source/repos/feature-voice-to-prompt
git checkout feature/voice-to-prompt
git merge feature/vtp-phase2-csharp-service --no-ff -m "Merge Phase 2: VoiceService with WebSocket client and PTT state machine"
git branch -d feature/vtp-phase2-csharp-service
```

---

## Phase 3: Confirmation Overlay

**Branch:** `feature/vtp-phase3-overlay`
**Deliverable:** Full end-to-end flow — speak, see refined prompt in overlay, edit if needed, Enter to inject into terminal, Esc to discard.

---

### Task 3.0: Set up Phase 3 branch

**Step 1: Create the phase branch**

```bash
cd C:/Users/therouxe/source/repos/feature-voice-to-prompt
git checkout -b feature/vtp-phase3-overlay
```

---

### Task 3.1: Create PromptOverlay UserControl

**Files:**
- Create: `tmuxlike/Controls/PromptOverlay.xaml`
- Create: `tmuxlike/Controls/PromptOverlay.xaml.cs`

**Step 1: Create the `tmuxlike/Controls/` directory if it doesn't exist**

```bash
mkdir -p C:/Users/therouxe/source/repos/feature-voice-to-prompt/tmuxlike/Controls
```

**Step 2: Create `tmuxlike/Controls/PromptOverlay.xaml`**

```xml
<UserControl x:Class="tmuxlike.Controls.PromptOverlay"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Visibility="Collapsed"
             KeyDown="Overlay_KeyDown">
    <Border Background="#2d2d2d" BorderBrush="#007acc" BorderThickness="0,2,0,0"
            Padding="16,12" MaxHeight="200">
        <DockPanel>
            <DockPanel DockPanel.Dock="Bottom" Margin="0,8,0,0">
                <TextBlock Foreground="#888" FontSize="11"
                           Text="Enter: Inject into terminal  |  Esc: Discard"
                           HorizontalAlignment="Center" />
            </DockPanel>
            <TextBlock DockPanel.Dock="Top" Text="Refined Prompt:" Foreground="#aaa"
                       FontSize="11" FontWeight="SemiBold" Margin="0,0,0,6" />
            <TextBox x:Name="PromptTextBox"
                     Background="#1e1e1e" Foreground="#e1e1e1"
                     BorderBrush="#3c3c3c" BorderThickness="1"
                     FontFamily="Cascadia Code,Consolas,Courier New"
                     FontSize="13" Padding="8,6"
                     AcceptsReturn="True" TextWrapping="Wrap"
                     VerticalScrollBarVisibility="Auto" />
        </DockPanel>
    </Border>
</UserControl>
```

**Step 3: Create `tmuxlike/Controls/PromptOverlay.xaml.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace tmuxlike.Controls;

public partial class PromptOverlay : UserControl
{
    public event Action<string>? PromptAccepted;
    public event Action? PromptDiscarded;

    public PromptOverlay()
    {
        InitializeComponent();
    }

    public void Show(string prompt)
    {
        PromptTextBox.Text = prompt;
        Visibility = Visibility.Visible;
        PromptTextBox.Focus();
        PromptTextBox.SelectAll();
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        PromptTextBox.Text = "";
    }

    public bool IsOpen => Visibility == Visibility.Visible;

    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            PromptDiscarded?.Invoke();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            var text = PromptTextBox.Text.Trim();
            Hide();
            if (!string.IsNullOrEmpty(text))
                PromptAccepted?.Invoke(text);
            e.Handled = true;
        }
        // Shift+Enter allows newlines in the textbox (default behavior)
    }
}
```

**Step 4: Commit**

```bash
git add tmuxlike/Controls/PromptOverlay.xaml tmuxlike/Controls/PromptOverlay.xaml.cs
git commit -m "feat(voice): create PromptOverlay UserControl with accept/discard"
```

---

### Task 3.2: Integrate PromptOverlay into MainWindow

**Files:**
- Modify: `tmuxlike/MainWindow.xaml`
- Modify: `tmuxlike/MainWindow.xaml.cs`

**Step 1: Add the overlay to `MainWindow.xaml`**

Add the namespace at the top of the `<Window>` tag:

```xml
xmlns:controls="clr-namespace:tmuxlike.Controls"
```

Inside the main `<DockPanel>`, add the overlay **between** the status bar `<Border>` and the toolbar `<Border>` (docked to Bottom, so it appears above the status bar):

```xml
<!-- Voice prompt overlay (above status bar) -->
<controls:PromptOverlay x:Name="VoiceOverlay" DockPanel.Dock="Bottom" />
```

**Step 2: Update `MainWindow.xaml.cs` to wire the overlay**

Replace the `OnVoicePromptReady` method:

```csharp
private void OnVoicePromptReady(string text)
{
    VoiceOverlay.Show(text);
}
```

In the `MainWindow()` constructor, after the voice service setup, add:

```csharp
VoiceOverlay.PromptAccepted += OnPromptAccepted;
VoiceOverlay.PromptDiscarded += OnPromptDiscarded;
```

Add the handler methods:

```csharp
private void OnPromptAccepted(string text)
{
    if (_currentWorktree != null && _focusedPaneIndex < _currentWorktree.Panes.Count)
    {
        var session = _currentWorktree.Panes[_focusedPaneIndex].Session;
        if (session != null)
        {
            try { session.WriteToTerm(text + "\r"); } catch { }
        }
    }

    // Return focus to the terminal
    if (_currentWorktree != null && _focusedPaneIndex < _currentWorktree.Panes.Count)
        _currentWorktree.Panes[_focusedPaneIndex].Control.Focus();
}

private void OnPromptDiscarded()
{
    // Return focus to the terminal
    if (_currentWorktree != null && _focusedPaneIndex < _currentWorktree.Panes.Count)
        _currentWorktree.Panes[_focusedPaneIndex].Control.Focus();
}
```

Update the `VoiceToggleCommand` binding to ignore toggle when overlay is open. Replace the existing binding:

```csharp
CommandBindings.Add(new CommandBinding(VoiceToggleCommand, (_, _) =>
{
    if (VoiceOverlay.IsOpen) return;
    _voiceService?.Toggle();
}));
```

**Step 3: Commit**

```bash
git add tmuxlike/MainWindow.xaml tmuxlike/MainWindow.xaml.cs
git commit -m "feat(voice): integrate PromptOverlay into MainWindow with terminal injection"
```

---

### Task 3.3: End-to-end test

**Step 1: Build**

```bash
cd C:/Users/therouxe/source/repos/feature-voice-to-prompt
dotnet build tmuxlike/tmuxlike.csproj
```

**Step 2: Full flow test**

1. Start the app
2. Wait for "Voice Ready" in status bar
3. Press `Ctrl+Shift+V` — status bar: "Recording..."
4. Speak a messy sentence (e.g., "uhh can you like create a new branch for the uh auth feature")
5. Press `Ctrl+Shift+V` — status bar: "Refining prompt..."
6. Overlay should slide up with cleaned English prompt
7. Edit if desired, press `Enter`
8. Verify text appears in the active terminal pane
9. Test `Esc` to discard

**Step 3: Test edge cases**

- Press `Ctrl+Shift+V` while overlay is open — should be ignored
- Switch worktree while recording — should still work
- Speak in French — should get English output

---

### Task 3.4: Merge Phase 3

**Step 1: Merge back to feature/voice-to-prompt**

```bash
cd C:/Users/therouxe/source/repos/feature-voice-to-prompt
git checkout feature/voice-to-prompt
git merge feature/vtp-phase3-overlay --no-ff -m "Merge Phase 3: Confirmation overlay with terminal injection"
git branch -d feature/vtp-phase3-overlay
```

---

## Phase 4: Polish & Documentation

**Branch:** `feature/vtp-phase4-polish`
**Deliverable:** Error handling hardening, 60s recording timeout, and README with setup instructions.

---

### Task 4.0: Set up Phase 4 branch

**Step 1: Create the phase branch**

```bash
cd C:/Users/therouxe/source/repos/feature-voice-to-prompt
git checkout -b feature/vtp-phase4-polish
```

---

### Task 4.1: Add 60-second recording timeout

**Files:**
- Modify: `voice-bridge/voice_bridge.py`

**Step 1: Add auto-stop timer to VoiceBridge**

In the `_handle_ptt_down` method, after `self._recording = True`, add a timeout task:

```python
        # Start a timeout to auto-stop after MAX_RECORD_SECONDS
        self._timeout_task = asyncio.create_task(
            self._recording_timeout(websocket)
        )
```

Add a new field to `__init__`:

```python
        self._timeout_task: asyncio.Task | None = None
```

Add the timeout method:

```python
    async def _recording_timeout(self, websocket):
        """Auto-stop recording after MAX_RECORD_SECONDS."""
        await asyncio.sleep(MAX_RECORD_SECONDS)
        if self._recording:
            log.warning("Recording timeout (%ds) — auto-stopping", MAX_RECORD_SECONDS)
            await self._handle_ptt_up(websocket)
```

In `_handle_ptt_up`, cancel the timeout at the start:

```python
        if self._timeout_task is not None:
            self._timeout_task.cancel()
            self._timeout_task = None
```

Also cancel in `_handle_cancel`:

```python
        if self._timeout_task is not None:
            self._timeout_task.cancel()
            self._timeout_task = None
```

**Step 2: Commit**

```bash
git add voice-bridge/voice_bridge.py
git commit -m "feat(voice-bridge): add 60-second recording auto-stop timeout"
```

---

### Task 4.2: Add README with setup instructions

**Files:**
- Create: `voice-bridge/README.md`

**Step 1: Create `voice-bridge/README.md`**

```markdown
# Voice Bridge — Voice-to-Prompt Backend

Python WebSocket server that captures audio, transcribes with Whisper, and refines into clean English prompts using a local LLM.

## Prerequisites

### 1. Python 3.10+

```bash
python --version
```

### 2. Ollama

Install Ollama and pull the Qwen model:

```bash
winget install Ollama.Ollama
ollama pull qwen2.5-coder:7b
```

Verify Ollama is running:

```bash
ollama list
# Should show: qwen2.5-coder:7b
```

Ollama runs as a background service on `http://localhost:11434`. If it's not running:

```bash
ollama serve
```

### 3. Python Dependencies

```bash
cd voice-bridge
pip install -r requirements.txt
```

## Usage

The voice bridge is started automatically by tmuxlike on launch. To run standalone for testing:

```bash
python voice_bridge.py
```

The server listens on `ws://localhost:5005`.

## Testing Standalone

With the server running, open a second terminal:

```bash
python -c "
import asyncio, websockets, json
async def test():
    async with websockets.connect('ws://localhost:5005') as ws:
        await ws.send(json.dumps({'action': 'PTT_DOWN'}))
        print(await ws.recv())
        import time; time.sleep(3)  # speak for 3 seconds
        await ws.send(json.dumps({'action': 'PTT_UP'}))
        print(await ws.recv())
        print(await ws.recv())
asyncio.run(test())
"
```

## Troubleshooting

| Issue | Fix |
|:---|:---|
| `Ollama not running` | Run `ollama serve` in a terminal |
| `No microphone detected` | Check Windows sound settings, ensure a mic is plugged in |
| `Python not found` | Ensure Python 3.10+ is in your PATH |
| Slow first transcription | Whisper downloads the model on first run (~150MB) |
```

**Step 2: Commit**

```bash
git add voice-bridge/README.md
git commit -m "docs: add voice-bridge README with setup and troubleshooting"
```

---

### Task 4.3: Update main README with voice feature section

**Files:**
- Modify: `README.md`

**Step 1: Read the existing README and add a voice-to-prompt section**

Add a section under the existing features (exact placement depends on current README structure). Add:

```markdown
## Voice-to-Prompt

Speak messy instructions, get clean English prompts injected into your terminal.

**Shortcut:** `Ctrl+Shift+V` (toggle recording on/off)

### Setup

1. Install [Ollama](https://ollama.com): `winget install Ollama.Ollama`
2. Pull the model: `ollama pull qwen2.5-coder:7b`
3. Install Python deps: `pip install -r voice-bridge/requirements.txt`

The voice bridge starts automatically with the app. See [voice-bridge/README.md](voice-bridge/README.md) for details.

### How It Works

1. Press `Ctrl+Shift+V` to start recording
2. Speak your instructions (any language — output is always English)
3. Press `Ctrl+Shift+V` to stop
4. Review the refined prompt in the overlay
5. `Enter` to inject into terminal, `Esc` to discard
```

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add voice-to-prompt section to main README"
```

---

### Task 4.4: Final integration test

**Step 1: Build**

```bash
dotnet build tmuxlike/tmuxlike.csproj
```

**Step 2: Full regression test checklist**

- [ ] App starts, voice bridge spawns, status bar shows "Voice Ready"
- [ ] `Ctrl+Shift+V` toggles recording → status bar updates
- [ ] Speak in English → get clean English prompt in overlay
- [ ] Speak in French → get English translation in overlay
- [ ] Edit prompt in overlay, press Enter → text injected into terminal
- [ ] Press Esc in overlay → discarded, focus returns to terminal
- [ ] `Ctrl+Shift+V` during processing → cancels, returns to Idle
- [ ] `Ctrl+Shift+V` while overlay is open → ignored
- [ ] Stop Ollama → error shown in status bar → auto-clears after 5s
- [ ] Recording auto-stops after 60 seconds
- [ ] Close app → Python process killed cleanly

---

### Task 4.5: Merge Phase 4

**Step 1: Merge back to feature/voice-to-prompt**

```bash
cd C:/Users/therouxe/source/repos/feature-voice-to-prompt
git checkout feature/voice-to-prompt
git merge feature/vtp-phase4-polish --no-ff -m "Merge Phase 4: Polish, timeouts, and documentation"
git branch -d feature/vtp-phase4-polish
```

---

## Final: PR to main

Once all 4 phases are merged into `feature/voice-to-prompt`:

```bash
cd C:/Users/therouxe/source/repos/feature-voice-to-prompt
git push -u origin feature/voice-to-prompt
gh pr create --title "feat: Voice-to-Prompt (Push-to-Talk)" --body "$(cat <<'EOF'
## Summary
- Push-to-Talk voice capture with Ctrl+Shift+V toggle
- Whisper transcription + Ollama LLM refinement (any language → English)
- Confirmation overlay with edit, inject (Enter), discard (Esc)
- Python sidecar auto-starts with app, auto-reconnects on failure
- Status bar shows voice state (Ready/Recording/Refining/Error)

## New Files
- `voice-bridge/` — Python WebSocket server (Whisper + Ollama)
- `tmuxlike/Services/VoiceService.cs` — WebSocket client + PTT state machine
- `tmuxlike/Controls/PromptOverlay.xaml` — Confirmation overlay

## Prerequisites
- Ollama with `qwen2.5-coder:7b`
- Python 3.10+ with `pip install -r voice-bridge/requirements.txt`

## Test Plan
- [ ] Voice Ready shown in status bar on app start
- [ ] Full flow: record → transcribe → refine → overlay → inject
- [ ] French speech → English output
- [ ] Cancel mid-processing
- [ ] Error handling (Ollama offline, no mic)
- [ ] 60s recording timeout
- [ ] App close kills Python process
EOF
)"
```
