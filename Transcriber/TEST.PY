import subprocess
import os
import sys
import json
from pathlib import Path

# --- Dependency Management ---
def ensure_dependencies():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    # Basic requirements for this script
    deps = ["faster-whisper", "torch", "pydub"]
    try:
        import faster_whisper
        import torch
        import pydub
    except ImportError:
        safe_print("! Missing dependencies. Installing...")
        subprocess.run([sys.executable, "-m", "pip", "install"] + deps, check=True)

def safe_print(*args, **kwargs):
    safe_args = [a.encode('ascii', errors='ignore').decode() if isinstance(a, str) else a for a in args]
    print(*safe_args, **kwargs)

# Conditional imports after dependency check
from faster_whisper import WhisperModel
import torch
from pydub.utils import mediainfo

# --- Configuration ---
MAX_WORDS_PER_SEGMENT = 8  # Adjust this to control "sentence" length
MAX_GAP_SECONDS = 1.5      # Break segment if there is a silence longer than this

def get_mp3_length(filepath):
    try:
        info = mediainfo(filepath)
        return round(float(info['duration']), 2)
    except Exception:
        return None

def process_word_timestamps(segments):
    """
    Groups individual words into small chunks to satisfy 'shorter sentences' requirement.
    """
    grouped_segments = []
    current_words = []
    
    for segment in segments:
        if not segment.words:
            continue
            
        for word in segment.words:
            current_words.append(word)
            
            # Logic to break: 
            # 1. Max word count reached OR 
            # 2. Significant pause after word OR
            # 3. Punctuation suggesting end of thought
            should_break = (
                len(current_words) >= MAX_WORDS_PER_SEGMENT or 
                (len(current_words) > 0 and word.word.strip().endswith(('.', '?', '!')))
            )

            if should_break:
                grouped_segments.append({
                    "start": round(current_words[0].start, 2),
                    "end": round(current_words[-1].end, 2),
                    "text": "".join([w.word for w in current_words]).strip(),
                    "words": [{"word": w.word.strip(), "start": w.start, "end": w.end} for w in current_words]
                })
                current_words = []

    # Catch remaining words
    if current_words:
        grouped_segments.append({
            "start": round(current_words[0].start, 2),
            "end": round(current_words[-1].end, 2),
            "text": "".join([w.word for w in current_words]).strip(),
            "words": [{"word": w.word.strip(), "start": w.start, "end": w.end} for w in current_words]
        })
        
    return grouped_segments

def transcribe_files(mp3_paths, device, output_path, model_size="tiny"):
    safe_print(f"Loading model '{model_size}' on {device}...")
    
    # Speed optimization: int8_float16 is usually fastest on modern NVIDIA GPUs
    compute_type = "float16" if device == "cuda" else "int8"
    model = WhisperModel(model_size, device=device, compute_type=compute_type)

    transcriptions = []
    total = len(mp3_paths)

    for idx, path in enumerate(mp3_paths):
        safe_print(f"Transcribing [{idx+1}/{total}]: {os.path.basename(path)}")
        
        # word_timestamps=True provides the alignment you need
        segments, info = model.transcribe(
            path, 
            beam_size=1,        # 1 = Greedy search (Fastest speed)
            word_timestamps=True,
            vad_filter=True     # Removes silence to speed up processing
        )
        
        # Process segments into short word-aligned chunks
        short_segments = process_word_timestamps(segments)

        transcriptions.append({
            "file": os.path.basename(path),
            "language": info.language,
            "length": get_mp3_length(path),
            "text": " ".join([s["text"] for s in short_segments]),
            "segments": short_segments
        })

    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(transcriptions, f, indent=2, ensure_ascii=False)

    safe_print(f"✅ Transcription complete. Output: {output_path}")

def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--file-list", help="Path to text file with MP3 paths")
    parser.add_argument("mp3_files", nargs="*", help="MP3 file paths")
    parser.add_argument("--device", choices=["cpu", "cuda", "auto"], default="auto")
    parser.add_argument("--output", default="transcriptions.json")
    parser.add_argument("--model-size", default="tiny", help="Use 'tiny' for max speed")
    args = parser.parse_args()

    if args.file_list:
        with open(args.file_list, "r", encoding="utf-8") as f:
            mp3_files = [line.strip() for line in f if line.strip()]
    else:
        mp3_files = args.mp3_files

    if not mp3_files:
        safe_print("❌ No files provided.")
        sys.exit(1)

    device = "cuda" if (args.device == "auto" and torch.cuda.is_available()) else args.device
    
    transcribe_files(mp3_files, device, args.output, model_size=args.model_size)

if __name__ == "__main__":
    ensure_dependencies()
    main()