# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Boutique is a WPF desktop application for Skyrim Special Edition modding. It syncs armor and clothing stats, keywords,
enchantments, and tempering recipes from master ESPs (like Requiem.esp) to appearance/glam mods, allowing players to use
cosmetic armor mods while maintaining balanced gameplay stats.

**Tech Stack:**

- .NET 8.0 with WPF (Windows Presentation Foundation)
- Mutagen library for reading/writing Bethesda plugin files (.esp, .esm, .esl)
- ReactiveUI for MVVM pattern
- Autofac for dependency injection
- Serilog for logging
- Nifly for reading NIF mesh files
- HelixToolkit for 3D model preview rendering

## Build and Development Commands

### Build

```bash
dotnet build
```

### Run

```bash
dotnet run
```

### Publish (Single-File Executable)

```powershell
pwsh scripts/publish-win.ps1
```

The publish script accepts optional parameters:

- `-Configuration` (default: Release)
- `-Runtime` (default: win-x64)
- `-FrameworkDependent` (switch for framework-dependent builds)

Output goes to `artifacts/publish/<runtime>/`

### Package Management

```bash
dotnet restore                    # Restore dependencies
dotnet list package               # List installed packages
dotnet add package <PackageName>  # Add a package
```

## Architecture

### MVVM Pattern with ReactiveUI

The application follows a strict MVVM (Model-View-ViewModel) architecture:

- **Models** (`Models/`): Data structures representing armor matches, patcher settings, outfit requests, etc.
- **ViewModels** (`ViewModels/`): ReactiveUI-based view models that expose commands and observable properties
- **Views** (`Views/`): XAML-based WPF views with minimal code-behind
- **Services** (`Services/`): Business logic layer that interfaces with Mutagen and handles patching operations

### Dependency Injection

All services are registered in `App.xaml.cs` using Autofac. The DI container resolves:

- Services (singletons): `MutagenService`, `PatchingService`, `MatchingService`, `ArmorPreviewService`,
  `DistributionDiscoveryService`
- ViewModels (singletons): `MainViewModel`, `SettingsViewModel`, `DistributionViewModel`
- Views: `MainWindow`

### Key Services

**MutagenService** (`Services/MutagenService.cs`)

- Initializes the Skyrim game environment using Mutagen
- Creates a LinkCache for resolving FormKeys across the load order
- Loads available plugins and armor records from plugins
- Entry point: `InitializeAsync(dataFolderPath)`

**PatchingService** (`Services/PatchingService.cs`)

- Creates patch ESP files by copying stats from target armors to source armors
- Handles outfit record creation (OTFT records)
- Manages master references in the patch mod header
- Key methods:
    - `CreatePatchAsync()`: Creates armor patches
    - `CreateOrUpdateOutfitsAsync()`: Creates/updates outfit records
    - `CopyArmorStats()`, `CopyKeywords()`, `CopyEnchantment()`: Copy operations

**MatchingService** (`Services/MatchingService.cs`)

- Auto-matches source armors to target armors based on name similarity
- Uses Jaccard similarity with armor-type-aware scoring
- Groups armors by outfit sets for batch operations
- Key method: `AutoMatchArmors(sourceArmors, targetArmors, confidenceThreshold)`

**ArmorPreviewService** (`Services/ArmorPreviewService.cs`)

- Loads NIF mesh files using NiflySharp
- Builds 3D preview scenes for armor visualization
- Resolves mesh paths from ArmorAddon records via the LinkCache

**DistributionDiscoveryService** (`Services/DistributionDiscoveryService.cs`)

- Discovers SPID (Spell Perk Item Distributor) and SkyPatcher distribution INI files
- Parses INI files for outfit distribution management
- Types: `DistributionFileType.Spid` and `DistributionFileType.SkyPatcher`

### Mutagen Integration

Mutagen is the core library for reading and writing Bethesda plugin files. Key concepts:

**Load Order & LinkCache**

- `IGameEnvironment` represents the Skyrim installation and load order
- `ILinkCache` allows resolution of FormKeys to their winning override records
- Always use the LinkCache for resolving FormKeys, never load plugins directly when the cache is available

**Reading Records**

- Use `SkyrimMod.CreateFromBinaryOverlay()` for read-only access (efficient)
- Records are immutable getters (e.g., `IArmorGetter`)
- Access via `mod.Armors`, `mod.Keywords`, `mod.ConstructibleObjects`, etc.

**Writing Records**

- Create a new `SkyrimMod` for the patch
- Use `patchMod.Armors.GetOrAddAsOverride(sourceArmor)` to create override records
- Override records preserve the source FormKey but allow modification
- Call `patchMod.WriteToBinary(outputPath)` to save

**Master References**

- Any FormKey referenced in the patch must have its ModKey in the master list
- Track all referenced ModKeys in a `HashSet<ModKey>`
- Call `EnsureMasters()` before writing the patch

### Main UI Flow

1. **Settings Panel**: User sets Skyrim Data path and output settings
2. **Initialize**: `MutagenService.InitializeAsync()` creates the game environment
3. **Load Plugins**: User selects source and target plugins
4. **Load Armors**: Service loads armor records from the selected plugins
5. **Matching**: Auto-match or manual matching of source armors to target armors
6. **Create Patch**: `PatchingService.CreatePatchAsync()` generates the patch ESP
7. **Distribution/Outfit Creation**: Optional outfit record creation for SPID/SkyPatcher

### Mod Organizer 2 (MO2) Integration

When run from MO2, the app detects the `MODORGANIZER2_EXECUTABLE` environment variable and automatically sets the data
path to MO2's virtual filesystem. This is checked in `SettingsViewModel`.

## Important Notes

### FormKey and ModKey

- `FormKey`: Unique identifier for a record (combines ModKey + local FormID)
- `ModKey`: Identifier for a plugin file (e.g., "Requiem.esp")
- Never hardcode FormKeys; always resolve via LinkCache

### Error Handling

- Mutagen operations can throw; always wrap in try-catch
- Log errors using the injected `ILogger` from Serilog
- User-facing errors should be returned as `(bool success, string message)` tuples

### Threading

- Most Mutagen operations are CPU-bound; wrap in `Task.Run()` for async
- Use `IProgress<T>` to report progress to the UI thread
- WPF bindings must be updated on the UI thread (ReactiveUI handles this)

### Testing Patches

Always test patches in-game:

1. Load order must be: Source mod → Target master → Patch
2. Patch records override source mod's armors
3. Check that stats, keywords, and enchantments are correct in-game

## Common Pitfalls

- **Don't forget to add masters**: Track every referenced ModKey and add to patch header
- **Override records, don't duplicate**: Use `GetOrAddAsOverride()` to modify existing records
- **LinkCache is required**: Initialize MutagenService before accessing armor records
- **Binary overlay vs. full load**: Use overlay for reading, full mod for writing
- **File locks**: The output ESP can be locked by MO2, xEdit, or Skyrim launcher

## CRITICAL: File Editing on Windows

### ⚠️ MANDATORY: Always Use Backslashes on Windows for File Paths

**When using Edit or MultiEdit tools on Windows, you MUST use backslashes (`\`) in file paths, NOT forward
slashes (`/`).**

#### ❌ WRONG - Will cause errors:

```
Edit(file_path: "D:/repos/project/file.tsx", ...)
MultiEdit(file_path: "D:/repos/project/file.tsx", ...)
```

#### ✅ CORRECT - Always works:

```
Edit(file_path: "D:\repos\project\file.tsx", ...)
MultiEdit(file_path: "D:\repos\project\file.tsx", ...)
```
