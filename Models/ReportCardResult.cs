namespace Boutique.Models;

public sealed record ReportCardResult(
  double NpcCoveragePercent,
  double ModUtilizationPercent,
  double VarietyPercent,
  double OverallPercent,
  int EligibleNpcCount,
  int CoveredNpcCount,
  int ModOutfitCount,
  int UsedModOutfitCount,
  int UniqueOutfitCount,
  IReadOnlyList<UncoveredAttributeRanking> UncoveredByFaction,
  IReadOnlyList<UncoveredAttributeRanking> UncoveredByClass,
  IReadOnlyList<UncoveredAttributeRanking> UncoveredByRace,
  IReadOnlyList<UncoveredAttributeRanking> UncoveredByMod,
  IReadOnlyList<UnusedOutfitGroup> UnusedOutfitGroups);

public sealed record UncoveredAttributeRanking(string Label, int UncoveredCount, int TotalCount);

public sealed record UnusedOutfitGroup(string PluginName, int Count, IReadOnlyList<string> OutfitEditorIds);
