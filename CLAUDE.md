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

### ReactiveUI Property Patterns

This project uses **ReactiveUI.Fody** and **C# 13 preview features** to minimize boilerplate. Always prefer these patterns:

#### Simple Reactive Properties → Use `[Reactive]`

For properties that just need change notification with no custom logic:

```csharp
// ✅ PREFERRED - Fody generates the backing field and RaiseAndSetIfChanged
[Reactive] public bool IsLoading { get; set; }
[Reactive] public string StatusMessage { get; set; } = "Ready";
```

```csharp
// ❌ AVOID - Verbose boilerplate
private bool _isLoading;
public bool IsLoading
{
    get => _isLoading;
    set => this.RaiseAndSetIfChanged(ref _isLoading, value);
}
```

#### Properties with Custom Logic → Use `field` keyword

For properties that need side effects or validation, use the C# 13 `field` keyword instead of explicit backing fields:

```csharp
// ✅ PREFERRED - No explicit backing field needed
public string? SelectedPlugin
{
    get => field;
    set
    {
        if (string.Equals(value, field)) return;
        this.RaiseAndSetIfChanged(ref field, value);
        _lastLoadedPlugin = null;  // Side effect
        _ = LoadPluginAsync(value);
    }
}
```

```csharp
// ❌ AVOID - Unnecessary backing field declaration
private string? _selectedPlugin;
public string? SelectedPlugin
{
    get => _selectedPlugin;
    set
    {
        if (string.Equals(value, _selectedPlugin)) return;
        this.RaiseAndSetIfChanged(ref _selectedPlugin, value);
        // ...
    }
}
```

#### Derived/Computed Properties → Use `WhenAnyValue`

For properties that depend on other properties, set up subscriptions in the constructor:

```csharp
// In constructor:
this.WhenAnyValue(x => x.IsPatching, x => x.IsCreatingOutfits)
    .Subscribe(_ => this.RaisePropertyChanged(nameof(IsProgressActive)));

// Property:
public bool IsProgressActive => IsPatching || IsCreatingOutfits;
```

#### Null Checks → Use Null-Conditional Operators

Prefer `?.` over explicit null checks:

```csharp
// ✅ PREFERRED
field?.IsSelected = false;
value?.DoSomething();

// ❌ AVOID
if (field != null)
{
    field.IsSelected = false;
}
```

**Note:** The project uses `<LangVersion>preview</LangVersion>` in the .csproj to enable the `field` keyword.

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

- Groups armors by outfit sets for batch operations
- Key method: `GroupByOutfit(armors)`

**ArmorPreviewService** (`Services/ArmorPreviewService.cs`)

- Loads NIF mesh files using NiflySharp
- Builds 3D preview scenes for armor visualization
- Resolves mesh paths from ArmorAddon records via the LinkCache

**DistributionDiscoveryService** (`Services/DistributionDiscoveryService.cs`)

- Discovers SPID (Spell Perk Item Distributor) and SkyPatcher distribution INI files
- Parses INI files for outfit distribution management
- Types: `DistributionFileType.Spid` and `DistributionFileType.SkyPatcher`

### SPID Distribution Syntax Reference

**Docs**: https://www.nexusmods.com/skyrimspecialedition/articles/6617

SPID distributes forms at runtime (no plugin/save changes). Configs are `*_DISTR.ini` files in `Data/`.

**General Syntax**:
```
FormType = FormOrEditorID|StringFilters|FormFilters|LevelFilters|TraitFilters|CountOrPackageIdx|Chance
           ─────────────── ───────────── ─────────── ──────────── ──────────── ───────────────── ──────
               required       optional     optional    optional     optional       optional      optional
```

**Example**: `Outfit = 1_Obi_Druchii|ActorTypeNPC|VampireFaction|NONE|F|NONE|5`
- Outfit `1_Obi_Druchii` → Female NPCs with `ActorTypeNPC` keyword AND in `VampireFaction` → 5% chance

**Form Types**: `Spell`, `Perk`, `Item`, `Shout`, `Package`, `Keyword`, `Outfit`, `SleepOutfit`, `Faction`, `Skin`

**Distributable Form** (position 1):
- EditorID: `ElvenMace`, `DefaultOutfit`
- FormID: `0x12345~MyPlugin.esp`

**StringFilters** (position 2) - NPC name, EditorID, keywords, race keywords:
- Exact: `Balgruuf`, `ActorTypeNPC`
- Exclude: `-Balgruuf`
- Partial: `*Guard` (matches "Whiterun Guard", "Falkreath Guard")
- Combine (AND): `ActorTypeNPC+Bandit+ActorTypeGhost`
- Multiple (OR): `Balgruuf,Ulfric,Elisif`

**FormFilters** (position 3) - Race, Class, Faction, CombatStyle, Outfit, Perk, VoiceType, Location, specific NPC:
- Include: `NordRace`, `CrimeFactionWhiterun`
- Exclude: `-NordRace`
- Combine (AND): `NordRace+CrimeFactionWhiterun`
- Plugin filter: `CoolNPCs.esp`

**LevelFilters** (position 4):
- Min level: `5` or `5/`
- Range: `5/20`
- Exact: `10/10`
- Skill: `14(50/50)` (skill index 14 = Destruction at exactly 50)

**TraitFilters** (position 5):
- `F` = Female, `M` = Male, `U` = Unique, `S` = Summonable, `C` = Child, `L` = Leveled, `T` = Teammate, `D` = Dead
- Exclude: `-U` (not unique)
- Combine: `-U/M/-C` (non-unique male adults)

**CountOrPackageIdx** (position 6):
- Items: count (`3`) or range (`10-20`)
- Packages: insertion index (0-based)

**Chance** (position 7): Percentage 0-100, default 100

**Filter Logic**: Sections are AND, expressions within a section are OR.
`Form = 0x12345|A,B|0x12,0x34` → "(A OR B) AND (0x12 OR 0x34)"

### SkyPatcher Distribution Syntax Reference

SkyPatcher modifies NPCs and outfits at runtime via INI files. Configs are typically in `Data/skse/plugins/SkyPatcher/npc/` for NPC operations and outfit-specific directories for outfit operations.

**General Syntax**: Multiple filters and operations can be combined with `:` separator:
```
filterByXXX=value:filterByYYY=value:operation=value
```

**FormKey Format**: `ModKey|FormID` (e.g., `Skyrim.esm|000FDEAC` or `MyMod.esp|FE000D65`)

#### Patch String Structure

Patch strings are modular and consist of two parts:

1. **Filter** - Specifies which objects to patch
2. **Operation** - Changes or actions to perform on the objects

**Example**:
```
filterByFactions=Skyrim.esm|000FDEAC:outfitDefault=MyMod.esp|FE000D65
```

**Multiple Values**: When something is named in plural, you can add multiple values separated by commas:
- `filterByNpcs=Skyrim.esm|13BBF,Skyrim.esm|1B07A` - filters multiple NPCs
- `filterByFactions=Skyrim.esm|000FDEAC,Skyrim.esm|0001BCC0` - filters multiple factions
- `filterByKeywords=Skyrim.esm|00013794,Skyrim.esm|00013795` - filters multiple keywords

**FormID & EditorID**: SkyPatcher supports both FormID and EditorID formats:
- FormID: `Skyrim.esm|000FDEAC` (recommended - copy full FormID from xEdit/Creation Kit)
- EditorID: `VigilantOfStendarrFaction` (works for most filters)

**FormID Shortening**: It's sometimes possible to shorten FormIDs (e.g., `myMod.esp|08000223` → `myMod.esp|223`), but it's best practice to use the full FormID to prevent errors.

**Note**: Some operations don't support EditorID:
- Outfit Patcher: `formsToReplace` requires FormID

#### Filter Logic

SkyPatcher supports three types of filter logic for each filter category:

**filterByXy** - AND logic: All values must match (comma-separated):
```
filterByKeywords=Skyrim.esm|00013794,Skyrim.esm|00013795
```
Both keywords must be present for the match to succeed.

**filterByXyOr** - OR logic: At least one value must match (comma-separated):
```
filterByKeywordsOr=Skyrim.esm|00013794,Skyrim.esm|00013795
```
At least one keyword must be present for the match to succeed.

**filterByXyExcluded** - Exclusion logic: If any value matches, the match fails (comma-separated):
```
filterByKeywordsExcluded=Skyrim.esm|00013794
```
If this keyword is present, the match fails.

**Filter Independence**: Filters work independently from each other. Each filter type is evaluated separately:
- If `filterByFactions` matches, `filterByKeywords` can also match
- If `filterByFactions` has no match, `filterByKeywords` can still have a match
- All filter types must pass for the operation to succeed

**Example**: 
```
filterByKeywords=Skyrim.esm|00013794,Skyrim.esm|00013795:filterByKeywordsOr=Skyrim.esm|00013796:filterByKeywordsExcluded=Skyrim.esm|00013797
```
- `filterByKeywords`: Both keywords (00013794 AND 00013795) must match
- `filterByKeywordsOr`: At least one keyword (00013796) must match
- `filterByKeywordsExcluded`: Keyword 00013797 must NOT be present
- All three conditions must pass for the operation to succeed

**No Filters**: If no filters are set, all records will be accessed.

**Player Exception**: The player is always excluded from race or keyword filtering. To target the player, use `filterByNpcs=Skyrim.esm|7` with no other filters.

#### Skyrim Biped Slot Index List

When filtering by biped slots, use these index values:

| Index | Slot |
|-------|------|
| 0 | Head |
| 1 | Hair |
| 2 | Body |
| 3 | Hand |
| 4 | Forearms |
| 5 | Amulet |
| 6 | Ring |
| 7 | Feet |
| 8 | Calves |
| 9 | Shield |
| 10 | Tail |
| 11 | LongHair |
| 12 | Circlet |
| 13 | Ears |
| 14 | ModMouth |
| 15 | ModNeck |
| 16 | ModChestPrimary |
| 17 | ModBack |
| 18 | ModMisc1 |
| 19 | ModPelvisPrimary |
| 20 | DecapitateHead |
| 21 | Decapitate |
| 22 | ModPelvisSecondary |
| 23 | ModLegRight |
| 24 | ModLegLeft |
| 25 | ModFaceJewelry |
| 26 | ModChestSecondary |
| 27 | ModShoulder |
| 28 | ModArmLeft |
| 29 | ModArmRight |
| 30 | ModMisc2 |
| 31 | FX01 |

#### NPC Filtering Operations (for Outfit Distribution)

**filterByNpcs** - Filter by specific NPCs (comma-separated):
```
filterByNpcs=Skyrim.esm|13BBF,Skyrim.esm|1B07A:outfitDefault=Skyrim.esm|D3E05
```

**filterByNpcsExcluded** - Exclude specific NPCs (comma-separated):
```
filterByNpcsExcluded=Skyrim.esm|13BBF
```

**filterByFactions** - Filter by factions (AND logic, comma-separated):
```
filterByFactions=Skyrim.esm|000FDEAC:outfitDefault=MyMod.esp|FE000D65
```

**filterByFactionsOr** - Filter by factions (OR logic, comma-separated):
```
filterByFactionsOr=Skyrim.esm|000FDEAC,Skyrim.esm|0001BCC0
```

**filterByFactionsExcluded** - Exclude factions (comma-separated):
```
filterByFactionsExcluded=Skyrim.esm|000FDEAC
```

**filterByRaces** - Filter by race (comma-separated):
```
filterByRaces=Skyrim.esm|000131E8:outfitDefault=Skyrim.esm|D3E05
```

**filterByDefaultOutfits** - Filter NPCs by their default outfit:
```
filterByDefaultOutfits=Skyrim.esm|246EE7
```

**filterByKeywords** - Filter by keywords (AND logic, comma-separated):
```
filterByKeywords=Skyrim.esm|00013794,Skyrim.esm|00013795:outfitDefault=Skyrim.esm|D3E05
```

**filterByKeywordsOr** - Filter by keywords (OR logic, comma-separated):
```
filterByKeywordsOr=Skyrim.esm|00013794,Skyrim.esm|00013795
```

**filterByKeywordsExcluded** - Exclude keywords (comma-separated):
```
filterByKeywordsExcluded=Skyrim.esm|00013794
```

**filterByEditorIdContains** - Filter by EditorID (AND logic, partial match, comma-separated):
```
filterByEditorIdContains=Bandit,Guard:outfitDefault=Skyrim.esm|D3E05
```

**filterByEditorIdContainsOr** - Filter by EditorID (OR logic, partial match, comma-separated):
```
filterByEditorIdContainsOr=Bandit,Guard
```

**filterByEditorIdContainsExcluded** - Exclude EditorIDs (comma-separated):
```
filterByEditorIdContainsExcluded=Bandit
```

**filterByGender** - Filter by gender:
```
filterByGender=female:outfitDefault=Skyrim.esm|D3E05
filterByGender=male:outfitDefault=Skyrim.esm|D3E06
```

**filterByModNames** - Filter by mod names (can be combined with other filters, comma-separated):
```
filterByModNames=SkyValor.esp:filterByFactions=Skyrim.esm|0001BCC0:outfitDefault=Skyrim.esm|D3E05
```

#### NPC Outfit Operations

**outfitDefault** - Change NPC's default outfit:
```
filterByFactions=Skyrim.esm|0001A692:outfitDefault=Skyrim.esm|D3E05
```

**outfitSleep** - Change NPC's sleep outfit:
```
filterByNpcs=Skyrim.esm|13BBF:outfitSleep=Skyrim.esm|D3E06
```

#### Outfit Filtering Operations

**filterByModNames** - Filter outfits by mod names (comma-separated):
```
filterByModNames=SkyValor.esp:filterByOutfits=Skyrim.esm|246EE7
```

**filterByOutfits** - Filter specific outfits (comma-separated):
```
filterByOutfits=Skyrim.esm|246EE7,Skyrim.esm|246EE8
```
Note: If no filter is used, it affects ALL outfits.

**filterByForms** - Filter outfits containing specific objects (AND logic, comma-separated):
```
filterByForms=Skyrim.esm|59A71
```

**filterByFormsOr** - Filter outfits containing specific objects (OR logic, comma-separated):
```
filterByFormsOr=Skyrim.esm|59A71,Skyrim.esm|59A72
```

**filterByFormsExclude** - Exclude outfits containing specific objects (comma-separated):
```
filterByFormsExclude=Skyrim.esm|59A71
```

#### Outfit Operations

**formsToAdd** - Add objects to outfit form lists (comma-separated):
```
filterByOutfits=Skyrim.esm|246EE7:formsToAdd=Skyrim.esm|59A71
```

**formsToRemove** - Remove objects from outfit form lists (comma-separated):
```
filterByOutfits=Skyrim.esm|246EE7:formsToRemove=Skyrim.esm|59A71
```

**formsToReplace** - Replace forms in outfit (FormID only):
```
filterByOutfits=Skyrim.esm|246EE7:formsToReplace=Skyrim.esm|59A71=Skyrim.esm|59A72
```

**clear** - Clear outfit and remove all records:
```
filterByOutfits=Skyrim.esm|246EE7:clear=true
```

#### Example SkyPatcher Lines

**Outfit Example** - Filter by faction and set outfit:
```
filterByFactions=Skyrim.esm|000FDEAC:outfitDefault=Obi - Eve's Sunfire Armor.esp|FE000D65
```

**Outfit Example** - Filter by NPCs and set outfit:
```
filterByNpcs=Skyrim.esm|13BBF:outfitDefault=Skyrim.esm|D3E05
```

**Outfit Example** - Combine multiple filter types:
```
filterByFactions=Skyrim.esm|0001BCC0:filterByNpcs=Skyrim.esm|13BBF:outfitDefault=Skyrim.esm|D3E05
```

**Outfit Example** - Combine filters with mod name filter:
```
filterByModNames=SkyValor.esp:filterByFactions=Skyrim.esm|0001BCC0:outfitDefault=Skyrim.esm|D3E05
```

**Outfit Example** - Filter by race and set outfit:
```
filterByRaces=Skyrim.esm|000131E8:outfitDefault=Skyrim.esm|D3E05
```

**Outfit Example** - Filter by keywords and set outfit:
```
filterByKeywords=Skyrim.esm|00013794:outfitDefault=Skyrim.esm|D3E05
```

**Outfit Example** - Combine multiple filter types (factions, keywords, gender):
```
filterByFactions=Skyrim.esm|0001BCC0:filterByKeywords=Skyrim.esm|00013794:filterByGender=female:outfitDefault=Skyrim.esm|D3E05
```

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
5. **Matching**: Manual matching of source armors to target armors
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

### Mutagen/Bethesda Plugin Pitfalls

- **Don't forget to add masters**: Track every referenced ModKey and add to patch header
- **Override records, don't duplicate**: Use `GetOrAddAsOverride()` to modify existing records
- **LinkCache is required**: Initialize MutagenService before accessing armor records
- **Binary overlay vs. full load**: Use overlay for reading, full mod for writing
- **File locks**: The output ESP can be locked by MO2, xEdit, or Skyrim launcher

### WPF/XAML Pitfalls

- **DataTrigger.Value cannot use Binding**: The `Value` property of `DataTrigger` must be a static value, NOT a binding. To compare two bound values, add a computed property to the ViewModel instead.

  #### ❌ WRONG - Will cause XamlParseException:
  ```xaml
  <DataTrigger Binding="{Binding DataContext.SelectedEntry, RelativeSource={RelativeSource AncestorType=UserControl}}" 
               Value="{Binding}">
      <Setter Property="BorderBrush" Value="#0078D4" />
  </DataTrigger>
  ```

  #### ✅ CORRECT - Use a computed property:
  ```xaml
  <!-- In ViewModel, add: -->
  public bool IsSelected { get; set; }
  
  <!-- In XAML: -->
  <DataTrigger Binding="{Binding IsSelected}" Value="True">
      <Setter Property="BorderBrush" Value="#0078D4" />
  </DataTrigger>
  ```

- **ObservableCollection.Count doesn't notify**: `ObservableCollection` raises `CollectionChanged` but doesn't raise `PropertyChanged` for `Count`. Use a computed property that raises `PropertyChanged` when the collection changes, or subscribe to `CollectionChanged` events.

- **ComboBox performance with large lists**: When binding to hundreds/thousands of items, use lazy loading (load on `DropDownOpened` event) and make the ComboBox editable with autocomplete for better UX.

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
