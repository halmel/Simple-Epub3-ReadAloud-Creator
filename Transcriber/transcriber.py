
def safe_print(*args, **kwargs):
    safe_args = []
    for a in args:
        if isinstance(a, str):
            safe_args.append(a.encode('ascii', errors='ignore').decode())
        else:
            safe_args.append(a)
        print(*safe_args, **kwargs)

import os
import sys
import json
import subprocess
def ensure_dependencies():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    req_path = os.path.join(script_dir, "requirements.txt")


    # Then install all requirements including torch from requirements.txt
    try:
        subprocess.run(
            [sys.executable, "-m", "pip", "install", "-r", req_path],
            check=True
        )
        safe_print(f"✓ Installed dependencies from {req_path}")
    except subprocess.CalledProcessError as e:
        safe_print(f"X Failed to install dependencies from {req_path}")
        safe_print(e)
        sys.exit(1)

ensure_dependencies()
import whisper
import torch
from pydub.utils import mediainfo
from pathlib import Path
import multiprocessing

import sys
import io
import ctypes
import os

if sys.platform == "win32":
    ctypes.windll.kernel32.SetConsoleOutputCP(65001)
    sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    sys.stderr = io.TextIOWrapper(sys.stderr.buffer, encoding='utf-8')




def debug_cuda():
    safe_print("🛠 CUDA Debugging Info:")
    try:
        safe_print(f" - torch.cuda.is_available(): {torch.cuda.is_available()}")
        safe_print(f" - torch.version.cuda: {torch.version.cuda}")
        safe_print(f" - torch.backends.cudnn.version(): {torch.backends.cudnn.version()}")
        safe_print(f" - torch.cuda.device_count(): {torch.cuda.device_count()}")
        if torch.cuda.is_available():
            safe_print(f" - torch.cuda.get_device_name(): {torch.cuda.get_device_name(0)}")
    except Exception as e:
        safe_print(f" - Error accessing torch.cuda: {e}")

    try:
        result = subprocess.run(["nvidia-smi"], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        if result.returncode == 0:
            safe_print(" - V nvidia-smi output:\n" + result.stdout)
        else:
            safe_print(" - X nvidia-smi error:\n" + result.stderr)
    except FileNotFoundError:
        safe_print(" - X nvidia-smi not found. Ensure NVIDIA drivers are installed and in PATH.")
    except Exception as e:
        safe_print(f" - Unexpected error running nvidia-smi: {e}")


def get_mp3_length(filepath):
    try:
        info = mediainfo(filepath)
        return round(float(info['duration']), 2)
    except Exception as e:
        return None


def transcribe_one_file(path, device):
    model = whisper.load_model("tiny", device=device)
    audio = whisper.load_audio(path)
    audio = whisper.pad_or_trim(audio)
    mel = whisper.log_mel_spectrogram(audio).to(device)

    lang_tup = model.detect_language(mel)
    lang = lang_tup[0][0] if isinstance(lang_tup, list) else lang_tup[0]
    language = lang if isinstance(lang, str) else max(lang_tup[1], key=lang_tup[1].get)

    result = model.transcribe(path, language=language, task="transcribe", fp16=(device == "cuda"))

    segments = []
    for seg in result.get("segments", []):
        segments.append({
            "id": seg.get("id"),
            "start": round(seg.get("start", 0), 2),
            "end": round(seg.get("end", 0), 2),
            "text": seg.get("text", "").strip(),
            "confidence": seg.get("confidence", None)
        })

    return {
        "file": os.path.basename(path),
        "language": language,
        "length": get_mp3_length(path),
        "text": result.get("text", "").strip(),
        "segments": segments
    }


# Top-level function for multiprocessing (to replace the lambda)
def transcribe_cpu(path):
    return transcribe_one_file(path, "cpu")


def transcribe_files_multicore(mp3_paths, output_path, num_workers):
    total = len(mp3_paths)
    transcriptions = []

    with multiprocessing.Pool(processes=num_workers) as pool:
        for idx, result in enumerate(pool.imap_unordered(transcribe_cpu, mp3_paths), 1):
            transcriptions.append(result)
            percent = int(idx * 100 / total)
            safe_print(f"PROGRESS:{percent}", flush=True)

    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(transcriptions, f, indent=2, ensure_ascii=False)

    safe_print(f"V Transcription complete. Output written to {output_path}")


def transcribe_files(mp3_paths, device_option, output_path, num_workers):
    if device_option == "auto":
        device = "cuda" if torch.cuda.is_available() else "cpu"
    else:
        device = device_option
        if device == "cuda" and not torch.cuda.is_available():
            safe_print("X CUDA was requested but is not available. Starting diagnostics...")
            debug_cuda()
            raise RuntimeError("CUDA requested but not available.")

    safe_print(f"Using device: {device}")

    if device == "cpu" and len(mp3_paths) > 1:
        transcribe_files_multicore(mp3_paths, output_path, num_workers)
        return

    # Single-thread fallback or CUDA path
    model = whisper.load_model("tiny", device=device)
    transcriptions = []

    total = len(mp3_paths)
    for idx, path in enumerate(mp3_paths):
        safe_print(f"Transcribing: {os.path.basename(path)}")

        audio = whisper.load_audio(path)
        audio = whisper.pad_or_trim(audio)
        mel = whisper.log_mel_spectrogram(audio).to(device)

        lang_tup = model.detect_language(mel)
        lang = lang_tup[0][0] if isinstance(lang_tup, list) else lang_tup[0]
        language = lang if isinstance(lang, str) else max(lang_tup[1], key=lang_tup[1].get)

        result = model.transcribe(path, language=language, task="transcribe", fp16=(device == "cuda"))

        segments = []
        for seg in result.get("segments", []):
            segments.append({
                "id": seg.get("id"),
                "start": round(seg.get("start", 0), 2),
                "end": round(seg.get("end", 0), 2),
                "text": seg.get("text", "").strip(),
                "confidence": seg.get("confidence", None)
            })

        transcriptions.append({
            "file": os.path.basename(path),
            "language": language,
            "length": get_mp3_length(path),
            "text": result.get("text", "").strip(),
            "segments": segments
        })

        percent = int((idx + 1) * 100 / total)
        safe_print(f"PROGRESS:{percent}", flush=True)

    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(transcriptions, f, indent=2, ensure_ascii=False)

    safe_print(f"V Transcription complete. Output written to {output_path}")


def main():
    import argparse

    # Windows UTF-8 console output fix
    if sys.platform == "win32":
        import ctypes
        ctypes.windll.kernel32.SetConsoleOutputCP(65001)

    parser = argparse.ArgumentParser()
    parser.add_argument("mp3_files", nargs="+", help="MP3 file paths")
    parser.add_argument("--device", choices=["cpu", "cuda", "auto"], default="auto", help="Device to use for transcription")
    parser.add_argument("--output", default="transcriptions.json", help="Path to output transcript JSON file")
    parser.add_argument("--workers", type=int, default=max(1, multiprocessing.cpu_count() // 2),
                        help="Number of worker processes to use (CPU only)")

    args = parser.parse_args()

    mp3_files = args.mp3_files
    device_option = args.device
    output_path = args.output
    num_workers = args.workers

    for f in mp3_files:
        if not Path(f).is_file():
            safe_print(f"X Error: '{f}' does not exist.")
            sys.exit(1)

    try:
        transcribe_files(mp3_files, device_option, output_path, num_workers)
    except Exception as e:
        # Removed emoji here to avoid encoding issues on Windows
        safe_print("Transcription failed:", str(e))
        sys.exit(1)


if __name__ == "__main__":
    multiprocessing.freeze_support()
    main()
