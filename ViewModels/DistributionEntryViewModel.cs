using System.Collections.ObjectModel;
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

public partial class DistributionEntryViewModel : ReactiveObject
{
    [Reactive] private int _chance = 100;

    [Reactive] private GenderFilter _gender = GenderFilter.Any;

    [Reactive] private bool? _isChild;

    [Reactive] private bool _isSelected;

    [Reactive] private string _keywordToDistribute = string.Empty;

    [Reactive] private string _levelFilters = string.Empty;

    [Reactive] private string _rawFormFilters = string.Empty;

    [Reactive] private string _rawStringFilters = string.Empty;

    private ObservableCollection<ClassRecordViewModel> _selectedClasses = [];
    private ObservableCollection<FactionRecordViewModel> _selectedFactions = [];
    private ObservableCollection<KeywordRecordViewModel> _selectedKeywords = [];
    private ObservableCollection<NpcRecordViewModel> _selectedNpcs = [];

    [Reactive] private IOutfitGetter? _selectedOutfit;

    private ObservableCollection<RaceRecordViewModel> _selectedRaces = [];

    [Reactive] private DistributionType _type = DistributionType.Outfit;

    [Reactive] private UniqueFilter _unique = UniqueFilter.Any;

    [Reactive] private bool _useChance;

    public DistributionEntryViewModel(
        DistributionEntry entry,
        Action<DistributionEntryViewModel>? removeAction = null,
        Func<bool>? isFormatChangingToSpid = null)
    {
        Entry = entry;
        Type = entry.Type;
        SelectedOutfit = entry.Outfit;
        KeywordToDistribute = entry.KeywordToDistribute ?? string.Empty;
        UseChance = entry.Chance.HasValue;
        Chance = entry.Chance ?? 100;
        LevelFilters = entry.LevelFilters ?? string.Empty;
        RawStringFilters = entry.RawStringFilters ?? string.Empty;
        RawFormFilters = entry.RawFormFilters ?? string.Empty;

        Gender = entry.TraitFilters.IsFemale switch
        {
            true => GenderFilter.Female,
            false => GenderFilter.Male,
            null => GenderFilter.Any
        };
        Unique = entry.TraitFilters.IsUnique switch
        {
            true => UniqueFilter.UniqueOnly,
            false => UniqueFilter.NonUniqueOnly,
            null => UniqueFilter.Any
        };
        IsChild = entry.TraitFilters.IsChild;

        this.WhenAnyValue(x => x.Type)
            .Skip(1)
            .Subscribe(type =>
            {
                Entry.Type = type;
                this.RaisePropertyChanged(nameof(IsOutfitDistribution));
                this.RaisePropertyChanged(nameof(IsKeywordDistribution));
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

        this.WhenAnyValue(x => x.Gender)
            .Skip(1)
            .Subscribe(gender =>
            {
                Entry.TraitFilters = Entry.TraitFilters with
                {
                    IsFemale = gender switch
                    {
                        GenderFilter.Female => true,
                        GenderFilter.Male => false,
                        _ => null
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
                        UniqueFilter.UniqueOnly => true,
                        UniqueFilter.NonUniqueOnly => false,
                        _ => null
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
                            UseChance = false;
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

        this.WhenAnyValue(x => x.LevelFilters)
            .Skip(1)
            .Subscribe(levelFilters =>
            {
                Entry.LevelFilters = string.IsNullOrWhiteSpace(levelFilters) ? null : levelFilters;
                RaiseEntryChanged();
            });

        this.WhenAnyValue(x => x.RawStringFilters)
            .Skip(1)
            .Subscribe(rawFilters =>
            {
                Entry.RawStringFilters = string.IsNullOrWhiteSpace(rawFilters) ? null : rawFilters;
                RaiseEntryChanged();
            });

        this.WhenAnyValue(x => x.RawFormFilters)
            .Skip(1)
            .Subscribe(rawFilters =>
            {
                Entry.RawFormFilters = string.IsNullOrWhiteSpace(rawFilters) ? null : rawFilters;
                RaiseEntryChanged();
            });

        RemoveCommand = ReactiveCommand.Create(() => removeAction?.Invoke(this));
    }

    public DistributionEntry Entry { get; }

    public bool IsOutfitDistribution => Type == DistributionType.Outfit;
    public bool IsKeywordDistribution => Type == DistributionType.Keyword;

    public static DistributionType[] TypeOptions { get; } = [DistributionType.Outfit, DistributionType.Keyword];
    public static GenderFilter[] GenderOptions { get; } = [GenderFilter.Any, GenderFilter.Female, GenderFilter.Male];

    public static UniqueFilter[] UniqueOptions { get; } =
        [UniqueFilter.Any, UniqueFilter.UniqueOnly, UniqueFilter.NonUniqueOnly];

    public bool HasTraitFilters => Gender != GenderFilter.Any || Unique != UniqueFilter.Any || IsChild.HasValue;

    public string TargetDisplayName => Type == DistributionType.Outfit
        ? SelectedOutfit?.EditorID ?? "(No outfit)"
        : !string.IsNullOrWhiteSpace(KeywordToDistribute)
            ? KeywordToDistribute
            : "(No keyword)";

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

    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

    public event EventHandler? EntryChanged;

    private void RaiseEntryChanged() => EntryChanged?.Invoke(this, EventArgs.Empty);

    private string BuildFilterSummary()
    {
        var parts = new List<string>();

        if (_selectedNpcs.Count > 0)
        {
            parts.Add($"{_selectedNpcs.Count} NPC(s)");
        }

        if (_selectedFactions.Count > 0)
        {
            parts.Add($"{_selectedFactions.Count} faction(s)");
        }

        if (_selectedKeywords.Count > 0)
        {
            parts.Add($"{_selectedKeywords.Count} keyword(s)");
        }

        if (_selectedRaces.Count > 0)
        {
            parts.Add($"{_selectedRaces.Count} race(s)");
        }

        if (_selectedClasses.Count > 0)
        {
            parts.Add($"{_selectedClasses.Count} class(es)");
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

        return parts.Count > 0 ? string.Join(", ", parts) : "No filters";
    }

    private void RaiseFilterSummaryChanged()
    {
        this.RaisePropertyChanged(nameof(FilterSummary));
        this.RaisePropertyChanged(nameof(TargetDisplayName));
    }

    public void UpdateEntryNpcs()
    {
        Entry.NpcFilters.Clear();
        Entry.NpcFilters.AddRange(SelectedNpcs.Select(npc => new FormKeyFilter(npc.FormKey, npc.IsExcluded)));
        RaiseFilterSummaryChanged();
        RaiseEntryChanged();
    }

    public void UpdateEntryFactions()
    {
        Entry.FactionFilters.Clear();
        Entry.FactionFilters.AddRange(SelectedFactions.Select(f => new FormKeyFilter(f.FormKey, f.IsExcluded)));
        RaiseFilterSummaryChanged();
        RaiseEntryChanged();
    }

    public void UpdateEntryKeywords()
    {
        Entry.KeywordFilters.Clear();
        foreach (var keyword in SelectedKeywords)
        {
            var editorId = keyword.KeywordRecord.EditorID;
            if (!string.IsNullOrWhiteSpace(editorId))
            {
                Entry.KeywordFilters.Add(new KeywordFilter(editorId, keyword.IsExcluded));
            }
        }

        RaiseFilterSummaryChanged();
        RaiseEntryChanged();
    }

    public void UpdateEntryRaces()
    {
        Entry.RaceFilters.Clear();
        Entry.RaceFilters.AddRange(SelectedRaces.Select(r => new FormKeyFilter(r.FormKey, r.IsExcluded)));
        RaiseFilterSummaryChanged();
        RaiseEntryChanged();
    }

    public void UpdateEntryClasses()
    {
        Entry.ClassFormKeys.Clear();
        Entry.ClassFormKeys.AddRange(SelectedClasses.Select(c => c.FormKey));
        RaiseFilterSummaryChanged();
        RaiseEntryChanged();
    }

    public static bool AddCriterion<T>(T item, ObservableCollection<T> collection, Action updateAction)
        where T : ISelectableRecordViewModel
    {
        if (collection.Any(existing => existing.FormKey == item.FormKey))
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

    public void AddNpc(NpcRecordViewModel npc) => AddCriterion(npc, _selectedNpcs, UpdateEntryNpcs);
    public void RemoveNpc(NpcRecordViewModel npc) => RemoveCriterion(npc, _selectedNpcs, UpdateEntryNpcs);

    public void AddFaction(FactionRecordViewModel faction) =>
        AddCriterion(faction, _selectedFactions, UpdateEntryFactions);

    public void RemoveFaction(FactionRecordViewModel faction) =>
        RemoveCriterion(faction, _selectedFactions, UpdateEntryFactions);

    public void AddKeyword(KeywordRecordViewModel keyword) =>
        AddCriterion(keyword, _selectedKeywords, UpdateEntryKeywords);

    public void RemoveKeyword(KeywordRecordViewModel keyword) =>
        RemoveCriterion(keyword, _selectedKeywords, UpdateEntryKeywords);

    public void AddRace(RaceRecordViewModel race) => AddCriterion(race, _selectedRaces, UpdateEntryRaces);
    public void RemoveRace(RaceRecordViewModel race) => RemoveCriterion(race, _selectedRaces, UpdateEntryRaces);

    public void AddClass(ClassRecordViewModel classVm) => AddCriterion(classVm, _selectedClasses, UpdateEntryClasses);

    public void RemoveClass(ClassRecordViewModel classVm) =>
        RemoveCriterion(classVm, _selectedClasses, UpdateEntryClasses);
}
