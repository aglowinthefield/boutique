namespace Boutique.Models;

public enum DistributionFileType
{
    [System.ComponentModel.Description("SPID")]
    Spid,
    [System.ComponentModel.Description("SkyPatcher")]
    SkyPatcher,
    [System.ComponentModel.Description("ESP")]
    Esp
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
