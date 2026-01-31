using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

/// <summary>
///     Represents a single outfit distribution from a SPID or SkyPatcher file.
/// </summary>
public sealed record OutfitDistribution(
    string FilePath,
    string FileName,
    DistributionFileType FileType,
    FormKey OutfitFormKey,
    string? OutfitEditorId,
    int ProcessingOrder,
    bool IsWinner,
    string? RawLine = null,
    string? TargetingDescription = null,
    int Chance = 100,
    bool TargetsAllNpcs = false,
    bool UsesKeywordTargeting = false,
    bool UsesFactionTargeting = false,
    bool UsesRaceTargeting = false,
    bool UsesTraitTargeting = false);
