import subprocess
import os
import sys
def ensure_dependencies():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    req_path = os.path.join(script_dir, "requirements.txt")

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

def safe_print(*args, **kwargs):
    safe_args = []
    for a in args:
        if isinstance(a, str):
            safe_args.append(a.encode('ascii', errors='ignore').decode())
        else:
            safe_args.append(a)
    print(*safe_args, **kwargs)

import json
from pathlib import Path
from faster_whisper import WhisperModel, BatchedInferencePipeline
import torch
from pydub.utils import mediainfo


# Utility: get audio duration
def get_mp3_length(filepath):
    try:
        info = mediainfo(filepath)
        return round(float(info['duration']), 2)
    except Exception:
        return None

# Batched transcription using faster-whisper
def transcribe_files_batched(mp3_paths, device, output_path, batch_size=8, model_size="tiny"):
    safe_print(f"Loading model '{model_size}' on {device}...")
    model = WhisperModel(model_size, device=device, compute_type="int8" if device=="cpu" else "float16")
    batched_model = BatchedInferencePipeline(model=model)

    transcriptions = []

    total = len(mp3_paths)
    for idx, path in enumerate(mp3_paths):
        safe_print(f"Transcribing [{idx+1}/{total}]: {os.path.basename(path)}")

        segments, info = batched_model.transcribe(path, batch_size=batch_size)
        segments = list(segments)  # Run transcription

        segment_data = []
        for seg in segments:
            segment_data.append({
                "start": round(seg.start, 2),
                "end": round(seg.end, 2),
                "text": seg.text.strip()
            })

        transcriptions.append({
            "file": os.path.basename(path),
            "language": info.language,
            "language_probability": info.language_probability,
            "length": get_mp3_length(path),
            "text": "".join([s["text"] for s in segment_data]),
            "segments": segment_data
        })

    # Save to JSON
    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(transcriptions, f, indent=2, ensure_ascii=False)

    safe_print(f"✅ Transcription complete. Output written to {output_path}")


def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--file-list", help="Path to a text file containing MP3 paths")
    parser.add_argument("mp3_files", nargs="*", help="MP3 file paths (ignored if --file-list is used)")
    parser.add_argument("--device", choices=["cpu", "cuda", "auto"], default="auto", help="Device to use")
    parser.add_argument("--output", default="transcriptions.json", help="Output JSON file path")
    parser.add_argument("--batch-size", type=int, default=8, help="Batch size for CPU transcription")
    parser.add_argument("--model-size", default="small", help="Model size (tiny, small, medium, large-v2, etc.)")
    args = parser.parse_args()

    # Load files
    if args.file_list:
        with open(args.file_list, "r", encoding="utf-8") as f:
            mp3_files = [line.strip() for line in f if line.strip()]
    else:
        mp3_files = args.mp3_files

    if not mp3_files:
        safe_print("❌ No MP3 files provided.")
        sys.exit(1)

    for f in mp3_files:
        if not Path(f).is_file():
            safe_print(f"❌ Error: '{f}' does not exist.")
            sys.exit(1)

    # Decide device
    device = "cuda" if (args.device=="auto" and torch.cuda.is_available()) else args.device
    safe_print(f"Using device: {device}")

    # Run batched transcription
    transcribe_files_batched(mp3_files, device, args.output, batch_size=args.batch_size, model_size=args.model_size)


if __name__ == "__main__":
    ensure_dependencies()
    main()
