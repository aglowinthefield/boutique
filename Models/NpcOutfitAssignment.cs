using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

/// <summary>
///     Represents an NPC and all outfit distributions targeting them,
///     along with the final resolved outfit.
/// </summary>
public sealed record NpcOutfitAssignment(
    /// <summary>The NPC's FormKey</summary>
    FormKey NpcFormKey,
    /// <summary>The NPC's EditorID</summary>
    string? EditorId,
    /// <summary>The NPC's display name</summary>
    string? Name,
    /// <summary>The mod that originally defines this NPC</summary>
    ModKey SourceMod,
    /// <summary>The final resolved outfit FormKey (from the winning distribution)</summary>
    FormKey? FinalOutfitFormKey,
    /// <summary>The final resolved outfit EditorID</summary>
    string? FinalOutfitEditorId,
    /// <summary>All distributions targeting this NPC</summary>
    IReadOnlyList<OutfitDistribution> Distributions,
    /// <summary>Whether this NPC has conflicting distributions (more than one)</summary>
    bool HasConflict)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : EditorId ?? "(No EditorID)";
    public string FormKeyString => NpcFormKey.ToString();
    public string ModDisplayName => SourceMod.FileName;
    public string FinalOutfitDisplay => FinalOutfitEditorId ?? FinalOutfitFormKey?.ToString() ?? "(None)";
}
