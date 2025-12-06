using System.Collections.Generic;
using System.Linq;
using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using ReactiveUI;

namespace Boutique.ViewModels;

public class NpcOutfitAssignmentViewModel : ReactiveObject
{
    private readonly NpcOutfitAssignment _assignment;
    private bool _isSelected;

    public NpcOutfitAssignmentViewModel(NpcOutfitAssignment assignment)
    {
        _assignment = assignment;
    }

    public NpcOutfitAssignment Assignment => _assignment;

    // NPC properties
    public FormKey NpcFormKey => _assignment.NpcFormKey;
    public string? EditorId => _assignment.EditorId;
    public string? Name => _assignment.Name;
    public string DisplayName => _assignment.DisplayName;
    public string FormKeyString => _assignment.FormKeyString;
    public string ModDisplayName => _assignment.ModDisplayName;

    // Outfit properties
    public FormKey? FinalOutfitFormKey => _assignment.FinalOutfitFormKey;
    public string? FinalOutfitEditorId => _assignment.FinalOutfitEditorId;
    public string FinalOutfitDisplay => _assignment.FinalOutfitDisplay;

    // Conflict info
    public bool HasConflict => _assignment.HasConflict;
    public int DistributionCount => _assignment.Distributions.Count;

    // Distributions for detail panel
    public IReadOnlyList<OutfitDistribution> Distributions => _assignment.Distributions;

    // Selection state for DataGrid
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>
    /// Gets a summary of the conflict (e.g., "2 files override")
    /// </summary>
    public string ConflictSummary => HasConflict 
        ? $"{DistributionCount} files" 
        : string.Empty;

    /// <summary>
    /// Gets the winning distribution file name
    /// </summary>
    public string WinningFileName => Distributions.FirstOrDefault(d => d.IsWinner)?.FileName ?? string.Empty;
    
    /// <summary>
    /// Gets the winning distribution
    /// </summary>
    private OutfitDistribution? WinningDistribution => Distributions.FirstOrDefault(d => d.IsWinner);
    
    /// <summary>
    /// Gets the targeting description for the winning distribution
    /// </summary>
    public string TargetingDescription => WinningDistribution?.TargetingDescription ?? string.Empty;
    
    /// <summary>
    /// Gets the chance percentage for the winning distribution
    /// </summary>
    public int Chance => WinningDistribution?.Chance ?? 100;
    
    /// <summary>
    /// Returns true if the winning distribution has a conditional chance (< 100%)
    /// </summary>
    public bool HasConditionalChance => Chance < 100;
    
    /// <summary>
    /// Gets a short targeting type summary for display in the grid
    /// </summary>
    public string TargetingType
    {
        get
        {
            var winner = WinningDistribution;
            if (winner == null)
                return string.Empty;
            
            if (winner.TargetsAllNpcs)
                return "All";
            
            var types = new List<string>();
            if (winner.UsesKeywordTargeting) types.Add("Keyword");
            if (winner.UsesFactionTargeting) types.Add("Faction");
            if (winner.UsesRaceTargeting) types.Add("Race");
            if (winner.UsesTraitTargeting) types.Add("Trait");
            
            return types.Count > 0 ? string.Join(", ", types) : "Specific";
        }
    }
    
    /// <summary>
    /// Gets the chance display string
    /// </summary>
    public string ChanceDisplay => Chance < 100 ? $"{Chance}%" : string.Empty;
}
