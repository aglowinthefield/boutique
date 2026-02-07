namespace Boutique.Models;

/// <summary>
///   Result of conflict detection between new distribution entries and existing distribution files.
/// </summary>
public sealed record ConflictDetectionResult(
  bool HasConflicts,
  bool ConflictsResolvedByFilename,
  string ConflictSummary,
  string SuggestedFileName,
  IReadOnlyList<NpcConflictInfo> Conflicts);
