using System.Windows;

namespace Boutique.Themes;

public static class ResourceKeys
{
  // Typography
  public static ComponentResourceKey HeadingTextBlockStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(HeadingTextBlockStyleKey));

  public static ComponentResourceKey FormKeyTextStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(FormKeyTextStyleKey));

  public static ComponentResourceKey SecondaryTextStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(SecondaryTextStyleKey));

  public static ComponentResourceKey LabelTextStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(LabelTextStyleKey));

  public static ComponentResourceKey EmptyStateTextStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(EmptyStateTextStyleKey));

  // Buttons
  public static ComponentResourceKey InlineRemoveButtonStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(InlineRemoveButtonStyleKey));

  public static ComponentResourceKey IconButtonStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(IconButtonStyleKey));

  public static ComponentResourceKey PrimaryButtonStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(PrimaryButtonStyleKey));

  // Borders / Containers
  public static ComponentResourceKey FilterChipStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(FilterChipStyleKey));

  public static ComponentResourceKey TagBadgeStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(TagBadgeStyleKey));

  public static ComponentResourceKey InfoPanelStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(InfoPanelStyleKey));

  public static ComponentResourceKey WarningPanelStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(WarningPanelStyleKey));

  public static ComponentResourceKey DropZoneStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(DropZoneStyleKey));

  public static ComponentResourceKey SelectableEntryStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(SelectableEntryStyleKey));

  // DataGrid Column Styles
  public static ComponentResourceKey DataGridFormKeyColumnStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(DataGridFormKeyColumnStyleKey));

  public static ComponentResourceKey DataGridSecondaryColumnStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(DataGridSecondaryColumnStyleKey));

  // ListBox
  public static ComponentResourceKey PlainListBoxItemStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(PlainListBoxItemStyleKey));

  // Settings Panel
  public static ComponentResourceKey SettingsLabelStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(SettingsLabelStyleKey));

  public static ComponentResourceKey SettingsRowMarginStyleKey { get; } =
    new(typeof(ResourceKeys), nameof(SettingsRowMarginStyleKey));

  // DataTemplates
  public static ComponentResourceKey FilterChipTemplateKey { get; } =
    new(typeof(ResourceKeys), nameof(FilterChipTemplateKey));

  public static ComponentResourceKey FilterDataGridTemplateKey { get; } =
    new(typeof(ResourceKeys), nameof(FilterDataGridTemplateKey));
}
