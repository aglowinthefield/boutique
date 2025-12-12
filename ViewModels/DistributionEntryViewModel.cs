using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Boutique.Models;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Boutique.ViewModels;

public class DistributionEntryViewModel : ReactiveObject
{
    private ObservableCollection<NpcRecordViewModel> _selectedNpcs = new();
    private ObservableCollection<FactionRecordViewModel> _selectedFactions = new();
    private ObservableCollection<KeywordRecordViewModel> _selectedKeywords = new();
    private ObservableCollection<RaceRecordViewModel> _selectedRaces = new();

    public DistributionEntryViewModel(
        DistributionEntry entry,
        System.Action<DistributionEntryViewModel>? removeAction = null,
        System.Action? onUseChanceChanging = null)
    {
        Entry = entry;
        SelectedOutfit = entry.Outfit;
        UseChance = entry.Chance.HasValue;
        Chance = entry.Chance ?? 100;
        
        // Initialize selected NPCs from entry
        if (entry.NpcFormKeys.Count > 0)
        {
            var npcVms = entry.NpcFormKeys
                .Select(fk => new NpcRecordViewModel(new NpcRecord(fk, null, null, fk.ModKey)))
                .ToList();
            
            foreach (var npcVm in npcVms)
            {
                // Don't set IsSelected - that's only for temporary picker selection state
                // The NPC is tracked by being in the SelectedNpcs collection
                _selectedNpcs.Add(npcVm);
            }
        }

        // Initialize selected Factions from entry
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

        // Initialize selected Keywords from entry
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

        // Initialize selected Races from entry
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

        // Sync SelectedOutfit changes back to Entry
        this.WhenAnyValue(x => x.SelectedOutfit)
            .Subscribe(outfit => Entry.Outfit = outfit);

        // Sync UseChance and Chance changes back to Entry
        // Show warning when enabling chance-based distribution
        var previousUseChance = UseChance;
        this.WhenAnyValue(x => x.UseChance)
            .Skip(1) // Skip initial value
            .Subscribe(useChance =>
            {
                var wasEnabled = previousUseChance;
                previousUseChance = useChance; // Update for next time
                
                if (useChance && !wasEnabled && onUseChanceChanging != null)
                {
                    // User is enabling chance - show warning
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
                        // Revert the change
                        previousUseChance = false; // Reset tracking
                        UseChance = false;
                        return;
                    }
                    
                    // User confirmed - allow the change
                    onUseChanceChanging();
                }
                
                Entry.Chance = useChance ? Chance : null;
            });
        
        this.WhenAnyValue(x => x.Chance)
            .Skip(1) // Skip initial value
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
        // Check if NPC is already in the list by FormKey
        if (!_selectedNpcs.Any(existing => existing.FormKey == npc.FormKey))
        {
            // Don't set IsSelected - that's only for temporary picker selection state
            // The NPC is tracked by being in the SelectedNpcs collection
            _selectedNpcs.Add(npc);
            UpdateEntryNpcs();
        }
    }

    public void RemoveNpc(NpcRecordViewModel npc)
    {
        if (_selectedNpcs.Remove(npc))
        {
            // Don't modify IsSelected - that's only for temporary picker selection state
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
