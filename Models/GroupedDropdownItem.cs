namespace Boutique.Models;

public enum DropdownItemKind
{
  Header,
  Value,
  Action
}

/// <summary>
///   Base class for items in a grouped dropdown.
///   Can be either a group header, a selectable item, or a special action.
/// </summary>
public abstract record GroupedDropdownItem
{
  public abstract string DisplayText { get; }
  public abstract bool IsSelectable { get; }
  public abstract DropdownItemKind Kind { get; }
}

/// <summary>
///   A non-selectable group header in the dropdown.
/// </summary>
public sealed record GroupedDropdownHeader(string GroupName) : GroupedDropdownItem
{
  public override string DisplayText => GroupName;
  public override bool IsSelectable => false;
  public override DropdownItemKind Kind => DropdownItemKind.Header;
}

/// <summary>
///   A selectable item in a grouped dropdown with a typed payload.
/// </summary>
/// <typeparam name="T">The type of the value payload.</typeparam>
public sealed record GroupedDropdownItem<T>(
  string Text,
  T Value,
  string? GroupName = null) : GroupedDropdownItem
{
  public override string DisplayText => Text;
  public override bool IsSelectable => true;
  public override DropdownItemKind Kind => DropdownItemKind.Value;
}

/// <summary>
///   A special action item in the dropdown (e.g., "New File", "Add New...").
/// </summary>
public sealed record GroupedDropdownAction(string Text, string ActionId) : GroupedDropdownItem
{
  public override string DisplayText => Text;
  public override bool IsSelectable => true;
  public override DropdownItemKind Kind => DropdownItemKind.Action;
}

/// <summary>
///   Result of organizing items into a grouped dropdown structure.
/// </summary>
public sealed record GroupedDropdownStructure<T>(
  IReadOnlyList<GroupedDropdownItem> Items,
  IReadOnlyList<string> GroupNames);

/// <summary>
///   Distribution file payload for the distribution file dropdown.
/// </summary>
public sealed record DistributionFileInfo(
  string FileName,
  string UniquePath,
  string FullPath);
