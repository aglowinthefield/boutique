using Mutagen.Bethesda.Plugins;

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
  IReadOnlyList<AffectedOutfitInfo> AllAffectedOutfits);
