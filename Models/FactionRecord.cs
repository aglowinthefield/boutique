using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

public sealed record FactionRecord(
  FormKey FormKey,
  string? EditorID,
  string? Name,
  ModKey ModKey) : IGameRecord
{
  /// <summary>
  ///   Gets the display name which prefers EditorID over localized Name to avoid duplicates in dropdowns.
  ///   EditorIDs are unique per record while Names can be duplicated across mods.
  /// </summary>
  public string DisplayName => !string.IsNullOrWhiteSpace(EditorID) ? EditorID : Name ?? "(No EditorID)";

  public string FormKeyString => FormKey.ToString();
  public string ModDisplayName => ModKey.FileName;

  public static FactionRecord FromGetter(IFactionGetter faction) =>
    new(faction.FormKey, faction.EditorID, faction.Name?.String, faction.FormKey.ModKey);
}
