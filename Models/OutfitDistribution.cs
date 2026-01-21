using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

/// <summary>
///     Represents a single outfit distribution from a SPID or SkyPatcher file.
/// </summary>
public sealed record OutfitDistribution(
    /// <summary>The source distribution file path</summary>
    string FilePath,
    /// <summary>The filename for display</summary>
    string FileName,
    /// <summary>SPID or SkyPatcher</summary>
    DistributionFileType FileType,
    /// <summary>The outfit FormKey being assigned</summary>
    FormKey OutfitFormKey,
    /// <summary>The outfit EditorID (if resolvable)</summary>
    string? OutfitEditorId,
    /// <summary>Processing order index (lower = processed earlier, higher = wins)</summary>
    int ProcessingOrder,
    /// <summary>Whether this distribution is the winner (last in processing order)</summary>
    bool IsWinner,
    /// <summary>The raw distribution line text</summary>
    string? RawLine = null,
    /// <summary>Human-readable description of how this NPC was targeted</summary>
    string? TargetingDescription = null,
    /// <summary>The chance percentage (0-100) for this distribution</summary>
    int Chance = 100,
    /// <summary>Whether this distribution targets all NPCs (no filters)</summary>
    bool TargetsAllNpcs = false,
    /// <summary>Whether this distribution uses keyword-based targeting</summary>
    bool UsesKeywordTargeting = false,
    /// <summary>Whether this distribution uses faction-based targeting</summary>
    bool UsesFactionTargeting = false,
    /// <summary>Whether this distribution uses race-based targeting</summary>
    bool UsesRaceTargeting = false,
    /// <summary>Whether this distribution uses trait-based targeting (gender, unique, etc.)</summary>
    bool UsesTraitTargeting = false);
