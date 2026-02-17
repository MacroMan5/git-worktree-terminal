"""Voice-to-Prompt bridge server.

WebSocket server that captures audio, transcribes with Whisper,
refines with Ollama, and returns clean prompts.
"""

import asyncio
import json
import logging

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


class VoiceBridge:
    """WebSocket server orchestrating record → transcribe → refine pipeline."""

    def __init__(self):
        self._recorder = AudioRecorder()
        self._transcriber = Transcriber()
        self._refiner = Refiner()
        self._recording = False
        self._cancel = False
        self._timeout_task: asyncio.Task | None = None

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
            self._timeout_task = asyncio.create_task(
                self._recording_timeout(websocket)
            )
        except Exception as e:
            await self._send(websocket, {"event": "ERROR", "message": f"Mic error: {e}"})

    async def _handle_ptt_up(self, websocket):
        """Stop recording, transcribe, refine, and send result."""
        if not self._recording:
            return
        self._recording = False
        if self._timeout_task is not None:
            self._timeout_task.cancel()
            self._timeout_task = None
        audio = self._recorder.stop()

        if self._cancel:
            return

        await self._send(websocket, {"event": "PROCESSING"})

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
        if self._timeout_task is not None:
            self._timeout_task.cancel()
            self._timeout_task = None
        if self._recording:
            self._recorder.stop()
            self._recording = False
        log.info("Operation cancelled")

    async def _recording_timeout(self, websocket):
        """Auto-stop recording after MAX_RECORD_SECONDS."""
        await asyncio.sleep(MAX_RECORD_SECONDS)
        if self._recording:
            log.warning("Recording timeout (%ds) — auto-stopping", MAX_RECORD_SECONDS)
            await self._handle_ptt_up(websocket)

    async def _send(self, websocket, data: dict):
        """Send a JSON event to the client."""
        await websocket.send(json.dumps(data))
        log.info("Sent: %s", data.get("event"))


async def main():
    bridge = VoiceBridge()
    log.info("Starting Voice Bridge on ws://%s:%d", HOST, PORT)
    async with websockets.serve(bridge.handler, HOST, PORT):
        await asyncio.Future()


if __name__ == "__main__":
    asyncio.run(main())
