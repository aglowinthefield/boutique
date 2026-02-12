using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

public sealed record ClassRecord(
  FormKey FormKey,
  string? EditorID,
  string? Name,
  ModKey ModKey) : IGameRecord
{
  public string DisplayName => !string.IsNullOrWhiteSpace(EditorID) ? EditorID : Name ?? "(No EditorID)";
  public string FormKeyString => FormKey.ToString();
  public string ModDisplayName => ModKey.FileName;

  public static ClassRecord FromGetter(IClassGetter classRecord) =>
    new(classRecord.FormKey, classRecord.EditorID, classRecord.Name.String, classRecord.FormKey.ModKey);
}
