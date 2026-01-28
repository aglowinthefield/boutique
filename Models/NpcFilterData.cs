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

    /// <summary>
    ///     Pre-calculated set of keys for fast string matching (ExactMatchesNpcStrings).
    ///     Includes Name, EditorID, Keywords, Template, Faction IDs, etc.
    /// </summary>
    public HashSet<string>? MatchKeys { get; set; }

    /// <summary>
    ///     Skill values indexed by SPID skill index (6-23).
    ///     Indices: 6=OneHanded, 7=TwoHanded, 8=Marksman, 9=Block, 10=Smithing,
    ///     11=HeavyArmor, 12=LightArmor, 13=Pickpocket, 14=Lockpicking, 15=Sneak,
    ///     16=Alchemy, 17=Speechcraft, 18=Alteration, 19=Conjuration, 20=Destruction,
    ///     21=Illusion, 22=Restoration, 23=Enchanting
    /// </summary>
    public byte[] SkillValues { get; init; } = new byte[24];

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
