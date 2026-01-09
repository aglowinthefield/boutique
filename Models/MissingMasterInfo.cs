using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

public record MissingMasterInfo(
    ModKey MissingMaster,
    IReadOnlyList<AffectedOutfitInfo> AffectedOutfits);

public record AffectedOutfitInfo(
    FormKey FormKey,
    string? EditorId,
    IReadOnlyList<FormKey> OrphanedArmorFormKeys);

public record MissingMastersResult(
    bool HasMissingMasters,
    IReadOnlyList<MissingMasterInfo> MissingMasters,
    IReadOnlyList<AffectedOutfitInfo> AllAffectedOutfits,
    IReadOnlyList<IOutfitGetter> ValidOutfits);
