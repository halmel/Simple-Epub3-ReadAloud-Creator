# New Project Structure - Visual Guide

## Complete File Tree

```
Epub3MediaOverlays.Core/
│
├── MediaOverlayGeneration/              ← NEW FEATURE FOLDER
│   │
│   ├── MediaOverlayGenerator.cs         ✨ PUBLIC: Main entry point
│   │   ├── GenerateEpub(BookData)       Primary method
│   │   ├── SplitTextSegmentsIntoWords()
│   │   ├── AssignSentenceIndices()
│   │   ├── TagWordsWithSmilSpans()
│   │   ├── CollectAudioLinkGaps()
│   │   └── FillSegmentGaps()
│   │
│   ├── MediaOverlayGeneratorSettings.cs ✨ PUBLIC: Configuration
│   │   ├── TranscriptionScript
│   │   ├── AlignmentConfig
│   │   └── FromAlingnerConfig()         Backward compatibility converter
│   │
│   ├── Models/                          ✨ PUBLIC: Data Models
│   │   │
│   │   ├── WordSegment.cs               Text word with audio links
│   │   │   ├── Word (string)
│   │   │   ├── FileName
│   │   │   ├── ParentXPath
│   │   │   ├── SentenceIndex
│   │   │   ├── LinkedSegments[]
│   │   │   ├── NormalizedWord
│   │   │   └── AssignListIndices()
│   │   │
│   │   ├── AudioFragment.cs             Audio timing + text
│   │   │   ├── Start (double)
│   │   │   ├── End (double)
│   │   │   ├── Text (string)
│   │   │   ├── FileId
│   │   │   ├── NormalizedText
│   │   │   └── AssignListIndices()
│   │   │
│   │   ├── AudioLinkGap.cs              Unlinked word regions
│   │   │   ├── StartSegmentIndex
│   │   │   ├── EndSegmentIndex
│   │   │   ├── AffectedWords[]
│   │   │   └── IsGap (bool)
│   │   │
│   │   └── HtmlTextSegment.cs           HTML text extraction
│   │       ├── FileName
│   │       ├── ParentXPath
│   │       ├── OriginalText
│   │       └── EditedText
│   │
│   └── Internal/                        ⚠️ PRIVATE: Not for external use
│       │
│       ├── EpubProcessor.cs             EPUB I/O and HTML manipulation
│       │   ├── LoadEpubAndExtractHtml()
│       │   ├── ExtractAllTextSegments()
│       │   ├── ApplyTextSegmentsToHtmlDocuments()
│       │   ├── RebuildEpubWithMedia()
│       │   ├── RecombineWordsIntoTextSegments()
│       │   ├── NormalizeSegmentsToFullMp3Length()
│       │   ├── LoadWordSegments()
│       │   ├── SaveWordSegments()
│       │   └── GetAllFilesOfType()
│       │
│       ├── TranscriptProcessor.cs       Transcription deserialization
│       │   ├── ExtractSegmentsWithFileId()
│       │   └── RunTranscription()
│       │
│       ├── AlignmentProcessor.cs        Audio-to-text fuzzy matching
│       │   ├── RunAlignment()
│       │   ├── RunAlingment()           (Backward compatibility)
│       │   ├── AlignMicroSegments()
│       │   ├── MatchFragmentAtWordIndex()
│       │   ├── NormalizeText()
│       │   ├── BuildCharArray()
│       │   ├── FindFragmentSequenceMatchInWordRange()
│       │   ├── ValidateExpansion()
│       │   ├── LogOutcome()
│       │   └── [20+ alignment methods]
│       │
│       ├── AlignmentConfiguration.cs    Algorithm tuning parameters
│       │   ├── MicroJobFragmentThreshold
│       │   ├── AnchorSearchDivisor
│       │   ├── ExpansionDepth
│       │   ├── ValidAnchorScoreThreshold
│       │   ├── BackupStrategyScoreThreshold
│       │   └── [16+ tuning parameters]
│       │
│       ├── TranscriptionRoot.cs         Internal JSON structure
│       │   ├── File (audio filename)
│       │   ├── Language
│       │   ├── FullText
│       │   ├── Fragments[]
│       │   └── LinkSegments()
│       │
│       └── SmilGenerator.cs             SMIL XML generation
│           ├── SmilDocument
│           ├── SmilBody
│           ├── SmilSeq
│           ├── SmilPar
│           ├── GenerateSmilFiles()
│           └── AddTotalLengthCommentToSmil()
│
├── Models/                              Existing models (unchanged)
│   ├── Book.cs
│   ├── BookGroupList.cs
│   └── Converters.cs
│
├── Services/                            Existing services (unchanged)
│   └── SettingsManager.cs
│       ├── AppSettings
│       └── JsonSettingsProvider
│
└── Utilities/                           ⚠️ DEPRECATED (old location)
    ├── GenerateEpubUtil.cs              (Replaced by MediaOverlayGenerator)
    ├── EpubUtility.cs                   (Replaced by EpubProcessor)
    ├── TranscriptClass.cs               (Replaced by TranscriptProcessor)
    ├── Alingner.cs                      (Replaced by AlignmentProcessor)
    ├── EpubSmilLib.cs                   (Replaced by SmilGenerator)
    └── AlingnerConfiguration.cs         (Replaced by AlignmentConfiguration)
```

## Class Dependency Graph

```
┌─────────────────────────────────────────────────────┐
│      PUBLIC: MediaOverlayGenerator                  │
│      (Main orchestrator - USE THIS)                 │
└─────────────────────────────────────────────────────┘
              ↓
    ┌─────────┴──────────┬──────────┬──────────┐
    │                    │          │          │
    ↓                    ↓          ↓          ↓
┌─────────┐         ┌───────┐  ┌──────┐  ┌────────┐
│  EPUB   │         │ SMIL  │  │ Align│  │Trans-  │
│Processor│         │ Gen   │  │ment  │  │script  │
│ (I/O)   │         │Process│  │Process│  │Process│
└─────────┘         └───────┘  └──────┘  └────────┘
    │                   │          │          │
    └─────────┬─────────┴──────────┴──────────┘
              │
              ↓
    ┌─────────────────────────┐
    │  PUBLIC: Data Models    │
    │  - WordSegment          │
    │  - AudioFragment        │
    │  - AudioLinkGap         │
    │  - HtmlTextSegment      │
    └─────────────────────────┘
```

## Usage Flow

```
┌──────────────────────────────────────────────────────────┐
│  WPF Frontend (MainWindow.xaml.cs)                       │
└──────────────────────────────────────────────────────────┘
         ↓
┌──────────────────────────────────────────────────────────┐
│  Create MediaOverlayGeneratorSettings                    │
│  - Set TranscriptionScript (CUDA or CPU)                │
│  - Set AlignmentConfig (tuning parameters)              │
└──────────────────────────────────────────────────────────┘
         ↓
┌──────────────────────────────────────────────────────────┐
│  new MediaOverlayGenerator(settings)                     │
└──────────────────────────────────────────────────────────┘
         ↓
┌──────────────────────────────────────────────────────────┐
│  generator.GenerateEpub(BookData)                        │
│  ├─ Step 1: Load EPUB                                   │
│  │  └─ Uses: EpubProcessor.LoadEpubAndExtractHtml()    │
│  ├─ Step 2: Extract text                                │
│  │  └─ Uses: EpubProcessor.ExtractAllTextSegments()     │
│  ├─ Step 3: Split into words                            │
│  │  └─ Uses: WordSegment model                          │
│  ├─ Step 4: Generate transcription                      │
│  │  └─ Uses: TranscriptProcessor.RunTranscription()     │
│  ├─ Step 5: Align audio to text                         │
│  │  └─ Uses: AlignmentProcessor.RunAlignment()          │
│  ├─ Step 6: Generate SMIL                               │
│  │  └─ Uses: SmilGenerator.GenerateSmilFiles()          │
│  ├─ Step 7: Fill gaps                                   │
│  │  └─ Uses: AudioLinkGap model                         │
│  └─ Step 8: Rebuild EPUB with media                     │
│     └─ Uses: EpubProcessor.RebuildEpubWithMedia()       │
└──────────────────────────────────────────────────────────┘
         ↓
    Output: EPUB3 with media overlays
```

## Namespace Usage Example

```csharp
// ✅ CORRECT: Only use these namespaces
using Epub3MediaOverlays.Core.MediaOverlayGeneration;           // Main API
using Epub3MediaOverlays.Core.MediaOverlayGeneration.Internal;  // Interfaces only

// ✅ These are accessible
var generator = new MediaOverlayGenerator(settings);
var config = new AlignmentConfiguration();
var segment = new WordSegment();
var fragment = new AudioFragment();
var script = new CUDAFasterWhisperScript(...);

// ❌ These will NOT compile (intentionally)
var processor = new AlignmentProcessor(...);    // ERROR: inaccessible due to level
var root = new TranscriptionRoot();             // ERROR: inaccessible due to level
var smilGen = new SmilGenerator();              // ERROR: inaccessible due to level

// Use the public API instead!
var generator = new MediaOverlayGenerator(settings);
generator.GenerateEpub(data);  // ✅ This handles everything internally
```

## File Statistics

| Component | Files | Lines | Purpose |
|-----------|-------|-------|---------|
| **Main Entry** | 1 | ~200 | Orchestration |
| **Processors** | 6 | ~2000 | Core algorithms |
| **Models** | 4 | ~200 | Data structures |
| **Configuration** | 1 | ~100 | Tuning parameters |
| **Total** | 12 | ~2500 | Complete feature |

## Type Safety Levels

| Access Level | Classes | Visible From | Example |
|--------------|---------|--------------|---------|
| **Public** | MediaOverlayGenerator | Everywhere | ✅ Can use in WPF |
| **Public** | Settings, Models | Everywhere | ✅ Can use in WPF |
| **Public** | Interfaces (ITranscriptionScript) | Everywhere | ✅ Can use in WPF |
| **Internal** | Processors | Core project only | ❌ Can't use in WPF |
| **Internal** | TranscriptionRoot, etc. | Core project only | ❌ Can't use in WPF |

---

**Key Principle**: Only `MediaOverlayGenerator` and its public types are meant to be used from outside. Everything else is implementation detail.

