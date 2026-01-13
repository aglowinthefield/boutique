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
    public int? Chance { get; set; }
}

public sealed record DistributionParseError(int LineNumber, string LineContent, string Reason)
{
    public string ErrorHeader => $"Line {LineNumber}: {Reason}";
}
