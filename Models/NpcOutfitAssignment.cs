using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

/// <summary>
///     Represents an NPC and all outfit distributions targeting them,
///     along with the final resolved outfit.
/// </summary>
public sealed record NpcOutfitAssignment(
    FormKey NpcFormKey,
    string? EditorId,
    string? Name,
    ModKey SourceMod,
    FormKey? FinalOutfitFormKey,
    string? FinalOutfitEditorId,
    IReadOnlyList<OutfitDistribution> Distributions,
    bool HasConflict)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : EditorId ?? "(No EditorID)";
    public string FormKeyString => NpcFormKey.ToString();
    public string ModDisplayName => SourceMod.FileName;
    public string FinalOutfitDisplay => FinalOutfitEditorId ?? FinalOutfitFormKey?.ToString() ?? "(None)";
}
