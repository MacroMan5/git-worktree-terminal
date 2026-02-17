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
