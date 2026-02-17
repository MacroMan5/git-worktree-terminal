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
