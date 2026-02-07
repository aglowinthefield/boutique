# Boutique: Complete User Guide

**Version 1.0** | Last Updated: February 2026

Boutique is a desktop tool for Skyrim Special Edition modding that helps you create custom outfit records and distribute
them to NPCs and containers at runtime. Whether you're building fashion-forward bandits or ensuring your followers wear
matching gear, Boutique streamlines the process.

## Table of Contents

1. [Introduction](#introduction)
  - [What is Boutique?](#what-is-boutique)
  - [Who Should Use Boutique?](#who-should-use-boutique)
  - [What You'll Need](#what-youll-need)
2. [Installation & Setup](#installation--setup)
  - [Downloading Boutique](#downloading-boutique)
  - [First Launch](#first-launch)
  - [Configuring Skyrim Data Path](#configuring-skyrim-data-path)
  - [Mod Organizer 2 Integration](#mod-organizer-2-integration)
3. [Quick Start: Creating Your First Outfit](#quick-start-creating-your-first-outfit)
  - [What are Outfits?](#what-are-outfits)
  - [Creating a Simple Bandit Outfit](#creating-a-simple-bandit-outfit)
  - [Step-by-Step Walkthrough](#step-by-step-walkthrough)
4. [Feature Guide: Outfit Creation](#feature-guide-outfit-creation)
  - [What are Outfit Records (OTFT)?](#what-are-outfit-records-otft)
  - [Creating a New Outfit](#creating-a-new-outfit)
  - [Selecting Armor Pieces](#selecting-armor-pieces)
  - [Biped Slot Conflicts](#biped-slot-conflicts)
  - [Using the 3D Preview](#using-the-3d-preview)
  - [Saving Outfits](#saving-outfits)
5. [Feature Guide: Distribution](#feature-guide-distribution)
  - [What is Distribution?](#what-is-distribution)
  - [SPID vs SkyPatcher vs CDF](#spid-vs-skypatcher-vs-cdf)
  - [Distribution Tab Overview](#distribution-tab-overview)
6. [Distribution: Create Tab](#distribution-create-tab)
  - [Creating Distribution Entries](#creating-distribution-entries)
  - [Selecting Target Outfits](#selecting-target-outfits)
  - [Adding Filters](#adding-filters)
  - [Understanding Filter Syntax](#understanding-filter-syntax)
  - [Saving Distribution Files](#saving-distribution-files)
7. [Distribution: NPCs Tab](#distribution-npcs-tab)
  - [Browsing All NPCs](#browsing-all-npcs)
  - [Filtering NPCs](#filtering-npcs)
  - [Finding Distribution Conflicts](#finding-distribution-conflicts)
  - [Copying Filter Criteria](#copying-filter-criteria)
  - [Live Syntax Preview](#live-syntax-preview)
8. [Distribution: Outfits Tab](#distribution-outfits-tab)
  - [Browsing All Outfit Records](#browsing-all-outfit-records)
  - [Viewing NPC Assignments](#viewing-npc-assignments)
  - [Distribution Impact Analysis](#distribution-impact-analysis)
  - [3D Outfit Preview](#3d-outfit-preview)
9. [Distribution: Containers Tab](#distribution-containers-tab)
  - [Container Distribution Framework (CDF)](#container-distribution-framework-cdf)
  - [Distributing Loot to Chests](#distributing-loot-to-chests)
  - [CDF File Discovery](#cdf-file-discovery)
10. [Feature Guide: Armor Patching](#feature-guide-armor-patching)
  - [Understanding Source vs Target](#understanding-source-vs-target)
  - [Loading Plugins](#loading-plugins)
  - [Matching Armors](#matching-armors)
  - [Glam Mode](#glam-mode)
  - [Creating the Patch](#creating-the-patch)
  - [Load Order Placement](#load-order-placement)
11. [Settings & Configuration](#settings--configuration)
  - [Skyrim Data Path](#skyrim-data-path)
  - [Output Patch Settings](#output-patch-settings)
  - [Language Selection](#language-selection)
  - [Theme (Light/Dark)](#theme-lightdark)
  - [Plugin Blacklist](#plugin-blacklist)
12. [Common Workflows](#common-workflows)
  - [Workflow 1: Create Custom Bandit Outfit](#workflow-1-create-custom-bandit-outfit)
  - [Workflow 2: Distribute Outfit to Faction](#workflow-2-distribute-outfit-to-faction)
  - [Workflow 3: Resolve Distribution Conflicts](#workflow-3-resolve-distribution-conflicts)
  - [Workflow 4: Patch Armor from Cosmetic Mod](#workflow-4-patch-armor-from-cosmetic-mod)
13. [Troubleshooting](#troubleshooting)
14. [FAQ](#faq)
15. [Advanced Tips](#advanced-tips)
16. [Reference: SPID Syntax](#reference-spid-syntax)
17. [Reference: SkyPatcher Syntax](#reference-skypatcher-syntax)
18. [Reference: CDF Syntax](#reference-cdf-syntax)
19. [Credits & Resources](#credits--resources)

---

## Introduction

### What is Boutique?

Boutique is a Windows desktop application that helps you manage outfits and armor in Skyrim Special Edition. It handles
three main tasks:

1. **Create Outfit Records**: Build custom outfit sets (OTFT records) from any armor pieces in your load order
2. **Distribute Outfits**: Assign outfits to NPCs and containers using SPID, SkyPatcher, or CDF
3. **Patch Armor Stats**: Copy gameplay stats from one armor mod to another (advanced feature)

The primary use case is outfit creation and distribution. If you've ever wanted to give all your bandits matching gear,
or ensure faction members wear lore-appropriate clothing, Boutique makes it easy.

### Who Should Use Boutique?

Boutique is designed for:

- **Modders** creating new armor distribution mods
- **Mod Authors** maintaining outfit and distribution files
- **Players** customizing their NPC appearances
- **Load Order Enthusiasts** fine-tuning their setup

You don't need coding experience. If you can use xEdit or Creation Kit, you can use Boutique.

### What You'll Need

**Requirements**:

- Windows 10/11 (64-bit)
- Skyrim Special Edition installed
- Basic familiarity with Skyrim modding concepts (plugins, load order, FormIDs)
- (Optional) Mod Organizer 2 for virtual filesystem support

**Recommended Tools**:

- xEdit (for verifying FormIDs and EditorIDs)
- SPID, SkyPatcher, or CDF installed in your game for distribution features

---

## Installation & Setup

### Downloading Boutique

1. Visit the Boutique page on Nexus Mods
2. Download the latest version (single executable or zip archive)
3. Extract to a folder of your choice (e.g., `C:\Tools\Boutique\`)
4. Run `Boutique.exe`

No installation required. Boutique is portable and creates its configuration files in `%localappdata%\Boutique\`.

![Placeholder: Boutique executable in folder]()

### First Launch

On first launch, Boutique will:

1. Create configuration directory: `%localappdata%\Boutique\`
2. Initialize log files: `%localappdata%\Boutique\logs\`
3. Show the main window with Settings panel open

You'll see a prompt to configure your Skyrim Data path.

![Placeholder: First launch with settings panel]()

### Configuring Skyrim Data Path

**Manual Configuration**:

1. Click the **Settings** button (gear icon) in the top-right
2. In the **Skyrim Data Path** field, click **Browse**
3. Navigate to your Skyrim Special Edition Data folder:
  - Steam: `C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition\Data\`
  - GOG: `C:\GOG Games\Skyrim Special Edition\Data\`
4. Click **Select Folder**
5. Boutique will scan your load order and display a success message

![Placeholder: Settings panel with data path configured]()
*The Settings panel with Skyrim Data path configured*

**Verification**:

If configured correctly, you'll see:

- Green checkmark or success message
- Plugin list populates in Outfit Creator and Armor Patch tabs

### Mod Organizer 2 Integration

If you run Boutique from Mod Organizer 2 (MO2):

1. In MO2, click the **Configure Executables** button (gear icon)
2. Click **Add**
3. **Title**: `Boutique`
4. **Binary**: Browse to `Boutique.exe`
5. Click **OK**
6. Run Boutique from MO2's executable dropdown

Boutique automatically detects MO2's virtual filesystem and uses the correct data path. No manual configuration needed!

![Placeholder: MO2 executable configuration]()
*Adding Boutique as an MO2 executable*

---

## Quick Start: Creating Your First Outfit

### What are Outfits?

In Skyrim, an **Outfit** (OTFT record) is a collection of armor pieces that NPCs can wear. Outfits are used by:

- Default NPC outfits (what they wear normally)
- Sleep outfits (what they wear when sleeping)
- Distribution frameworks (SPID, SkyPatcher) to assign gear at runtime

Creating custom outfits allows you to:

- Give factions consistent appearances
- Create themed armor sets for different NPC types
- Control exactly what gear NPCs wear without manually editing each NPC

### Creating a Simple Bandit Outfit

Let's create a custom bandit outfit using Boutique.

**Goal**: Create an outfit called "Bandit Chief Heavy Armor" with full steel plate armor.

**Prerequisites**:

- Skyrim Data path configured
- No additional mods required (we'll use vanilla armor)

### Step-by-Step Walkthrough

**Step 1: Open Outfit Creator Tab**

1. Click the **Outfit Creator** tab in the main window
2. You'll see three panels: Plugins (left), Armor Pieces (middle), Outfit Drafts (right)

![Placeholder: Outfit Creator tab overview]()
*The Outfit Creator tab with three main panels*

**Step 2: Select Source Plugin**

1. In the **Plugins** panel (left), scroll to find `Skyrim.esm`
2. Click `Skyrim.esm` to select it
3. The **Armor Pieces** panel (middle) will populate with all armors from Skyrim.esm

![Placeholder: Plugin selection showing Skyrim.esm selected]()
*Selecting Skyrim.esm as the source plugin*

**Step 3: Select Armor Pieces**

1. In the **Armor Pieces** panel, use the search box to filter for "Steel Plate"
2. Hold `Ctrl` and click to select multiple items:
  - Steel Plate Armor
  - Steel Plate Helmet
  - Steel Plate Gauntlets
  - Steel Plate Boots
3. Click the **Add to Draft** button (or drag selected items to the right panel)

![Placeholder: Armor piece selection with multiple items selected]()
*Selecting multiple steel plate armor pieces*

**Step 4: Review Outfit Draft**

1. The **Outfit Drafts** panel (right) now shows your selected items
2. Each item displays its biped slots (Head, Body, Hands, Feet)
3. Boutique automatically checks for slot conflicts (none in this case)

![Placeholder: Outfit draft queue with armor pieces]()
*Outfit draft with steel plate armor pieces added*

**Step 5: Preview the Outfit (Optional)**

1. Click the **Preview** button above the draft panel
2. A 3D preview window opens showing the outfit
3. Rotate the model to inspect the armor
4. Close the preview when satisfied

![Placeholder: 3D preview showing steel plate outfit]()
*3D preview of the steel plate bandit outfit*

**Step 6: Save the Outfit**

1. In the **Outfit Name** field, enter: `Bandit_Chief_Heavy`
2. Select the output plugin:
  - Create new ESP: Click **New Plugin**, enter `MyBanditOutfits.esp`
  - Or select existing plugin from dropdown
3. Click **Save Outfit**
4. Success message appears: "Outfit 'Bandit_Chief_Heavy' saved to MyBanditOutfits.esp"

**Step 7: Verify in xEdit (Optional)**

1. Open xEdit and load `MyBanditOutfits.esp`
2. Expand **Outfit** category
3. Find `Bandit_Chief_Heavy` record
4. Verify all armor pieces are listed

**What's Next?**

Now that you have a custom outfit, you can:

- Distribute it to NPCs using the Distribution tab (see [Distribution: Create Tab](#distribution-create-tab))
- Create more outfits for different situations
- Build a complete outfit overhaul mod

---

## Feature Guide: Outfit Creation

This section covers the Outfit Creator feature in depth.

### What are Outfit Records (OTFT)?

Outfit records (type: OTFT) are Skyrim records that define a set of wearable items. They're referenced by:

- **NPC Records**: Default outfit, sleep outfit
- **Leveled NPCs**: Outfit templates for spawned NPCs
- **Distribution Frameworks**: SPID and SkyPatcher assign outfits at runtime

**Key Properties**:

- **EditorID**: Internal name (e.g., `Bandit_Light_Outfit`)
- **FormID**: Unique identifier (e.g., `0x12345`)
- **Items List**: Array of armor/clothing FormIDs

**Why Use Outfits Instead of Direct Armor Assignment?**

Outfits provide:

- **Reusability**: One outfit assigned to many NPCs
- **Maintainability**: Edit outfit once, affects all users
- **Compatibility**: Distribution frameworks work best with outfits
- **Performance**: More efficient than item-by-item distribution

### Creating a New Outfit

**Process Overview**:

1. Select source plugin(s) containing desired armor
2. Choose armor pieces from the plugin
3. Add pieces to outfit draft
4. Resolve any biped slot conflicts
5. Name and save the outfit to an ESP

**Outfit Draft Panel**

The outfit draft (right panel) is your workspace. It shows:

- Selected armor pieces
- Biped slots each piece occupies
- Slot conflicts (if any)
- Total piece count

You can:

- Drag items to reorder
- Remove items by clicking the X button
- Clear entire draft with **Clear All**

### Selecting Armor Pieces

**Plugin Selection**:

The left panel lists all plugins in your load order. You can:

- Click a plugin to view its armors
- Search plugins by name
- Select multiple plugins (Ctrl+Click) to view combined armor list

**Armor DataGrid**:

The middle panel shows a sortable, filterable table of armors:

| Column   | Description                              |
|----------|------------------------------------------|
| EditorID | Internal name (e.g., `ArmorIronCuirass`) |
| Name     | Display name (e.g., "Iron Armor")        |
| Slots    | Biped slots (e.g., "Body, Hands")        |
| Type     | Light, Heavy, Clothing                   |
| FormID   | Unique identifier                        |

**Filtering**:

Use the search box to filter by:

- EditorID: `Iron`
- Name: `Helmet`
- Type: `Heavy`

**Selection**:

- Single-click: Select one armor
- Ctrl+Click: Add to selection
- Shift+Click: Select range
- Drag: Add to outfit draft

### Biped Slot Conflicts

Skyrim armor uses 32 biped slots (Head, Body, Hands, Feet, etc.). An NPC can only wear one item per slot.

**Conflict Detection**:

Boutique highlights conflicts when:

- Two pieces occupy the same slot
- Example: Two helmets (both use Slot 0 - Head)

**Conflict Indicator**:

- Red text or warning icon next to conflicting pieces
- Tooltip explains which slots conflict

**Resolving Conflicts**:

1. Remove one of the conflicting pieces
2. Or keep both if you want a fallback option (some mods handle this gracefully)

**Common Conflicts**:

| Conflict         | Cause                   | Solution                        |
|------------------|-------------------------|---------------------------------|
| Two helmets      | Both use Head slot      | Keep only one                   |
| Chest + Robe     | Both use Body slot      | Choose chest or robe            |
| Gloves + Bracers | Both may use Hands slot | Check slot assignments in xEdit |

### Using the 3D Preview

The 3D preview lets you visualize the outfit before saving.

**Opening Preview**:

1. Click **Preview** button in Outfit Creator
2. A new window opens with 3D model
3. The model wears all pieces in the draft

**Controls**:

- **Left-click drag**: Rotate camera
- **Right-click drag**: Pan camera
- **Scroll wheel**: Zoom in/out
- **Reset**: Button to reset camera

**Preview Limitations**:

- Shows default body mesh (may not match your character body mod)
- Some armor pieces may not load if meshes are missing
- No texture preview (meshes only)

**Troubleshooting Preview**:

If preview doesn't load:

- Verify armor meshes exist in Data/Meshes/
- Check log file: `%localappdata%\Boutique\logs\Boutique-YYYYMMDD.log`
- Some modded armors use custom skeletons and may not preview correctly

### Saving Outfits

**Naming Guidelines**:

- Use descriptive EditorIDs: `Bandit_Light_Armor`, `Guard_Whiterun_Officer`
- Avoid spaces (use underscores)
- Prefix by category for organization: `Bandit_`, `Guard_`, `Civilian_`

**Output Plugin Selection**:

You can save to:

1. **New Plugin**: Creates a new ESP with your outfits
  - Best for standalone outfit packs
  - Example: `MyCustomOutfits.esp`

2. **Existing Plugin**: Adds outfit to existing ESP
  - Best for updating your own mod
  - Boutique loads the plugin, adds the outfit, and saves

**Save Process**:

1. Enter outfit name in **Outfit Name** field
2. Choose output plugin
3. Click **Save Outfit**
4. Boutique creates/updates the ESP in your Data folder
5. Success message confirms save

**Post-Save**:

- Outfit is immediately available in your load order
- You can reference it in xEdit, CK, or distribution files
- Use the outfit's FormID for distribution (see Distribution sections)

**Example Output**:

If you saved "Bandit_Chief_Heavy" to `MyBanditOutfits.esp`:

- **File**: `Data\MyBanditOutfits.esp`
- **Record**: Outfit `Bandit_Chief_Heavy` [OTFT:00000001]
- **Contains**: FormIDs of all selected armor pieces

---

## Feature Guide: Distribution

### What is Distribution?

Distribution is the process of assigning items, outfits, spells, or perks to NPCs and containers at runtime. Unlike ESP
edits, distribution happens when the game loads, making it:

- **Dynamic**: Reacts to your load order
- **Compatible**: No conflicts with other mods editing NPCs
- **Flexible**: Use filters to target specific NPCs

**Use Cases**:

- Give all bandits matching outfits
- Equip faction members with custom gear
- Add loot to specific containers
- Distribute items based on level, race, or location

### SPID vs SkyPatcher vs CDF

Boutique supports three distribution frameworks:

| Framework      | Target        | Syntax             | File Format            | Best For                          |
|----------------|---------------|--------------------|------------------------|-----------------------------------|
| **SPID**       | NPCs          | Simple, line-based | `.ini` (`*_DISTR.ini`) | General NPC outfit distribution   |
| **SkyPatcher** | NPCs, Outfits | Modular filters    | `.ini`                 | Complex filtering, outfit editing |
| **CDF**        | Containers    | JSON rules         | `.json`                | Container loot distribution       |

**SPID (Spell Perk Item Distributor)**:

- **Pros**: Simple syntax, widely used, lightweight
- **Cons**: Limited filtering options
- **Files**: `Data/*_DISTR.ini`
- **Example**: `Outfit = Bandit_Heavy|ActorTypeNPC|NONE|NONE|M`

**SkyPatcher**:

- **Pros**: Advanced filtering, can edit outfits
- **Cons**: More complex syntax
- **Files**: `Data/skse/plugins/SkyPatcher/npc/*.ini`
- **Example**: `filterByFactions=Skyrim.esm|000FDEAC:outfitDefault=MyMod.esp|00001234`

**CDF (Container Distribution Framework)**:

- **Pros**: Powerful rule system, supports conditions
- **Cons**: JSON syntax may be unfamiliar
- **Files**: `Data/SKSE/Plugins/ContainerDistributionFramework/*.json`
- **Example**: JSON rules for container targeting

**Which Should You Use?**

- **Start with SPID**: Easiest to learn, covers most use cases
- **Use SkyPatcher**: When you need complex filters (e.g., "Female NPCs in Solitude with ActorTypeNPC keyword")
- **Use CDF**: For container loot only

### Distribution Tab Overview

The Distribution tab has four sub-tabs:

1. **Create**: Build new distribution entries
2. **NPCs**: Browse and filter NPCs, view assignments
3. **Outfits**: Browse outfit records, see distribution impact
4. **Containers**: Manage container distribution (CDF)

Each tab serves a different purpose in your distribution workflow.

![Placeholder: Distribution tab showing all four sub-tabs]()
*The Distribution tab with Create, NPCs, Outfits, and Containers sub-tabs*

---

## Distribution: Create Tab

The Create tab is your workspace for building distribution entries.

### Creating Distribution Entries

**Process Overview**:

1. Select distribution type (SPID or SkyPatcher)
2. Choose target outfit
3. Add filters (gender, faction, race, etc.)
4. Preview generated syntax
5. Save to distribution file

**Layout**:

The Create tab has four panels:

- **Top-left**: Entry list (your distribution entries)
- **Bottom-left**: File preview (raw INI content)
- **Top-right**: Entry editor (outfit and form selection)
- **Bottom-right**: Filter configuration tabs

### Selecting Target Outfits

**Step 1: Choose Distribution Type**

At the top of the entry editor:

- **SPID**: Line-based syntax
- **SkyPatcher**: Filter-based syntax

**Step 2: Select Outfit**

1. Click the **Outfit** dropdown
2. Search for your outfit by EditorID or FormID
3. Select the outfit (e.g., `Bandit_Chief_Heavy`)

**Outfit Selector Features**:

- Autocomplete search
- Shows FormID and plugin name
- Filters by plugin if needed

### Adding Filters

Filters determine which NPCs receive the outfit.

**Filter Categories**:

Boutique provides six filter tabs:

1. **Gender**: Male, Female, or Both
2. **Unique**: Unique NPCs only, Generic NPCs only, or Both
3. **Faction**: Filter by faction membership
4. **Race**: Filter by NPC race
5. **Class**: Filter by NPC class
6. **Keywords**: Filter by NPC keywords

**Using Filters**:

1. Click a filter tab (e.g., **Faction**)
2. Search for the faction (e.g., "BanditFaction")
3. Select from dropdown
4. Filter is added to your entry

**Multiple Filters**:

You can combine filters:

- **SPID**: Filters are AND logic (NPC must match all)
- **SkyPatcher**: Offers AND, OR, and Exclude options

**Example**:

Filters:

- Gender: Female
- Faction: BanditFaction
- Race: NordRace

Result: Female Nord bandits

### Understanding Filter Syntax

**SPID Syntax Preview**:

As you add filters, Boutique shows the generated SPID line:

```ini
Outfit = Bandit_Chief_Heavy|ActorTypeNPC|BanditFaction+NordRace|NONE|F
```

Breakdown:

- `Outfit`: Distribution type
- `Bandit_Chief_Heavy`: Target outfit EditorID
- `ActorTypeNPC`: String filter (keyword)
- `BanditFaction+NordRace`: Form filters (AND logic)
- `NONE`: Level filter (none)
- `F`: Trait filter (Female)

**SkyPatcher Syntax Preview**:

```ini
filterByFactions=Skyrim.esm|000FDEAC:filterByRaces=Skyrim.esm|000131E8:filterByGender=female:outfitDefault=MyMod.esp|00001234
```

Breakdown:

- `filterByFactions`: Faction filter
- `filterByRaces`: Race filter
- `filterByGender`: Gender filter
- `outfitDefault`: Operation (set default outfit)

**Syntax Preview Panel**:

The bottom-right panel shows live syntax as you configure filters. Use this to:

- Learn the syntax
- Copy for manual editing
- Verify correctness

### Saving Distribution Files

**File Naming**:

Boutique automatically names files with a `Z-` prefix for load order:

- SPID: `Z-Boutique_Bandits_DISTR.ini`
- SkyPatcher: `Z-Boutique_Bandits.ini`
- CDF: `Z-Boutique_Containers.json`

The `Z-` prefix ensures your distribution loads last, overriding other mods.

**Save Process**:

1. Configure your entry (outfit + filters)
2. Click **Add Entry** to add to the entry list
3. Repeat for more entries
4. Click **Save File**
5. Choose file name or use default
6. Boutique writes the file to the correct location:
  - SPID: `Data/`
  - SkyPatcher: `Data/skse/plugins/SkyPatcher/npc/`
  - CDF: `Data/SKSE/Plugins/ContainerDistributionFramework/`

**Editing Existing Files**:

1. Click **Load File** in the Create tab
2. Browse to your distribution file
3. Entries appear in the entry list
4. Edit entries, add new ones, or delete
5. Click **Save File** to update

**File Preview**:

The bottom-left panel shows the raw file content. You can:

- Review before saving
- Copy for manual editing
- Verify syntax correctness

---

## Distribution: NPCs Tab

The NPCs tab is a browser for all NPCs in your load order.

### Browsing All NPCs

**NPC DataGrid**:

The main panel shows a searchable table of NPCs:

| Column         | Description              |
|----------------|--------------------------|
| Name           | NPC display name         |
| EditorID       | Internal name            |
| Race           | NPC race                 |
| Gender         | Male or Female           |
| Faction        | Primary faction (if any) |
| Default Outfit | Current outfit EditorID  |
| Source Plugin  | Plugin defining the NPC  |

**Sorting**:

Click column headers to sort:

- Alphabetical: Name, EditorID
- Grouped: Race, Gender, Faction

### Filtering NPCs

**Filter Panel** (top):

Boutique provides quick filters:

- **Gender**: Male / Female / Both
- **Unique**: Unique / Generic / Both
- **Faction**: Dropdown of all factions
- **Race**: Dropdown of all races
- **Keyword**: Search by keyword (e.g., "ActorTypeNPC")

**Search Box**:

Type to filter the NPC list by:

- Name
- EditorID
- Plugin name

**Combined Filters**:

All filters combine with AND logic:

Example:

- Gender: Female
- Race: NordRace
- Faction: GuardFaction

Result: Female Nord guards

### Finding Distribution Conflicts

**Conflict Highlighting**:

Boutique highlights NPCs with multiple outfit distributions:

- **Orange**: Multiple SPID rules target this NPC
- **Red**: Conflicting SkyPatcher rules (priority unclear)

**Conflict Details Panel** (bottom):

Click an NPC to see:

- All distribution files targeting this NPC
- Order of precedence
- Which outfit will actually be applied

**Resolving Conflicts**:

1. Identify conflicting NPCs in the DataGrid
2. Click the NPC to view details
3. Determine which distribution should win
4. Edit or delete conflicting files
5. Reload Boutique to verify resolution

### Copying Filter Criteria

**Workflow**:

You found an NPC you want to target. How do you write the filters?

1. Click the NPC in the DataGrid
2. In the detail panel, click **Copy Filters**
3. Switch to the **Create** tab
4. Filters are pre-filled based on the NPC's properties

**What Gets Copied**:

- Gender
- Race
- Primary faction
- Class (if applicable)
- Keywords (if applicable)

Now you can tweak the filters or add the outfit directly.

### Live Syntax Preview

**Syntax Panel** (bottom-right):

Shows how the current filters translate to SPID or SkyPatcher syntax.

**Use Cases**:

- Learn distribution syntax by example
- Verify your filters match the NPC
- Copy syntax for manual editing

**Example**:

Selected NPC: Female Nord bandit

Generated SPID:

```ini
Outfit = YourOutfit|ActorTypeNPC|BanditFaction+NordRace|NONE|F
```

Generated SkyPatcher:

```ini
filterByFactions=Skyrim.esm|000FDEAC:filterByRaces=Skyrim.esm|000131E8:filterByGender=female:outfitDefault=YourMod.esp|00001234
```

---

## Distribution: Outfits Tab

The Outfits tab lets you browse outfit records and see their distribution impact.

### Browsing All Outfit Records

**Outfit DataGrid**:

| Column    | Description            |
|-----------|------------------------|
| EditorID  | Outfit name            |
| FormID    | Unique identifier      |
| Plugin    | Source plugin          |
| NPC Count | NPCs using this outfit |
| Items     | Number of armor pieces |

**Sorting and Searching**:

- Sort by NPC count to find most-used outfits
- Search by EditorID to find specific outfits

### Viewing NPC Assignments

**NPC Assignment Panel** (bottom-left):

Click an outfit to see:

- List of NPCs assigned this outfit
- How they're assigned:
  - **Direct**: NPC record references outfit
  - **SPID**: SPID distribution assigns outfit
  - **SkyPatcher**: SkyPatcher assigns outfit

**Why This Matters**:

- Identify which NPCs will be affected if you edit the outfit
- Find unused outfits (NPC Count = 0)
- Debug why an NPC isn't wearing expected gear

### Distribution Impact Analysis

**Distribution Files Panel** (bottom-right):

Shows all distribution files affecting the selected outfit:

- File name
- Distribution type (SPID, SkyPatcher)
- Number of entries targeting this outfit

**Impact Summary**:

Boutique calculates:

- Total NPCs potentially affected
- Plugins depending on this outfit
- Conflicts with other outfits

**Use Cases**:

- Before editing an outfit, see who uses it
- Identify if deleting an outfit will break distribution
- Find redundant distribution entries

### 3D Outfit Preview

**Preview Button**:

Click **Preview** to open a 3D view of the outfit.

Same controls as Outfit Creator preview:

- Rotate, pan, zoom
- Visualize all armor pieces
- Verify appearance

---

## Distribution: Containers Tab

The Containers tab manages container distribution using CDF.

### Container Distribution Framework (CDF)

CDF adds, removes, or replaces items in containers at runtime based on JSON rules.

**Use Cases**:

- Add custom loot to specific chests
- Remove vanilla items from containers
- Replace items (e.g., iron ingots → gold ingots)

**File Location**:

CDF files must be in:

```
Data/SKSE/Plugins/ContainerDistributionFramework/*.json
```

### Distributing Loot to Chests

**Creating a CDF Rule**:

1. Click **New Rule** in the Containers tab
2. Select rule type:
  - **Add**: Add items to containers
  - **Remove**: Remove items
  - **Replace**: Swap items
3. Configure conditions (which containers to target):
  - Container base form
  - Location
  - Worldspace
  - Globals (level checks, quest flags)
4. Configure changes (what to add/remove/replace)
5. Preview JSON
6. Save file

**Example Rule**:

Add 10 gold to all Dwemer chests in Markarth:

```json
{
  "rules": [
    {
      "friendlyName": "Add gold to Dwemer chests in Markarth",
      "conditions": {
        "containers": ["0x1EDDD|Skyrim.esm"],
        "locations": ["0x18A59|Skyrim.esm"]
      },
      "changes": [
        {
          "add": ["0xF|Skyrim.esm"],
          "count": 10
        }
      ]
    }
  ]
}
```

### CDF File Discovery

**File List**:

Boutique scans and lists all CDF JSON files in your Data folder.

**Editing Existing Files**:

1. Click a file in the list
2. Rules appear in the editor
3. Edit, add, or delete rules
4. Save to update the file

**Validation**:

Boutique validates JSON syntax before saving. Errors are highlighted with explanations.

---

## Feature Guide: Armor Patching

Armor patching is an advanced feature for copying stats between armor mods.

### Understanding Source vs Target

**Source Armor**: The cosmetic armor you want to wear

**Target Armor**: The gameplay armor with balanced stats

**Goal**: Copy target stats to source armor, so source armor has target stats.

**Use Case**:

You have a cosmetic armor mod (looks great, but stats are unbalanced) and a gameplay overhaul mod (balanced stats). You
want to wear the cosmetic armor with balanced stats.

**Example**:

- Source: "Fancy Robe" (looks great, 0 armor rating)
- Target: "Requiem Mage Robe" (balanced for Requiem)
- Result: "Fancy Robe" with Requiem stats

### Loading Plugins

**Source Plugin**:

1. Go to **Armor Patch** tab
2. In the **Source** panel, select the plugin with cosmetic armors
3. Boutique loads all armor records from the plugin

**Target Plugin**:

1. In the **Target** panel, select the gameplay mod
2. Boutique loads armors with stats to copy

**Matching Interface**:

The center panel shows side-by-side comparison:

- Left: Source armors
- Right: Target armors
- Match button: Connect source to target

### Matching Armors

**Manual Matching**:

1. Select a source armor (left panel)
2. Select a target armor (right panel)
3. Click **Match**
4. Boutique creates a mapping: Source → Target

**Auto-Match**:

Boutique can auto-match based on:

- Name similarity
- Slot similarity
- EditorID similarity

Click **Auto-Match** to attempt automatic matching. Review results before patching.

**Match Preview**:

The bottom panel shows all matches:

| Source    | Target          | Slots | Stats to Copy                |
|-----------|-----------------|-------|------------------------------|
| FancyRobe | RequiemMageRobe | Body  | Armor, Keywords, Enchantment |

### Glam Mode

Glam Mode is a special patching option for pure cosmetic builds.

**What It Does**:

Sets all armor ratings to 0, making armor purely cosmetic.

**Use Case**:

You want armor appearance without affecting gameplay difficulty.

**Enabling Glam Mode**:

1. Check the **Glam Mode** checkbox
2. Proceed with patching
3. Result: Armor pieces have 0 armor rating, but keep keywords and enchantments

### Creating the Patch

**Patch Process**:

1. Match all desired armors
2. Configure patch settings:
  - Output ESP name (e.g., `MyCosmeticPatch.esp`)
  - Enable/disable Glam Mode
3. Click **Create Patch**
4. Boutique generates the patch ESP in your Data folder

**What Gets Copied**:

- Armor rating
- Keywords (e.g., ArmorHeavy, ArmorLight)
- Enchantments
- Tempering recipes (if applicable)

**What Doesn't Get Copied**:

- Appearance (meshes, textures)
- EditorID
- FormID

### Load Order Placement

**Correct Load Order**:

```
1. Skyrim.esm
2. [Other masters]
3. CosmeticArmorMod.esp (source)
4. GameplayOverhaul.esp (target)
5. MyCosmeticPatch.esp (your patch)
```

**Why This Order Matters**:

Your patch overrides the source armor, applying target stats. It must load after both source and target.

**Verification**:

1. Open xEdit
2. Load your patch
3. Check armor records
4. Verify stats match target

---

## Settings & Configuration

### Skyrim Data Path

**Location**: Settings panel → **Skyrim Data Path**

**Purpose**: Tells Boutique where your Skyrim installation is located.

**Manual Configuration**:

1. Click **Browse**
2. Navigate to `Skyrim Special Edition\Data\`
3. Select folder

**MO2 Auto-Detection**:

If run from MO2, Boutique auto-detects the virtual filesystem. No manual configuration needed.

**Troubleshooting**:

- Verify path ends with `\Data\`
- Check that `Skyrim.esm` exists in the folder
- If using MO2, ensure you ran Boutique from MO2 executable list

### Output Patch Settings

**Output Path**: Where Boutique saves patch ESPs.

**Default**: Same as Skyrim Data path.

**Custom Path**:

1. Click **Browse** next to Output Path
2. Select a different folder
3. Useful for keeping patches separate

**Patch File Name**:

Default: `Boutique_Patch.esp`

Change to:

- `MyArmorPatch.esp`
- `CosmeticOverhaul.esp`

### Language Selection

Boutique supports multiple languages:

- English
- German (Deutsch)
- French (Français)

**Changing Language**:

1. Open Settings panel
2. Select language from dropdown
3. Restart Boutique for full effect

**Note**: Translations are community-contributed. Some text may remain in English.

### Theme (Light/Dark)

**Theme Toggle**: Settings panel → **Theme**

- **Light**: Light background, dark text
- **Dark**: Dark background, light text

**Font Scaling**:

Adjust UI font size:

- Small (90%)
- Medium (100%)
- Large (110%)

### Plugin Blacklist

**Purpose**: Hide unwanted plugins from Boutique's plugin lists.

**Use Cases**:

- Hide test plugins
- Hide plugins with invalid records
- Reduce clutter in plugin dropdowns

**Adding to Blacklist**:

1. Open Settings panel
2. Click **Manage Blacklist**
3. Enter plugin name (e.g., `MyTestMod.esp`)
4. Click **Add**
5. Plugin disappears from lists

**Removing from Blacklist**:

1. Open blacklist manager
2. Select plugin
3. Click **Remove**

---

## Common Workflows

This section provides complete, real-world examples.

### Workflow 1: Create Custom Bandit Outfit

**Goal**: Create a "Bandit Mage" outfit with robes and a hood.

**Prerequisites**:

- Skyrim Data path configured

**Steps**:

1. **Open Outfit Creator tab**

2. **Select Plugin**:
  - Choose `Skyrim.esm`

3. **Select Armor Pieces**:
  - Search "Mage"
  - Select:
    - Mage Robes (Unhooded)
    - Mage Hood
    - Mage Boots

4. **Add to Draft**:
  - Click **Add to Draft**

5. **Verify No Conflicts**:
  - Check draft panel for red warnings
  - All pieces should be green (no conflicts)

6. **Preview** (optional):
  - Click **Preview**
  - Inspect the outfit

7. **Save Outfit**:
  - Outfit Name: `Bandit_Mage_Robes`
  - Output Plugin: Create new → `MyBanditOutfits.esp`
  - Click **Save Outfit**

8. **Verify**:
  - Open xEdit
  - Load `MyBanditOutfits.esp`
  - Find `Bandit_Mage_Robes` outfit
  - Verify armor pieces are listed

**Result**: You now have a custom bandit mage outfit ready for distribution.

---

### Workflow 2: Distribute Outfit to Faction

**Goal**: Give all female bandits the "Bandit_Mage_Robes" outfit.

**Prerequisites**:

- "Bandit_Mage_Robes" outfit created
- SPID installed in your game

**Steps**:

1. **Open Distribution → Create tab**

2. **Select Distribution Type**:
  - Choose **SPID**

3. **Select Outfit**:
  - Dropdown: `Bandit_Mage_Robes`

4. **Add Filters**:
  - Click **Gender** tab → Select **Female**
  - Click **Faction** tab → Search "Bandit" → Select `BanditFaction`

5. **Preview Syntax**:
  - Check syntax panel (bottom-right):
    ```ini
    Outfit = Bandit_Mage_Robes|ActorTypeNPC|BanditFaction|NONE|F
    ```

6. **Add Entry**:
  - Click **Add Entry**
  - Entry appears in list (top-left)

7. **Save File**:
  - Click **Save File**
  - File name: `Z-Boutique_Bandit_Mages_DISTR.ini`
  - Saves to `Data\Z-Boutique_Bandit_Mages_DISTR.ini`

8. **Test In-Game**:
  - Load Skyrim
  - Find a female bandit
  - Verify she's wearing mage robes

**Result**: All female bandits now wear your custom mage robes.

---

### Workflow 3: Resolve Distribution Conflicts

**Goal**: Two SPID files both target the same NPC. Resolve the conflict.

**Prerequisites**:

- Multiple distribution files installed

**Steps**:

1. **Open Distribution → NPCs tab**

2. **Identify Conflicts**:
  - Orange/red highlighted NPCs indicate conflicts

3. **Select NPC**:
  - Click the highlighted NPC (e.g., "Bandit Marauder")

4. **View Details** (bottom panel):
  - Shows:
    - Distribution file 1: `Z-Mod1_Bandits_DISTR.ini` → Outfit A
    - Distribution file 2: `Z-Mod2_Bandits_DISTR.ini` → Outfit B

5. **Determine Winner**:
  - SPID uses file name order (alphabetically last wins)
  - `Z-Mod2_Bandits_DISTR.ini` wins (later alphabetically)

6. **Resolve**:
  - **Option 1**: Delete one file
  - **Option 2**: Rename files to control order:
    - `Z-Mod1_Bandits_DISTR.ini` → `ZZ-Mod1_Bandits_DISTR.ini`
  - **Option 3**: Edit one file to exclude conflicting NPCs

7. **Reload Boutique**:
  - Click **Refresh** in NPCs tab
  - Conflict should disappear

**Result**: Conflict resolved, NPCs wear the intended outfit.

---

### Workflow 4: Patch Armor from Cosmetic Mod

**Goal**: Copy Requiem stats to a cosmetic armor mod.

**Prerequisites**:

- Requiem installed
- Cosmetic armor mod installed
- Basic understanding of armor stats

**Steps**:

1. **Open Armor Patch tab**

2. **Select Source Plugin**:
  - Left panel: Choose cosmetic armor mod (e.g., `FancyArmors.esp`)

3. **Select Target Plugin**:
  - Right panel: Choose `Requiem.esp`

4. **Match Armors**:
  - Find cosmetic "Fancy Iron Armor" (left)
  - Find Requiem "Iron Armor" (right)
  - Click both, then click **Match**

5. **Repeat for All Armors**:
  - Match each cosmetic piece to a Requiem equivalent

6. **Review Matches** (bottom panel):
  - Verify all matches are correct

7. **Configure Patch**:
  - Output ESP: `MyRequiemCosmeticPatch.esp`
  - Glam Mode: Unchecked (we want Requiem stats)

8. **Create Patch**:
  - Click **Create Patch**
  - Boutique generates `MyRequiemCosmeticPatch.esp`

9. **Load Order**:
  - Place patch after both source and target:
    ```
    FancyArmors.esp
    Requiem.esp
    MyRequiemCosmeticPatch.esp
    ```

10. **Verify in xEdit**:
  - Open patch in xEdit
  - Check "Fancy Iron Armor" record
  - Verify armor rating matches Requiem

11. **Test In-Game**:
  - Equip cosmetic armor
  - Check stats in inventory
  - Should match Requiem armor

**Result**: Cosmetic armor now has balanced Requiem stats.

---

## Troubleshooting

### "Missing Masters" Error

**Symptom**: Error when loading a plugin or saving an outfit.

**Cause**: Plugin references FormIDs from missing master files.

**Solution**:

1. Open xEdit
2. Load the plugin showing the error
3. Check File Header → Master Files
4. Install all required master plugins
5. Reload Boutique

**Prevention**: Always load full mod setups, not just individual plugins.

---

### "File is Locked" Error

**Symptom**: Can't save ESP or distribution file.

**Cause**: File is open in another program (xEdit, Creation Kit, or game).

**Solution**:

1. Close xEdit, Creation Kit, and Skyrim
2. If using MO2, close MO2 instance
3. Try saving again in Boutique

**Prevention**: Close all modding tools before using Boutique.

---

### "Invalid FormID" Error

**Symptom**: Can't select an outfit or armor.

**Cause**: FormID format is incorrect or record doesn't exist.

**Solution**:

1. Verify FormID in xEdit
2. Copy full FormID (e.g., `[01] 0x00001234`)
3. Ensure plugin is loaded in Boutique

**Prevention**: Use Boutique's dropdowns instead of manually entering FormIDs.

---

### Distribution Not Applying

**Symptom**: NPC doesn't wear the distributed outfit in-game.

**Cause**: Multiple possible issues.

**Solution**:

1. **Check SPID/SkyPatcher is installed**:
  - SPID: `Data\SKSE\Plugins\po3_SpellPerkItemDistributor.dll`
  - SkyPatcher: `Data\SKSE\Plugins\SkyPatcher.dll`

2. **Verify distribution file location**:
  - SPID: `Data\*_DISTR.ini`
  - SkyPatcher: `Data\skse\plugins\SkyPatcher\npc\*.ini`

3. **Check filter syntax**:
  - Open file in text editor
  - Compare to syntax reference (see [Reference: SPID Syntax](#reference-spid-syntax))

4. **Test with simple rule**:
  - Create a test SPID line:
    ```ini
    Outfit = YourOutfit|NONE|NONE|NONE|NONE
    ```
  - This targets ALL NPCs (no filters)
  - If this works, your filters are too restrictive

5. **Check SPID log**:
  - Location: `%userprofile%\Documents\My Games\Skyrim Special Edition\SKSE\`
  - Look for errors related to your file

**Prevention**: Test distribution in-game after creating entries.

---

### 3D Preview Not Loading

**Symptom**: Preview window is blank or shows error.

**Cause**: Missing mesh files or unsupported armor type.

**Solution**:

1. Verify armor meshes exist in `Data\Meshes\`
2. Check log file: `%localappdata%\Boutique\logs\Boutique-YYYYMMDD.log`
3. Look for mesh load errors

**Workaround**: Skip preview, outfit will still save correctly.

**Prevention**: Use armors with standard mesh paths.

---

### MO2 Integration Issues

**Symptom**: Boutique doesn't detect MO2's virtual filesystem.

**Cause**: Boutique not launched from MO2.

**Solution**:

1. Add Boutique to MO2 executables (see [Mod Organizer 2 Integration](#mod-organizer-2-integration))
2. Run Boutique from MO2 executable dropdown
3. Verify environment variable: `MODORGANIZER2_EXECUTABLE`

**Prevention**: Always launch Boutique from MO2 when using MO2.

---

### Patch Not Working In-Game (Armor Patching)

**Symptom**: Armor in-game doesn't have patched stats.

**Cause**: Load order incorrect.

**Solution**:

1. Verify load order:
   ```
   Source.esp (cosmetic mod)
   Target.esp (gameplay mod)
   YourPatch.esp (Boutique patch)
   ```
2. Patch must load last
3. Use LOOT or MO2 to adjust load order

**Prevention**: Always check load order after creating patches.

---

## FAQ

### Load Order Questions

**Q: Where should distribution files load in the load order?**

A: Distribution files (SPID, SkyPatcher, CDF) are not plugins. They don't appear in load order. They load based on file
name alphabetically. Prefix with `Z-` to ensure they load last.

---

**Q: Does my patch ESP need to load after both source and target?**

A: Yes. For armor patching, load order must be: Source → Target → Patch.

---

### Compatibility Questions

**Q: Is Boutique compatible with [mod name]?**

A: Boutique works with any mod that uses standard Skyrim records. It reads your load order and creates
patches/distribution files. If you can load the mod in xEdit, Boutique can work with it.

---

**Q: Can I use SPID and SkyPatcher at the same time?**

A: Yes. SPID and SkyPatcher can coexist. If both target the same NPC, SkyPatcher typically wins (loads later). Test
in-game to verify behavior.

---

**Q: Will Boutique conflict with other NPC overhauls?**

A: No. Boutique uses runtime distribution, which doesn't edit NPC records. Your distribution files add outfits at
runtime, leaving NPC records untouched.

---

### Performance Questions

**Q: Does distribution impact game performance?**

A: Minimal impact. SPID and SkyPatcher are optimized for runtime distribution. Large distribution files (1000+ entries)
may add a second or two to load times.

---

**Q: Can I distribute to thousands of NPCs?**

A: Yes. Distribution frameworks handle large-scale distribution efficiently. Test in-game to verify performance is
acceptable.

---

### Feature Questions

**Q: Can Boutique edit existing outfits?**

A: Yes. Load an outfit in the Outfit Creator, modify it, and save to the same plugin. Boutique updates the outfit
record.

---

**Q: Can I distribute items other than outfits?**

A: SPID and SkyPatcher support spells, perks, items, and more. CDF supports container loot. Boutique currently focuses
on outfits, but you can manually edit distribution files for other item types.

---

**Q: Can I use Boutique with Skyrim VR or Legendary Edition?**

A: No. Boutique is designed for Skyrim Special Edition only.

---

### Troubleshooting Questions

**Q: Why doesn't my outfit show up in xEdit?**

A: Verify the output plugin is in your Data folder and loaded in xEdit's master list. Check Boutique logs for save
errors.

---

**Q: Where are Boutique's log files?**

A: `%localappdata%\Boutique\logs\Boutique-YYYYMMDD.log`

Example: `C:\Users\YourName\AppData\Local\Boutique\logs\Boutique-20260206.log`

---

**Q: How do I reset Boutique settings?**

A: Delete the config directory: `%localappdata%\Boutique\.config\`

Boutique will recreate default settings on next launch.

---

## Advanced Tips

### Filter Optimization

**Tip 1: Use Specific Filters**

Broad filters (e.g., "all NPCs") are less efficient than specific filters (e.g., "BanditFaction + Female").

**Why**: Distribution frameworks check every NPC at runtime. Specific filters reduce checks.

**Example**:

Less efficient:

```ini
Outfit = MyOutfit|NONE|NONE|NONE|NONE
```

More efficient:

```ini
Outfit = MyOutfit|ActorTypeNPC|BanditFaction|NONE|F
```

---

**Tip 2: Avoid Overlapping Filters**

If multiple rules target the same NPCs, only one wins. Consolidate rules when possible.

**Example**:

Instead of:

```ini
Outfit = Bandit_Light|NONE|BanditFaction|NONE|NONE
Outfit = Bandit_Heavy|NONE|BanditFaction|NONE|NONE
```

Use finer filters:

```ini
Outfit = Bandit_Light|NONE|BanditFaction|NONE|NONE|NONE|50
Outfit = Bandit_Heavy|NONE|BanditFaction|NONE|NONE|NONE|50
```

(50% chance for each, randomly distributed)

---

### Batch Operations

**Tip 3: Create Multiple Outfits at Once**

Use the Outfit Creator's multi-select to speed up outfit creation:

1. Select 10-20 armor pieces
2. Group by type (light armor, heavy armor, robes)
3. Create outfit drafts for each group
4. Save all to one plugin

**Result**: One plugin with many outfits, ready for distribution.

---

**Tip 4: Duplicate Distribution Entries**

In the Create tab:

1. Create one distribution entry with filters
2. Right-click → **Duplicate**
3. Change only the outfit or one filter
4. Save all entries at once

**Result**: Faster distribution file creation.

---

### Working with Large Load Orders

**Tip 5: Use Plugin Blacklist**

Hide test plugins, patches, and high-poly mods from Boutique:

1. Settings → **Manage Blacklist**
2. Add plugins you don't need
3. Clutter-free plugin lists

---

**Tip 6: Filter Plugins by Category**

In Outfit Creator and Armor Patch tabs:

- Search box: Type "Armor" to show only armor plugins
- Sort: Click **Plugin Name** to alphabetize

---

### Distribution Priority Rules

**Tip 7: Control Load Order with File Names**

SPID and SkyPatcher load alphabetically. Use prefixes:

- `AA-`: High priority (loads first)
- `Z-`: Low priority (loads last, overrides others)
- `ZZ-`: Lowest priority (final override)

**Example**:

```
AA-CoreOutfits_DISTR.ini
Mod1_DISTR.ini
Mod2_DISTR.ini
ZZ-MyCustomOverrides_DISTR.ini
```

`ZZ-MyCustomOverrides` wins conflicts.

---

**Tip 8: Use SkyPatcher for Precision**

SPID is simple but limited. SkyPatcher offers:

- AND/OR/Exclude filters
- Multiple faction filters
- Outfit editing (add/remove items from outfits)

Use SkyPatcher when SPID can't express your filters.

---

## Reference: SPID Syntax

SPID (Spell Perk Item Distributor) uses line-based syntax in `*_DISTR.ini` files.

### Basic Syntax

```
FormType = Form|StringFilters|FormFilters|LevelFilters|TraitFilters|Count|Chance
```

**Positions**:

1. **FormType**: `Outfit`, `Spell`, `Perk`, `Item`, `Faction`, `Keyword`
2. **Form**: EditorID or FormID (`OutfitBandit` or `0x12345~MyMod.esp`)
3. **StringFilters**: String matching (EditorID, name, keywords)
4. **FormFilters**: Form matching (race, faction, class)
5. **LevelFilters**: Level range (e.g., `5/20`)
6. **TraitFilters**: Gender, unique, etc. (`F`, `M`, `U`)
7. **Count**: Number of items (for items, not outfits)
8. **Chance**: Percentage (0-100, default 100)

### String Filters

**Exact Match**:

```
Outfit = MyOutfit|ActorTypeNPC
```

**Exclude**:

```
Outfit = MyOutfit|-ActorTypeNPC
```

**Partial Match**:

```
Outfit = MyOutfit|*Guard
```

**Multiple (OR)**:

```
Outfit = MyOutfit|ActorTypeNPC,Bandit
```

**Combined (AND)**:

```
Outfit = MyOutfit|ActorTypeNPC+Bandit
```

### Form Filters

**Race**:

```
Outfit = MyOutfit|NONE|NordRace
```

**Faction**:

```
Outfit = MyOutfit|NONE|BanditFaction
```

**Combined (AND)**:

```
Outfit = MyOutfit|NONE|NordRace+BanditFaction
```

**Multiple (OR)**:

```
Outfit = MyOutfit|NONE|NordRace,BretonRace
```

### Level Filters

**Min Level**:

```
Outfit = MyOutfit|NONE|NONE|10/
```

**Level Range**:

```
Outfit = MyOutfit|NONE|NONE|10/20
```

**Exact Level**:

```
Outfit = MyOutfit|NONE|NONE|15/15
```

### Trait Filters

| Code | Meaning    |
|------|------------|
| `F`  | Female     |
| `M`  | Male       |
| `U`  | Unique     |
| `S`  | Summonable |
| `C`  | Child      |
| `L`  | Leveled    |
| `T`  | Teammate   |
| `D`  | Dead       |

**Example**:

```
Outfit = MyOutfit|NONE|NONE|NONE|F
```

(Female NPCs only)

**Exclude**:

```
Outfit = MyOutfit|NONE|NONE|NONE|-U
```

(Non-unique NPCs only)

### Chance

**50% Chance**:

```
Outfit = MyOutfit|NONE|NONE|NONE|NONE|NONE|50
```

### Complete Examples

**Example 1: Female Nord bandits**:

```
Outfit = Bandit_Female_Light|ActorTypeNPC|NordRace+BanditFaction|NONE|F
```

**Example 2: Level 10+ guards**:

```
Outfit = Guard_Heavy|ActorTypeNPC|GuardFaction|10/|NONE
```

**Example 3: 25% chance for all NPCs**:

```
Outfit = Rare_Outfit|NONE|NONE|NONE|NONE|NONE|25
```

---

## Reference: SkyPatcher Syntax

SkyPatcher uses modular filter-based syntax in `.ini` files.

### Basic Syntax

```
filterByXXX=value:filterByYYY=value:operation=value
```

**Components**:

- **Filters**: Target selection (which NPCs)
- **Operations**: What to do (set outfit, add items)

### FormID Format

**Full FormID** (recommended):

```
Skyrim.esm|000FDEAC
```

**Shortened** (works but less safe):

```
Skyrim.esm|FDEAC
```

**EditorID** (most filters):

```
VigilantOfStendarrFaction
```

### Filter Logic

**AND (all must match)**:

```
filterByKeywords=Keyword1,Keyword2
```

**OR (at least one must match)**:

```
filterByKeywordsOr=Keyword1,Keyword2
```

**Exclude**:

```
filterByKeywordsExcluded=Keyword1
```

### NPC Filters

**By NPC**:

```
filterByNpcs=Skyrim.esm|13BBF,Skyrim.esm|1B07A
```

**By Faction**:

```
filterByFactions=Skyrim.esm|000FDEAC
```

**By Race**:

```
filterByRaces=Skyrim.esm|000131E8
```

**By Keywords**:

```
filterByKeywords=Skyrim.esm|00013794
```

**By Gender**:

```
filterByGender=female
```

**By EditorID**:

```
filterByEditorIdContains=Bandit,Guard
```

### Operations

**Set Default Outfit**:

```
outfitDefault=MyMod.esp|FE000D65
```

**Set Sleep Outfit**:

```
outfitSleep=Skyrim.esm|D3E06
```

### Complete Examples

**Example 1: Female bandits**:

```
filterByFactions=Skyrim.esm|000FDEAC:filterByGender=female:outfitDefault=MyMod.esp|00001234
```

**Example 2: Multiple filters**:

```
filterByFactions=Skyrim.esm|0001BCC0:filterByKeywords=Skyrim.esm|00013794:filterByGender=female:outfitDefault=MyMod.esp|00001234
```

**Example 3: Specific NPCs**:

```
filterByNpcs=Skyrim.esm|13BBF:outfitDefault=Skyrim.esm|D3E05
```

---

## Reference: CDF Syntax

CDF (Container Distribution Framework) uses JSON for container loot distribution.

### JSON Structure

```json
{
  "rules": [
    {
      "friendlyName": "Description",
      "conditions": { ... },
      "changes": [ ... ]
    }
  ]
}
```

### Rule Types

Determined by `changes` fields:

| Fields                     | Rule Type           | Behavior                    |
|----------------------------|---------------------|-----------------------------|
| `add` only                 | Add                 | Adds items                  |
| `remove` only              | Remove              | Removes items               |
| `removeByKeywords` only    | Remove By Keywords  | Removes items with keywords |
| `add` + `remove`           | Replace             | Replaces items 1:1          |
| `add` + `removeByKeywords` | Replace By Keywords | Replaces keyword items      |

### FormID Format

```
0xFormID|PluginName.esp
```

Example:

```
0xF|Skyrim.esm
```

### Changes Examples

**Add 10 Gold**:

```json
{
  "changes": [
    {
      "add": ["0xF|Skyrim.esm"],
      "count": 10
    }
  ]
}
```

**Remove All Iron Ingots**:

```json
{
  "changes": [
    {
      "remove": "0x5ACE4|Skyrim.esm"
    }
  ]
}
```

**Remove Raw Food (by keyword)**:

```json
{
  "changes": [
    {
      "removeByKeywords": ["FoodRaw"]
    }
  ]
}
```

**Replace Iron with Gold**:

```json
{
  "changes": [
    {
      "remove": "0x5ACE4|Skyrim.esm",
      "add": ["0x5AD9E|Skyrim.esm"]
    }
  ]
}
```

### Conditions

**Target Specific Container**:

```json
{
  "conditions": {
    "containers": ["0x1EDDD|Skyrim.esm"]
  }
}
```

**Target Location**:

```json
{
  "conditions": {
    "locations": ["0x18A59|Skyrim.esm"]
  }
}
```

**Global Check**:

```json
{
  "conditions": {
    "globals": ["DragonsKilled|10.0"]
  }
}
```

**Player Skill**:

```json
{
  "conditions": {
    "playerSkills": ["Lockpicking|50.0"]
  }
}
```

### Special Flags

```json
{
  "conditions": {
    "bypassUnsafeContainers": true,
    "onlyVendors": true,
    "randomAdd": true
  }
}
```

### Complete Example

```json
{
  "rules": [
    {
      "friendlyName": "Add gold to Dwemer chests in Markarth when DragonsKilled >= 10",
      "conditions": {
        "containers": ["0x1EDDD|Skyrim.esm"],
        "globals": ["DragonsKilled|10.0"],
        "locations": ["0x18A59|Skyrim.esm"]
      },
      "changes": [
        {
          "add": ["0xF|Skyrim.esm"],
          "count": 10
        }
      ]
    }
  ]
}
```

---

## Credits & Resources

### Boutique Development

**Author**: [Your Name]

**Contributors**: [List contributors]

**Source Code**: [GitHub Repository]

### Distribution Frameworks

**SPID (Spell Perk Item Distributor)**:

- Author: powerofthree
- Nexus: [SPID Nexus Page]
- Documentation: [SPID Nexus Article]

**SkyPatcher**:

- Author: [SkyPatcher Author]
- Nexus: [SkyPatcher Nexus Page]

**CDF (Container Distribution Framework)**:

- Author: SeaSparrowOG
- Nexus: [CDF Nexus Page]
- GitHub: [CDF GitHub Wiki]

### Tools and Libraries

**Mutagen**: Bethesda plugin manipulation library

- GitHub: [Mutagen Repository]

**NiflySharp**: NIF mesh file reader

- GitHub: [NiflySharp Repository]

**HelixToolkit**: 3D visualization

- GitHub: [HelixToolkit Repository]

### Community Resources

**Skyrim Modding Community**:

- r/skyrimmods
- Nexus Mods Forums
- xEdit Discord

**Tutorials and Guides**:

- [SPID Syntax Guide on Nexus]
- [SkyPatcher Documentation]
- [CDF GitHub Wiki]

### Support

**Bug Reports**: [GitHub Issues]

**Feature Requests**: [GitHub Issues]

**Questions**: [Nexus Mods Comments]

---

**Thank you for using Boutique!**

*This documentation is version-controlled and maintained at the boutique.wiki repository.*

*Last Updated: February 2026*
