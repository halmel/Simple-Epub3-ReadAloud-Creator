import os
import subprocess
import sys
import json
from pathlib import Path
import torch

# 1. FIX: Added flush=True to ensure C# receives output immediately
def safe_print(*args, **kwargs):
    safe_args = []
    for a in args:
        if isinstance(a, str):
            safe_args.append(a.encode('ascii', errors='ignore').decode())
        else:
            safe_args.append(a)
    
    # flush=True is the magic fix for "live" console updates
    print("faster_whisper_CUDA: ", *safe_args, **kwargs, flush=True)

def ensure_dependencies():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    req_path = os.path.join(script_dir, "faster_whisper_requirements.txt")
    
    if not os.path.exists(req_path):
        return

    try:
        # pip usually flushes its own output, which is why you saw it live
        subprocess.run(
            [sys.executable, "-m", "pip", "install", "-r", req_path],
            check=True,
            capture_output=False 
        )
        safe_print(f"✓ Dependencies checked.")
    except subprocess.CalledProcessError as e:
        safe_print(f"X Failed to install dependencies.")
        sys.exit(1)

# Deferred imports to ensure safe_print is available for errors
from faster_whisper import WhisperModel, BatchedInferencePipeline
from pydub.utils import mediainfo

def get_mp3_length(filepath):
    try:
        info = mediainfo(filepath)
        return round(float(info['duration']), 2)
    except Exception:
        return None

def split_by_punctuation(segments):
    """
    Takes segments with word_timestamps and splits them 
    into phrases based on '.' or ','
    """
    processed_fragments = []
    
    for seg in segments:
        if not seg.words:
            continue
            
        current_words = []
        start_time = None

        for word in seg.words:
            if start_time is None:
                start_time = word.start
            
            current_words.append(word.word.strip())
            
            # Check if word ends with punctuation
            if any(punc in word.word for punc in [".", ","]):
                processed_fragments.append({
                    "start": round(start_time, 2),
                    "end": round(word.end, 2),
                    "text": " ".join(current_words)
                })
                current_words = []
                start_time = None
        
        # Catch any leftover words in the segment
        if current_words:
            processed_fragments.append({
                "start": round(start_time, 2),
                "end": round(seg.end, 2),
                "text": " ".join(current_words)
            })
            
    return processed_fragments

def transcribe_files_batched(mp3_paths, device, output_path, batch_size=8, model_size="tiny"):
    safe_print(f"Loading model '{model_size}' on {device}...")
    
    compute_type = "int8" if device == "cpu" else "float16"
    model = WhisperModel(model_size, device=device, compute_type=compute_type)
    batched_model = BatchedInferencePipeline(model=model)

    transcriptions = []
    total = len(mp3_paths)

    for idx, path in enumerate(mp3_paths):
        # Notify C# of progress (assuming your C# logic looks for "PROGRESS:")
        progress_percent = int(((idx) / total) * 100)
        print(f"PROGRESS:{progress_percent}", flush=True)
        
        safe_print(f"Transcribing [{idx+1}/{total}]: {os.path.basename(path)}")

        segments, info = batched_model.transcribe(
            path, 
            batch_size=batch_size, 
            word_timestamps=True 
        )
        
        # Convert generator to list to run transcription
        segments_list = list(segments)
        
        # Apply the punctuation splitting logic
        fragmented_data = split_by_punctuation(segments_list)

        transcriptions.append({
            "file": os.path.basename(path),
            "language": info.language,
            "language_probability": info.language_probability,
            "length": get_mp3_length(path),
            "full_text": "".join([f["text"] + " " for f in fragmented_data]).strip(),
            "fragments": fragmented_data # This contains your , and . splits
        })

    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(transcriptions, f, indent=2, ensure_ascii=False)

    print(f"PROGRESS:100", flush=True)
    safe_print(f"✅ Transcription complete. Output: {output_path}")

def main():
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--file-list", required=True)
    parser.add_argument("--output", default="transcriptions.json")
    parser.add_argument("--model", default="tiny")
    parser.add_argument("--device", default="cuda")
    args = parser.parse_args()

    if not os.path.exists(args.file_list):
        safe_print(f"❌ Error: File list not found.")
        sys.exit(1)

    with open(args.file_list, "r", encoding="utf-8") as f:
        mp3_files = [line.strip() for line in f if line.strip()]

    if not mp3_files:
        safe_print("❌ No MP3 files provided.")
        sys.exit(1)

    transcribe_files_batched(mp3_files, args.device, args.output, 8, model_size=args.model)

if __name__ == "__main__":
    ensure_dependencies()
    main()