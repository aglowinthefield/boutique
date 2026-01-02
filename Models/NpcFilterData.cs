using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

public sealed class NpcFilterData
{
    public required FormKey FormKey { get; init; }
    public required string? EditorId { get; init; }
    public required string? Name { get; init; }
    public required ModKey SourceMod { get; init; }

    public required IReadOnlySet<string> Keywords { get; init; }
    public required IReadOnlyList<FactionMembership> Factions { get; init; }
    public required FormKey? RaceFormKey { get; init; }
    public required string? RaceEditorId { get; init; }
    public required FormKey? ClassFormKey { get; init; }
    public required string? ClassEditorId { get; init; }
    public required FormKey? CombatStyleFormKey { get; init; }
    public required string? CombatStyleEditorId { get; init; }
    public required FormKey? VoiceTypeFormKey { get; init; }
    public required string? VoiceTypeEditorId { get; init; }
    public required FormKey? DefaultOutfitFormKey { get; init; }
    public required string? DefaultOutfitEditorId { get; init; }
    public required bool IsFemale { get; init; }
    public required bool IsUnique { get; init; }
    public required bool IsSummonable { get; init; }
    public required bool IsChild { get; init; }
    public required bool IsLeveled { get; init; }
    public required short Level { get; init; }
    public required FormKey? TemplateFormKey { get; init; }
    public required string? TemplateEditorId { get; init; }

    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : EditorId ?? "(No EditorID)";
    public string FormKeyString => FormKey.ToString();
    public string ModDisplayName => SourceMod.FileName;
}

public sealed class FactionMembership
{
    public required FormKey FactionFormKey { get; init; }
    public required string? FactionEditorId { get; init; }
    public required int Rank { get; init; }
}
