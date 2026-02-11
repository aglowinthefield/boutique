namespace Boutique.Models;

public sealed record IntraFileConflictResult(
  bool HasConflicts,
  string ConflictSummary,
  bool HasOverlaps,
  string OverlapSummary);
