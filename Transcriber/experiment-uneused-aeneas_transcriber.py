#!/usr/bin/env python3
"""
Align audiobook audio with ebook text using system-installed Aeneas.
Compatible with Windows Aeneas all-in-one package.
"""

import os
import sys
import subprocess
from pathlib import Path


def safe_print(*args, **kwargs):
    """Safe print that avoids Unicode console issues on Windows."""
    safe_args = []
    for a in args:
        if isinstance(a, str):
            safe_args.append(a.encode("ascii", errors="ignore").decode())
        else:
            safe_args.append(a)
    print(*safe_args, **kwargs)
    sys.stdout.flush()


def ensure_dependencies():
    """Ensure non-Aeneas dependencies are available."""
    import importlib.util

    def is_installed(package):
        return importlib.util.find_spec(package) is not None

    # These should be handled by pip if missing.
    needed = ["pydub", "numpy", "torch"]
    missing = [pkg for pkg in needed if not is_installed(pkg)]

    if missing:
        safe_print(f"Installing dependencies: {', '.join(missing)}")
        try:
            subprocess.run([sys.executable, "-m", "pip", "install", *missing], check=True)
            safe_print("✓ Dependencies installed successfully")
        except subprocess.CalledProcessError as e:
            safe_print("X Failed to install dependencies:", e)
            sys.exit(1)
    else:
        safe_print("✓ All Python dependencies already installed.")


# Ensure Whisper-related dependencies, not Aeneas itself
ensure_dependencies()

# --- Try importing system-wide Aeneas ---
try:
    from aeneas.executetask import ExecuteTask
    from aeneas.task import Task
except ImportError:
    safe_print("X Aeneas not found. Please install the Aeneas Windows all-in-one package:")
    safe_print("→ https://github.com/readbeyond/aeneas-installer")
    sys.exit(1)


def align_audio_with_text(audio_path, text_path, output_path):
    """Run Aeneas to align audio with text."""
    if not os.path.exists(audio_path):
        raise FileNotFoundError(f"Audio file not found: {audio_path}")
    if not os.path.exists(text_path):
        raise FileNotFoundError(f"Text file not found: {text_path}")

    safe_print(f"Aligning '{audio_path}' with '{text_path}'...")
    safe_print("PROGRESS:10")

    # Basic English configuration (change 'eng' for other languages)
    config = "task_language=eng|is_text_type=mplain|os_task_file_format=json"

    task = Task(config_string=config)
    task.audio_file_path_absolute = str(Path(audio_path).resolve())
    task.text_file_path_absolute = str(Path(text_path).resolve())
    task.sync_map_file_path_absolute = str(Path(output_path).resolve())

    safe_print("PROGRESS:40")
    ExecuteTask(task).execute()

    safe_print("PROGRESS:80")
    task.output_sync_map_file()

    safe_print("PROGRESS:100")
    safe_print(f"✓ Alignment complete. Output saved to {output_path}")


def main():
    import argparse

    parser = argparse.ArgumentParser(description="Align audiobook with ebook text using Aeneas.")
    parser.add_argument("--audio", required=True, help="Path to the audio file (e.g., combined.mp3)")
    parser.add_argument("--text", required=True, help="Path to the text file containing ebook content")
    parser.add_argument("--output", default="aeneas_output.json", help="Output JSON file path")
    args = parser.parse_args()

    try:
        align_audio_with_text(args.audio, args.text, args.output)
    except Exception as e:
        safe_print("X Alignment failed:", str(e))
        sys.exit(1)


if __name__ == "__main__":
    main()
