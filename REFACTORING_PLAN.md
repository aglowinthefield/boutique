# DistributionViewModel Refactoring Plan

## Current State Analysis

**File Size**: 1,830 lines  
**Responsibilities**: 3 distinct tabs + shared concerns  
**Complexity Issues**:
- Multiple responsibilities mixed in one class
- Complex conflict detection logic embedded in ViewModel
- File parsing logic mixed with presentation logic
- NPC/Outfit resolution helpers scattered throughout

## Refactoring Strategy

### Phase 1: Extract Tab-Specific ViewModels

#### 1.1 Create `DistributionFilesTabViewModel`
**Location**: `ViewModels/DistributionFilesTabViewModel.cs`  
**Responsibilities**:
- File discovery and listing (`RefreshAsync`)
- File selection (`SelectedFile`, `Files`)
- Line filtering (`LineFilter`, `FilteredLines`)
- Preview for distribution lines (`PreviewLineAsync`, `PreviewLineCommand`)

**Properties to Extract**:
- `Files` (ObservableCollection<DistributionFileViewModel>)
- `SelectedFile` (DistributionFileViewModel?)
- `LineFilter` (string)
- `FilteredLines` (IEnumerable<DistributionLine>)
- `IsLoading` (shared, but managed per-tab)
- `StatusMessage` (shared, but managed per-tab)

**Commands to Extract**:
- `RefreshCommand`
- `PreviewLineCommand`

**Dependencies**:
- `IDistributionDiscoveryService`
- `IArmorPreviewService`
- `IMutagenService`
- `ILogger`

**Estimated Lines**: ~200-250

---

#### 1.2 Create `DistributionEditTabViewModel`
**Location**: `ViewModels/DistributionEditTabViewModel.cs`  
**Responsibilities**:
- Distribution entry management (add/remove/select)
- NPC selection and assignment
- Outfit selection
- File loading/saving
- Preview generation
- Conflict detection (delegates to service)

**Properties to Extract**:
- `DistributionEntries` (ObservableCollection<DistributionEntryViewModel>)
- `SelectedEntry` (DistributionEntryViewModel?)
- `AvailableNpcs` (ObservableCollection<NpcRecordViewModel>)
- `FilteredNpcs` (ObservableCollection<NpcRecordViewModel>)
- `NpcSearchText` (string)
- `AvailableOutfits` (ObservableCollection<IOutfitGetter>)
- `AvailableDistributionFiles` (ObservableCollection<DistributionFileSelectionItem>)
- `SelectedDistributionFile` (DistributionFileSelectionItem?)
- `IsCreatingNewFile` (bool)
- `NewFileName` (string)
- `DistributionFilePath` (string)
- `DistributionPreviewText` (string)
- Conflict properties: `HasConflicts`, `ConflictsResolvedByFilename`, `ConflictSummary`, `SuggestedFileName`

**Commands to Extract**:
- `AddDistributionEntryCommand`
- `RemoveDistributionEntryCommand`
- `SelectEntryCommand`
- `AddSelectedNpcsToEntryCommand`
- `SaveDistributionFileCommand`
- `LoadDistributionFileCommand`
- `ScanNpcsCommand`
- `SelectDistributionFilePathCommand`
- `PreviewEntryCommand`

**Methods to Extract**:
- `AddDistributionEntry()`
- `RemoveDistributionEntry()`
- `SelectEntry()`
- `AddSelectedNpcsToEntry()`
- `SaveDistributionFileAsync()`
- `LoadDistributionFileAsync()`
- `ScanNpcsAsync()`
- `SelectDistributionFilePath()`
- `UpdateAvailableDistributionFiles()`
- `UpdateDistributionFilePathFromNewFileName()`
- `UpdateDistributionPreview()`
- `LoadAvailableOutfitsAsync()`
- `MergeOutfitsFromPatchFileAsync()`
- `EnsureOutfitsLoaded()`
- `CreateEntryViewModel()`
- `ResolveEntryOutfit()`
- `ResolveNpcFormKeys()`
- `ResolveNpcFormKey()`
- `UpdateFilteredNpcs()`
- `OnDistributionEntriesChanged()`
- `SubscribeToEntryChanges()`

**Dependencies**:
- `IDistributionFileWriterService`
- `INpcScanningService`
- `IDistributionConflictDetectionService` (new)
- `IArmorPreviewService`
- `IMutagenService`
- `SettingsViewModel`
- `ILogger`

**Estimated Lines**: ~600-700

---

#### 1.3 Create `DistributionNpcsTabViewModel`
**Location**: `ViewModels/DistributionNpcsTabViewModel.cs`  
**Responsibilities**:
- NPC outfit scanning
- NPC outfit assignment viewing
- Filtering and search
- Outfit contents display

**Properties to Extract**:
- `NpcOutfitAssignments` (ObservableCollection<NpcOutfitAssignmentViewModel>)
- `FilteredNpcOutfitAssignments` (ObservableCollection<NpcOutfitAssignmentViewModel>)
- `SelectedNpcAssignment` (NpcOutfitAssignmentViewModel?)
- `NpcOutfitSearchText` (string)
- `SelectedNpcOutfitContents` (string)

**Commands to Extract**:
- `ScanNpcOutfitsCommand`
- `PreviewNpcOutfitCommand`

**Methods to Extract**:
- `ScanNpcOutfitsAsync()`
- `PreviewNpcOutfitAsync()`
- `UpdateFilteredNpcOutfitAssignments()`
- `UpdateSelectedNpcOutfitContents()`

**Dependencies**:
- `INpcScanningService`
- `INpcOutfitResolutionService`
- `IDistributionDiscoveryService` (for getting files)
- `IArmorPreviewService`
- `IMutagenService`
- `SettingsViewModel`
- `ILogger`

**Estimated Lines**: ~200-250

---

### Phase 2: Extract Conflict Detection Service

#### 2.1 Create `IDistributionConflictDetectionService`
**Location**: `Services/IDistributionConflictDetectionService.cs`

**Interface**:
```csharp
public interface IDistributionConflictDetectionService
{
    /// <summary>
    /// Detects conflicts between new distribution entries and existing distribution files.
    /// </summary>
    ConflictDetectionResult DetectConflicts(
        IReadOnlyList<DistributionEntryViewModel> entries,
        IReadOnlyList<DistributionFileViewModel> existingFiles,
        string newFileName,
        ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache);
}
```

#### 2.2 Create `ConflictDetectionResult` Model
**Location**: `Models/ConflictDetectionResult.cs`

**Properties**:
- `HasConflicts` (bool)
- `ConflictsResolvedByFilename` (bool)
- `ConflictSummary` (string)
- `SuggestedFileName` (string)
- `Conflicts` (IReadOnlyList<NpcConflictInfo>)

#### 2.3 Create `DistributionConflictDetectionService`
**Location**: `Services/DistributionConflictDetectionService.cs`

**Methods to Extract**:
- `DetectConflicts()` - Main entry point
- `BuildExistingDistributionMap()` - Builds NPC FormKey -> distribution info map
- `ExtractNpcFormKeysFromLine()` - Parses NPC FormKeys from distribution lines
- `ExtractOutfitNameFromLine()` - Extracts outfit name from line
- `DoesFileLoadAfterAll()` - Checks filename ordering
- `CalculateZPrefixedFileName()` - Generates Z-prefixed filename
- `TryParseFormKeyLocal()` - Helper for parsing FormKeys

**Dependencies**:
- `ILogger`

**Estimated Lines**: ~300-400

---

### Phase 3: Extract File Parsing Utilities

#### 3.1 Create `DistributionLineParser` Utility
**Location**: `Utilities/DistributionLineParser.cs`

**Static Methods**:
- `ExtractNpcFormKeysFromLine()` - Extracts NPC FormKeys from SkyPatcher/SPID lines
- `ExtractOutfitNameFromLine()` - Extracts outfit name from line
- `TryParseFormKey()` - Parses FormKey from string

**Estimated Lines**: ~150-200

---

### Phase 4: Refactor Main ViewModel

#### 4.1 Simplified `DistributionViewModel`
**Location**: `ViewModels/DistributionViewModel.cs` (refactored)

**New Responsibilities**:
- Tab coordination (`SelectedTabIndex`)
- Shared state management (`IsLoading`, `StatusMessage`)
- Delegation to tab ViewModels
- Preview interaction (`ShowPreview`)

**Properties**:
- `SelectedTabIndex` (int)
- `IsLoading` (bool) - aggregated from tabs
- `StatusMessage` (string) - aggregated from tabs
- `Settings` (SettingsViewModel) - exposed for binding
- `FilesTab` (DistributionFilesTabViewModel)
- `EditTab` (DistributionEditTabViewModel)
- `NpcsTab` (DistributionNpcsTabViewModel)
- `ShowPreview` (Interaction<ArmorPreviewScene, Unit>)

**Methods**:
- Constructor - wires up tab ViewModels
- Tab change handlers
- Status aggregation from tabs

**Estimated Lines**: ~150-200

---

## Implementation Order

### Step 1: Extract Conflict Detection Service
**Why First**: It's the most self-contained and can be tested independently.

1. Create `IDistributionConflictDetectionService` interface
2. Create `ConflictDetectionResult` model
3. Create `DistributionConflictDetectionService` implementation
4. Update `DistributionViewModel` to use the service
5. Test conflict detection still works

### Step 2: Extract File Parsing Utilities
**Why Second**: These are pure functions, easy to extract and test.

1. Create `DistributionLineParser` utility class
2. Move parsing methods from ViewModel
3. Update conflict detection service to use parser
4. Test parsing still works

### Step 3: Extract Files Tab ViewModel
**Why Third**: Simplest tab, good starting point.

1. Create `DistributionFilesTabViewModel`
2. Move Files tab properties and methods
3. Update main ViewModel to delegate
4. Update XAML bindings (if needed)
5. Test Files tab functionality

### Step 4: Extract NPCs Tab ViewModel
**Why Fourth**: Second simplest, mostly independent.

1. Create `DistributionNpcsTabViewModel`
2. Move NPCs tab properties and methods
3. Update main ViewModel to delegate
4. Update XAML bindings (if needed)
5. Test NPCs tab functionality

### Step 5: Extract Edit Tab ViewModel
**Why Last**: Most complex, depends on conflict service.

1. Create `DistributionEditTabViewModel`
2. Move Edit tab properties and methods
3. Wire up conflict detection service
4. Update main ViewModel to delegate
5. Update XAML bindings (if needed)
6. Test Edit tab functionality

### Step 6: Simplify Main ViewModel
1. Remove extracted code
2. Add tab ViewModel properties
3. Wire up tab coordination
4. Aggregate status from tabs
5. Final testing

---

## XAML Binding Updates

The XAML will need to be updated to bind through the tab ViewModels:

**Before**:
```xaml
<DataGrid ItemsSource="{Binding Files}" />
```

**After**:
```xaml
<DataGrid ItemsSource="{Binding FilesTab.Files}" />
```

Or use a `ContentControl` with `DataTemplate` per tab to keep bindings clean.

---

## Benefits

1. **Reduced Complexity**: Main ViewModel goes from 1,830 lines to ~150-200 lines
2. **Single Responsibility**: Each ViewModel handles one tab
3. **Testability**: Tab ViewModels and services can be tested independently
4. **Maintainability**: Changes to one tab don't affect others
5. **Reusability**: Conflict detection service can be reused elsewhere
6. **Readability**: Smaller, focused classes are easier to understand

---

## Estimated Final Line Counts

- `DistributionViewModel`: ~150-200 lines (from 1,830)
- `DistributionFilesTabViewModel`: ~200-250 lines
- `DistributionEditTabViewModel`: ~600-700 lines
- `DistributionNpcsTabViewModel`: ~200-250 lines
- `DistributionConflictDetectionService`: ~300-400 lines
- `DistributionLineParser`: ~150-200 lines

**Total**: ~1,600-2,000 lines (similar total, but much better organized)

---

## Migration Notes

1. **Breaking Changes**: XAML bindings will need updates
2. **DI Registration**: New services need to be registered in `App.xaml.cs`
3. **Testing**: Each extracted component should be tested before moving to next step
4. **Incremental**: Can be done one step at a time, testing after each

---

## Risk Mitigation

1. **Incremental Refactoring**: Extract one piece at a time, test thoroughly
2. **Preserve Behavior**: Keep existing functionality exactly as-is
3. **Version Control**: Commit after each successful extraction
4. **XAML Updates**: Update bindings incrementally, test UI after each change

