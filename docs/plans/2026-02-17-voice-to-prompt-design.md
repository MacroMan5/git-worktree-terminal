# Voice-to-Prompt Feature Design

A Push-to-Talk experience where a developer speaks messy, natural-language instructions and gets a clean, well-structured English prompt injected into the active terminal pane.

**Scope:** Voice capture, transcription, text cleanup/translation, and injection. The LLM does NOT receive codebase context — it purely reorganizes and translates spoken text into clean English prompts.

---

## 1. Architecture

Two-process sidecar model:

```
┌─────────────────────────────────┐     WebSocket (JSON)     ┌──────────────────────────┐
│  tmuxlike (C# WPF)              │◄═══════════════════════►│  voice-bridge (Python)    │
│                                 │   ws://localhost:5005    │                          │
│  • PTT key detection            │                          │  • Audio capture (mic)    │
│  • Status bar updates           │   C#→Py: PTT_DOWN/UP    │  • Whisper transcription  │
│  • Confirmation overlay         │   Py→C#: FINAL_PROMPT   │  • LLM refinement (Qwen)  │
│  • Text injection into TermPTY  │                          │                          │
└─────────────────────────────────┘                          └──────────────────────────┘
```

### Sidecar Lifecycle

- `MainWindow` spawns `python voice_bridge.py` as a child process on startup
- WebSocket client in C# connects to `ws://localhost:5005`
- On disconnect: status bar shows "Voice Offline", auto-retry every 5 seconds
- On app close: kill the Python process

### New Files

| File | Purpose |
|:---|:---|
| `voice-bridge/voice_bridge.py` | Python WebSocket server + Whisper + LLM |
| `voice-bridge/requirements.txt` | Python dependencies |
| `tmuxlike/Services/VoiceService.cs` | WebSocket client, PTT state machine, process management |
| `tmuxlike/Controls/PromptOverlay.xaml` | Confirmation/edit overlay |

---

## 2. Communication Protocol

### C# to Python (commands)

```json
{"action": "PTT_DOWN"}
{"action": "PTT_UP"}
{"action": "CANCEL"}
```

### Python to C# (events)

```json
{"event": "LISTENING"}
{"event": "PROCESSING"}
{"event": "FINAL_PROMPT", "text": "..."}
{"event": "ERROR", "message": "..."}
```

### State Machine

```
Idle ──[Ctrl+Shift+V]──► Recording ──[Ctrl+Shift+V]──► Processing ──► Confirmation Overlay
 ▲                           │                              │
 │                        [CANCEL]                       [ERROR]
 │                           │                              │
 └───────────────────────────┴──────────────────────────────┘
```

### Status Bar States

| State | Display |
|:---|:---|
| Idle (connected) | `🎙️ Voice Ready` |
| Idle (disconnected) | `⚠️ Voice Offline` |
| Recording | `🔴 Recording... (Ctrl+Shift+V to stop)` |
| Processing | `⏳ Refining prompt...` |
| Error | `❌ Voice error: <message>` (auto-clears after 5s) |

---

## 3. Python Backend (`voice-bridge/`)

### Structure

```
voice_bridge.py
├── WebSocket server (websockets library, port 5005)
├── AudioRecorder class
│   ├── start() — opens mic stream via sounddevice
│   └── stop() — closes stream, returns audio buffer (numpy array)
├── Transcriber class
│   ├── transcribe(audio) — runs faster-whisper
│   └── Model: "base.en" (upgradeable to "small.en")
├── Refiner class
│   ├── refine(transcript) — calls Ollama API (localhost:11434)
│   ├── Model: Qwen2.5-Coder-7B
│   └── System prompt (see below)
└── Main loop
    ├── Wait for PTT_DOWN → start recording
    ├── Wait for PTT_UP → stop → transcribe → refine
    ├── Send FINAL_PROMPT back
    └── Handle CANCEL at any point
```

### System Prompt for Refinement

```
You are a voice-to-text cleanup assistant for a software developer.

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
- Return ONLY the cleaned text, nothing else
```

### Dependencies

**`requirements.txt`:**

```
websockets>=12.0
faster-whisper>=1.0
sounddevice>=0.4
numpy>=1.24
requests>=2.31
```

### Prerequisites (external)

```
1. Python 3.10+
2. Ollama (https://ollama.com)
   - Install:  winget install Ollama.Ollama
   - Pull model:  ollama pull qwen2.5-coder:7b
   - Runs as background service on localhost:11434
3. pip install -r voice-bridge/requirements.txt
```

---

## 4. C# Frontend Integration

### `VoiceService.cs`

```
VoiceService.cs
├── Process management
│   ├── StartBridge() — spawns "python voice_bridge.py"
│   ├── StopBridge() — kills process on app shutdown
│   └── Stderr monitoring — logs Python errors
├── WebSocket client (System.Net.WebSockets.ClientWebSocket)
│   ├── ConnectAsync() — connects to ws://localhost:5005
│   ├── Auto-reconnect loop (5s interval)
│   └── ReceiveLoop() — dispatches events to UI thread
├── PTT state machine
│   ├── State: Idle, Recording, Processing
│   ├── Toggle() — Ctrl+Shift+V handler
│   │   ├── Idle → send PTT_DOWN → Recording
│   │   ├── Recording → send PTT_UP → Processing
│   │   └── Processing → send CANCEL → Idle
│   └── OnEvent(json) — handles all Python events
└── C# Events
    ├── StateChanged(VoiceState) — updates status bar
    └── PromptReady(string text) — shows overlay
```

### `PromptOverlay.xaml`

```
┌──────────────────────────────────────────────────┐
│ Refined Prompt:                                   │
│ ┌──────────────────────────────────────────────┐  │
│ │ Create a new authentication middleware       │  │
│ │ that validates JWT tokens on all             │  │
│ │ protected API endpoints                      │  │
│ └──────────────────────────────────────────────┘  │
│              [Enter: Inject]  [Esc: Discard]      │
└──────────────────────────────────────────────────┘
```

- Dark themed (`#2d2d2d`), docked above status bar, `Visibility="Collapsed"` by default
- Editable `TextBox` for manual edits
- `Enter` → `WriteToTerm(text + "\r")` on focused pane, hide overlay
- `Esc` → discard, hide overlay, return focus to terminal
- PTT toggle (`Ctrl+Shift+V`) ignored while overlay is visible

### MainWindow Changes

- Add `PromptOverlay` to XAML, docked bottom above status bar
- Add `VoiceToggleCommand` + `Ctrl+Shift+V` keybinding
- Add dynamic `TextBlock` to status bar (right-aligned) for voice state
- New field: `private VoiceService _voiceService`
- Constructor: instantiate, subscribe to events
- `Window_Closing`: add `_voiceService.StopBridge()`

---

## 5. Error Handling

### Python Sidecar Failures

| Scenario | Handling |
|:---|:---|
| Python not installed | Process start fails → `"⚠️ Voice: Python not found"` |
| `voice_bridge.py` crashes | WebSocket disconnect → `"⚠️ Voice Offline"` → auto-retry 5s |
| Ollama not running | Python sends ERROR → `"❌ Ollama not running"` |
| Mic not available | Python sends ERROR → `"❌ No microphone detected"` |

### C# Edge Cases

| Scenario | Handling |
|:---|:---|
| No focused pane | Discard prompt silently |
| Worktree switch during recording | Inject into whatever pane is focused when prompt arrives |
| Overlay open + PTT pressed | Ignore PTT while overlay is visible |

### Timeouts

| Step | Timeout | On timeout |
|:---|:---|:---|
| WebSocket connect | 3s | Retry after 5s |
| LLM refinement | 10s | ERROR event, return to Idle |
| Recording duration | 60s max | Auto-stop recording |

---

## 6. Implementation Phases

### Phase 1 — Python backend
- `voice_bridge.py`, `requirements.txt`
- Test standalone with a WebSocket client (e.g., `websocat`)
- **Deliverable:** working Python server independent of tmuxlike

### Phase 2 — C# service layer
- `VoiceService.cs` — process spawning, WebSocket, state machine
- `Ctrl+Shift+V` keybinding, status bar voice state
- **Deliverable:** PTT toggles recording, status bar updates, prompt logged to Debug

### Phase 3 — Confirmation overlay
- `PromptOverlay.xaml` / `.xaml.cs`
- Wire to `VoiceService.PromptReady`, inject via `WriteToTerm()`
- **Deliverable:** full end-to-end flow

### Phase 4 — Polish & docs
- All error states and timeouts
- 60s recording cap
- README with prerequisites (Python, Ollama, model pull)
- **Deliverable:** production-ready feature
