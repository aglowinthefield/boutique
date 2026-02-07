using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

public sealed record RaceRecord(
  FormKey FormKey,
  string? EditorID,
  string? Name,
  ModKey ModKey) : IGameRecord
{
  /// <summary>
  ///   Display name prefers EditorID over localized Name to avoid duplicates in dropdowns.
  ///   EditorIDs are unique per record while Names can be duplicated across mods.
  /// </summary>
  public string DisplayName => !string.IsNullOrWhiteSpace(EditorID) ? EditorID : Name ?? "(No EditorID)";

  public string FormKeyString => FormKey.ToString();
  public string ModDisplayName => ModKey.FileName;
}
