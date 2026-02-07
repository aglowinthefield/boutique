using System.Collections.ObjectModel;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.ViewModels;

public class ExistingOutfitViewModel
{
  public ExistingOutfitViewModel(
    string displayName,
    string editorId,
    FormKey formKey,
    IEnumerable<IArmorGetter>? pieces)
  {
    DisplayName = displayName;
    EditorId = editorId;
    FormKey = formKey;

    var pieceList = pieces?.ToList() ?? [];
    Pieces = new ReadOnlyCollection<IArmorGetter>(pieceList);
    PieceCount = Pieces.Count;
    FormIdDisplay = $"0x{formKey.ID:X8}";
  }

  public string DisplayName { get; }
  public string EditorId { get; }
  public FormKey FormKey { get; }
  public IReadOnlyList<IArmorGetter> Pieces { get; }
  public int PieceCount { get; }
  public string FormIdDisplay { get; }

  public string Summary =>
    $"{FormatName()} — {PieceCount} piece(s) — FormID {FormIdDisplay}";

  private string FormatName()
  {
    return string.Equals(DisplayName, EditorId, StringComparison.Ordinal)
      ? DisplayName
      : $"{DisplayName} ({EditorId})";
  }
}
