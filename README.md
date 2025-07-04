# 📚 Simple EPUB3 Read-Aloud Creator

> ⚠️ **ALPHA SOFTWARE – USE AT YOUR OWN RISK**  
> This project is still in early development and may break, crash, or corrupt files.  
> Please **test on backups** and do not use on important data.
A simple tool for creating synchronized audio books from text books.

---

Inspired by:
- [Storyteller Platform](https://storyteller-platform.gitlab.io/storyteller/)
- [syncabook by r4victor](https://github.com/r4victor/syncabook)

The goal of this project is to create a simpler, more user-friendly alternative to the Storyteller app.

---

## 🧰 Requirements
- ✅ [.NET Desktop Runtime 9](https://dotnet.microsoft.com/download/dotnet/9.0/runtime-desktop)  (Required to **run** the WPF application)
- ✅ [Python 3.8+](https://www.python.org/downloads/)
- ✅ (Optional) CUDA-compatible GPU with [CUDA Toolkit & Drivers](https://developer.nvidia.com/cuda-downloads) for accelerated transcription (if using Whisper with GPU support)

## 🛠️ How to Use

1. **Download and extract** the latest release from GitHub.

2. **Run the app**  
   Navigate to:  /LatestRelease/net9.0-windows/
   and launch:  Readaloud-Epub3-Creator.exe

3. **Initial Setup**  
- On first launch, open the **Settings** tab.
- Choose your processing device:  
  - `CUDA` (GPU – faster, requires CUDA drivers)  
  - `CPU` (slower, but works without GPU)
- If using CPU, set the desired number of workers (cores to use).

4. **Add Books**  
You have two options:
- **Create New Book**: Add a single book by selecting its `.mp3` files and matching `.epub`.
- **Import from Folder**: Add multiple books at once by choosing a folder that contains `.mp3` and `.epub` files (anywhere inside).  
  The files must share matching name segments to be paired correctly.

5. **Start Alignment**
- Click **Align** on the book.
- Use **Show/Hide Console** to view progress and debug messages.
- ⚠️ **Note**: The first run can take a long time as it installs Python dependencies and downloads the Whisper model.
  - Example: A long book may take ~20 minutes on an RTX 4070 Ti (after initial setup).

6. **Check Results**
- Processed books will appear in:  
  ```
  Ebooks/<GroupName>/ProcessedBooks
  ```
  Default group is `Main`.

7. **View Logs**
   - Use the **View Alignment Log** button to inspect detailed logs (note: the window may take time to load, especially for large books or if there are issues).
   - For a quick check, look at the **Hidden Success Logs** count:
     - If the number is in the **tens of thousands**, it usually means alignment completed successfully with minimal or no problems.


8. **Organize with Groups**
- Use the **Group system** to organize your books into manageable collections.
- Each group creates a separate folder in `Ebooks/` for better library organization.

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
## 🐞 Debugging & Development

- If the alignment result looks wrong or broken, you can send the alignment log file for inspection: Ebooks<GroupName><BookName>\OriginalEpub\AlignmentLog.json
- 
This log helps diagnose what went wrong during the alignment process.


- **Developing the app**:  
Open the solution in **Visual Studio** to start contributing.

- The transcriber logic is mostly in the `transcriber.py` script.  
You can simply edit and save this file, and changes will be reflected in the app immediately.

---

### 🔧 To Do

- UI design improvements  
- Better book processing feedback  
- Properly stop the Python script when the app closes  
- Model size selector option  
- Multi-language testing — in theory it should work, possibly with larger models  
- Alignment optimization  
- Ability to set alignment parameters from the app’s settings
