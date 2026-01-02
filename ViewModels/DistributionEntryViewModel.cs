using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Boutique.Models;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

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

public class DistributionEntryViewModel : ReactiveObject
{
    private ObservableCollection<NpcRecordViewModel> _selectedNpcs = [];
    private ObservableCollection<FactionRecordViewModel> _selectedFactions = [];
    private ObservableCollection<KeywordRecordViewModel> _selectedKeywords = [];
    private ObservableCollection<RaceRecordViewModel> _selectedRaces = [];

    public event EventHandler? EntryChanged;

    private void RaiseEntryChanged() => EntryChanged?.Invoke(this, EventArgs.Empty);

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
            .Skip(1)
            .Subscribe(outfit =>
            {
                Entry.Outfit = outfit;
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
                RaiseEntryChanged();
            });

        this.WhenAnyValue(x => x.Chance)
            .Skip(1)
            .Subscribe(chance =>
            {
                if (UseChance)
                {
                    Entry.Chance = chance;
                    RaiseEntryChanged();
                }
            });

        RemoveCommand = ReactiveCommand.Create(() => removeAction?.Invoke(this));
    }

    public DistributionEntry Entry { get; }

    [Reactive] public IOutfitGetter? SelectedOutfit { get; set; }

    [Reactive] public bool UseChance { get; set; }
    [Reactive] public int Chance { get; set; } = 100;
    [Reactive] public GenderFilter Gender { get; set; } = GenderFilter.Any;
    [Reactive] public UniqueFilter Unique { get; set; } = UniqueFilter.Any;
    [Reactive] public bool? IsChild { get; set; }

    public static GenderFilter[] GenderOptions { get; } = [GenderFilter.Any, GenderFilter.Female, GenderFilter.Male];
    public static UniqueFilter[] UniqueOptions { get; } = [UniqueFilter.Any, UniqueFilter.UniqueOnly, UniqueFilter.NonUniqueOnly];

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
        Entry.NpcFormKeys.Clear();
        Entry.NpcFormKeys.AddRange(SelectedNpcs.Select(npc => npc.FormKey));
        RaiseEntryChanged();
    }

    public void UpdateEntryFactions()
    {
        Entry.FactionFormKeys.Clear();
        Entry.FactionFormKeys.AddRange(SelectedFactions.Select(faction => faction.FormKey));
        RaiseEntryChanged();
    }

    public void UpdateEntryKeywords()
    {
        Entry.KeywordFormKeys.Clear();
        Entry.KeywordFormKeys.AddRange(SelectedKeywords.Select(keyword => keyword.FormKey));
        RaiseEntryChanged();
    }

    public void UpdateEntryRaces()
    {
        Entry.RaceFormKeys.Clear();
        Entry.RaceFormKeys.AddRange(SelectedRaces.Select(race => race.FormKey));
        RaiseEntryChanged();
    }

    public bool AddCriterion<T>(T item, ObservableCollection<T> collection, Action updateAction)
        where T : ISelectableRecordViewModel
    {
        if (collection.Any(existing => existing.FormKey == item.FormKey))
            return false;

        collection.Add(item);
        updateAction();
        return true;
    }

    public bool RemoveCriterion<T>(T item, ObservableCollection<T> collection, Action updateAction)
        where T : class
    {
        if (!collection.Remove(item))
            return false;

        updateAction();
        return true;
    }

    public void AddNpc(NpcRecordViewModel npc) => AddCriterion(npc, _selectedNpcs, UpdateEntryNpcs);
    public void RemoveNpc(NpcRecordViewModel npc) => RemoveCriterion(npc, _selectedNpcs, UpdateEntryNpcs);

    public void AddFaction(FactionRecordViewModel faction) => AddCriterion(faction, _selectedFactions, UpdateEntryFactions);
    public void RemoveFaction(FactionRecordViewModel faction) => RemoveCriterion(faction, _selectedFactions, UpdateEntryFactions);

    public void AddKeyword(KeywordRecordViewModel keyword) => AddCriterion(keyword, _selectedKeywords, UpdateEntryKeywords);
    public void RemoveKeyword(KeywordRecordViewModel keyword) => RemoveCriterion(keyword, _selectedKeywords, UpdateEntryKeywords);

    public void AddRace(RaceRecordViewModel race) => AddCriterion(race, _selectedRaces, UpdateEntryRaces);
    public void RemoveRace(RaceRecordViewModel race) => RemoveCriterion(race, _selectedRaces, UpdateEntryRaces);
}
