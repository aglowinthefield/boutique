using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

public sealed record KeywordRecord(
  FormKey FormKey,
  string? EditorID,
  ModKey ModKey,
  string? SourceName = null) : IGameRecord
{
  public string DisplayName => EditorID ?? "(No EditorID)";
  public string FormKeyString => FormKey.ToString();
  public string ModDisplayName => SourceName ?? (ModKey.IsNull ? string.Empty : ModKey.FileName);

  public static KeywordRecord FromGetter(IKeywordGetter keyword) =>
    new(keyword.FormKey, keyword.EditorID, keyword.FormKey.ModKey);
}
