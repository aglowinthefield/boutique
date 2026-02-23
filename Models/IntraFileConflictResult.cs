namespace Boutique.Models;

public sealed record IntraFileConflictResult(
  int TotalOverlappingNpcCount,
  IReadOnlyList<string> OutfitNames,
  IReadOnlyDictionary<(string, string), int> PairwiseOverlapCounts);
