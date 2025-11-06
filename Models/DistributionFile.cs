using System.Collections.Generic;

namespace Boutique.Models;

public enum DistributionFileType
{
    Spid,
    SkyPatcher
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
    string? Value);

public sealed record DistributionFile(
    string FileName,
    string FullPath,
    string RelativePath,
    DistributionFileType Type,
    IReadOnlyList<DistributionLine> Lines);
