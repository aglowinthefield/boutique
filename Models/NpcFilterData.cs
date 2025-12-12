using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

/// <summary>
/// Contains all NPC data needed for SPID filter matching.
/// This includes keywords, factions, race, gender, level, and other properties
/// that SPID uses to determine if a distribution applies to an NPC.
/// </summary>
public sealed class NpcFilterData
{
    public required FormKey FormKey { get; init; }
    public required string? EditorId { get; init; }
    public required string? Name { get; init; }
    public required ModKey SourceMod { get; init; }
    
    // Keywords (from NPC record + race keywords)
    public required IReadOnlySet<string> Keywords { get; init; }
    
    // Factions the NPC belongs to
    public required IReadOnlyList<FactionMembership> Factions { get; init; }
    
    // Race info
    public required FormKey? RaceFormKey { get; init; }
    public required string? RaceEditorId { get; init; }
    
    // Class info
    public required FormKey? ClassFormKey { get; init; }
    public required string? ClassEditorId { get; init; }
    
    // Combat style
    public required FormKey? CombatStyleFormKey { get; init; }
    public required string? CombatStyleEditorId { get; init; }
    
    // Voice type
    public required FormKey? VoiceTypeFormKey { get; init; }
    public required string? VoiceTypeEditorId { get; init; }
    
    // Default outfit (for outfit filtering)
    public required FormKey? DefaultOutfitFormKey { get; init; }
    public required string? DefaultOutfitEditorId { get; init; }
    
    // Traits
    public required bool IsFemale { get; init; }
    public required bool IsUnique { get; init; }
    public required bool IsSummonable { get; init; }
    public required bool IsChild { get; init; }
    public required bool IsLeveled { get; init; }
    
    // Level (for level filter matching)
    public required short Level { get; init; }
    
    // Template info for template-based filtering
    public required FormKey? TemplateFormKey { get; init; }
    public required string? TemplateEditorId { get; init; }
    
    // Display helpers
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : EditorId ?? "(No EditorID)";
    public string FormKeyString => FormKey.ToString();
    public string ModDisplayName => SourceMod.FileName;
}

/// <summary>
/// Represents an NPC's membership in a faction with rank info.
/// </summary>
public sealed class FactionMembership
{
    public required FormKey FactionFormKey { get; init; }
    public required string? FactionEditorId { get; init; }
    public required int Rank { get; init; }
}

