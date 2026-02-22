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
  IReadOnlyList<NpcFactionGroup> UncoveredNpcGroups,
  IReadOnlyList<UnusedOutfitGroup> UnusedOutfitGroups);

public sealed record NpcFactionGroup(string GroupName, int Count, IReadOnlyList<string> NpcNames);

public sealed record UnusedOutfitGroup(string PluginName, int Count, IReadOnlyList<string> OutfitEditorIds);
