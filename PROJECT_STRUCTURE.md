# .NET Epub3 Media Overlays Creator - Project Structure

## Overview

This solution has been reorganized from a monolithic project into a clean two-tier architecture:

```
DotNet-Epub3-MediaOverlays-Creator.sln
├── Epub3MediaOverlays.Core/          [Backend - Business Logic]
│   ├── Models/                        [Data models and domain objects]
│   │   ├── Book.cs
│   │   ├── BookgroupList.cs
│   │   └── Converters.cs
│   ├── Services/                      [Service layer]
│   │   └── SettingsManager.cs
│   └── Utilities/                     [EPUB3 processing logic]
│       ├── Alingner.cs
│       ├── AlingnerConfiguration.cs
│       ├── AlingnerNew.cs
│       ├── EpubSmilLib.cs
│       ├── EpubUtility.cs
│       ├── GenerateEpubUtil.cs
│       └── TranscriptClass.cs
└── Epub3MediaOverlays.Wpf/           [Frontend - WPF UI]
    ├── Views/                         [Main application windows]
    │   ├── MainWindow.xaml
    │   ├── LogViewerWindow.xaml
    │   ├── SettingsWindow.xaml
    │   └── ReprocessOptionsWindow.xaml
    ├── Dialogs/                       [Dialog windows]
    │   ├── CreateBookWindow.xaml
    │   ├── CreateGroupWindow.xaml
    │   └── MoveBooksWindow.xaml
    ├── Classes/                       [UI utilities]
    ├── App.xaml
    └── App.xaml.cs
```

## Architecture Benefits

### Clear Separation of Concerns
- **Core Project**: Pure business logic, no UI dependencies
- **WPF Project**: User interface only, depends on Core

### Frontend Replaceability
The Core project has no dependencies on any UI framework, making it easy to:
- Replace WPF with WinUI 3
- Create a Web UI (ASP.NET Core)
- Build a cross-platform UI (MAUI)
- Create a CLI tool

Simply create a new project that references `Epub3MediaOverlays.Core`.

### Namespace Organization
- **`Epub3MediaOverlays.Core.Models`** - Domain objects (Book, BookgroupList, etc.)
- **`Epub3MediaOverlays.Core.Services`** - Service layer (SettingsManager, etc.)
- **`Epub3MediaOverlays.Core.Utilities`** - Processing utilities (Alingner, EpubUtility, etc.)
- **`Epub3MediaOverlays.Wpf`** - All WPF-specific code

## Previously Named
- Solution: ~~`epub-to-epub3.sln`~~ → `DotNet-Epub3-MediaOverlays-Creator.sln`
- Main Project: ~~`Readaloud-Epub3-Creator`~~ → `Epub3MediaOverlays.Wpf` (Frontend) + `Epub3MediaOverlays.Core` (Backend)

## Why This Reorganization?

1. **Maintainability**: Backend logic is isolated from UI concerns
2. **Testability**: Core functionality can be tested without WPF dependencies
3. **Extensibility**: Easy to add new frontends or alternative implementations
4. **Clarity**: Explicit folder structure communicates the architecture
5. **Reusability**: Other projects can use just the Core library

## Building the Solution

```bash
# Build entire solution
dotnet build DotNet-Epub3-MediaOverlays-Creator.sln

# Build only Core (for embedding in other projects)
dotnet build Epub3MediaOverlays.Core/Epub3MediaOverlays.Core.csproj

# Build only WPF (requires Core)
dotnet build Epub3MediaOverlays.Wpf/Epub3MediaOverlays.Wpf.csproj
```

## Example: Creating an Alternative Frontend

To create a new WinUI 3 frontend:

```
├── Epub3MediaOverlays.Core/
├── Epub3MediaOverlays.Wpf/          [Keep existing WPF]
└── Epub3MediaOverlays.WinUI/        [New WinUI 3 frontend]
    ├── MainWindow.xaml
    ├── MainWindow.xaml.cs
    └── Epub3MediaOverlays.WinUI.csproj (references Core)
```

Both frontends would share the exact same Core project without any modifications.
