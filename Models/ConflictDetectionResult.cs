namespace Boutique.Models;

/// <summary>
/// Result of conflict detection between new distribution entries and existing distribution files.
/// </summary>
public sealed record ConflictDetectionResult(
    /// <summary>Whether conflicts were detected</summary>
    bool HasConflicts,
    /// <summary>Whether conflicts are resolved by filename ordering</summary>
    bool ConflictsResolvedByFilename,
    /// <summary>Human-readable summary of conflicts</summary>
    string ConflictSummary,
    /// <summary>Suggested filename with Z-prefix to ensure proper load order</summary>
    string SuggestedFileName,
    /// <summary>List of all detected conflicts</summary>
    IReadOnlyList<NpcConflictInfo> Conflicts);

