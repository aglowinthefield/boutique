using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Boutique.Models;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Boutique.ViewModels;

/// <summary>
/// Options for gender filter in SPID trait filters.
/// </summary>
public enum GenderFilter
{
    Any,
    Female,
    Male
}

/// <summary>
/// Options for unique NPC filter in SPID trait filters.
/// </summary>
public enum UniqueFilter
{
    Any,
    UniqueOnly,
    NonUniqueOnly
}

public class DistributionEntryViewModel : ReactiveObject
{
    private ObservableCollection<NpcRecordViewModel> _selectedNpcs = [];
    private ObservableCollection<FactionRecordViewModel> _selectedFactions = [];
    private ObservableCollection<KeywordRecordViewModel> _selectedKeywords = [];
    private ObservableCollection<RaceRecordViewModel> _selectedRaces = [];

    public DistributionEntryViewModel(
        DistributionEntry entry,
        System.Action<DistributionEntryViewModel>? removeAction = null,
        Func<bool>? isFormatChangingToSpid = null)
    {
        Entry = entry;
        SelectedOutfit = entry.Outfit;
        UseChance = entry.Chance.HasValue;
        Chance = entry.Chance ?? 100;

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

        if (entry.NpcFormKeys.Count > 0)
        {
            var npcVms = entry.NpcFormKeys
                .Select(fk => new NpcRecordViewModel(new NpcRecord(fk, null, null, fk.ModKey)))
                .ToList();

            foreach (var npcVm in npcVms)
            {
                _selectedNpcs.Add(npcVm);
            }
        }

        if (entry.FactionFormKeys.Count > 0)
        {
            var factionVms = entry.FactionFormKeys
                .Select(fk => new FactionRecordViewModel(new FactionRecord(fk, null, null, fk.ModKey)))
                .ToList();

            foreach (var factionVm in factionVms)
            {
                _selectedFactions.Add(factionVm);
            }
        }

        if (entry.KeywordFormKeys.Count > 0)
        {
            var keywordVms = entry.KeywordFormKeys
                .Select(fk => new KeywordRecordViewModel(new KeywordRecord(fk, null, fk.ModKey)))
                .ToList();

            foreach (var keywordVm in keywordVms)
            {
                _selectedKeywords.Add(keywordVm);
            }
        }

        if (entry.RaceFormKeys.Count > 0)
        {
            var raceVms = entry.RaceFormKeys
                .Select(fk => new RaceRecordViewModel(new RaceRecord(fk, null, null, fk.ModKey)))
                .ToList();

            foreach (var raceVm in raceVms)
            {
                _selectedRaces.Add(raceVm);
            }
        }

        this.WhenAnyValue(x => x.SelectedOutfit)
            .Subscribe(outfit => Entry.Outfit = outfit);

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
            });

        this.WhenAnyValue(x => x.IsChild)
            .Skip(1)
            .Subscribe(isChild => Entry.TraitFilters = Entry.TraitFilters with { IsChild = isChild });

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
                        var result = System.Windows.MessageBox.Show(
                            "Enabling chance-based distribution will change the file format to SPID.\n\n" +
                            "SkyPatcher does not support chance-based outfit distribution. " +
                            "The file will be saved in SPID format to support this feature.\n\n" +
                            "Do you want to continue?",
                            "Format Change Required",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Warning);

                        if (result == System.Windows.MessageBoxResult.No)
                        {
                            previousUseChance = false;
                            UseChance = false;
                            return;
                        }
                    }
                }

                Entry.Chance = useChance ? Chance : null;
            });

        this.WhenAnyValue(x => x.Chance)
            .Skip(1)
            .Subscribe(chance =>
            {
                if (UseChance)
                {
                    Entry.Chance = chance;
                }
            });

        RemoveCommand = ReactiveCommand.Create(() => removeAction?.Invoke(this));
    }

    public DistributionEntry Entry { get; }

    [Reactive] public IOutfitGetter? SelectedOutfit { get; set; }

    /// <summary>
    /// Whether chance-based distribution is enabled for this entry.
    /// </summary>
    [Reactive] public bool UseChance { get; set; }

    /// <summary>
    /// Chance percentage (0-100) for distribution. Only used if UseChance is true.
    /// Defaults to 100.
    /// </summary>
    [Reactive] public int Chance { get; set; } = 100;

    /// <summary>
    /// Gender filter for this distribution entry.
    /// </summary>
    [Reactive] public GenderFilter Gender { get; set; } = GenderFilter.Any;

    /// <summary>
    /// Unique NPC filter for this distribution entry.
    /// </summary>
    [Reactive] public UniqueFilter Unique { get; set; } = UniqueFilter.Any;

    /// <summary>
    /// Child filter for this distribution entry. Null = any, true = children only, false = adults only.
    /// </summary>
    [Reactive] public bool? IsChild { get; set; }

    /// <summary>
    /// Available gender filter options for UI binding.
    /// </summary>
    public static GenderFilter[] GenderOptions { get; } = [GenderFilter.Any, GenderFilter.Female, GenderFilter.Male];

    /// <summary>
    /// Available unique filter options for UI binding.
    /// </summary>
    public static UniqueFilter[] UniqueOptions { get; } = [UniqueFilter.Any, UniqueFilter.UniqueOnly, UniqueFilter.NonUniqueOnly];

    /// <summary>
    /// Returns true if any trait filters are set (non-default values).
    /// </summary>
    public bool HasTraitFilters => Gender != GenderFilter.Any || Unique != UniqueFilter.Any || IsChild.HasValue;

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

    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

    [Reactive] public bool IsSelected { get; set; }

    public void UpdateEntryNpcs()
    {
        if (Entry != null)
        {
            Entry.NpcFormKeys.Clear();
            Entry.NpcFormKeys.AddRange(SelectedNpcs.Select(npc => npc.FormKey));
        }
    }

    public void UpdateEntryFactions()
    {
        if (Entry != null)
        {
            Entry.FactionFormKeys.Clear();
            Entry.FactionFormKeys.AddRange(SelectedFactions.Select(faction => faction.FormKey));
        }
    }

    public void UpdateEntryKeywords()
    {
        if (Entry != null)
        {
            Entry.KeywordFormKeys.Clear();
            Entry.KeywordFormKeys.AddRange(SelectedKeywords.Select(keyword => keyword.FormKey));
        }
    }

    public void UpdateEntryRaces()
    {
        if (Entry != null)
        {
            Entry.RaceFormKeys.Clear();
            Entry.RaceFormKeys.AddRange(SelectedRaces.Select(race => race.FormKey));
        }
    }

    public void AddNpc(NpcRecordViewModel npc)
    {
        if (!_selectedNpcs.Any(existing => existing.FormKey == npc.FormKey))
        {
            _selectedNpcs.Add(npc);
            UpdateEntryNpcs();
        }
    }

    public void RemoveNpc(NpcRecordViewModel npc)
    {
        if (_selectedNpcs.Remove(npc))
        {
            UpdateEntryNpcs();
        }
    }

    public void AddFaction(FactionRecordViewModel faction)
    {
        if (!_selectedFactions.Any(existing => existing.FormKey == faction.FormKey))
        {
            _selectedFactions.Add(faction);
            UpdateEntryFactions();
        }
    }

    public void RemoveFaction(FactionRecordViewModel faction)
    {
        if (_selectedFactions.Remove(faction))
        {
            UpdateEntryFactions();
        }
    }

    public void AddKeyword(KeywordRecordViewModel keyword)
    {
        if (!_selectedKeywords.Any(existing => existing.FormKey == keyword.FormKey))
        {
            _selectedKeywords.Add(keyword);
            UpdateEntryKeywords();
        }
    }

    public void RemoveKeyword(KeywordRecordViewModel keyword)
    {
        if (_selectedKeywords.Remove(keyword))
        {
            UpdateEntryKeywords();
        }
    }

    public void AddRace(RaceRecordViewModel race)
    {
        if (!_selectedRaces.Any(existing => existing.FormKey == race.FormKey))
        {
            _selectedRaces.Add(race);
            UpdateEntryRaces();
        }
    }

    public void RemoveRace(RaceRecordViewModel race)
    {
        if (_selectedRaces.Remove(race))
        {
            UpdateEntryRaces();
        }
    }
}
