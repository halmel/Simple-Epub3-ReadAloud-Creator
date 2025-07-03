# 📚 Simple EPUB3 Read-Aloud Creator

> ⚠️ **ALPHA SOFTWARE – USE AT YOUR OWN RISK**  
> This project is still in early development and may break, crash, or corrupt files.  
> Please **test on backups** and do not use on important data.

---

## 🧰 Requirements
- ✅ [.NET Desktop Runtime 9](https://dotnet.microsoft.com/download/dotnet/9.0/runtime-desktop)  
  (Required to **run** the WPF application) :contentReference[oaicite:1]{index=1}
- ✅ [Python 3.8+](https://www.python.org/downloads/)
- ✅ (Optional) CUDA-compatible GPU with [CUDA Toolkit & Drivers](https://developer.nvidia.com/cuda-downloads) for accelerated transcription (if using Whisper with GPU support)

---

A simple tool for creating synchronized audio books from text books.

Inspired by:
- [Storyteller Platform](https://storyteller-platform.gitlab.io/storyteller/)
- [syncabook by r4victor](https://github.com/r4victor/syncabook)

The goal of this project is to create a simpler, more user-friendly alternative to the Storyteller app.

---

## 📖 Reading the Synced Book

You can use the following apps to read EPUB3 books with synced audio:

### ✅ [Thorium Reader](https://www.edrlab.org/software/thorium-reader/)  
**Platforms:** Windows, macOS, Linux  
Great for testing read-aloud EPUB3 books. Actively maintained.

---

### ✅ [Menestrello](https://github.com/readbeyond/menestrello)  
**Platforms:** iOS, Android  
Good for reading.  
⚠️ One known issue: the audio playback bar is broken — even with books made by Storyteller. Root cause unknown.

---

### ✅ [Storyteller Mobile App](https://storyteller-platform.gitlab.io/storyteller/docs/reading-your-books/storyteller-apps)  
**Platforms:** iOS, Android  
Supports manual import of processed books.  
A solid and stable option for EPUB3 read-aloud support.

---
