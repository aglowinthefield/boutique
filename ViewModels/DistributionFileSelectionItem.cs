namespace Boutique.ViewModels;

public class DistributionFileSelectionItem(
    bool isNewFile,
    DistributionFileViewModel? file,
    bool hasDuplicateFileName = false)
{
    public bool IsNewFile { get; } = isNewFile;
    public DistributionFileViewModel? File { get; } = file;
    public bool HasDuplicateFileName { get; } = hasDuplicateFileName;

    public string ModName => IsNewFile ? string.Empty : File?.ModName ?? string.Empty;

    public string DisplayName
    {
        get
        {
            if (IsNewFile)
                return "<New File>";
            if (HasDuplicateFileName)
                return File?.UniquePath ?? string.Empty;
            return File?.FileName ?? string.Empty;
        }
    }

    public override string ToString() => DisplayName;
}
