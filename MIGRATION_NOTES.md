# Migration Notes: Old Structure → New Structure

## File Mappings

### Core Project - Backend Logic

**Old Location** → **New Location**

#### Models
- `Readaloud-Epub3-Creator/Classes/Book.cs` → `Epub3MediaOverlays.Core/Models/Book.cs`
- `Readaloud-Epub3-Creator/Classes/BookgroupList.cs` → `Epub3MediaOverlays.Core/Models/BookgroupList.cs`
- `Readaloud-Epub3-Creator/Classes/Converters.cs` → `Epub3MediaOverlays.Core/Models/Converters.cs`

#### Services
- `Readaloud-Epub3-Creator/Classes/SettingsManager.cs` → `Epub3MediaOverlays.Core/Services/SettingsManager.cs`

#### Utilities (EPUB3 Processing)
- `Readaloud-Epub3-Creator/AlingnerUtil/Alingner.cs` → `Epub3MediaOverlays.Core/Utilities/Alingner.cs`
- `Readaloud-Epub3-Creator/AlingnerUtil/AlingnerConfiguration.cs` → `Epub3MediaOverlays.Core/Utilities/AlingnerConfiguration.cs`
- `Readaloud-Epub3-Creator/AlingnerUtil/AlingnerNew.cs` → `Epub3MediaOverlays.Core/Utilities/AlingnerNew.cs`
- `Readaloud-Epub3-Creator/AlingnerUtil/EpubSmilLib.cs` → `Epub3MediaOverlays.Core/Utilities/EpubSmilLib.cs`
- `Readaloud-Epub3-Creator/AlingnerUtil/EpubUtility.cs` → `Epub3MediaOverlays.Core/Utilities/EpubUtility.cs`
- `Readaloud-Epub3-Creator/AlingnerUtil/GenerateEpubUtil.cs` → `Epub3MediaOverlays.Core/Utilities/GenerateEpubUtil.cs`
- `Readaloud-Epub3-Creator/AlingnerUtil/TranscriptClass.cs` → `Epub3MediaOverlays.Core/Utilities/TranscriptClass.cs`

### WPF Project - Frontend UI

**Old Location** → **New Location**

#### Main Application
- `Readaloud-Epub3-Creator/App.xaml` → `Epub3MediaOverlays.Wpf/App.xaml`
- `Readaloud-Epub3-Creator/App.xaml.cs` → `Epub3MediaOverlays.Wpf/App.xaml.cs`

#### Main Windows
- `Readaloud-Epub3-Creator/MainWindow.xaml` → `Epub3MediaOverlays.Wpf/Views/MainWindow.xaml`
- `Readaloud-Epub3-Creator/MainWindow.xaml.cs` → `Epub3MediaOverlays.Wpf/Views/MainWindow.xaml.cs`
- `Readaloud-Epub3-Creator/LogViewerWindow.xaml` → `Epub3MediaOverlays.Wpf/Views/LogViewerWindow.xaml`
- `Readaloud-Epub3-Creator/LogViewerWindow.xaml.cs` → `Epub3MediaOverlays.Wpf/Views/LogViewerWindow.xaml.cs`
- `Readaloud-Epub3-Creator/SettingsWindow.xaml` → `Epub3MediaOverlays.Wpf/Views/SettingsWindow.xaml`
- `Readaloud-Epub3-Creator/SettingsWindow.xaml.cs` → `Epub3MediaOverlays.Wpf/Views/SettingsWindow.xaml.cs`
- `Readaloud-Epub3-Creator/ReprocessOptionsWindow.xaml` → `Epub3MediaOverlays.Wpf/Views/ReprocessOptionsWindow.xaml`
- `Readaloud-Epub3-Creator/ReprocessOptionsWindow.xaml.cs` → `Epub3MediaOverlays.Wpf/Views/ReprocessOptionsWindow.xaml.cs`

#### Dialog Windows
- `Readaloud-Epub3-Creator/Dialogs/CreateBookWindow.xaml` → `Epub3MediaOverlays.Wpf/Dialogs/CreateBookWindow.xaml`
- `Readaloud-Epub3-Creator/Dialogs/CreateBookWindow.xaml.cs` → `Epub3MediaOverlays.Wpf/Dialogs/CreateBookWindow.xaml.cs`
- `Readaloud-Epub3-Creator/Dialogs/CreateGroupWindow.xaml` → `Epub3MediaOverlays.Wpf/Dialogs/CreateGroupWindow.xaml`
- `Readaloud-Epub3-Creator/Dialogs/CreateGroupWindow.xaml.cs` → `Epub3MediaOverlays.Wpf/Dialogs/CreateGroupWindow.xaml.cs`
- `Readaloud-Epub3-Creator/Dialogs/MoveBooksWindow.xaml` → `Epub3MediaOverlays.Wpf/Dialogs/MoveBooksWindow.xaml`
- `Readaloud-Epub3-Creator/Dialogs/MoveBooksWindow.xaml.cs` → `Epub3MediaOverlays.Wpf/Dialogs/MoveBooksWindow.xaml.cs`

#### Other
- `Readaloud-Epub3-Creator/AssemblyInfo.cs` → `Epub3MediaOverlays.Wpf/AssemblyInfo.cs`

### Solution Files
- `epub-to-epub3.sln` → `DotNet-Epub3-MediaOverlays-Creator.sln` (New, replaces old)

### New Project Files (Created)
- `Epub3MediaOverlays.Core/Epub3MediaOverlays.Core.csproj` (Backend class library)
- `Epub3MediaOverlays.Wpf/Epub3MediaOverlays.Wpf.csproj` (Frontend WPF application)

## Namespace Changes

### Old
All code used: `namespace Readaloud_Epub3_Creator`

### New

**Core Project Namespaces:**
- `Epub3MediaOverlays.Core.Models` - Domain models
- `Epub3MediaOverlays.Core.Services` - Business services
- `Epub3MediaOverlays.Core.Utilities` - EPUB3 processing utilities

**WPF Project Namespace:**
- `Epub3MediaOverlays.Wpf` - All WPF UI code (with sub-namespaces for Views, Dialogs, Classes)

## Dependencies

### Core Project (`Epub3MediaOverlays.Core`)
- EpubSharp 1.1.5
- F23.StringSimilarity 7.0.0
- FuzzySearch.Net 1.1.0
- FuzzySharp 2.0.2
- HtmlAgilityPack 1.12.1
- Microsoft.Extensions.DependencyInjection 9.0.6
- Microsoft.Extensions.Options 9.0.6
- Newtonsoft.Json 13.0.3

### WPF Project (`Epub3MediaOverlays.Wpf`)
- All Core project dependencies (via project reference)
- CommunityToolkit.Mvvm 8.4.2
- ModernWpfUI 0.9.6

## Breaking Changes

None to functionality! The reorganization is purely structural:
- Business logic remains unchanged
- UI logic remains unchanged
- All algorithms and processing are identical

However, **if you have external code** that references the old project:
- Update namespace from `Readaloud_Epub3_Creator` to appropriate new namespaces
- Update assembly references from `Readaloud-Epub3-Creator` to `Epub3MediaOverlays.Core`

## Building After Migration

### Command Line
```bash
# Full build
dotnet build DotNet-Epub3-MediaOverlays-Creator.sln

# Or open in Visual Studio and Rebuild Solution
```

### Visual Studio
1. Open `DotNet-Epub3-MediaOverlays-Creator.sln`
2. Build → Rebuild Solution
3. Set `Epub3MediaOverlays.Wpf` as startup project
4. Press F5 to run

## Git Considerations

The old project folder `Readaloud-Epub3-Creator` still exists in the repository. To clean up:

```bash
# After verifying everything works with new structure
git rm -r Readaloud-Epub3-Creator/
git commit -m "Remove old monolithic project folder - replaced by split Core/Wpf architecture"
```

Or keep both temporarily during transition period if needed.
