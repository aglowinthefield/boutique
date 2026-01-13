using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

public enum DistributionType
{
    Outfit,
    Keyword
}

public sealed class DistributionEntry
{
    public DistributionType Type { get; set; } = DistributionType.Outfit;
    public IOutfitGetter? Outfit { get; set; }
    public string? KeywordToDistribute { get; set; }
    public List<FormKey> NpcFormKeys { get; set; } = [];
    public List<FormKey> FactionFormKeys { get; set; } = [];

    /// <summary>
    /// Keyword EditorIDs used for filtering. Includes both game keywords and virtual
    /// keywords (SPID-distributed via Keyword = lines).
    /// </summary>
    public List<string> KeywordEditorIds { get; set; } = [];

    public List<FormKey> RaceFormKeys { get; set; } = [];
    public List<FormKey> ClassFormKeys { get; set; } = [];
    public List<FormKey> CombatStyleFormKeys { get; set; } = [];
    public List<FormKey> OutfitFilterFormKeys { get; set; } = [];
    public List<FormKey> PerkFormKeys { get; set; } = [];
    public List<FormKey> VoiceTypeFormKeys { get; set; } = [];
    public List<FormKey> LocationFormKeys { get; set; } = [];
    public List<FormKey> FormListFormKeys { get; set; } = [];

    public SpidTraitFilters TraitFilters { get; set; } = new();

    /// <summary>
    /// SPID level filters (position 4). Supports level ranges (e.g., "5/20") and
    /// skill filters (e.g., "12(85/999)" for Alteration skill 85+).
    /// </summary>
    public string? LevelFilters { get; set; }

    public int? Chance { get; set; }
}

public sealed record DistributionParseError(int LineNumber, string LineContent, string Reason)
{
    public string ErrorHeader => $"Line {LineNumber}: {Reason}";
}
