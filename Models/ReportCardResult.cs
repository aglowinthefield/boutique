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
  IReadOnlyList<UncoveredAttributeRanking> UncoveredByMod);

public sealed record UncoveredAttributeRanking(string Label, int UncoveredCount, int TotalCount);
