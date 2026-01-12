# Boutique

Boutique helps you create and manage outfit distribution files for SPID and SkyPatcher, browse NPCs and their outfit assignments, create custom outfit records, and sync armor stats between mods.

## Features

### Distribution Management

-   **Browse Distribution Files**: Scan and view all SPID (`*_DISTR.ini`) and SkyPatcher distribution files in your Data folder
-   **Preview Distributions**: See which outfits are being distributed and preview them in 3D
-   **Create Distribution Entries**: Build new distribution rules with an intuitive UI
-   **Conflict Detection**: Automatically detects when your distributions conflict with existing files and suggests Z-prefixed filenames for proper load order priority
-   **Dual Format Support**: Generate both SPID and SkyPatcher syntax from the same filters

### NPC Browser

-   **View All NPCs**: Browse every NPC in your load order with their current outfit assignments
-   **See Distribution Sources**: For each NPC, see which distribution files are affecting their outfit
-   **Conflict Highlighting**: Easily spot NPCs with multiple conflicting outfit distributions
-   **Advanced Filtering**: Filter NPCs by gender, unique status, faction, race, keyword, and more
-   **Copy Filters**: Build a filter in the NPC browser and copy it directly to your distribution entries
-   **Live Syntax Preview**: See the generated SPID and SkyPatcher syntax as you adjust filters

### Outfit Browser

-   **Browse All Outfits**: View every outfit record (OTFT) in your load order
-   **NPC Assignments**: Select an outfit to see which NPCs have it assigned (via ESP or distribution)
-   **Hide Vanilla**: Filter out base game outfits to focus on modded content
-   **3D Preview**: Preview any outfit's armor pieces in a 3D viewer

### Outfit Creator

-   **Create OTFT Records**: Build new outfit records by selecting armor pieces from any plugin
-   **Drag & Drop**: Drag armors between plugins and outfit drafts
-   **Slot Conflict Detection**: Prevents adding armor pieces with conflicting body slots
-   **Edit Existing Outfits**: Load and modify outfits from your output plugin
-   **3D Preview**: Preview outfit drafts before saving

### Armor Patching

-   **Sync Armor Stats**: Copy stats, keywords, enchantments, and tempering recipes from one armor to another
-   **Glam Mode**: Zero out armor rating for purely cosmetic armors
-   **Batch Processing**: Map multiple armors at once for efficient patching

## Requirements

-   Windows 10/11
-   [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (download the "Desktop Runtime" for Windows x64)
-   Skyrim Special Edition
-   For distribution features: SPID or SkyPatcher installed

## Installation

1. Download the latest release from the Releases page
2. Extract to a folder of your choice
3. Run `Boutique.exe`

## Quick Start

### Creating Outfit Distributions

1. **Initialize**: Set your Skyrim Data path in Settings and click Initialize
2. **Go to Distribution tab**: Select the "Create" sub-tab
3. **Add Entry**: Click "Add Entry" to create a new distribution rule
4. **Select Outfit**: Choose the outfit you want to distribute from the dropdown
5. **Add Filters**: Use the NPCs, Factions, Keywords, or Races tabs to add targeting filters
6. **Save**: Choose a filename and click "Save File"

### Using the NPC Browser to Build Filters

1. Go to **Distribution > NPCs** tab
2. Use the filter dropdowns (Gender, Unique, Faction, Race, Keyword) to narrow down NPCs
3. The filtered list shows exactly which NPCs will be affected
4. Click **Copy Filter** to save your current filter criteria
5. Switch to the **Create** tab and click **Paste Filter** to apply it to an entry

### Creating Custom Outfits

1. Go to the **Outfit Creator** tab
2. Select a source plugin containing the armors you want
3. Select armor pieces from the list (multi-select supported)
4. Click **Create Outfit** and enter a name
5. Optionally add more pieces by dragging from the armor list
6. Click **Save Outfits** to write the OTFT records to your output plugin

### Patching Armor Stats

1. Go to the **Armor Patch** tab
2. Select your cosmetic armor mod as the Source Plugin
3. Select the mod with desired stats as the Target Plugin (e.g., Requiem.esp)
4. Map source armors to target armors using "Map Selection"
5. Click **Create Patch** to generate the patch ESP

## Building from Source

1. Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. Clone this repository
3. Open terminal in the project folder
4. Run: `dotnet build`
5. Run: `dotnet run`

Or open `Boutique.csproj` in Visual Studio 2022 / Rider and build.

## Publishing

Run the PowerShell script to produce a ready-to-ship single-file EXE:

```powershell
pwsh scripts/publish-win.ps1
```

This creates a framework-dependent build (~80MB) that requires .NET 8 Desktop Runtime.

Add `-SelfContained` for a larger (~220MB) standalone build that bundles the .NET runtime:

```powershell
pwsh scripts/publish-win.ps1 -SelfContained
```

Outputs land in `artifacts/publish/<runtime>/`.

## Distribution File Formats

### SPID (Spell Perk Item Distributor)

SPID uses `*_DISTR.ini` files in your Data folder. Example syntax:

```ini
Outfit = MyCustomOutfit|ActorTypeNPC|BanditFaction|NONE|F|NONE|100
```

This distributes `MyCustomOutfit` to female NPCs with the `ActorTypeNPC` keyword who are in `BanditFaction`.

### SkyPatcher

SkyPatcher uses INI files typically in `Data/skse/plugins/SkyPatcher/`. Example syntax:

```ini
filterByFactions=Skyrim.esm|0001BCC0:filterByGender=female:outfitDefault=MyMod.esp|FE000D65
```

Boutique can generate both formats from the same filter configuration.

## License

This project uses the following libraries:

-   [Mutagen](https://github.com/Mutagen-Modding/Mutagen) - GPL3 License
-   [ReactiveUI](https://github.com/reactiveui/ReactiveUI) - MIT License
-   [Autofac](https://github.com/autofac/Autofac) - MIT License
-   [NiflySharp](https://github.com/ousnius/NiflySharp) - For NIF mesh reading
-   [HelixToolkit](https://github.com/helix-toolkit/helix-toolkit) - MIT License (3D rendering)

## Credits

-   **Mutagen** by Noggog and contributors
-   **SPID** by powerofthree
-   **SkyPatcher** by SkyPatcher team
-   Skyrim modding community

## Disclaimer

This tool creates and modifies Skyrim plugin and INI files. Always backup your Data folder before using. The author is not responsible for any issues that may arise from using this tool.
