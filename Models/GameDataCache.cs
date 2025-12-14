using MessagePack;

namespace Boutique.Models;

/// <summary>
/// Metadata for cache validation and invalidation.
/// </summary>
[MessagePackObject]
public sealed class CacheMetadata
{
    /// <summary>Application version when cache was created.</summary>
    [Key(0)]
    public required string AppVersion { get; init; }

    /// <summary>Hash of the Skyrim data path.</summary>
    [Key(1)]
    public required string DataPathHash { get; init; }

    /// <summary>Signature of the load order (plugins.txt content hash or plugin list hash).</summary>
    [Key(2)]
    public required string LoadOrderSignature { get; init; }

    /// <summary>Signature of distribution files (paths + timestamps hash).</summary>
    [Key(3)]
    public required string DistributionFilesSignature { get; init; }

    /// <summary>When the cache was created.</summary>
    [Key(4)]
    public required DateTime CreatedAt { get; init; }
}

/// <summary>
/// Root container for cached game data.
/// </summary>
[MessagePackObject]
public sealed class GameDataCache
{
    /// <summary>Metadata for validation.</summary>
    [Key(0)]
    public required CacheMetadata Metadata { get; init; }

    /// <summary>Cached NPC filter data.</summary>
    [Key(1)]
    public required List<NpcFilterDataDto> NpcFilterData { get; init; }

    /// <summary>Cached distribution files.</summary>
    [Key(2)]
    public required List<DistributionFileDto> DistributionFiles { get; init; }
}

/// <summary>
/// Serializable DTO for NpcFilterData. FormKey and ModKey are stored as strings.
/// </summary>
[MessagePackObject]
public sealed class NpcFilterDataDto
{
    /// <summary>FormKey as string (e.g., "000012:Skyrim.esm").</summary>
    [Key(0)]
    public required string FormKeyString { get; init; }

    [Key(1)]
    public required string? EditorId { get; init; }

    [Key(2)]
    public required string? Name { get; init; }

    /// <summary>Source mod filename (e.g., "Skyrim.esm").</summary>
    [Key(3)]
    public required string SourceModFileName { get; init; }

    [Key(4)]
    public required List<string> Keywords { get; init; }

    [Key(5)]
    public required List<FactionMembershipDto> Factions { get; init; }

    [Key(6)]
    public required string? RaceFormKeyString { get; init; }

    [Key(7)]
    public required string? RaceEditorId { get; init; }

    [Key(8)]
    public required string? ClassFormKeyString { get; init; }

    [Key(9)]
    public required string? ClassEditorId { get; init; }

    [Key(10)]
    public required string? CombatStyleFormKeyString { get; init; }

    [Key(11)]
    public required string? CombatStyleEditorId { get; init; }

    [Key(12)]
    public required string? VoiceTypeFormKeyString { get; init; }

    [Key(13)]
    public required string? VoiceTypeEditorId { get; init; }

    [Key(14)]
    public required string? DefaultOutfitFormKeyString { get; init; }

    [Key(15)]
    public required string? DefaultOutfitEditorId { get; init; }

    [Key(16)]
    public required bool IsFemale { get; init; }

    [Key(17)]
    public required bool IsUnique { get; init; }

    [Key(18)]
    public required bool IsSummonable { get; init; }

    [Key(19)]
    public required bool IsChild { get; init; }

    [Key(20)]
    public required bool IsLeveled { get; init; }

    [Key(21)]
    public required short Level { get; init; }

    [Key(22)]
    public required string? TemplateFormKeyString { get; init; }

    [Key(23)]
    public required string? TemplateEditorId { get; init; }
}

/// <summary>
/// Serializable DTO for FactionMembership.
/// </summary>
[MessagePackObject]
public sealed class FactionMembershipDto
{
    [Key(0)]
    public required string FactionFormKeyString { get; init; }

    [Key(1)]
    public required string? FactionEditorId { get; init; }

    [Key(2)]
    public required int Rank { get; init; }
}

/// <summary>
/// Serializable DTO for DistributionFile.
/// </summary>
[MessagePackObject]
public sealed class DistributionFileDto
{
    [Key(0)]
    public required string FileName { get; init; }

    [Key(1)]
    public required string FullPath { get; init; }

    [Key(2)]
    public required string RelativePath { get; init; }

    /// <summary>DistributionFileType as int.</summary>
    [Key(3)]
    public required int Type { get; init; }

    [Key(4)]
    public required List<DistributionLineDto> Lines { get; init; }

    [Key(5)]
    public required int OutfitDistributionCount { get; init; }
}

/// <summary>
/// Serializable DTO for DistributionLine.
/// </summary>
[MessagePackObject]
public sealed class DistributionLineDto
{
    [Key(0)]
    public required int LineNumber { get; init; }

    [Key(1)]
    public required string RawText { get; init; }

    /// <summary>DistributionLineKind as int.</summary>
    [Key(2)]
    public required int Kind { get; init; }

    [Key(3)]
    public required string? SectionName { get; init; }

    [Key(4)]
    public required string? Key { get; init; }

    [Key(5)]
    public required string? Value { get; init; }

    [Key(6)]
    public required bool IsOutfitDistribution { get; init; }

    [Key(7)]
    public required List<string> OutfitFormKeys { get; init; }
}
