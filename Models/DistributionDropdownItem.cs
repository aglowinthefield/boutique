namespace Boutique.Models;

/// <summary>
///     Base class for items in the distribution file dropdown.
///     Can be either a group header or a selectable file.
/// </summary>
public abstract record DistributionDropdownItem
{
    public abstract string DisplayText { get; }
    public abstract bool IsSelectable { get; }
}

/// <summary>
///     A non-selectable group header in the dropdown.
/// </summary>
public sealed record DistributionGroupHeader(string GroupName) : DistributionDropdownItem
{
    public override string DisplayText => GroupName;
    public override bool IsSelectable => false;
}

/// <summary>
///     A selectable file item in the dropdown.
/// </summary>
public sealed record DistributionFileItem(
    string FileName,
    string UniquePath,
    string FullPath,
    string GroupName) : DistributionDropdownItem
{
    public override string DisplayText => UniquePath;
    public override bool IsSelectable => true;
}

/// <summary>
///     Special item for creating a new file.
/// </summary>
public sealed record DistributionNewFileItem : DistributionDropdownItem
{
    private DistributionNewFileItem()
    {
    }

    public static DistributionNewFileItem Instance { get; } = new();

    public override string DisplayText => "<New File>";
    public override bool IsSelectable => true;
}

/// <summary>
///     Result of organizing distribution files into a dropdown structure.
/// </summary>
public sealed record DistributionDropdownStructure(
    IReadOnlyList<DistributionDropdownItem> Items,
    IReadOnlyList<string> GroupNames);
