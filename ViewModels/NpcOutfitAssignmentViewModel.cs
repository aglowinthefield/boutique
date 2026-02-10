using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public partial class NpcOutfitAssignmentViewModel(NpcOutfitAssignment assignment) : ReactiveObject
{
  /// <summary>Selection state for DataGrid binding.</summary>
  [Reactive] private bool _isSelected;

  /// <summary>The underlying assignment model.</summary>
  public NpcOutfitAssignment Assignment { get; } = assignment;

  /// <summary>NPC FormKey.</summary>
  public FormKey NpcFormKey => Assignment.NpcFormKey;

  /// <summary>NPC EditorID.</summary>
  public string? EditorId => Assignment.EditorId;

  /// <summary>NPC display name.</summary>
  public string? Name => Assignment.Name;

  /// <summary>Display name (Name if available, otherwise EditorID).</summary>
  public string DisplayName => Assignment.DisplayName;

  /// <summary>FormKey as string.</summary>
  public string FormKeyString => Assignment.FormKeyString;

  /// <summary>Source mod display name.</summary>
  public string ModDisplayName => Assignment.ModDisplayName;

  /// <summary>Final resolved outfit FormKey.</summary>
  public FormKey? FinalOutfitFormKey => Assignment.FinalOutfitFormKey;

  /// <summary>Final resolved outfit EditorID.</summary>
  public string? FinalOutfitEditorId => Assignment.FinalOutfitEditorId;

  /// <summary>Final outfit display string.</summary>
  public string FinalOutfitDisplay => Assignment.FinalOutfitDisplay;

  /// <summary>Whether this NPC has conflicting outfit distributions.</summary>
  public bool HasConflict => Assignment.HasConflict;

  /// <summary>Number of distributions affecting this NPC.</summary>
  public int DistributionCount => Assignment.Distributions.Count;

  /// <summary>All distributions for this NPC (for detail panel).</summary>
  public IReadOnlyList<OutfitDistribution> Distributions => Assignment.Distributions;

  /// <summary>
  ///   Gets a summary of the conflict (e.g., "2 files override")
  /// </summary>
  public string ConflictSummary => HasConflict
                                     ? $"{DistributionCount} files"
                                     : string.Empty;

  /// <summary>
  ///   Gets the winning distribution file name
  /// </summary>
  public string WinningFileName => Distributions.FirstOrDefault(d => d.IsWinner)?.FileName ?? string.Empty;

  /// <summary>
  ///   Gets the winning distribution
  /// </summary>
  private OutfitDistribution? WinningDistribution => Distributions.FirstOrDefault(d => d.IsWinner);

  /// <summary>
  ///   Gets the targeting description for the winning distribution
  /// </summary>
  public string TargetingDescription => WinningDistribution?.TargetingDescription ?? string.Empty;

  /// <summary>
  ///   Gets the chance percentage for the winning distribution
  /// </summary>
  public int Chance => WinningDistribution?.Chance ?? 100;

  /// <summary>
  ///   Returns true if the winning distribution has a conditional chance (< 100%)
  /// </summary>
  public bool HasConditionalChance => Chance < 100;

  /// <summary>
  ///   Gets a short targeting type summary for display in the grid
  /// </summary>
  public string TargetingType
  {
    get
    {
      var winner = WinningDistribution;
      if (winner == null)
      {
        return string.Empty;
      }

      if (winner.TargetsAllNpcs)
      {
        return "All";
      }

      var types = new List<string>();
      if (winner.UsesKeywordTargeting)
      {
        types.Add("Keyword");
      }

      if (winner.UsesFactionTargeting)
      {
        types.Add("Faction");
      }

      if (winner.UsesRaceTargeting)
      {
        types.Add("Race");
      }

      if (winner.UsesTraitTargeting)
      {
        types.Add("Trait");
      }

      return types.Count > 0 ? string.Join(", ", types) : "Specific";
    }
  }

  /// <summary>
  ///   Gets the chance display string
  /// </summary>
  public string ChanceDisplay => Chance < 100 ? $"{Chance}%" : string.Empty;
}
