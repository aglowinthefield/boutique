# Boutique

A WPF application for Skyrim Special Edition modding that syncs armor and clothing stats, keywords, enchantments, and tempering recipes from master ESPs (like Requiem.esp, FTweaks, etc.) to appearance/glam mods.

## Features

- **Mod Organizer 2 Integration**: Auto-detects data path when run from MO2!
- **Auto-Matching**: Automatically matches source armor pieces to target armors based on name similarity
- **Manual Override**: Manually select target armors for each source armor
- **Batch Processing**: Select entire outfit sets and patch them all at once
- **Comprehensive Syncing**: Copies:
  - Stats (Armor Rating, Weight, Value)
  - Keywords (for categorization and perk compatibility)
  - Enchantments (magical effects)
  - Tempering recipes (crafting improvements)
- **User-Friendly UI**: Three-panel interface with progress tracking

## Requirements

- Windows 10/11
- .NET 8.0 Runtime
- Skyrim Special Edition
- Mods to patch (e.g., armor appearance mods from Nexus)

## Installation

1. Download the latest release from the Releases page
2. Extract to a folder of your choice
3. Run `Boutique.exe`

## Building from Source

1. Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. Clone this repository
3. Open terminal in the project folder
4. Run: `dotnet build`
5. Run: `dotnet run`

Or open `Boutique.csproj` in Visual Studio 2022 and build.

## Publishing

Run the PowerShell script to produce a ready-to-ship single-file EXE:

```powershell
pwsh scripts/publish-win.ps1
```

Adjust `-Configuration`, `-Runtime`, or add `-FrameworkDependent` if you need a framework-dependent build. Outputs land in `artifacts/publish/<runtime>/`.

## Running from Mod Organizer 2 (Recommended!)

The easiest way to use Boutique is to run it directly from Mod Organizer 2. It will automatically detect your Skyrim data path and all loaded mods!

### Setup in MO2:

1. In Mod Organizer 2, click the **gears icon** (⚙️) next to the "Run" button
2. Click the **+** button to add a new executable
3. Fill in the following:
   - **Title**: `Boutique`
   - **Binary**: Browse to `Boutique.exe`
   - Leave other fields as default
4. Click **OK**

### To Use:

1. Select "Boutique" from the dropdown in MO2
2. Click **Run**
3. The app will automatically detect your Skyrim data path (you'll see "Detected from Mod Organizer 2" in green)
4. Click **Initialize** and proceed with patching!

**Benefits of running from MO2:**
- ✅ Automatically detects the correct Skyrim data path
- ✅ Sees all plugins as they appear in your load order
- ✅ Patch ESP is automatically placed in your MO2 overwrite folder
- ✅ No manual path configuration needed!

## Usage (Standalone)

If you prefer to run the tool standalone without MO2, follow these steps:

### Step 1: Configure Settings

1. **Skyrim Data Path**: Click "Auto-Detect" to automatically find your Skyrim SE Data folder, or use "Browse..." to manually select it
2. **Output Path**: Set where the patch ESP will be created (typically your Skyrim Data folder)
3. **Patch File Name**: Name your patch (default: `GlamPatch.esp`)
4. Click **Initialize** to load the Mutagen environment

### Step 2: Select Plugins

1. **Source Plugin**: Select the armor mod you want to patch (e.g., `ClericOutfitSE.esp`)
2. Click **Load Source Armors**
3. **Target Plugin**: Select the master ESP with the stats you want (e.g., `Requiem.esp`)
4. Click **Load Target Armors**

### Step 3: Match Armors

**Option A: Auto-Match**
1. Adjust the **Auto-Match Threshold** slider (60% recommended)
2. Click **Auto-Match**
3. Review the matches in the Matching panel

**Option B: Manual Match**
1. In the Matching panel, click the Target Armor dropdown for each source armor
2. Select the appropriate target armor from the list

**Option C: Batch Outfit Selection**
1. In the Source Armors panel, check the boxes for armors in the same outfit
2. Click **Select Outfit** to automatically select all related pieces
3. Then use Auto-Match or manual matching

### Step 4: Create Patch

1. Review all matches to ensure correct pairings
2. Click **Create Patch**
3. Wait for the progress bar to complete
4. Your patch ESP will be created at the specified location

### Step 5: Activate in Mod Manager

1. Refresh your mod manager (MO2, Vortex, etc.)
2. Activate `GlamPatch.esp` (or your chosen name)
3. Ensure it loads **after** both:
   - The source armor mod
   - The target master ESP (e.g., Requiem.esp)
4. Launch Skyrim and enjoy!

## Example Use Case

**Scenario**: You want to use the [Cleric Outfit SE](https://www.nexusmods.com/skyrimspecialedition/mods/163298) appearance with Requiem's "Leather Boots" stats.

1. Load `ClericOutfitSE.esp` as Source Plugin
2. Load `Requiem.esp` as Target Plugin
3. Use Auto-Match (it should match boots to boots, etc.)
4. If auto-match isn't perfect, manually select "Leather Boots" from Requiem for the Cleric boots
5. Select all pieces of the Cleric outfit
6. Click Create Patch
7. Load `GlamPatch.esp` after both ClericOutfitSE.esp and Requiem.esp

Now the Cleric Outfit will have Requiem's stats, keywords, and balance!

## How It Works

The patcher uses [Mutagen](https://github.com/Mutagen-Modding/Mutagen), a C# library for manipulating Bethesda plugin files.

**Process**:
1. Reads armor records from source mod (preserves appearance/models)
2. Reads armor records from target master ESP (extracts stats/keywords)
3. Creates override records that combine:
   - Source armor's appearance and models
   - Target armor's stats, keywords, and enchantments
4. Finds and copies tempering recipes (COBJ records) from target to source
5. Writes a new ESP patch file

## Troubleshooting

### "No plugins found"
- Ensure Skyrim Data Path is correct
- Click "Auto-Detect" or manually browse to the Data folder
- The Data folder should contain Skyrim.esm and other plugin files

### "Error loading armors"
- Ensure the selected plugin is a valid Skyrim SE plugin
- Try reloading or restarting the application
- Some plugins may be encrypted or have non-standard formats

### "Auto-match found no matches"
- Lower the Auto-Match Threshold slider
- Try manual matching instead
- Ensure target plugin contains similar armor types

### Patch doesn't work in game
- Ensure the patch ESP is activated in your mod manager
- Ensure load order is correct: Source mod → Master ESP → Patch
- Check for conflicts with other mods that modify the same armors

## License

This project uses the following libraries:
- [Mutagen](https://github.com/Mutagen-Modding/Mutagen) - MIT License
- [ReactiveUI](https://github.com/reactiveui/ReactiveUI) - MIT License
- [Autofac](https://github.com/autofac/Autofac) - MIT License

## Credits

- **Mutagen** by Noggog and contributors
- **Requiem** by The Requiem Dungeon Masters
- Skyrim modding community

## Support

For issues, questions, or feature requests, please open an issue on the GitHub repository.

## Disclaimer

This tool modifies Skyrim plugin files. Always backup your Data folder before using. The author is not responsible for any issues that may arise from using this tool.
