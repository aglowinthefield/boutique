using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using Boutique.Models;
using Boutique.Services;
using Boutique.Utilities;
using DynamicData;
using DynamicData.Binding;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Boutique.ViewModels;

public partial class OutfitCreatorViewModel : ReactiveObject, IDisposable
{
  private readonly Subject<Unit>                    _autoSaveTrigger = new();
  private readonly IObservable<bool>                _canCopyExistingOutfits;
  private readonly IObservable<bool>                _canCreateOutfit;
  private readonly IObservable<bool>                _canLoadOutfitPlugin;
  private readonly IObservable<bool>                _canSaveOutfits;
  private readonly CompositeDisposable              _disposables = new();
  private readonly OutfitDraftManager               _draftManager;
  private readonly ILogger                          _logger;
  private readonly MutagenService                   _mutagenService;
  private readonly SourceList<ArmorRecordViewModel> _outfitArmorsSource  = new();
  private readonly SourceList<string>               _outfitPluginsSource = new();
  private readonly PatchingService                  _patchingService;
  private readonly ArmorPreviewService              _previewService;

  [Reactive] private bool _hasExistingPluginOutfits;
  [Reactive] private bool _hasOutfitDrafts;
  [Reactive] private bool _hasPendingOutfitDeletions;
  [Reactive] private bool _isCreatingOutfits;

  private            string? _lastLoadedOutfitPlugin;
  [Reactive] private int     _outfitArmorsTotalCount;
  [Reactive] private string  _outfitPluginSearchText = string.Empty;
  [Reactive] private string  _outfitSearchText       = string.Empty;
  [Reactive] private int     _selectedOutfitArmorCount;
  private            IList   _selectedOutfitArmors = new List<ArmorRecordViewModel>();
  [Reactive] private string? _selectedOutfitArmorType;
  [Reactive] private string? _selectedOutfitSlot;
  [Reactive] private string  _statusMessage = "Ready";
  private            bool    _suppressPluginDeselection;

  public OutfitCreatorViewModel(
    OutfitDraftManager draftManager,
    MutagenService mutagenService,
    ArmorPreviewService previewService,
    PatchingService patchingService,
    SettingsViewModel settings,
    ILogger logger)
  {
    _draftManager    = draftManager;
    _mutagenService  = mutagenService;
    _previewService  = previewService;
    _patchingService = patchingService;
    Settings         = settings;
    _logger          = logger.ForContext<OutfitCreatorViewModel>();

    // Wire up draft manager events
    _draftManager.StatusChanged += message => StatusMessage = message;
    _draftManager.DraftModified += TriggerAutoSave;
    _draftManager.RequestNameAsync = async tuple =>
      await RequestOutfitName.Handle(tuple).ToTask();
    _draftManager.PreviewDraftAsync = PreviewDraftAsync;
    _draftManager.ConfirmDeleteAsync = async message =>
      await ConfirmDelete.Handle(message).ToTask();

    _draftManager.WhenAnyValue(m => m.HasDrafts)
                 .Subscribe(v => HasOutfitDrafts = v);
    _draftManager.WhenAnyValue(m => m.HasPendingDeletions)
                 .Subscribe(v => HasPendingOutfitDeletions = v);
    _draftManager.WhenAnyValue(m => m.HasExistingOutfits)
                 .Subscribe(v => HasExistingPluginOutfits = v);

    OutfitDrafts              = _draftManager.QueueItems;
    ExistingOutfits           = _draftManager.ExistingOutfits;
    HasOutfitDrafts           = _draftManager.HasDrafts;
    HasExistingPluginOutfits  = _draftManager.HasExistingOutfits;
    HasPendingOutfitDeletions = _draftManager.HasPendingDeletions;

    // Auto-save logic
    _disposables.Add(
      _autoSaveTrigger
        .Throttle(TimeSpan.FromMilliseconds(1500))
        .ObserveOn(RxApp.MainThreadScheduler)
        .Where(_ => (HasOutfitDrafts || HasPendingOutfitDeletions) && !IsCreatingOutfits)
        .SelectMany(_ => Observable.FromAsync(SaveOutfitsAsync))
        .Subscribe());

    // Configure filtering
    ConfigureOutfitPluginsFiltering();

    // Setup can-execute observables
    _canCreateOutfit = this.WhenAnyValue(x => x.SelectedOutfitArmorCount, count => count > 0);
    _canSaveOutfits = this.WhenAnyValue(
      x => x.HasOutfitDrafts,
      x => x.HasPendingOutfitDeletions,
      x => x.IsCreatingOutfits,
      (hasDrafts, hasDeletions, isBusy) => (hasDrafts || hasDeletions) && !isBusy);
    _canLoadOutfitPlugin =
      this.WhenAnyValue(x => x.SelectedOutfitPlugin, plugin => !string.IsNullOrWhiteSpace(plugin));
    _canCopyExistingOutfits = this.WhenAnyValue(x => x.HasExistingPluginOutfits);

    // Auto-load from output plugin when patch filename changes or on initialization
    Settings.WhenAnyValue(x => x.PatchFileName)
            .Where(_ => _mutagenService.IsInitialized)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOn(RxApp.MainThreadScheduler)
            .SelectMany(_ => Observable.FromAsync(LoadOutfitsFromOutputPluginAsync))
            .Subscribe();

    // Also load when MutagenService initializes (if PatchFileName is already set)
    _mutagenService.Initialized += async (_, _) =>
    {
      if (!string.IsNullOrWhiteSpace(Settings.PatchFileName))
      {
        await LoadOutfitsFromOutputPluginAsync();
      }
    };
  }

  public Interaction<(string Prompt, string DefaultValue), string?> RequestOutfitName { get; } = new();
  public Interaction<string, bool> ConfirmDelete { get; } = new();
  public Interaction<ArmorPreviewSceneCollection, Unit> ShowPreview { get; } = new();
  public Interaction<MissingMastersResult, bool> HandleMissingMasters { get; } = new();
  public Interaction<(string Title, string Message), Unit> ShowError { get; } = new();

  public SettingsViewModel Settings { get; }

  public ReadOnlyObservableCollection<string> FilteredOutfitPlugins { get; private set; } = null!;
  public ReadOnlyObservableCollection<ArmorRecordViewModel> FilteredOutfitArmors { get; private set; } = null!;

  public List<string> OutfitArmorTypeFilterOptions { get; } = ["(All)", "Heavy", "Light", "Clothing"];

  public List<string> OutfitSlotFilterOptions { get; } =
    [
      "(All)", "Head", "Hair", "Body", "Hands", "Forearms", "Amulet", "Ring", "Feet", "Calves", "Shield",
      "LongHair", "Circlet", "Ears"
    ];

  public IList SelectedOutfitArmors
  {
    get => _selectedOutfitArmors;
    set
    {
      if (value.Equals(_selectedOutfitArmors))
      {
        return;
      }

      _selectedOutfitArmors = value;
      this.RaisePropertyChanged();
      SelectedOutfitArmorCount = _selectedOutfitArmors.OfType<ArmorRecordViewModel>().Count();
    }
  }

  public string? SelectedOutfitPlugin
  {
    get;
    set
    {
      if (string.Equals(value, field, StringComparison.Ordinal))
      {
        return;
      }

      if (string.IsNullOrWhiteSpace(value) && _suppressPluginDeselection)
      {
        return;
      }

      _draftManager.ClearExistingOutfits();

      this.RaiseAndSetIfChanged(ref field, value);
      _logger.Information("Selected outfit plugin set to {Plugin}", value ?? "<none>");

      _lastLoadedOutfitPlugin = null;

      if (string.IsNullOrWhiteSpace(value))
      {
        _outfitArmorsSource.Clear();
        OutfitArmorsTotalCount = 0;
        SelectedOutfitArmors   = Array.Empty<ArmorRecordViewModel>();
        OutfitSearchText       = string.Empty;
        return;
      }

      _ = LoadOutfitPluginAsync();
    }
  }

  public ReadOnlyObservableCollection<IOutfitQueueItem> OutfitDrafts { get; }
  public ReadOnlyObservableCollection<ExistingOutfitViewModel> ExistingOutfits { get; }

  public void Dispose()
  {
    _mutagenService.Initialized    -= OnMutagenInitialized;
    _mutagenService.PluginsChanged -= OnPluginsChanged;
    _disposables.Dispose();
    _autoSaveTrigger.Dispose();
    GC.SuppressFinalize(this);
  }

  private void ConfigureOutfitPluginsFiltering()
  {
    // Subscribe to events
    _mutagenService.Initialized    += OnMutagenInitialized;
    _mutagenService.PluginsChanged += OnPluginsChanged;

    // Load immediately if already initialized
    if (_mutagenService.IsInitialized)
    {
      _ = LoadOutfitPluginsAsync();
    }

    _disposables.Add(
      _outfitPluginsSource.Connect()
                          .Filter(
                            this.WhenAnyValue(x => x.OutfitPluginSearchText)
                                .Throttle(TimeSpan.FromMilliseconds(150))
                                .Select(BuildPluginPredicate))
                          .Sort(SortExpressionComparer<string>.Ascending(p => p))
                          .ObserveOn(RxApp.MainThreadScheduler)
                          .Bind(out var filteredPlugins)
                          .DisposeMany()
                          .Subscribe());

    FilteredOutfitPlugins = filteredPlugins;

    _disposables.Add(
      _outfitArmorsSource.Connect()
                         .Filter(
                           this.WhenAnyValue(
                                 x => x.OutfitSearchText,
                                 x => x.SelectedOutfitArmorType,
                                 x => x.SelectedOutfitSlot)
                               .Throttle(TimeSpan.FromMilliseconds(150))
                               .Select(tuple => BuildArmorPredicate(tuple.Item1, tuple.Item2, tuple.Item3)))
                         .Sort(SortExpressionComparer<ArmorRecordViewModel>.Ascending(a => a.DisplayName))
                         .ObserveOn(RxApp.MainThreadScheduler)
                         .Bind(out var filteredArmors)
                         .DisposeMany()
                         .Subscribe());

    FilteredOutfitArmors = filteredArmors;
  }

  private static Func<string, bool> BuildPluginPredicate(string searchText) =>
    string.IsNullOrWhiteSpace(searchText)
      ? _ => true
      : plugin => plugin.Contains(searchText, StringComparison.OrdinalIgnoreCase);

  private static Func<ArmorRecordViewModel, bool> BuildArmorPredicate(
    string? searchText,
    string? armorType,
    string? slot)
  {
    return armor =>
    {
      if (!string.IsNullOrWhiteSpace(searchText) &&
          !armor.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) &&
          !armor.EditorID.Contains(searchText, StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }

      if (!string.IsNullOrWhiteSpace(armorType) && armorType != "(All)" &&
          !string.Equals(armor.ArmorType, armorType, StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }

      if (!string.IsNullOrWhiteSpace(slot) && slot != "(All)" &&
          !armor.SlotSummary.Contains(slot, StringComparison.OrdinalIgnoreCase))
      {
        return false;
      }

      return true;
    };
  }

  [ReactiveCommand(CanExecute = nameof(_canLoadOutfitPlugin))]
  public async Task LoadOutfitPluginAsync(bool forceReload = true)
  {
    var plugin = SelectedOutfitPlugin;
    if (string.IsNullOrWhiteSpace(plugin))
    {
      return;
    }

    if (!forceReload && string.Equals(
          plugin,
          _lastLoadedOutfitPlugin,
          StringComparison.OrdinalIgnoreCase))
    {
      _logger.Debug("Plugin {Plugin} already loaded, skipping reload.", plugin);
      return;
    }

    await LoadOutfitArmorsAsync(plugin);
    await LoadExistingOutfitsAsync(plugin);
  }

  private async Task LoadOutfitArmorsAsync(string plugin)
  {
    if (string.IsNullOrWhiteSpace(plugin))
    {
      SelectedOutfitArmors = Array.Empty<ArmorRecordViewModel>();
      return;
    }

    try
    {
      StatusMessage = $"Loading armors from {plugin}...";
      _logger.Information("Loading outfit armors from plugin {Plugin}", plugin);

      var armorsFromPlugin = await _mutagenService.LoadArmorsFromPluginAsync(plugin);
      var armors = armorsFromPlugin
                   .Select(a => new ArmorRecordViewModel(a, _mutagenService.LinkCache))
                   .ToList();

      _logger.Debug("Found {Count} armors in {Plugin}", armors.Count, plugin);

      _outfitArmorsSource.Edit(list =>
      {
        list.Clear();
        list.AddRange(armors);
      });

      OutfitArmorsTotalCount  = armors.Count;
      _lastLoadedOutfitPlugin = plugin;

      SelectedOutfitArmors = FilteredOutfitArmors.Any()
                               ? new List<ArmorRecordViewModel> { FilteredOutfitArmors[0] }
                               : Array.Empty<ArmorRecordViewModel>();

      StatusMessage = $"Loaded {armors.Count} armors from {plugin}.";
      _logger.Information("Loaded {Count} outfit armors from {Plugin}", armors.Count, plugin);
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to load outfit armors from plugin {Plugin}", plugin);
      StatusMessage = $"Error loading armors from {plugin}: {ex.Message}";
      await ShowError.Handle(("Error Loading Plugin", $"Failed to load armors from {plugin}:\n{ex.Message}"));
    }
  }

  private Task<int> LoadExistingOutfitsAsync(string plugin) =>
    _draftManager.LoadExistingOutfitsAsync(
      plugin,
      _mutagenService.LinkCache,
      _mutagenService.LoadOutfitsFromPluginAsync,
      p => string.Equals(
        SelectedOutfitPlugin,
        p,
        StringComparison.OrdinalIgnoreCase));

  [ReactiveCommand(CanExecute = nameof(_canCopyExistingOutfits))]
  private void CopyExistingOutfits() => _draftManager.CopyExistingOutfits(_mutagenService.LinkCache);

  private async Task LoadOutfitsFromOutputPluginAsync()
  {
    var outputPlugin = Settings.PatchFileName;
    if (string.IsNullOrWhiteSpace(outputPlugin))
    {
      _logger.Debug("No output plugin configured, skipping auto-load of existing outfits.");
      return;
    }

    var targetModKey = ModKey.FromFileName(outputPlugin);
    _draftManager.CurrentPatchName = outputPlugin;
    _draftManager.ClearDraftsFromOtherPlugins(targetModKey);

    var patchPath = Settings.FullOutputPath;
    if (string.IsNullOrEmpty(patchPath) || !File.Exists(patchPath))
    {
      _logger.Debug("Patch file does not exist at {Path}, skipping auto-load.", patchPath);
      return;
    }

    var missingMastersResult = await _patchingService.CheckMissingMastersAsync(patchPath);
    if (missingMastersResult.HasMissingMasters)
    {
      _logger.Warning(
        "Missing masters detected in patch {Plugin}: {Masters}",
        outputPlugin,
        string.Join(", ", missingMastersResult.MissingMasters.Select(m => m.MissingMaster.FileName)));

      var shouldClean = await HandleMissingMasters.Handle(missingMastersResult);
      if (shouldClean)
      {
        var (success, message) = await _patchingService.CleanPatchMissingMastersAsync(
                                   patchPath,
                                   missingMastersResult.AllAffectedOutfits);

        if (success)
        {
          StatusMessage = message;
          _logger.Information("Patch cleaned: {Message}", message);
        }
        else
        {
          _logger.Error("Failed to clean patch: {Message}", message);
          await ShowError.Handle(("Error Cleaning Patch", message));
          return;
        }
      }
      else
      {
        _logger.Information("User chose to add masters back instead of cleaning patch.");
        return;
      }
    }

    // Load outfits from the output plugin
    var outfits = await _mutagenService.LoadOutfitsFromPluginAsync(outputPlugin);

    // Add them as drafts to the queue
    _draftManager.AddDraftsFromOutfits(
      outfits,
      _mutagenService.LinkCache!,
      targetModKey,
      (formKey, _) => GetWinningModForOutfit(formKey));

    _logger.Information("Auto-loaded outfits from {Plugin}", outputPlugin);
  }

  public async Task SyncOutfitPluginWithTargetAsync(string targetPlugin, bool forceOutfitReload)
  {
    if (string.IsNullOrWhiteSpace(targetPlugin))
    {
      return;
    }

    var shouldSwitch = string.IsNullOrWhiteSpace(SelectedOutfitPlugin) ||
                       string.Equals(
                         SelectedOutfitPlugin,
                         MainViewModel.AllPluginsOption,
                         StringComparison.OrdinalIgnoreCase);

    if (shouldSwitch)
    {
      _suppressPluginDeselection = true;
      try
      {
        SelectedOutfitPlugin = targetPlugin;
        await LoadOutfitPluginAsync(true);
      }
      finally
      {
        _suppressPluginDeselection = false;
      }
    }
    else if (forceOutfitReload && string.Equals(
               SelectedOutfitPlugin,
               targetPlugin,
               StringComparison.OrdinalIgnoreCase))
    {
      await LoadOutfitPluginAsync(true);
    }
  }

  public Task CreateOutfitFromPiecesAsync(
    IReadOnlyList<ArmorRecordViewModel> pieces,
    string? defaultName = null) =>
    _draftManager.CreateDraftAsync(pieces, defaultName);

  [ReactiveCommand(CanExecute = nameof(_canCreateOutfit))]
  private async Task CreateOutfitAsync()
  {
    var selectedArmors = SelectedOutfitArmors.OfType<ArmorRecordViewModel>().ToList();
    if (selectedArmors.Count > 0)
    {
      await CreateOutfitFromPiecesAsync(selectedArmors);
    }
  }

  public async Task PreviewDraftAsync(OutfitDraftViewModel draft)
  {
    var pieces = draft.GetPieces();
    if (pieces.Count == 0)
    {
      _logger.Warning("Cannot preview empty outfit draft {EditorId}", draft.EditorId);
      return;
    }

    var metadata = new OutfitMetadata(draft.Name, draft.EditorId, false);
    var collection = new ArmorPreviewSceneCollection(
      1,
      0,
      [metadata],
      async (_, gender) =>
      {
        var scene = await _previewService.BuildPreviewAsync(pieces, gender);
        return scene with { OutfitLabel = draft.Name, SourceFile = draft.EditorId };
      });

    await ShowPreview.Handle(collection);
  }

  public bool TryAddPiecesToDraft(OutfitDraftViewModel draft, IReadOnlyList<ArmorRecordViewModel> pieces) =>
    _draftManager.TryAddPieces(draft, pieces);

  public void MoveDraft(IOutfitQueueItem item, int targetIndex, bool insertBefore = true) =>
    _draftManager.MoveItem(item, targetIndex, insertBefore);

  public List<IOutfitQueueItem> GetGroupedItems(OutfitSeparatorViewModel separator) =>
    _draftManager.GetGroupedItems(separator);

  public void AddSeparator() => _draftManager.AddSeparator();

  public async Task PreviewArmorAsync(ArmorRecordViewModel armor)
  {
    try
    {
      StatusMessage = $"Building preview for '{armor.DisplayName}'...";

      var metadata = new OutfitMetadata(armor.DisplayName, armor.Armor.FormKey.ModKey.FileName.String, false);
      var collection = new ArmorPreviewSceneCollection(
        1,
        0,
        [metadata],
        async (_, gender) =>
        {
          var scene = await _previewService.BuildPreviewAsync([armor], gender);
          return scene with
                 {
                   OutfitLabel = armor.DisplayName, SourceFile = armor.Armor.FormKey.ModKey.FileName.String
                 };
        });

      await ShowPreview.Handle(collection);
      StatusMessage = $"Preview ready for '{armor.DisplayName}'.";
    }
    catch (Exception ex)
    {
      StatusMessage = $"Preview error: {ex.Message}";
      _logger.Error(ex, "Failed to build armor preview for {Armor}.", armor.DisplayName);
    }
  }

  private void TriggerAutoSave() => _autoSaveTrigger.OnNext(Unit.Default);

  [ReactiveCommand(CanExecute = nameof(_canSaveOutfits))]
  private async Task SaveOutfitsAsync()
  {
    if (!_draftManager.HasUnsavedChanges())
    {
      StatusMessage = "No outfits to save or delete.";
      _logger.Debug("SaveOutfitsAsync invoked with no drafts or deletions.");
      return;
    }

    IsCreatingOutfits = true;
    StatusMessage     = "Saving outfits...";
    _logger.Information("Starting outfit save operation.");

    try
    {
      var requests = _draftManager.BuildSaveRequests();
      var (success, message, results) = await _patchingService.CreateOrUpdateOutfitsAsync(
                                          requests,
                                          Settings.FullOutputPath);

      if (success)
      {
        _draftManager.ProcessSaveResults(results);
        StatusMessage = message;
        _logger.Information("Outfit save completed: {Message}", message);

        if (results.Count > 0)
        {
          await LoadOutfitsFromOutputPluginAsync();
        }
      }
      else
      {
        _logger.Error("Outfit save failed: {Message}", message);
        StatusMessage = $"Error: {message}";
        await ShowError.Handle(("Error Saving Outfits", message));
      }
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Exception during outfit save operation");
      StatusMessage = $"Error saving outfits: {ex.Message}";
      await ShowError.Handle(("Error Saving Outfits", $"An unexpected error occurred:\n{ex.Message}"));
    }
    finally
    {
      IsCreatingOutfits = false;
    }
  }

  public async Task OnOutfitCopiedToCreator(CopiedOutfit copiedOutfit)
  {
    _logger.Information("Outfit copy requested: {Description}", copiedOutfit.Description);

    if (!_mutagenService.LinkCache!.TryResolve<IOutfitGetter>(copiedOutfit.OutfitFormKey, out var outfit))
    {
      _logger.Warning("Could not resolve outfit {FormKey}", copiedOutfit.OutfitFormKey);
      return;
    }

    var result = OutfitResolver.GatherArmorPieces(outfit, _mutagenService.LinkCache);
    if (result.ArmorPieces.Count == 0)
    {
      _logger.Warning("Outfit {FormKey} resolved to 0 armor pieces.", copiedOutfit.OutfitFormKey);
      return;
    }

    var winningMod = GetWinningModForOutfit(copiedOutfit.OutfitFormKey);
    if (winningMod != null && copiedOutfit.IsOverride)
    {
      CreateOverrideDraft(outfit, result.ArmorPieces, winningMod);
    }
    else
    {
      var defaultName = "btq_" + (copiedOutfit.OutfitEditorId ?? outfit.FormKey.ToString());
      await CreateOutfitFromPiecesAsync(result.ArmorPieces, defaultName);
    }
  }

  private void CreateOverrideDraft(
    IOutfitGetter outfit,
    IReadOnlyList<ArmorRecordViewModel> armorPieces,
    ModKey? winningMod) => _draftManager.CreateOverrideDraft(outfit, armorPieces, winningMod);

  private ModKey? GetWinningModForOutfit(FormKey formKey)
  {
    if (_mutagenService.LinkCache!.TryResolve<IOutfitGetter>(formKey, out var resolved))
    {
      return resolved.FormKey.ModKey;
    }

    return formKey.ModKey;
  }

  private async void OnMutagenInitialized(object? sender, EventArgs e) => await LoadOutfitPluginsAsync();
  private async void OnPluginsChanged(object? sender, EventArgs e) => await LoadOutfitPluginsAsync();

  private async Task LoadOutfitPluginsAsync()
  {
    var plugins = await _mutagenService.GetPluginsWithArmorsOrOutfitsAsync();
    _outfitPluginsSource.Edit(list =>
    {
      list.Clear();
      list.AddRange(plugins);
    });
  }
}
