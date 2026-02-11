using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Linq;
using System.Windows;
using Boutique.Models;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public enum GenderFilter
{
  Any,
  Female,
  Male
}

public enum UniqueFilter
{
  Any,
  UniqueOnly,
  NonUniqueOnly
}

public enum LevelFilterMode
{
  None,
  Level,
  SkillLevel,
  SkillWeight,
  Raw
}

public sealed record SkillFilterOption(int Index, string Name)
{
  public string DisplayText => $"{Index}. {Name}";
}

public partial class DistributionEntryViewModel : ReactiveObject
{
  [Reactive] private int _chance = 100;

  [Reactive] private string _exclusiveGroupFormsText = string.Empty;

  [Reactive] private string _exclusiveGroupName = string.Empty;

  [Reactive] private GenderFilter _gender = GenderFilter.Any;

  [Reactive] private bool? _isChild;

  [Reactive] private bool _isSelected;

  private bool _isSynchronizingLevelFilter;

  [Reactive] private string _keywordToDistribute = string.Empty;

  [Reactive] private string _levelFilterMax = string.Empty;

  [Reactive] private string _levelFilterMin = string.Empty;

  [Reactive] private LevelFilterMode _levelFilterMode = LevelFilterMode.None;

  [Reactive] private string _levelFilters = string.Empty;

  [Reactive] private string _rawFormFilters = string.Empty;

  [Reactive] private string _rawStringFilters = string.Empty;

  private ObservableCollection<ClassRecordViewModel>    _selectedClasses   = [];
  private ObservableCollection<FactionRecordViewModel> _selectedFactions  = [];
  private ObservableCollection<KeywordRecordViewModel> _selectedKeywords  = [];
  private ObservableCollection<LocationRecordViewModel> _selectedLocations = [];

  [Reactive] private SkillFilterOption?                       _selectedLevelSkill;
  private            ObservableCollection<NpcRecordViewModel> _selectedNpcs = [];

  [Reactive] private IOutfitGetter?                              _selectedOutfit;
  private            ObservableCollection<OutfitRecordViewModel> _selectedOutfitFilters = [];

  private ObservableCollection<RaceRecordViewModel> _selectedRaces = [];

  [Reactive] private DistributionType _type = DistributionType.Outfit;

  [Reactive] private UniqueFilter _unique = UniqueFilter.Any;

  [Reactive] private bool _useChance;

  [Reactive] private FilterLogicMode _npcLogicMode = FilterLogicMode.And;

  [Reactive] private FilterLogicMode _factionLogicMode = FilterLogicMode.And;

  [Reactive] private FilterLogicMode _keywordLogicMode = FilterLogicMode.And;

  [Reactive] private FilterLogicMode _raceLogicMode = FilterLogicMode.And;

  [Reactive] private FilterLogicMode _classLogicMode = FilterLogicMode.And;

  [Reactive] private FilterLogicMode _locationLogicMode = FilterLogicMode.And;

  [Reactive] private FilterLogicMode _outfitFilterLogicMode = FilterLogicMode.And;

  public DistributionEntryViewModel(
    DistributionEntry entry,
    Action<DistributionEntryViewModel>? removeAction = null,
    Func<bool>? isFormatChangingToSpid = null)
  {
    Entry               = entry;
    Type                = entry.Type;
    SelectedOutfit      = entry.Outfit;
    KeywordToDistribute = entry.KeywordToDistribute ?? string.Empty;
    ExclusiveGroupName  = entry.ExclusiveGroupName ?? string.Empty;
    ExclusiveGroupFormsText = entry.ExclusiveGroupForms.Count > 0
                                ? string.Join(",", entry.ExclusiveGroupForms)
                                : string.Empty;
    UseChance              = entry.Chance.HasValue;
    Chance                 = entry.Chance ?? 100;
    LevelFilters           = entry.LevelFilters ?? string.Empty;
    SelectedLevelSkill     = SkillFilterOptions.Count > 0 ? SkillFilterOptions[0] : null;
    ParseLevelFiltersToUi(LevelFilters);
    RawStringFilters       = entry.RawStringFilters ?? string.Empty;
    RawFormFilters         = entry.RawFormFilters ?? string.Empty;
    NpcLogicMode           = entry.NpcLogicMode;
    FactionLogicMode       = entry.FactionLogicMode;
    KeywordLogicMode       = entry.KeywordLogicMode;
    RaceLogicMode          = entry.RaceLogicMode;
    ClassLogicMode         = entry.ClassLogicMode;
    LocationLogicMode      = entry.LocationLogicMode;
    OutfitFilterLogicMode  = entry.OutfitFilterLogicMode;

    Gender = entry.TraitFilters.IsFemale switch
    {
      true  => GenderFilter.Female,
      false => GenderFilter.Male,
      null  => GenderFilter.Any
    };
    Unique = entry.TraitFilters.IsUnique switch
    {
      true  => UniqueFilter.UniqueOnly,
      false => UniqueFilter.NonUniqueOnly,
      null  => UniqueFilter.Any
    };
    IsChild = entry.TraitFilters.IsChild;

    this.WhenAnyValue(x => x.Type)
        .Skip(1)
        .Subscribe(type =>
        {
          Entry.Type = type;
          this.RaisePropertyChanged(nameof(IsOutfitDistribution));
          this.RaisePropertyChanged(nameof(IsKeywordDistribution));
          this.RaisePropertyChanged(nameof(IsExclusiveGroupDistribution));
          RaiseFilterSummaryChanged();
          RaiseEntryChanged();
        });

    this.WhenAnyValue(x => x.SelectedOutfit)
        .Skip(1)
        .Subscribe(outfit =>
        {
          Entry.Outfit = outfit;
          RaiseFilterSummaryChanged();
          RaiseEntryChanged();
        });

    this.WhenAnyValue(x => x.KeywordToDistribute)
        .Skip(1)
        .Subscribe(keyword =>
        {
          Entry.KeywordToDistribute = keyword;
          RaiseFilterSummaryChanged();
          RaiseEntryChanged();
        });

    this.WhenAnyValue(x => x.ExclusiveGroupName)
        .Skip(1)
        .Subscribe(groupName =>
        {
          Entry.ExclusiveGroupName = groupName;
          RaiseFilterSummaryChanged();
          RaiseEntryChanged();
        });

    this.WhenAnyValue(x => x.ExclusiveGroupFormsText)
        .Skip(1)
        .Subscribe(formsText =>
        {
          Entry.ExclusiveGroupForms =
          [
            .. formsText
               .Split(',', StringSplitOptions.RemoveEmptyEntries)
               .Select(f => f.Trim())
               .Where(f => !string.IsNullOrWhiteSpace(f))
          ];
          RaiseFilterSummaryChanged();
          RaiseEntryChanged();
        });

    this.WhenAnyValue(x => x.Gender)
        .Skip(1)
        .Subscribe(gender =>
        {
          Entry.TraitFilters = Entry.TraitFilters with
                               {
                                 IsFemale = gender switch
                                 {
                                   GenderFilter.Female => true,
                                   GenderFilter.Male   => false,
                                   _                   => null
                                 }
                               };
          RaiseFilterSummaryChanged();
          RaiseEntryChanged();
        });

    this.WhenAnyValue(x => x.Unique)
        .Skip(1)
        .Subscribe(unique =>
        {
          Entry.TraitFilters = Entry.TraitFilters with
                               {
                                 IsUnique = unique switch
                                 {
                                   UniqueFilter.UniqueOnly    => true,
                                   UniqueFilter.NonUniqueOnly => false,
                                   _                          => null
                                 }
                               };
          RaiseFilterSummaryChanged();
          RaiseEntryChanged();
        });

    this.WhenAnyValue(x => x.IsChild)
        .Skip(1)
        .Subscribe(isChild =>
        {
          Entry.TraitFilters = Entry.TraitFilters with { IsChild = isChild };
          RaiseEntryChanged();
        });

    var previousUseChance = UseChance;
    this.WhenAnyValue(x => x.UseChance)
        .Skip(1)
        .Subscribe(useChance =>
        {
          var wasEnabled = previousUseChance;
          previousUseChance = useChance;

          if (useChance && !wasEnabled && isFormatChangingToSpid != null)
          {
            if (isFormatChangingToSpid())
            {
              var result = MessageBox.Show(
                "Enabling chance-based distribution will change the file format to SPID.\n\n" +
                "SkyPatcher does not support chance-based outfit distribution. " +
                "The file will be saved in SPID format to support this feature.\n\n" +
                "Do you want to continue?",
                "Format Change Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

              if (result == MessageBoxResult.No)
              {
                previousUseChance = false;
                UseChance         = false;
                return;
              }
            }
          }

          Entry.Chance = useChance ? Chance : null;
          RaiseFilterSummaryChanged();
          RaiseEntryChanged();
        });

    this.WhenAnyValue(x => x.Chance)
        .Skip(1)
        .Subscribe(chance =>
        {
          if (UseChance)
          {
            Entry.Chance = chance;
            RaiseFilterSummaryChanged();
            RaiseEntryChanged();
          }
        });

    SubscribeLogicMode(x => x.NpcLogicMode, m => Entry.NpcLogicMode = m);
    SubscribeLogicMode(x => x.FactionLogicMode, m => Entry.FactionLogicMode = m);
    SubscribeLogicMode(x => x.KeywordLogicMode, m => Entry.KeywordLogicMode = m);
    SubscribeLogicMode(x => x.RaceLogicMode, m => Entry.RaceLogicMode = m);
    SubscribeLogicMode(x => x.ClassLogicMode, m => Entry.ClassLogicMode = m);
    SubscribeLogicMode(x => x.LocationLogicMode, m => Entry.LocationLogicMode = m);
    SubscribeLogicMode(x => x.OutfitFilterLogicMode, m => Entry.OutfitFilterLogicMode = m);

    this.WhenAnyValue(x => x.LevelFilters)
        .Skip(1)
        .Subscribe(levelFilters =>
        {
          Entry.LevelFilters = string.IsNullOrWhiteSpace(levelFilters) ? null : levelFilters;
          RaiseFilterSummaryChanged();
          RaiseEntryChanged();
        });

    this.WhenAnyValue(
          x => x.LevelFilterMode,
          x => x.SelectedLevelSkill,
          x => x.LevelFilterMin,
          x => x.LevelFilterMax)
        .Skip(1)
        .Subscribe(_ => RebuildLevelFiltersFromUi());

    this.WhenAnyValue(x => x.LevelFilterMode)
        .Skip(1)
        .Subscribe(_ =>
        {
          this.RaisePropertyChanged(nameof(IsSkillLevelMode));
          this.RaisePropertyChanged(nameof(IsRawLevelFilterMode));
          this.RaisePropertyChanged(nameof(IsStructuredLevelFilterMode));
          this.RaisePropertyChanged(nameof(HasRangeLevelFilterMode));
        });

    this.WhenAnyValue(x => x.RawStringFilters)
        .Skip(1)
        .Subscribe(rawFilters =>
        {
          Entry.RawStringFilters = string.IsNullOrWhiteSpace(rawFilters) ? null : rawFilters;
          RaiseFilterSummaryChanged();
          RaiseEntryChanged();
        });

    this.WhenAnyValue(x => x.RawFormFilters)
        .Skip(1)
        .Subscribe(rawFilters =>
        {
          Entry.RawFormFilters = string.IsNullOrWhiteSpace(rawFilters) ? null : rawFilters;
          RaiseFilterSummaryChanged();
          RaiseEntryChanged();
        });

    RemoveCommand = ReactiveCommand.Create(() => removeAction?.Invoke(this));
  }

  public DistributionEntry Entry { get; }

  public bool IsOutfitDistribution => Type == DistributionType.Outfit;
  public bool IsKeywordDistribution => Type == DistributionType.Keyword;
  public bool IsExclusiveGroupDistribution => Type == DistributionType.ExclusiveGroup;

  public bool IsSkillLevelMode =>
    LevelFilterMode == LevelFilterMode.SkillLevel || LevelFilterMode == LevelFilterMode.SkillWeight;

  public bool IsRawLevelFilterMode => LevelFilterMode == LevelFilterMode.Raw;
  public bool IsStructuredLevelFilterMode => !IsRawLevelFilterMode;
  public bool HasRangeLevelFilterMode => LevelFilterMode == LevelFilterMode.Level || IsSkillLevelMode;

  public static DistributionType[] TypeOptions { get; } =
    [DistributionType.Outfit, DistributionType.Keyword, DistributionType.ExclusiveGroup];

  public static GenderFilter[] GenderOptions { get; } = [GenderFilter.Any, GenderFilter.Female, GenderFilter.Male];

  public static UniqueFilter[] UniqueOptions { get; } =
    [UniqueFilter.Any, UniqueFilter.UniqueOnly, UniqueFilter.NonUniqueOnly];

  public static LevelFilterMode[] LevelFilterModeOptions { get; } =
    [
      LevelFilterMode.None, LevelFilterMode.Level, LevelFilterMode.SkillLevel, LevelFilterMode.SkillWeight,
      LevelFilterMode.Raw
    ];

  public static IReadOnlyList<SkillFilterOption> SkillFilterOptions { get; } =
    [
      new(0, "One-Handed"),
      new(1, "Two-Handed"),
      new(2, "Archery"),
      new(3, "Block"),
      new(4, "Smithing"),
      new(5, "Heavy Armor"),
      new(6, "Light Armor"),
      new(7, "Pickpocket"),
      new(8, "Lockpicking"),
      new(9, "Sneak"),
      new(10, "Alchemy"),
      new(11, "Speech"),
      new(12, "Alteration"),
      new(13, "Conjuration"),
      new(14, "Destruction"),
      new(15, "Illusion"),
      new(16, "Restoration"),
      new(17, "Enchanting")
    ];

  public bool HasTraitFilters => Gender != GenderFilter.Any || Unique != UniqueFilter.Any || IsChild.HasValue;

  public bool HasUnresolvedFilters =>
    !string.IsNullOrWhiteSpace(RawStringFilters) || !string.IsNullOrWhiteSpace(RawFormFilters);

  public bool HasAnyResolvedFilters =>
    _selectedNpcs.Count > 0 || _selectedFactions.Count > 0 || _selectedKeywords.Count > 0 ||
    _selectedRaces.Count > 0 || _selectedClasses.Count > 0 || _selectedLocations.Count > 0 ||
    _selectedOutfitFilters.Count > 0 || HasTraitFilters;

  public string TargetDisplayName
  {
    get
    {
      if (Type == DistributionType.Outfit)
      {
        return SelectedOutfit?.EditorID ?? "(No outfit)";
      }

      if (Type == DistributionType.Keyword)
      {
        return !string.IsNullOrWhiteSpace(KeywordToDistribute) ? KeywordToDistribute : "(No keyword)";
      }

      return !string.IsNullOrWhiteSpace(ExclusiveGroupName) ? ExclusiveGroupName : "(No group)";
    }
  }

  public string FilterSummary => BuildFilterSummary();

  public ObservableCollection<NpcRecordViewModel> SelectedNpcs
  {
    get => _selectedNpcs;
    set
    {
      this.RaiseAndSetIfChanged(ref _selectedNpcs, value);
      UpdateEntryNpcs();
    }
  }

  public ObservableCollection<FactionRecordViewModel> SelectedFactions
  {
    get => _selectedFactions;
    set
    {
      this.RaiseAndSetIfChanged(ref _selectedFactions, value);
      UpdateEntryFactions();
    }
  }

  public ObservableCollection<KeywordRecordViewModel> SelectedKeywords
  {
    get => _selectedKeywords;
    set
    {
      this.RaiseAndSetIfChanged(ref _selectedKeywords, value);
      UpdateEntryKeywords();
    }
  }

  public ObservableCollection<RaceRecordViewModel> SelectedRaces
  {
    get => _selectedRaces;
    set
    {
      this.RaiseAndSetIfChanged(ref _selectedRaces, value);
      UpdateEntryRaces();
    }
  }

  public ObservableCollection<ClassRecordViewModel> SelectedClasses
  {
    get => _selectedClasses;
    set
    {
      this.RaiseAndSetIfChanged(ref _selectedClasses, value);
      UpdateEntryClasses();
    }
  }

  public ObservableCollection<LocationRecordViewModel> SelectedLocations
  {
    get => _selectedLocations;
    set
    {
      this.RaiseAndSetIfChanged(ref _selectedLocations, value);
      UpdateEntryLocations();
    }
  }

  public ObservableCollection<OutfitRecordViewModel> SelectedOutfitFilters
  {
    get => _selectedOutfitFilters;
    set
    {
      this.RaiseAndSetIfChanged(ref _selectedOutfitFilters, value);
      UpdateEntryOutfitFilters();
    }
  }

  public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

  public event EventHandler? EntryChanged;

  private void RaiseEntryChanged() => EntryChanged?.Invoke(this, EventArgs.Empty);

  private void SubscribeLogicMode(
    Expression<Func<DistributionEntryViewModel, FilterLogicMode>> property,
    Action<FilterLogicMode> setter) =>
    this.WhenAnyValue(property)
        .Skip(1)
        .Subscribe(mode =>
        {
          setter(mode);
          RaiseEntryChanged();
        });

  private void SyncEntryList<T>(List<T> target, IEnumerable<T> items)
  {
    target.Clear();
    target.AddRange(items);
    RaiseFilterSummaryChanged();
    RaiseEntryChanged();
  }

  private void ParseLevelFiltersToUi(string? levelFiltersText)
  {
    _isSynchronizingLevelFilter = true;
    try
    {
      var text = levelFiltersText?.Trim();
      if (string.IsNullOrWhiteSpace(text) || text.Equals("NONE", StringComparison.OrdinalIgnoreCase))
      {
        LevelFilterMode = LevelFilterMode.None;
        LevelFilterMin  = string.Empty;
        LevelFilterMax  = string.Empty;
        return;
      }

      if (text.Contains(','))
      {
        LevelFilterMode = LevelFilterMode.Raw;
        return;
      }

      if (TryParseSkillLevelFilter(text, out var isWeight, out var skillIndex, out var minValue, out var maxValue))
      {
        LevelFilterMode    = isWeight ? LevelFilterMode.SkillWeight : LevelFilterMode.SkillLevel;
        SelectedLevelSkill = GetSkillFilterOption(skillIndex);
        LevelFilterMin     = minValue.ToString(CultureInfo.InvariantCulture);
        LevelFilterMax     = maxValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return;
      }

      if (TryParseRange(text, out var minLevel, out var maxLevel))
      {
        LevelFilterMode = LevelFilterMode.Level;
        LevelFilterMin  = minLevel.ToString(CultureInfo.InvariantCulture);
        LevelFilterMax  = maxLevel?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return;
      }

      LevelFilterMode = LevelFilterMode.Raw;
    }
    finally
    {
      _isSynchronizingLevelFilter = false;
    }
  }

  private void RebuildLevelFiltersFromUi()
  {
    if (_isSynchronizingLevelFilter)
    {
      return;
    }

    var rebuilt = LevelFilterMode switch
    {
      LevelFilterMode.None        => null,
      LevelFilterMode.Level       => BuildRangeSyntax(LevelFilterMin, LevelFilterMax),
      LevelFilterMode.SkillLevel  => BuildSkillSyntax(false, SelectedLevelSkill, LevelFilterMin, LevelFilterMax),
      LevelFilterMode.SkillWeight => BuildSkillSyntax(true, SelectedLevelSkill, LevelFilterMin, LevelFilterMax),
      LevelFilterMode.Raw         => null,
      _                           => null
    };

    if (LevelFilterMode == LevelFilterMode.Raw)
    {
      return;
    }

    var newText = rebuilt ?? string.Empty;
    if (!string.Equals(LevelFilters, newText, StringComparison.Ordinal))
    {
      _isSynchronizingLevelFilter = true;
      try
      {
        LevelFilters = newText;
      }
      finally
      {
        _isSynchronizingLevelFilter = false;
      }
    }
  }

  private static string? BuildSkillSyntax(
    bool isWeight,
    SkillFilterOption? skill,
    string minText,
    string maxText)
  {
    if (skill == null)
    {
      return null;
    }

    var range = BuildRangeSyntax(minText, maxText);
    if (string.IsNullOrWhiteSpace(range))
    {
      return null;
    }

    var prefix = isWeight ? "w" : string.Empty;
    return $"{prefix}{skill.Index}({range})";
  }

  private static string? BuildRangeSyntax(string minText, string maxText)
  {
    var hasMin = int.TryParse(minText, out var minValue);
    var hasMax = int.TryParse(maxText, out var maxValue);

    if (!hasMin)
    {
      return null;
    }

    if (!hasMax)
    {
      return $"{minValue}/";
    }

    return $"{minValue}/{maxValue}";
  }

  private static bool TryParseSkillLevelFilter(
    string text,
    out bool isWeight,
    out int skillIndex,
    out int minValue,
    out int? maxValue)
  {
    isWeight   = false;
    skillIndex = 0;
    minValue   = 0;
    maxValue   = null;

    var openParen  = text.IndexOf('(');
    var closeParen = text.LastIndexOf(')');
    if (openParen < 0 || closeParen <= openParen || closeParen != text.Length - 1)
    {
      return false;
    }

    var prefix = text[..openParen].Trim();
    if (string.IsNullOrWhiteSpace(prefix))
    {
      return false;
    }

    if (prefix.StartsWith('w') || prefix.StartsWith('W'))
    {
      isWeight = true;
      prefix   = prefix[1..];
    }

    if (!int.TryParse(prefix, out skillIndex) || skillIndex < 0 || skillIndex > 17)
    {
      return false;
    }

    var rangePart = text[(openParen + 1)..closeParen].Trim();
    return TryParseRange(rangePart, out minValue, out maxValue);
  }

  private static SkillFilterOption? GetSkillFilterOption(int index)
  {
    for (var i = 0; i < SkillFilterOptions.Count; i++)
    {
      var option = SkillFilterOptions[i];
      if (option.Index == index)
      {
        return option;
      }
    }

    return SkillFilterOptions.Count > 0 ? SkillFilterOptions[0] : null;
  }

  private static bool TryParseRange(string text, out int minValue, out int? maxValue)
  {
    maxValue = null;

    var slashIndex = text.IndexOf('/');
    if (slashIndex < 0)
    {
      return int.TryParse(text, out minValue);
    }

    var minPart = text[..slashIndex].Trim();
    var maxPart = slashIndex < text.Length - 1 ? text[(slashIndex + 1)..].Trim() : string.Empty;

    if (!int.TryParse(minPart, out minValue))
    {
      return false;
    }

    if (!string.IsNullOrWhiteSpace(maxPart))
    {
      if (!int.TryParse(maxPart, out var parsedMax))
      {
        return false;
      }

      maxValue = parsedMax;
    }

    return true;
  }

  private string BuildFilterSummary()
  {
    if (Type == DistributionType.ExclusiveGroup)
    {
      return Entry.ExclusiveGroupForms.Count > 0
               ? $"{Entry.ExclusiveGroupForms.Count} form(s)"
               : "No forms";
    }

    var parts = new List<string>();

    (int Count, string Label)[] filters =
    [
      (_selectedNpcs.Count, "NPC(s)"),
      (_selectedFactions.Count, "faction(s)"),
      (_selectedKeywords.Count, "keyword(s)"),
      (_selectedRaces.Count, "race(s)"),
      (_selectedClasses.Count, "class(es)"),
      (_selectedLocations.Count, "location(s)"),
      (_selectedOutfitFilters.Count, "outfit(s)")
    ];

    foreach (var (count, label) in filters)
    {
      if (count > 0)
      {
        parts.Add($"{count} {label}");
      }
    }

    if (Gender != GenderFilter.Any)
    {
      parts.Add(Gender.ToString());
    }

    if (Unique != UniqueFilter.Any)
    {
      parts.Add(Unique == UniqueFilter.UniqueOnly ? "Unique" : "Non-Unique");
    }

    if (UseChance && Chance < 100)
    {
      parts.Add($"{Chance}%");
    }

    if (!string.IsNullOrWhiteSpace(LevelFilters))
    {
      parts.Add("Level/Skill");
    }

    if (!string.IsNullOrWhiteSpace(_rawStringFilters))
    {
      parts.Add("String filters");
    }

    if (!string.IsNullOrWhiteSpace(_rawFormFilters))
    {
      parts.Add("Form filters");
    }

    return parts.Count > 0 ? string.Join(", ", parts) : "No filters";
  }

  private void RaiseFilterSummaryChanged()
  {
    this.RaisePropertyChanged(nameof(FilterSummary));
    this.RaisePropertyChanged(nameof(TargetDisplayName));
    this.RaisePropertyChanged(nameof(HasUnresolvedFilters));
    this.RaisePropertyChanged(nameof(HasAnyResolvedFilters));
  }

  public void UpdateEntryNpcs() =>
    SyncEntryList(Entry.NpcFilters, _selectedNpcs.Select(x => new FormKeyFilter(x.FormKey, x.IsExcluded)));

  public void UpdateEntryFactions() =>
    SyncEntryList(Entry.FactionFilters, _selectedFactions.Select(x => new FormKeyFilter(x.FormKey, x.IsExcluded)));

  public void UpdateEntryKeywords() =>
    SyncEntryList(
      Entry.KeywordFilters,
      _selectedKeywords
        .Where(k => !string.IsNullOrWhiteSpace(k.KeywordRecord.EditorID))
        .Select(k => new KeywordFilter(k.KeywordRecord.EditorID!, k.IsExcluded)));

  public void UpdateEntryRaces() =>
    SyncEntryList(Entry.RaceFilters, _selectedRaces.Select(x => new FormKeyFilter(x.FormKey, x.IsExcluded)));

  public void UpdateEntryClasses() =>
    SyncEntryList(Entry.ClassFormKeys, _selectedClasses.Select(x => x.FormKey));

  public void UpdateEntryLocations() =>
    SyncEntryList(Entry.LocationFormKeys, _selectedLocations.Select(x => x.FormKey));

  public void UpdateEntryOutfitFilters() =>
    SyncEntryList(Entry.OutfitFilterFormKeys, _selectedOutfitFilters.Select(x => x.FormKey));

  public static bool AddCriterion<T>(T item, ObservableCollection<T> collection, Action updateAction)
    where T : ISelectableRecordViewModel
  {
    bool isDuplicate;
    if (item is KeywordRecordViewModel keywordVm)
    {
      isDuplicate = collection.OfType<KeywordRecordViewModel>()
                              .Any(existing =>
                                     string.Equals(
                                       existing.EditorID,
                                       keywordVm.EditorID,
                                       StringComparison.OrdinalIgnoreCase));
    }
    else
    {
      isDuplicate = collection.Any(existing => existing.FormKey == item.FormKey);
    }

    if (isDuplicate)
    {
      return false;
    }

    collection.Add(item);
    updateAction();
    return true;
  }

  public static bool RemoveCriterion<T>(T item, ObservableCollection<T> collection, Action updateAction)
    where T : class
  {
    if (!collection.Remove(item))
    {
      return false;
    }

    updateAction();
    return true;
  }

  public bool AddNpc(NpcRecordViewModel npc) => AddCriterion(npc, _selectedNpcs, UpdateEntryNpcs);
  public void RemoveNpc(NpcRecordViewModel npc) => RemoveCriterion(npc, _selectedNpcs, UpdateEntryNpcs);

  public bool AddFaction(FactionRecordViewModel faction) =>
    AddCriterion(faction, _selectedFactions, UpdateEntryFactions);

  public void RemoveFaction(FactionRecordViewModel faction) =>
    RemoveCriterion(faction, _selectedFactions, UpdateEntryFactions);

  public bool AddKeyword(KeywordRecordViewModel keyword) =>
    AddCriterion(keyword, _selectedKeywords, UpdateEntryKeywords);

  public void RemoveKeyword(KeywordRecordViewModel keyword) =>
    RemoveCriterion(keyword, _selectedKeywords, UpdateEntryKeywords);

  public bool AddRace(RaceRecordViewModel race) => AddCriterion(race, _selectedRaces, UpdateEntryRaces);
  public void RemoveRace(RaceRecordViewModel race) => RemoveCriterion(race, _selectedRaces, UpdateEntryRaces);

  public bool AddClass(ClassRecordViewModel classVm) => AddCriterion(classVm, _selectedClasses, UpdateEntryClasses);

  public void RemoveClass(ClassRecordViewModel classVm) =>
    RemoveCriterion(classVm, _selectedClasses, UpdateEntryClasses);

  public bool AddLocation(LocationRecordViewModel location) =>
    AddCriterion(location, _selectedLocations, UpdateEntryLocations);

  public void RemoveLocation(LocationRecordViewModel location) =>
    RemoveCriterion(location, _selectedLocations, UpdateEntryLocations);

  public bool AddOutfitFilter(OutfitRecordViewModel outfit) =>
    AddCriterion(outfit, _selectedOutfitFilters, UpdateEntryOutfitFilters);

  public void RemoveOutfitFilter(OutfitRecordViewModel outfit) =>
    RemoveCriterion(outfit, _selectedOutfitFilters, UpdateEntryOutfitFilters);
}
