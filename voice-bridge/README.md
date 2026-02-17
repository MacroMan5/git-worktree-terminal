# Voice Bridge

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
