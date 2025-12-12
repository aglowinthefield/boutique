using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Boutique.Models;
using Boutique.Services;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Boutique.ViewModels;

public enum DistributionTab
{
    Create = 0,
    Files = 1,
    Npcs = 2,
    Outfits = 3
}

public class DistributionViewModel : ReactiveObject
{
    private readonly ILogger _logger;
    private readonly SettingsViewModel _settings;

    public DistributionViewModel(
        DistributionDiscoveryService discoveryService,
        DistributionFileWriterService fileWriterService,
        NpcScanningService npcScanningService,
        NpcOutfitResolutionService npcOutfitResolutionService,
        DistributionConflictDetectionService conflictDetectionService,
        SettingsViewModel settings,
        ArmorPreviewService armorPreviewService,
        MutagenService mutagenService,
        ILogger logger)
    {
        _settings = settings;
        _logger = logger.ForContext<DistributionViewModel>();

        // Create tab ViewModels
        FilesTab = new DistributionFilesTabViewModel(
            discoveryService,
            armorPreviewService,
            mutagenService,
            settings,
            logger);

        EditTab = new DistributionEditTabViewModel(
            fileWriterService,
            npcScanningService,
            conflictDetectionService,
            armorPreviewService,
            mutagenService,
            settings,
            logger);

        NpcsTab = new DistributionNpcsTabViewModel(
            npcScanningService,
            npcOutfitResolutionService,
            discoveryService,
            armorPreviewService,
            mutagenService,
            settings,
            logger);

        OutfitsTab = new DistributionOutfitsTabViewModel(
            npcScanningService,
            npcOutfitResolutionService,
            discoveryService,
            armorPreviewService,
            mutagenService,
            settings,
            logger);

        // Wire up preview interactions - forward tab previews to main preview
        FilesTab.ShowPreview.RegisterHandler(async interaction =>
        {
            await ShowPreview.Handle(interaction.Input);
            interaction.SetOutput(Unit.Default);
        });
        EditTab.ShowPreview.RegisterHandler(async interaction =>
        {
            await ShowPreview.Handle(interaction.Input);
            interaction.SetOutput(Unit.Default);
        });
        NpcsTab.ShowPreview.RegisterHandler(async interaction =>
        {
            await ShowPreview.Handle(interaction.Input);
            interaction.SetOutput(Unit.Default);
        });
        OutfitsTab.ShowPreview.RegisterHandler(async interaction =>
        {
            await ShowPreview.Handle(interaction.Input);
            interaction.SetOutput(Unit.Default);
        });

        // Wire up Files tab refresh to update Edit and NPCs tabs
        // Subscribe to Files collection changes to update other tabs
        FilesTab.WhenAnyValue(vm => vm.Files)
            .Subscribe(files =>
            {
                var fileList = files.ToList();
                EditTab.SetDistributionFiles(fileList);
                EditTab.SetDistributionFilesInternal(fileList);
                NpcsTab.SetDistributionFilesInternal(fileList);
                OutfitsTab.SetDistributionFilesInternal(fileList);
                // Notify parent bindings that Files collection changed
                this.RaisePropertyChanged(nameof(Files));
            });
        
        // Also subscribe to CollectionChanged to catch in-place modifications
        FilesTab.Files.CollectionChanged += (sender, e) =>
        {
            var fileList = FilesTab.Files.ToList();
            EditTab.SetDistributionFiles(fileList);
            EditTab.SetDistributionFilesInternal(fileList);
            NpcsTab.SetDistributionFilesInternal(fileList);
            OutfitsTab.SetDistributionFilesInternal(fileList);
            // Notify parent bindings that Files collection changed
            this.RaisePropertyChanged(nameof(Files));
        };
        
        // Forward property changes from FilesTab to parent for bindings
        FilesTab.WhenAnyValue(vm => vm.SelectedFile)
            .Subscribe(_ => 
            {
                this.RaisePropertyChanged(nameof(SelectedFile));
                // FilteredLines depends on SelectedFile, so notify when it changes
                this.RaisePropertyChanged(nameof(FilteredLines));
            });
        FilesTab.WhenAnyValue(vm => vm.LineFilter)
            .Subscribe(_ => 
            {
                this.RaisePropertyChanged(nameof(LineFilter));
                // FilteredLines depends on LineFilter, so notify when it changes
                this.RaisePropertyChanged(nameof(FilteredLines));
            });
        
        // Forward property changes from EditTab to parent for bindings
        EditTab.WhenAnyValue(vm => vm.DistributionFilePath)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(DistributionFilePath)));
        EditTab.WhenAnyValue(vm => vm.DistributionPreviewText)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(DistributionPreviewText)));
        EditTab.WhenAnyValue(vm => vm.SelectedDistributionFile)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedDistributionFile)));
        EditTab.WhenAnyValue(vm => vm.AvailableDistributionFiles)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(AvailableDistributionFiles)));
        EditTab.WhenAnyValue(vm => vm.DistributionEntries)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(DistributionEntries)));
        EditTab.WhenAnyValue(vm => vm.SelectedEntry)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedEntry)));
        EditTab.WhenAnyValue(vm => vm.IsCreatingNewFile)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(IsCreatingNewFile)));
        EditTab.WhenAnyValue(vm => vm.ShowNewFileNameInput)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(ShowNewFileNameInput)));
        EditTab.WhenAnyValue(vm => vm.NewFileName)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(NewFileName)));
        EditTab.WhenAnyValue(vm => vm.HasConflicts)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(HasConflicts)));
        EditTab.WhenAnyValue(vm => vm.ConflictsResolvedByFilename)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(ConflictsResolvedByFilename)));
        EditTab.WhenAnyValue(vm => vm.ConflictSummary)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(ConflictSummary)));
        EditTab.WhenAnyValue(vm => vm.SuggestedFileName)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SuggestedFileName)));
        EditTab.WhenAnyValue(vm => vm.DistributionFormat)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(DistributionFormat)));
        
        // Forward property changes from NpcsTab to parent for bindings
        NpcsTab.WhenAnyValue(vm => vm.SelectedNpcAssignment)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedNpcAssignment)));
        NpcsTab.WhenAnyValue(vm => vm.NpcOutfitAssignments)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(NpcOutfitAssignments)));
        NpcsTab.WhenAnyValue(vm => vm.FilteredNpcOutfitAssignments)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(FilteredNpcOutfitAssignments)));
        NpcsTab.WhenAnyValue(vm => vm.SelectedNpcOutfitContents)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedNpcOutfitContents)));

        // Wire up Edit tab file save to refresh Files tab
        EditTab.FileSaved += async filePath =>
        {
            await FilesTab.RefreshCommand.Execute();
            // Update Edit tab with refreshed files
            EditTab.SetDistributionFiles(FilesTab.Files.ToList());
            EditTab.SetDistributionFilesInternal(FilesTab.Files.ToList());
        };
        
        // Subscribe to collection changes for collections that need forwarding
        EditTab.AvailableDistributionFiles.CollectionChanged += (sender, e) => 
            this.RaisePropertyChanged(nameof(AvailableDistributionFiles));
        EditTab.DistributionEntries.CollectionChanged += (sender, e) => 
            this.RaisePropertyChanged(nameof(DistributionEntries));
        EditTab.FilteredNpcs.CollectionChanged += (sender, e) => 
            this.RaisePropertyChanged(nameof(FilteredNpcs));
        EditTab.FilteredFactions.CollectionChanged += (sender, e) => 
            this.RaisePropertyChanged(nameof(FilteredFactions));
        EditTab.FilteredKeywords.CollectionChanged += (sender, e) => 
            this.RaisePropertyChanged(nameof(FilteredKeywords));
        EditTab.FilteredRaces.CollectionChanged += (sender, e) => 
            this.RaisePropertyChanged(nameof(FilteredRaces));
        NpcsTab.NpcOutfitAssignments.CollectionChanged += (sender, e) => 
            this.RaisePropertyChanged(nameof(NpcOutfitAssignments));
        NpcsTab.FilteredNpcOutfitAssignments.CollectionChanged += (sender, e) => 
            this.RaisePropertyChanged(nameof(FilteredNpcOutfitAssignments));
        
        // Forward search text changes
        EditTab.WhenAnyValue(vm => vm.NpcSearchText)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(NpcSearchText)));
        EditTab.WhenAnyValue(vm => vm.FactionSearchText)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(FactionSearchText)));
        EditTab.WhenAnyValue(vm => vm.KeywordSearchText)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(KeywordSearchText)));
        EditTab.WhenAnyValue(vm => vm.RaceSearchText)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(RaceSearchText)));
        NpcsTab.WhenAnyValue(vm => vm.NpcOutfitSearchText)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(NpcOutfitSearchText)));
        OutfitsTab.WhenAnyValue(vm => vm.OutfitSearchText)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(OutfitSearchText)));
        OutfitsTab.WhenAnyValue(vm => vm.HideVanillaOutfits)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(HideVanillaOutfits)));
        OutfitsTab.WhenAnyValue(vm => vm.SelectedOutfit)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedOutfit)));
        OutfitsTab.WhenAnyValue(vm => vm.Outfits)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(Outfits)));
        OutfitsTab.WhenAnyValue(vm => vm.FilteredOutfits)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(FilteredOutfits)));
        OutfitsTab.WhenAnyValue(vm => vm.SelectedOutfitNpcAssignments)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedOutfitNpcAssignments)));
        OutfitsTab.SelectedOutfitNpcAssignments.CollectionChanged += (sender, e) => 
            this.RaisePropertyChanged(nameof(SelectedOutfitNpcAssignments));

        // Aggregate loading state from tabs
        this.WhenAnyValue(
            vm => vm.FilesTab.IsLoading,
            vm => vm.EditTab.IsLoading,
            vm => vm.NpcsTab.IsLoading,
            vm => vm.OutfitsTab.IsLoading,
            (files, edit, npcs, outfits) => files || edit || npcs || outfits)
            .Subscribe(loading => IsLoading = loading);

        // Aggregate status messages from tabs (prioritize non-empty messages)
        this.WhenAnyValue(
            vm => vm.FilesTab.StatusMessage,
            vm => vm.EditTab.StatusMessage,
            vm => vm.NpcsTab.StatusMessage,
            vm => vm.OutfitsTab.StatusMessage,
            (files, edit, npcs, outfits) => 
                !string.IsNullOrWhiteSpace(edit) ? edit :
                !string.IsNullOrWhiteSpace(npcs) ? npcs :
                !string.IsNullOrWhiteSpace(outfits) ? outfits :
                !string.IsNullOrWhiteSpace(files) ? files :
                "Ready")
            .Subscribe(msg => StatusMessage = msg);

        // Handle tab changes
        this.WhenAnyValue(vm => vm.SelectedTabIndex)
            .Subscribe(index =>
            {
                this.RaisePropertyChanged(nameof(IsEditMode));

                if (index == (int)DistributionTab.Create)
                {
                    // Initialize Create tab when selected - always update to ensure latest files
                    var fileList = FilesTab.Files.ToList();
                    EditTab.SetDistributionFiles(fileList);
                    EditTab.SetDistributionFilesInternal(fileList);
                    
                    // Select "New File" by default if nothing selected
                    if (EditTab.SelectedDistributionFile == null)
                    {
                        var newFileItem = EditTab.AvailableDistributionFiles.FirstOrDefault(f => f.IsNewFile);
                        if (newFileItem != null)
                        {
                            EditTab.SelectedDistributionFile = newFileItem;
                        }
                    }
                }
                else if (index == (int)DistributionTab.Files)
                {
                    // Auto-refresh Files tab when selected if files haven't been loaded yet
                    if (FilesTab.Files.Count == 0 && !FilesTab.IsLoading && !string.IsNullOrWhiteSpace(_settings.SkyrimDataPath))
                    {
                        _logger.Debug("Files tab selected, triggering auto-refresh");
                        _ = FilesTab.RefreshCommand.Execute();
                    }
                }
                else if (index == (int)DistributionTab.Npcs)
                {
                    // Update NPCs tab with files from Files tab
                    NpcsTab.SetDistributionFilesInternal(FilesTab.Files.ToList());
                    
                    // Auto-scan NPC outfits when NPCs tab is selected (if not already scanned)
                    if (NpcsTab.NpcOutfitAssignments.Count == 0 && !NpcsTab.IsLoading)
                    {
                        _logger.Debug("NPCs tab selected, triggering auto-scan");
                        _ = NpcsTab.ScanNpcOutfitsCommand.Execute();
                    }
                }
                else if (index == (int)DistributionTab.Outfits)
                {
                    // Update Outfits tab with files from Files tab
                    OutfitsTab.SetDistributionFilesInternal(FilesTab.Files.ToList());
                    
                    // Auto-load outfits when Outfits tab is selected (if not already loaded)
                    if (OutfitsTab.Outfits.Count == 0 && !OutfitsTab.IsLoading)
                    {
                        _logger.Debug("Outfits tab selected, triggering auto-load");
                        _ = OutfitsTab.LoadOutfitsCommand.Execute();
                    }
                }
            });
    }

    /// <summary>
    /// Exposes SettingsViewModel for data binding in SettingsPanelView.
    /// This ensures consistent settings state across all tabs.
    /// </summary>
    public SettingsViewModel Settings => _settings;

    /// <summary>UI: TabControl SelectedIndex binding - controls which tab (Files/Edit/NPCs) is visible.</summary>
    [Reactive] public int SelectedTabIndex { get; set; }

    /// <summary>Computed property indicating if Create tab is currently selected.</summary>
    public bool IsEditMode => SelectedTabIndex == (int)DistributionTab.Create;

    /// <summary>UI: ProgressBar visibility - true when any tab is loading (scanning files, loading data, etc.).</summary>
    [Reactive] public bool IsLoading { get; private set; }

    /// <summary>UI: TextBlock at bottom showing status messages from all tabs (prioritizes Edit > NPCs > Files).</summary>
    [Reactive] public string StatusMessage { get; private set; } = "Ready";

    /// <summary>Internal ViewModel for Files tab - not directly bound to UI.</summary>
    public DistributionFilesTabViewModel FilesTab { get; }

    /// <summary>Internal ViewModel for Edit tab - not directly bound to UI.</summary>
    public DistributionEditTabViewModel EditTab { get; }

    /// <summary>Internal ViewModel for NPCs tab - not directly bound to UI.</summary>
    public DistributionNpcsTabViewModel NpcsTab { get; }

    /// <summary>Internal ViewModel for Outfits tab - not directly bound to UI.</summary>
    public DistributionOutfitsTabViewModel OutfitsTab { get; }

    /// <summary>Interaction for showing outfit preview windows (used by all tabs).</summary>
    public Interaction<ArmorPreviewScene, Unit> ShowPreview { get; } = new();

    // Expose tab properties for backward compatibility with XAML bindings
    // These delegate to the appropriate tab ViewModel

    // ============================================================================
    // FILES TAB PROPERTIES
    // ============================================================================
    
    /// <summary>UI: DataGrid in Files tab showing discovered distribution files.</summary>
    public ObservableCollection<DistributionFileViewModel> Files => FilesTab.Files;
    
    /// <summary>UI: Selected file in Files tab DataGrid, used to show preview.</summary>
    public DistributionFileViewModel? SelectedFile
    {
        get => FilesTab.SelectedFile;
        set => FilesTab.SelectedFile = value;
    }
    
    /// <summary>UI: TextBox in Files tab for filtering preview lines.</summary>
    public string LineFilter
    {
        get => FilesTab.LineFilter;
        set => FilesTab.LineFilter = value;
    }
    
    /// <summary>UI: Filtered lines shown in Files tab preview based on LineFilter.</summary>
    public IEnumerable<DistributionLine> FilteredLines => FilesTab.FilteredLines;
    
    /// <summary>UI: "Refresh" button in Files tab to reload distribution files.</summary>
    public ReactiveCommand<Unit, Unit> RefreshCommand => FilesTab.RefreshCommand;
    
    /// <summary>UI: Eye icon button on each preview line to show outfit preview.</summary>
    public ReactiveCommand<DistributionLine, Unit> PreviewLineCommand => FilesTab.PreviewLineCommand;

    // ============================================================================
    // EDIT TAB PROPERTIES
    // ============================================================================
    
    /// <summary>UI: ItemsControl in Edit tab showing list of distribution entries being edited.</summary>
    public ObservableCollection<DistributionEntryViewModel> DistributionEntries => EditTab.DistributionEntries;
    
    /// <summary>UI: Currently selected distribution entry (highlighted with blue border).</summary>
    public DistributionEntryViewModel? SelectedEntry
    {
        get => EditTab.SelectedEntry;
        set => EditTab.SelectedEntry = value;
    }
    
    /// <summary>UI: ComboBox in Edit tab for selecting outfit for each entry.</summary>
    public ObservableCollection<IOutfitGetter> AvailableOutfits => EditTab.AvailableOutfits;
    
    /// <summary>UI: ComboBox in Edit tab for selecting which distribution file to edit (or "New File").</summary>
    public ObservableCollection<DistributionFileSelectionItem> AvailableDistributionFiles => EditTab.AvailableDistributionFiles;
    
    /// <summary>UI: Selected item in the distribution file ComboBox.</summary>
    public DistributionFileSelectionItem? SelectedDistributionFile
    {
        get => EditTab.SelectedDistributionFile;
        set => EditTab.SelectedDistributionFile = value;
    }
    
    /// <summary>UI: ComboBox in Edit tab for selecting distribution format (SPID or SkyPatcher).</summary>
    public IReadOnlyList<DistributionFileType> AvailableFormats => EditTab.AvailableFormats;
    
    /// <summary>UI: Selected format in the format ComboBox (SPID or SkyPatcher).</summary>
    public DistributionFileType DistributionFormat
    {
        get => EditTab.DistributionFormat;
        set => EditTab.DistributionFormat = value;
    }
    
    /// <summary>UI: Indicates if user is creating a new file (vs editing existing).</summary>
    public bool IsCreatingNewFile => EditTab.IsCreatingNewFile;
    
    /// <summary>UI: Controls visibility of filename TextBox (shown when creating new file).</summary>
    public bool ShowNewFileNameInput => EditTab.ShowNewFileNameInput;
    
    /// <summary>UI: TextBox in Edit tab for entering new distribution file name.</summary>
    public string NewFileName
    {
        get => EditTab.NewFileName;
        set => EditTab.NewFileName = value;
    }
    
    /// <summary>UI: Full path to the distribution file being edited (read-only display).</summary>
    public string DistributionFilePath => EditTab.DistributionFilePath;
    
    /// <summary>UI: TextBox in Edit tab showing preview of generated distribution file content.</summary>
    public string DistributionPreviewText => EditTab.DistributionPreviewText;
    
    /// <summary>UI: Warning banner visibility - true when conflicts detected with existing files.</summary>
    public bool HasConflicts => EditTab.HasConflicts;
    
    /// <summary>UI: Success banner visibility - true when conflicts exist but are resolved by filename ordering.</summary>
    public bool ConflictsResolvedByFilename => EditTab.ConflictsResolvedByFilename;
    
    /// <summary>UI: Text in conflict warning/success banner explaining detected conflicts.</summary>
    public string ConflictSummary => EditTab.ConflictSummary;
    
    /// <summary>UI: Suggested filename with Z-prefix shown in conflict warning banner.</summary>
    public string SuggestedFileName => EditTab.SuggestedFileName;
    
    // Filter collections for NPCs, Factions, Keywords, Races tabs in Edit tab
    /// <summary>UI: All available NPCs loaded from plugins (used in NPCs filter tab).</summary>
    public ObservableCollection<NpcRecordViewModel> AvailableNpcs => EditTab.AvailableNpcs;
    
    /// <summary>UI: Filtered NPCs shown in DataGrid based on NpcSearchText.</summary>
    public ObservableCollection<NpcRecordViewModel> FilteredNpcs => EditTab.FilteredNpcs;
    
    /// <summary>UI: All available factions loaded from plugins (used in Factions filter tab).</summary>
    public ObservableCollection<FactionRecordViewModel> AvailableFactions => EditTab.AvailableFactions;
    
    /// <summary>UI: Filtered factions shown in DataGrid based on FactionSearchText.</summary>
    public ObservableCollection<FactionRecordViewModel> FilteredFactions => EditTab.FilteredFactions;
    
    /// <summary>UI: All available keywords loaded from plugins (used in Keywords filter tab).</summary>
    public ObservableCollection<KeywordRecordViewModel> AvailableKeywords => EditTab.AvailableKeywords;
    
    /// <summary>UI: Filtered keywords shown in DataGrid based on KeywordSearchText.</summary>
    public ObservableCollection<KeywordRecordViewModel> FilteredKeywords => EditTab.FilteredKeywords;
    
    /// <summary>UI: All available races loaded from plugins (used in Races filter tab).</summary>
    public ObservableCollection<RaceRecordViewModel> AvailableRaces => EditTab.AvailableRaces;
    
    /// <summary>UI: Filtered races shown in DataGrid based on RaceSearchText.</summary>
    public ObservableCollection<RaceRecordViewModel> FilteredRaces => EditTab.FilteredRaces;
    
    // Search text boxes for filtering
    /// <summary>UI: TextBox in NPCs filter tab for searching NPCs by name/EditorID.</summary>
    public string NpcSearchText
    {
        get => EditTab.NpcSearchText;
        set => EditTab.NpcSearchText = value;
    }
    
    /// <summary>UI: TextBox in Factions filter tab for searching factions by name/EditorID.</summary>
    public string FactionSearchText
    {
        get => EditTab.FactionSearchText;
        set => EditTab.FactionSearchText = value;
    }
    
    /// <summary>UI: TextBox in Keywords filter tab for searching keywords by EditorID.</summary>
    public string KeywordSearchText
    {
        get => EditTab.KeywordSearchText;
        set => EditTab.KeywordSearchText = value;
    }
    
    /// <summary>UI: TextBox in Races filter tab for searching races by name/EditorID.</summary>
    public string RaceSearchText
    {
        get => EditTab.RaceSearchText;
        set => EditTab.RaceSearchText = value;
    }
    // Edit tab commands
    /// <summary>UI: "Add Entry" button in Edit tab to create new distribution entry.</summary>
    public ReactiveCommand<Unit, Unit> AddDistributionEntryCommand => EditTab.AddDistributionEntryCommand;
    
    /// <summary>UI: "Remove" button on each distribution entry to delete it.</summary>
    public ReactiveCommand<DistributionEntryViewModel, Unit> RemoveDistributionEntryCommand => EditTab.RemoveDistributionEntryCommand;
    
    /// <summary>UI: Radio button "Assign" on each entry to select it for editing.</summary>
    public ReactiveCommand<DistributionEntryViewModel, Unit> SelectEntryCommand => EditTab.SelectEntryCommand;
    
    /// <summary>UI: "Add Selected NPCs to Entry" button in NPCs filter tab.</summary>
    public ReactiveCommand<Unit, Unit> AddSelectedNpcsToEntryCommand => EditTab.AddSelectedNpcsToEntryCommand;
    
    /// <summary>UI: "Add Selected Factions to Entry" button in Factions filter tab.</summary>
    public ReactiveCommand<Unit, Unit> AddSelectedFactionsToEntryCommand => EditTab.AddSelectedFactionsToEntryCommand;
    
    /// <summary>UI: "Add Selected Keywords to Entry" button in Keywords filter tab.</summary>
    public ReactiveCommand<Unit, Unit> AddSelectedKeywordsToEntryCommand => EditTab.AddSelectedKeywordsToEntryCommand;
    
    /// <summary>UI: "Add Selected Races to Entry" button in Races filter tab.</summary>
    public ReactiveCommand<Unit, Unit> AddSelectedRacesToEntryCommand => EditTab.AddSelectedRacesToEntryCommand;
    
    /// <summary>UI: "Save File" button in Edit tab to write distribution file to disk.</summary>
    public ReactiveCommand<Unit, Unit> SaveDistributionFileCommand => EditTab.SaveDistributionFileCommand;
    
    /// <summary>UI: "Load File" button in Edit tab to load existing distribution file.</summary>
    public ReactiveCommand<Unit, Unit> LoadDistributionFileCommand => EditTab.LoadDistributionFileCommand;
    
    /// <summary>UI: "Scan NPCs" button (if present) to load NPCs from plugins.</summary>
    public ReactiveCommand<Unit, Unit> ScanNpcsCommand => EditTab.ScanNpcsCommand;
    
    /// <summary>UI: "Browse..." button next to filename TextBox to select file location.</summary>
    public ReactiveCommand<Unit, Unit> SelectDistributionFilePathCommand => EditTab.SelectDistributionFilePathCommand;
    
    /// <summary>UI: Eye icon button on each entry to preview the selected outfit.</summary>
    public ReactiveCommand<DistributionEntryViewModel, Unit> PreviewEntryCommand => EditTab.PreviewEntryCommand;
    
    /// <summary>Called when outfit ComboBox opens to ensure outfits are loaded.</summary>
    public void EnsureOutfitsLoaded() => EditTab.EnsureOutfitsLoaded();

    // ============================================================================
    // NPCs TAB PROPERTIES
    // ============================================================================
    
    /// <summary>UI: DataGrid in NPCs tab showing all NPCs with their resolved outfit assignments.</summary>
    public ObservableCollection<NpcOutfitAssignmentViewModel> NpcOutfitAssignments => NpcsTab.NpcOutfitAssignments;
    
    /// <summary>UI: Selected row in NPCs tab DataGrid, used to show distribution details and outfit contents.</summary>
    public NpcOutfitAssignmentViewModel? SelectedNpcAssignment
    {
        get => NpcsTab.SelectedNpcAssignment;
        set => NpcsTab.SelectedNpcAssignment = value;
    }
    
    /// <summary>UI: Filtered NPCs shown in DataGrid based on NpcOutfitSearchText.</summary>
    public ObservableCollection<NpcOutfitAssignmentViewModel> FilteredNpcOutfitAssignments => NpcsTab.FilteredNpcOutfitAssignments;
    
    /// <summary>UI: TextBox in NPCs tab for searching NPCs by name/EditorID/outfit/mod.</summary>
    public string NpcOutfitSearchText
    {
        get => NpcsTab.NpcOutfitSearchText;
        set => NpcsTab.NpcOutfitSearchText = value;
    }
    
    /// <summary>UI: TextBox in NPCs tab detail panel showing formatted outfit contents (armor pieces).</summary>
    public string SelectedNpcOutfitContents => NpcsTab.SelectedNpcOutfitContents;
    
    /// <summary>UI: "↻ Refresh" button in NPCs tab to rescan distribution files for NPC assignments.</summary>
    public ReactiveCommand<Unit, Unit> ScanNpcOutfitsCommand => NpcsTab.ScanNpcOutfitsCommand;
    
    /// <summary>UI: "Preview Outfit" button in NPCs tab detail panel to show 3D outfit preview.</summary>
    public ReactiveCommand<NpcOutfitAssignmentViewModel, Unit> PreviewNpcOutfitCommand => NpcsTab.PreviewNpcOutfitCommand;

    // ============================================================================
    // OUTFITS TAB PROPERTIES
    // ============================================================================
    
    /// <summary>UI: DataGrid in Outfits tab showing all outfits from the load order.</summary>
    public ObservableCollection<OutfitRecordViewModel> Outfits => OutfitsTab.Outfits;
    
    /// <summary>UI: Selected row in Outfits tab DataGrid, used to show which NPCs it's distributed to.</summary>
    public OutfitRecordViewModel? SelectedOutfit
    {
        get => OutfitsTab.SelectedOutfit;
        set => OutfitsTab.SelectedOutfit = value;
    }
    
    /// <summary>UI: Filtered outfits shown in DataGrid based on OutfitSearchText.</summary>
    public ObservableCollection<OutfitRecordViewModel> FilteredOutfits => OutfitsTab.FilteredOutfits;
    
    /// <summary>UI: TextBox in Outfits tab for searching outfits by EditorID/FormKey/mod.</summary>
    public string OutfitSearchText
    {
        get => OutfitsTab.OutfitSearchText;
        set => OutfitsTab.OutfitSearchText = value;
    }
    
    /// <summary>UI: Checkbox in Outfits tab to hide vanilla outfits.</summary>
    public bool HideVanillaOutfits
    {
        get => OutfitsTab.HideVanillaOutfits;
        set => OutfitsTab.HideVanillaOutfits = value;
    }
    
    /// <summary>UI: List of NPCs that have the selected outfit distributed to them.</summary>
    public ObservableCollection<NpcOutfitAssignmentViewModel> SelectedOutfitNpcAssignments => OutfitsTab.SelectedOutfitNpcAssignments;
    
    /// <summary>UI: "Load Outfits" button in Outfits tab to load outfits from load order.</summary>
    public ReactiveCommand<Unit, Unit> LoadOutfitsCommand => OutfitsTab.LoadOutfitsCommand;
    
    /// <summary>UI: "Preview Outfit" button in Outfits tab to show 3D outfit preview.</summary>
    public ReactiveCommand<OutfitRecordViewModel, Unit> PreviewOutfitCommand => OutfitsTab.PreviewOutfitCommand;

    // ============================================================================
    // SHARED/UTILITY PROPERTIES
    // ============================================================================
    
    /// <summary>Indicates if MutagenService is initialized (used to enable/disable UI features).</summary>
    public bool IsInitialized => EditTab.IsInitialized || NpcsTab.IsInitialized || OutfitsTab.IsInitialized;

    /// <summary>UI: TextBlock at top showing the Skyrim data path being scanned.</summary>
    public string DataPath => _settings.SkyrimDataPath;
}
