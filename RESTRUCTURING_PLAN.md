/*
 * PROJECT RESTRUCTURING SUMMARY
 * ==============================
 * 
 * OBJECTIVE:
 * Separate Front-End (WPF) and Back-End (Core) concerns while making it clear that:
 * 1. All utilities in MediaOverlayGeneration are tightly coupled INTERNAL implementation details
 * 2. Only MediaOverlayGenerator should be exposed as the public API
 * 3. Naming should be more explicit about what each component does
 * 
 * NEW STRUCTURE:
 * ==============
 *
 * Epub3MediaOverlays.Core/
 * │
 * ├── Models/                           [PUBLIC DATA MODELS]
 * │   ├── Book.cs                       (No change)
 * │   ├── BookGroupList.cs              (No change)
 * │   └── Converters.cs                 (No change)
 * │
 * ├── Services/                         [PUBLIC SERVICES]
 * │   └── SettingsManager.cs            (No change)
 * │
 * ├── MediaOverlayGeneration/           [NEW: Core business logic - TIGHTLY COUPLED]
 * │   │
 * │   ├── MediaOverlayGenerator.cs      [MAIN PUBLIC ENTRY POINT]
 * │   │   └── Contains: GenerateEpub() orchestration
 * │   │
 * │   ├── MediaOverlayGeneratorSettings.cs
 * │   │   └── Contains: MediaOverlayGeneratorSettings (extracted from GenerateEpubUtil)
 * │   │
 * │   ├── Models/                       [Data models used only within this feature]
 * │   │   ├── WordSegment.cs            (Extracted, renamed from WordSegment in GenerateEpubUtil)
 * │   │   ├── AudioFragment.cs          (Extracted, renamed from Fragment)
 * │   │   ├── AudioLinkGap.cs           (Extracted)
 * │   │   └── HtmlTextSegment.cs        (Extracted)
 * │   │
 * │   └── Internal/                     [PRIVATE IMPLEMENTATION - NOT FOR EXTERNAL USE]
 * │       ├── EpubProcessor.cs          (Renamed from EpubUtility)
 * │       │   └── Handles: EPUB loading, HTML extraction, SMIL generation, EPUB rebuild
 * │       │
 * │       ├── TranscriptProcessor.cs    (Renamed from TranscriptClass)
 * │       │   └── Handles: Transcript deserialization, fragment extraction
 * │       │
 * │       ├── AlignmentProcessor.cs     (Renamed from AlingnerNew)
 * │       │   └── Handles: Audio-to-text alignment using fuzzy matching
 * │       │
 * │       ├── AlignmentConfiguration.cs (Moved and kept in Internal)
 * │       │   └── Handles: Tunable parameters for alignment algorithm
 * │       │
 * │       └── SmilGenerator.cs          (Renamed from EpubSmilLib)
 * │           └── Handles: SMIL file generation and XML manipulation
 * │
 * └── Utilities/                        [DEPRECATED - Kept for backward compatibility]
 *     ├── GenerateEpubUtil.cs           (Facade to MediaOverlayGenerator)
 *     ├── EpubUtility.cs                (Facade to EpubProcessor)
 *     ├── TranscriptClass.cs            (Facade to TranscriptProcessor)
 *     ├── Alingner.cs                   (Facade to AlignmentProcessor)
 *     ├── EpubSmilLib.cs                (Facade to SmilGenerator)
 *     └── AlingnerConfiguration.cs      (Facade to AlignmentConfiguration)
 * 
 * 
 * NAMING RATIONALE:
 * =================
 *
 * OLD NAME                    NEW NAME                   REASON
 * ─────────────────────────────────────────────────────────────────────
 * GenerateEpubUtil           MediaOverlayGenerator      More explicit about creating media overlays
 * EpubUtility                EpubProcessor              Emphasizes it PROCESSES EPUB content
 * TranscriptClass            TranscriptProcessor       Emphasizes it PROCESSES transcriptions
 * AlingnerNew                AlignmentProcessor        More explicit - creates alignments
 * EpubSmilLib                SmilGenerator             Emphasizes it GENERATES SMIL
 * Fragment                   AudioFragment             Clarifies it's audio data
 * Root                        TranscriptionRoot        Clarifies the JSON structure source
 * 
 * 
 * COUPLING VISIBILITY:
 * ====================
 *
 * BEFORE: Hard to see that all Utils were tightly coupled
 *   - Files scattered in Utilities/
 *   - Circular imports and static usings made coupling invisible
 *   - Developers might try to use EpubUtility independently
 *
 * AFTER: Clear hierarchy and intent
 *   - All in MediaOverlayGeneration/ folder (obvious single feature)
 *   - Internal/ subfolder makes private implementation clear
 *   - Only MediaOverlayGenerator.cs is public API
 *   - Other classes are for MediaOverlayGenerator's use only
 *
 * 
 * FRONT-END INTEGRATION:
 * ======================
 *
 * MainWindow.xaml.cs currently uses:
 *   using Epub3MediaOverlays.Core.Utilities;
 *   ...
 *   GenerateEpubUtil generator = new GenerateEpubUtil(settings);
 *   generator.GenerateEpub(data);
 *
 * CHANGE TO:
 *   using Epub3MediaOverlays.Core.MediaOverlayGeneration;
 *   ...
 *   MediaOverlayGenerator generator = new MediaOverlayGenerator(settings);
 *   generator.GenerateEpub(data);
 *
 * The old Utilities namespace will provide facades for backward compatibility during transition.
 *
 *
 * MIGRATION PHASES:
 * =================
 *
 * PHASE 1 (Done):         Create new folder structure with new files
 * PHASE 2 (Recommended):  Update MainWindow.xaml.cs to use new namespace
 * PHASE 3 (Optional):     Keep old files as facades for 1-2 releases, then remove
 * PHASE 4 (Final):        Delete old Utilities files after backward compat period
 *
 */
