using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

/// <summary>
///   Represents conflict information for an NPC when creating a new distribution file.
/// </summary>
public sealed record NpcConflictInfo(

  /// <summary>The NPC's FormKey</summary>
  FormKey NpcFormKey,

  /// <summary>The NPC's display name</summary>
  string? DisplayName,

  /// <summary>The existing distribution file that targets this NPC</summary>
  string ExistingFileName,

  /// <summary>The outfit currently assigned by the existing distribution</summary>
  string? ExistingOutfitName,

  /// <summary>The outfit that will be assigned by the new distribution</summary>
  string? NewOutfitName,

  /// <summary>The chance percentage of the existing distribution (0-100)</summary>
  int ExistingChance = 100,

  /// <summary>The chance percentage of the new distribution (0-100)</summary>
  int NewChance = 100)
{
  /// <summary>True if this represents a hard conflict (both distributions have 100% chance)</summary>
  public bool IsHardConflict => ExistingChance == 100 && NewChance == 100;

  /// <summary>True if this represents a probabilistic overlap (at least one has < 100% chance)</summary>
  public bool IsProbabilisticOverlap => !IsHardConflict;
}
