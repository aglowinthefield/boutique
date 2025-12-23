namespace Boutique.ViewModels;

public class DistributionFileSelectionItem(bool isNewFile, DistributionFileViewModel? file)
{
    public bool IsNewFile { get; } = isNewFile;
    public DistributionFileViewModel? File { get; } = file;

    private string DisplayName
    {
        get
        {
            if (IsNewFile)
                return "<New File>";
            return File?.FileName ?? string.Empty;
        }
    }

    public override string ToString() => DisplayName;
}
