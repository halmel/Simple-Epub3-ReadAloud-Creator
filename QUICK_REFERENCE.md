# Quick Reference Guide

## Getting Started

### Open the New Solution
```
File → Open Project/Solution → Select: DotNet-Epub3-MediaOverlays-Creator.sln
```

### Build
```bash
dotnet build DotNet-Epub3-MediaOverlays-Creator.sln
```

### Run
```bash
# Visual Studio: Press F5 or use Debug menu
# Command Line:
dotnet run --project Epub3MediaOverlays.Wpf/Epub3MediaOverlays.Wpf.csproj
```

---

## Project Structure at a Glance

```
Solution: DotNet-Epub3-MediaOverlays-Creator.sln
│
├─ Epub3MediaOverlays.Core/              (Backend - Business Logic)
│  ├─ Epub3MediaOverlays.Core.csproj
│  ├─ Models/
│  │  ├─ Book.cs
│  │  ├─ BookgroupList.cs
│  │  └─ Converters.cs
│  ├─ Services/
│  │  └─ SettingsManager.cs
│  └─ Utilities/
│     ├─ Alingner.cs
│     ├─ AlingnerConfiguration.cs
│     ├─ AlingnerNew.cs
│     ├─ EpubSmilLib.cs
│     ├─ EpubUtility.cs
│     ├─ GenerateEpubUtil.cs
│     └─ TranscriptClass.cs
│
└─ Epub3MediaOverlays.Wpf/               (Frontend - UI)
   ├─ Epub3MediaOverlays.Wpf.csproj
   ├─ App.xaml & App.xaml.cs
   ├─ Views/
   │  ├─ MainWindow.xaml
   │  ├─ LogViewerWindow.xaml
   │  ├─ SettingsWindow.xaml
   │  └─ ReprocessOptionsWindow.xaml
   ├─ Dialogs/
   │  ├─ CreateBookWindow.xaml
   │  ├─ CreateGroupWindow.xaml
   │  └─ MoveBooksWindow.xaml
   └─ Classes/
```

---

## Namespace Reference

### Using the Core Library

In any WPF window or class:

```csharp
// For data models
using Epub3MediaOverlays.Core.Models;
// Access: Book, BookgroupList, Converters

// For services
using Epub3MediaOverlays.Core.Services;
// Access: SettingsManager

// For EPUB3 utilities
using Epub3MediaOverlays.Core.Utilities;
// Access: Alingner, EpubUtility, EpubSmilLib, etc.
```

### Class Locations

| Class | Namespace | Location |
|-------|-----------|----------|
| `Book` | `Core.Models` | `Epub3MediaOverlays.Core/Models/Book.cs` |
| `BookgroupList` | `Core.Models` | `Epub3MediaOverlays.Core/Models/BookgroupList.cs` |
| `SettingsManager` | `Core.Services` | `Epub3MediaOverlays.Core/Services/SettingsManager.cs` |
| `Alingner` | `Core.Utilities` | `Epub3MediaOverlays.Core/Utilities/Alingner.cs` |
| `EpubUtility` | `Core.Utilities` | `Epub3MediaOverlays.Core/Utilities/EpubUtility.cs` |
| `EpubSmilLib` | `Core.Utilities` | `Epub3MediaOverlays.Core/Utilities/EpubSmilLib.cs` |
| `TranscriptClass` | `Core.Utilities` | `Epub3MediaOverlays.Core/Utilities/TranscriptClass.cs` |

---

## Adding New Features

### Adding a New Model Class
1. Create file in: `Epub3MediaOverlays.Core/Models/`
2. Namespace: `namespace Epub3MediaOverlays.Core.Models;`
3. Use it in WPF with: `using Epub3MediaOverlays.Core.Models;`

### Adding a New Service
1. Create file in: `Epub3MediaOverlays.Core/Services/`
2. Namespace: `namespace Epub3MediaOverlays.Core.Services;`
3. Use it in WPF with: `using Epub3MediaOverlays.Core.Services;`

### Adding a New UI Window
1. Create XAML in: `Epub3MediaOverlays.Wpf/Views/` or `Epub3MediaOverlays.Wpf/Dialogs/`
2. Namespace: `namespace Epub3MediaOverlays.Wpf;`
3. Use Core classes as needed

---

## Common Tasks

### Finding a Class
Use the class location table above or search by namespace:

```csharp
// To use Book class:
using Epub3MediaOverlays.Core.Models;
var book = new Book();

// To use EPUB utilities:
using Epub3MediaOverlays.Core.Utilities;
var utility = new EpubUtility();
```

### Adding to Solution Configurations
The solution supports Debug and Release builds for both projects.

### Running Unit Tests (if added)
```bash
dotnet test DotNet-Epub3-MediaOverlays-Creator.sln
```

---

## Dependencies

### Core Project
- EpubSharp.dll
- FuzzySharp
- HtmlAgilityPack
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Options
- Newtonsoft.Json

### WPF Project
- All Core dependencies (via project reference)
- CommunityToolkit.Mvvm
- ModernWpfUI

---

## Documentation Files

| File | Purpose |
|------|---------|
| `PROJECT_STRUCTURE.md` | Detailed architecture explanation |
| `MIGRATION_NOTES.md` | Old-to-new file mapping and changes |
| `RESTRUCTURING_COMPLETE.md` | Summary of completed work |
| `QUICK_REFERENCE.md` | This file - quick lookup guide |

---

## Need to Replace the Frontend?

The architecture is designed to support this!

### Example: Create a WinUI 3 Frontend

```
1. New Project: Epub3MediaOverlays.WinUI
2. Add reference: Project → Epub3MediaOverlays.Core
3. Build your UI using:
   - using Epub3MediaOverlays.Core.Models;
   - using Epub3MediaOverlays.Core.Services;
   - using Epub3MediaOverlays.Core.Utilities;
4. No changes needed to Core - same business logic!
```

---

## Troubleshooting

**Problem:** "Type or namespace 'X' not found"
- **Solution:** Add the correct `using` statement from the table above

**Problem:** Build fails
- **Solution:** Ensure both projects are in the solution and project references are correct

**Problem:** Can't find old code
- **Solution:** Use the MIGRATION_NOTES.md to find where old files are now located

**Problem:** Application won't start
- **Solution:** Ensure `Epub3MediaOverlays.Wpf` is set as the startup project
