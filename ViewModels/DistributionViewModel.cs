using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive;
using System.Reactive.Linq;
using Boutique.Models;
using Boutique.Services;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Boutique.ViewModels;

public enum DistributionTab
{
    Create = 0,
    Npcs = 1,
    Outfits = 2
}

public partial class DistributionViewModel : ReactiveObject
{
    private readonly GameDataCacheService _cache;
    private readonly ILogger _logger;

    [Reactive] private bool _isLoading;

    [Reactive] private int _selectedTabIndex;

    [Reactive] private string _statusMessage = "Ready";

    public DistributionViewModel(
        DistributionFileWriterService fileWriterService,
        NpcScanningService npcScanningService,
        NpcOutfitResolutionService npcOutfitResolutionService,
        GameDataCacheService gameDataCache,
        SettingsViewModel settings,
        ArmorPreviewService armorPreviewService,
        MutagenService mutagenService,
        GuiSettingsService guiSettings,
        ILogger logger)
    {
        Settings = settings;
        _cache = gameDataCache;
        _logger = logger.ForContext<DistributionViewModel>();

        // Commands for loading/refreshing distribution files (calls cache methods)
        RefreshCommand = ReactiveCommand.CreateFromTask(gameDataCache.ReloadAsync);
        EnsureLoadedCommand = ReactiveCommand.CreateFromTask(gameDataCache.EnsureLoadedAsync);

        EditTab = new DistributionEditTabViewModel(
            fileWriterService,
            armorPreviewService,
            mutagenService,
            gameDataCache,
            settings,
            guiSettings,
            logger);

        NpcsTab = new DistributionNpcsTabViewModel(
            armorPreviewService,
            mutagenService,
            gameDataCache,
            logger);

        OutfitsTab = new DistributionOutfitsTabViewModel(
            npcScanningService,
            npcOutfitResolutionService,
            armorPreviewService,
            mutagenService,
            gameDataCache,
            settings,
            logger);

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

        EditTab.WhenAnyValue(vm => vm.DistributionFilePath)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(DistributionFilePath)));
        EditTab.WhenAnyValue(vm => vm.DistributionFileContent)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(DistributionFileContent)));
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

        NpcsTab.WhenAnyValue(vm => vm.SelectedNpcAssignment)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedNpcAssignment)));
        NpcsTab.WhenAnyValue(vm => vm.SelectedNpcOutfitContents)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedNpcOutfitContents)));
        NpcsTab.WhenAnyValue(vm => vm.SelectedNpcFilterData)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedNpcFilterData)));

        NpcsTab.WhenAnyValue(vm => vm.SelectedGenderFilter)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedGenderFilter)));
        NpcsTab.WhenAnyValue(vm => vm.SelectedUniqueFilter)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedUniqueFilter)));
        NpcsTab.WhenAnyValue(vm => vm.SelectedTemplatedFilter)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedTemplatedFilter)));
        NpcsTab.WhenAnyValue(vm => vm.SelectedChildFilter)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedChildFilter)));
        NpcsTab.WhenAnyValue(vm => vm.SelectedFaction)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedFactionForNpcFilter)));
        NpcsTab.WhenAnyValue(vm => vm.SelectedRace)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedRaceForNpcFilter)));
        NpcsTab.WhenAnyValue(vm => vm.SelectedKeyword)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(SelectedKeywordForNpcFilter)));
        NpcsTab.WhenAnyValue(vm => vm.GeneratedSpidSyntax)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(GeneratedSpidSyntax)));
        NpcsTab.WhenAnyValue(vm => vm.GeneratedSkyPatcherSyntax)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(GeneratedSkyPatcherSyntax)));
        NpcsTab.WhenAnyValue(vm => vm.FilterDescription)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(FilterDescription)));
        NpcsTab.WhenAnyValue(vm => vm.HasActiveFilters)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(HasActiveFilters)));
        NpcsTab.WhenAnyValue(vm => vm.FilteredCount)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(FilteredCount)));
        NpcsTab.WhenAnyValue(vm => vm.TotalCount)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(TotalCount)));

        EditTab.FileSaved += async _ =>
        {
            // Refresh cache to pick up new/updated files
            await _cache.ReloadAsync();
        };

        NpcsTab.FilterCopied += (_, copiedFilter) =>
        {
            EditTab.CopiedFilter = copiedFilter;
            _logger.Debug("Filter copied from NPCs tab: {Description}", copiedFilter.Description);
        };

        OutfitsTab.OutfitCopied += (_, copiedOutfit) =>
        {
            OutfitCopiedToCreator?.Invoke(this, copiedOutfit);
            _logger.Debug("Outfit copy requested from Outfits tab: {Description}", copiedOutfit.Description);
        };

        EditTab.WhenAnyValue(vm => vm.CopiedFilter)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(CopiedFilter)));
        EditTab.WhenAnyValue(vm => vm.HasCopiedFilter)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(HasCopiedFilter)));

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
        ((INotifyCollectionChanged)NpcsTab.FilteredNpcOutfitAssignments).CollectionChanged += (sender, e) =>
            this.RaisePropertyChanged(nameof(FilteredNpcOutfitAssignments));
        ((INotifyCollectionChanged)NpcsTab.AvailableFactions).CollectionChanged += (sender, e) =>
            this.RaisePropertyChanged(nameof(AvailableFactionsForNpcFilter));
        ((INotifyCollectionChanged)NpcsTab.AvailableRaces).CollectionChanged += (sender, e) =>
            this.RaisePropertyChanged(nameof(AvailableRacesForNpcFilter));
        ((INotifyCollectionChanged)NpcsTab.AvailableKeywords).CollectionChanged += (sender, e) =>
            this.RaisePropertyChanged(nameof(AvailableKeywordsForNpcFilter));

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

        this.WhenAnyValue(
                vm => vm.EditTab.IsLoading,
                vm => vm.NpcsTab.IsLoading,
                vm => vm.OutfitsTab.IsLoading,
                (edit, npcs, outfits) => edit || npcs || outfits)
            .Subscribe(loading => IsLoading = loading);

        this.WhenAnyValue(
                vm => vm.EditTab.StatusMessage,
                vm => vm.NpcsTab.StatusMessage,
                vm => vm.OutfitsTab.StatusMessage,
                (edit, npcs, outfits) => GetFirstNonEmptyStatus(edit, npcs, outfits))
            .Subscribe(msg => StatusMessage = msg);

        this.WhenAnyValue(vm => vm.SelectedTabIndex)
            .Subscribe(index =>
            {
                this.RaisePropertyChanged(nameof(IsEditMode));

                switch (index)
                {
                    case (int)DistributionTab.Create:
                    {
                        if (EditTab.SelectedDistributionFile == null)
                        {
                            var newFileItem = EditTab.AvailableDistributionFiles.FirstOrDefault(f => f.IsNewFile);
                            if (newFileItem != null)
                            {
                                EditTab.SelectedDistributionFile = newFileItem;
                            }
                        }

                        break;
                    }

                    case (int)DistributionTab.Npcs:
                        break;
                    case (int)DistributionTab.Outfits:
                    {
                        if (OutfitsTab.Outfits.Count == 0 && !OutfitsTab.IsLoading)
                        {
                            _logger.Debug("Outfits tab selected, triggering auto-load");
                            _ = OutfitsTab.LoadOutfitsCommand.Execute();
                        }

                        break;
                    }
                }
            });
    }

    public SettingsViewModel Settings { get; }

    public bool IsEditMode => SelectedTabIndex == (int)DistributionTab.Create;

    public DistributionEditTabViewModel EditTab { get; }
    public DistributionNpcsTabViewModel NpcsTab { get; }
    public DistributionOutfitsTabViewModel OutfitsTab { get; }
    public Interaction<ArmorPreviewSceneCollection, Unit> ShowPreview { get; } = new();

    private static string GetFirstNonEmptyStatus(params string[] statuses) =>
        statuses.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "Ready";

    /// <summary>
    ///     Event raised when an outfit should be copied to the Outfit Creator tab.
    /// </summary>
    public event EventHandler<CopiedOutfit>? OutfitCopiedToCreator;

    #region Distribution Files

    public ReadOnlyObservableCollection<DistributionFileViewModel> Files => _cache.AllDistributionFiles;
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> EnsureLoadedCommand { get; }

    #endregion

    #region Edit Tab Properties

    public ObservableCollection<DistributionEntryViewModel> DistributionEntries => EditTab.DistributionEntries;

    public DistributionEntryViewModel? SelectedEntry
    {
        get => EditTab.SelectedEntry;
        set => EditTab.SelectedEntry = value;
    }

    public ObservableCollection<IOutfitGetter> AvailableOutfits => EditTab.AvailableOutfits;

    public ObservableCollection<DistributionFileSelectionItem> AvailableDistributionFiles =>
        EditTab.AvailableDistributionFiles;

    public DistributionFileSelectionItem? SelectedDistributionFile
    {
        get => EditTab.SelectedDistributionFile;
        set => EditTab.SelectedDistributionFile = value;
    }

    public IReadOnlyList<DistributionFileType> AvailableFormats => EditTab.AvailableFormats;

    public DistributionFileType DistributionFormat
    {
        get => EditTab.DistributionFormat;
        set => EditTab.DistributionFormat = value;
    }

    public bool IsCreatingNewFile => EditTab.IsCreatingNewFile;
    public bool ShowNewFileNameInput => EditTab.ShowNewFileNameInput;

    public string NewFileName
    {
        get => EditTab.NewFileName;
        set => EditTab.NewFileName = value;
    }

    public string DistributionFilePath => EditTab.DistributionFilePath;
    public string DistributionFileContent => EditTab.DistributionFileContent;
    public bool HasConflicts => EditTab.HasConflicts;
    public bool ConflictsResolvedByFilename => EditTab.ConflictsResolvedByFilename;
    public string ConflictSummary => EditTab.ConflictSummary;
    public string SuggestedFileName => EditTab.SuggestedFileName;
    public ReadOnlyObservableCollection<NpcRecordViewModel> AvailableNpcs => EditTab.AvailableNpcs;
    public ObservableCollection<NpcRecordViewModel> FilteredNpcs => EditTab.FilteredNpcs;
    public ReadOnlyObservableCollection<FactionRecordViewModel> AvailableFactions => EditTab.AvailableFactions;
    public ObservableCollection<FactionRecordViewModel> FilteredFactions => EditTab.FilteredFactions;
    public ReadOnlyObservableCollection<KeywordRecordViewModel> AvailableKeywords => EditTab.AvailableKeywords;
    public ObservableCollection<KeywordRecordViewModel> FilteredKeywords => EditTab.FilteredKeywords;
    public ReadOnlyObservableCollection<RaceRecordViewModel> AvailableRaces => EditTab.AvailableRaces;
    public ObservableCollection<RaceRecordViewModel> FilteredRaces => EditTab.FilteredRaces;

    public string NpcSearchText
    {
        get => EditTab.NpcSearchText;
        set => EditTab.NpcSearchText = value;
    }

    public string FactionSearchText
    {
        get => EditTab.FactionSearchText;
        set => EditTab.FactionSearchText = value;
    }

    public string KeywordSearchText
    {
        get => EditTab.KeywordSearchText;
        set => EditTab.KeywordSearchText = value;
    }

    public string RaceSearchText
    {
        get => EditTab.RaceSearchText;
        set => EditTab.RaceSearchText = value;
    }

    public ReactiveCommand<Unit, Unit> AddDistributionEntryCommand => EditTab.AddDistributionEntryCommand;

    public ReactiveCommand<DistributionEntryViewModel, Unit> RemoveDistributionEntryCommand =>
        EditTab.RemoveDistributionEntryCommand;

    public ReactiveCommand<DistributionEntryViewModel, Unit> SelectEntryCommand => EditTab.SelectEntryCommand;
    public ReactiveCommand<Unit, Unit> AddSelectedNpcsToEntryCommand => EditTab.AddSelectedNpcsToEntryCommand;
    public ReactiveCommand<Unit, Unit> AddSelectedFactionsToEntryCommand => EditTab.AddSelectedFactionsToEntryCommand;
    public ReactiveCommand<Unit, Unit> AddSelectedKeywordsToEntryCommand => EditTab.AddSelectedKeywordsToEntryCommand;
    public ReactiveCommand<Unit, Unit> AddSelectedRacesToEntryCommand => EditTab.AddSelectedRacesToEntryCommand;
    public ReactiveCommand<Unit, Unit> SaveDistributionFileCommand => EditTab.SaveDistributionFileCommand;
    public ReactiveCommand<Unit, Unit> ScanNpcsCommand => EditTab.ScanNpcsCommand;
    public ReactiveCommand<Unit, Unit> SelectDistributionFilePathCommand => EditTab.SelectDistributionFilePathCommand;
    public ReactiveCommand<DistributionEntryViewModel, Unit> PreviewEntryCommand => EditTab.PreviewEntryCommand;
    public ReactiveCommand<Unit, Unit> PasteFilterToEntryCommand => EditTab.PasteFilterToEntryCommand;
    public CopiedNpcFilter? CopiedFilter => EditTab.CopiedFilter;
    public bool HasCopiedFilter => EditTab.HasCopiedFilter;

    public void EnsureOutfitsLoaded() => EditTab.EnsureOutfitsLoaded();

    #endregion

    #region NPCs Tab Properties

    public NpcOutfitAssignmentViewModel? SelectedNpcAssignment
    {
        get => NpcsTab.SelectedNpcAssignment;
        set => NpcsTab.SelectedNpcAssignment = value;
    }

    public ReadOnlyObservableCollection<NpcOutfitAssignmentViewModel> FilteredNpcOutfitAssignments =>
        NpcsTab.FilteredNpcOutfitAssignments;

    public string NpcOutfitSearchText
    {
        get => NpcsTab.NpcOutfitSearchText;
        set => NpcsTab.NpcOutfitSearchText = value;
    }

    public string SelectedNpcOutfitContents => NpcsTab.SelectedNpcOutfitContents;
    public NpcFilterData? SelectedNpcFilterData => NpcsTab.SelectedNpcFilterData;
    public ReactiveCommand<Unit, Unit> ScanNpcOutfitsCommand => NpcsTab.ScanNpcOutfitsCommand;

    public ReactiveCommand<NpcOutfitAssignmentViewModel, Unit> PreviewNpcOutfitCommand =>
        NpcsTab.PreviewNpcOutfitCommand;

    public ReactiveCommand<OutfitDistribution, Unit> PreviewDistributionOutfitCommand =>
        NpcsTab.PreviewDistributionOutfitCommand;

    public IReadOnlyList<string> GenderFilterOptions => NpcsTab.GenderFilterOptions;

    public string SelectedGenderFilter
    {
        get => NpcsTab.SelectedGenderFilter;
        set => NpcsTab.SelectedGenderFilter = value;
    }

    public IReadOnlyList<string> UniqueFilterOptions => NpcsTab.UniqueFilterOptions;

    public string SelectedUniqueFilter
    {
        get => NpcsTab.SelectedUniqueFilter;
        set => NpcsTab.SelectedUniqueFilter = value;
    }

    public IReadOnlyList<string> TemplatedFilterOptions => NpcsTab.TemplatedFilterOptions;

    public string SelectedTemplatedFilter
    {
        get => NpcsTab.SelectedTemplatedFilter;
        set => NpcsTab.SelectedTemplatedFilter = value;
    }

    public IReadOnlyList<string> ChildFilterOptions => NpcsTab.ChildFilterOptions;

    public string SelectedChildFilter
    {
        get => NpcsTab.SelectedChildFilter;
        set => NpcsTab.SelectedChildFilter = value;
    }

    public ReadOnlyObservableCollection<FactionRecordViewModel> AvailableFactionsForNpcFilter => NpcsTab.AvailableFactions;

    public FactionRecordViewModel? SelectedFactionForNpcFilter
    {
        get => NpcsTab.SelectedFaction;
        set => NpcsTab.SelectedFaction = value;
    }

    public ReadOnlyObservableCollection<RaceRecordViewModel> AvailableRacesForNpcFilter => NpcsTab.AvailableRaces;

    public RaceRecordViewModel? SelectedRaceForNpcFilter
    {
        get => NpcsTab.SelectedRace;
        set => NpcsTab.SelectedRace = value;
    }

    public ReadOnlyObservableCollection<KeywordRecordViewModel> AvailableKeywordsForNpcFilter => NpcsTab.AvailableKeywords;

    public KeywordRecordViewModel? SelectedKeywordForNpcFilter
    {
        get => NpcsTab.SelectedKeyword;
        set => NpcsTab.SelectedKeyword = value;
    }

    public ReactiveCommand<Unit, Unit> ClearFiltersCommand => NpcsTab.ClearFiltersCommand;
    public ReactiveCommand<Unit, Unit> CopyFilterCommand => NpcsTab.CopyFilterCommand;
    public string GeneratedSpidSyntax => NpcsTab.GeneratedSpidSyntax;
    public string GeneratedSkyPatcherSyntax => NpcsTab.GeneratedSkyPatcherSyntax;
    public string FilterDescription => NpcsTab.FilterDescription;
    public bool HasActiveFilters => NpcsTab.HasActiveFilters;
    public int FilteredCount => NpcsTab.FilteredCount;
    public int TotalCount => NpcsTab.TotalCount;

    #endregion

    #region Outfits Tab Properties

    public ObservableCollection<OutfitRecordViewModel> Outfits => OutfitsTab.Outfits;

    public OutfitRecordViewModel? SelectedOutfit
    {
        get => OutfitsTab.SelectedOutfit;
        set => OutfitsTab.SelectedOutfit = value;
    }

    public ObservableCollection<OutfitRecordViewModel> FilteredOutfits => OutfitsTab.FilteredOutfits;

    public string OutfitSearchText
    {
        get => OutfitsTab.OutfitSearchText;
        set => OutfitsTab.OutfitSearchText = value;
    }

    public bool HideVanillaOutfits
    {
        get => OutfitsTab.HideVanillaOutfits;
        set => OutfitsTab.HideVanillaOutfits = value;
    }

    public ObservableCollection<NpcOutfitAssignmentViewModel> SelectedOutfitNpcAssignments =>
        OutfitsTab.SelectedOutfitNpcAssignments;

    public ReactiveCommand<Unit, Unit> LoadOutfitsCommand => OutfitsTab.LoadOutfitsCommand;
    public ReactiveCommand<OutfitRecordViewModel, Unit> PreviewOutfitCommand => OutfitsTab.PreviewOutfitCommand;

    #endregion

    #region Shared Properties

    public bool IsInitialized => EditTab.IsInitialized || NpcsTab.IsInitialized || OutfitsTab.IsInitialized;
    public string DataPath => Settings.SkyrimDataPath;

    #endregion
}
