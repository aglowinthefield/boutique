using System.ComponentModel;

namespace Boutique.Models;

public enum DistributionFileType
{
    [Description("SPID")] Spid,
    [Description("SkyPatcher")] SkyPatcher,
    [Description("ESP")] Esp,
    [Description("CDF")] Cdf,
}

public enum DistributionTargetCategory
{
    [Description("NPCs")] Npcs,
    [Description("Containers")] Containers,
}

public sealed record DistributionFormatOption(
    DistributionFileType FileType,
    DistributionTargetCategory Category,
    string DisplayName)
{
    public static IReadOnlyList<DistributionFormatOption> All { get; } =
    [
        new(DistributionFileType.SkyPatcher, DistributionTargetCategory.Npcs, "SkyPatcher"),
        new(DistributionFileType.Spid, DistributionTargetCategory.Npcs, "SPID"),
        new(DistributionFileType.Cdf, DistributionTargetCategory.Containers, "CDF"),
    ];

    public static DistributionFormatOption? FromFileType(DistributionFileType type) =>
        All.FirstOrDefault(o => o.FileType == type);

    public string CategoryName => Category switch
    {
        DistributionTargetCategory.Npcs => "NPCs",
        DistributionTargetCategory.Containers => "Containers",
        _ => Category.ToString(),
    };
}

public enum DistributionLineKind
{
    Blank,
    Comment,
    Section,
    KeyValue,
    Other
}

public sealed record DistributionLine(
    int LineNumber,
    string RawText,
    DistributionLineKind Kind,
    string? SectionName,
    string? Key,
    string? Value,
    bool IsOutfitDistribution,
    IReadOnlyList<string> OutfitFormKeys,
    bool IsKeywordDistribution = false,
    string? KeywordIdentifier = null);

public sealed record DistributionFile(
    string FileName,
    string FullPath,
    string RelativePath,
    DistributionFileType Type,
    IReadOnlyList<DistributionLine> Lines,
    int OutfitDistributionCount,
    int KeywordDistributionCount = 0);
