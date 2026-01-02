using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

public sealed class DistributionEntry
{
    public IOutfitGetter? Outfit { get; set; }
    public List<FormKey> NpcFormKeys { get; set; } = [];
    public List<FormKey> FactionFormKeys { get; set; } = [];
    public List<FormKey> KeywordFormKeys { get; set; } = [];
    public List<FormKey> RaceFormKeys { get; set; } = [];

    public SpidTraitFilters TraitFilters { get; set; } = new();
    public int? Chance { get; set; }
}
