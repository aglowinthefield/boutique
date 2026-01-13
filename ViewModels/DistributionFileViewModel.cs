using System.IO;
using Boutique.Models;
using ReactiveUI;

namespace Boutique.ViewModels;

public class DistributionFileViewModel(DistributionFile file) : ReactiveObject
{
    public string FileName => file.FileName;
    public string RelativePath => file.RelativePath;
    public string Directory => Path.GetDirectoryName(file.RelativePath) ?? string.Empty;
    public string FullPath => file.FullPath;
    public IReadOnlyList<DistributionLine> Lines => file.Lines;
    public DistributionFileType Type => file.Type;

    public string UniquePath => ExtractUniquePath();

    public string ModName => ExtractModName();

    public string TypeDisplay => file.Type switch
    {
        DistributionFileType.Spid => "SPID",
        DistributionFileType.SkyPatcher => "SkyPatcher",
        _ => file.Type.ToString()
    };

    public int RecordCount => file.Lines.Count(l => l.Kind == DistributionLineKind.KeyValue);
    public int CommentCount => file.Lines.Count(l => l.Kind == DistributionLineKind.Comment);
    public int OutfitCount => file.OutfitDistributionCount;
    public int KeywordCount => file.KeywordDistributionCount;
    public bool HasKeywordDistributions => file.KeywordDistributionCount > 0;

    private string ExtractUniquePath()
    {
        var relativePath = RelativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var parts = relativePath.Split(Path.DirectorySeparatorChar);

        if (file.Type == DistributionFileType.SkyPatcher)
        {
            var npcIndex = Array.FindIndex(parts, p => p.Equals("npc", StringComparison.OrdinalIgnoreCase));
            if (npcIndex >= 0 && npcIndex < parts.Length - 1)
                return string.Join("/", parts.Skip(npcIndex + 1));

            var outfitIndex = Array.FindIndex(parts, p => p.Equals("outfit", StringComparison.OrdinalIgnoreCase));
            if (outfitIndex >= 0 && outfitIndex < parts.Length - 1)
                return string.Join("/", parts.Skip(outfitIndex + 1));

            var skypatcherIndex = Array.FindIndex(parts, p => p.Equals("SkyPatcher", StringComparison.OrdinalIgnoreCase));
            if (skypatcherIndex >= 0 && skypatcherIndex < parts.Length - 1)
                return string.Join("/", parts.Skip(skypatcherIndex + 1));
        }

        var dir = Path.GetDirectoryName(relativePath);
        if (!string.IsNullOrEmpty(dir))
        {
            var dirParts = dir.Split(Path.DirectorySeparatorChar);
            if (dirParts.Length > 0)
                return $"{dirParts[^1]}/{FileName}";
        }

        return FileName;
    }

    private string ExtractModName()
    {
        var relativePath = RelativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var parts = relativePath.Split(Path.DirectorySeparatorChar);

        var skseIndex = Array.FindIndex(parts, p => p.Equals("SKSE", StringComparison.OrdinalIgnoreCase));
        if (skseIndex > 0)
            return parts[skseIndex - 1];

        if (file.Type == DistributionFileType.SkyPatcher)
        {
            var npcIndex = Array.FindIndex(parts, p => p.Equals("npc", StringComparison.OrdinalIgnoreCase));
            if (npcIndex >= 0 && npcIndex < parts.Length - 2)
                return parts[npcIndex + 1];

            var outfitIndex = Array.FindIndex(parts, p => p.Equals("outfit", StringComparison.OrdinalIgnoreCase));
            if (outfitIndex >= 0 && outfitIndex < parts.Length - 2)
                return parts[outfitIndex + 1];

            var weaponIndex = Array.FindIndex(parts, p => p.Equals("weapon", StringComparison.OrdinalIgnoreCase));
            if (weaponIndex >= 0 && weaponIndex < parts.Length - 2)
                return parts[weaponIndex + 1];
        }

        return string.Empty;
    }
}
