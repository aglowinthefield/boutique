using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

public sealed record LocationRecord(
  FormKey FormKey,
  string? EditorID,
  string? Name,
  ModKey ModKey) : IGameRecord
{
  public string DisplayName => !string.IsNullOrWhiteSpace(EditorID) ? EditorID : Name ?? "(No EditorID)";
  public string FormKeyString => FormKey.ToString();
  public string ModDisplayName => ModKey.FileName;

  public static LocationRecord FromGetter(ILocationGetter location) =>
    new(location.FormKey, location.EditorID, location.Name?.String, location.FormKey.ModKey);
}
