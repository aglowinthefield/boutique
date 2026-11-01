using System.Collections.ObjectModel;

namespace Boutique.Utilities;

/// <summary>Checks the document's edit lock before a collection mutation, not after notification.</summary>
internal sealed class EditableDistributionCollection<T>(Action ensureEditable, Action<T>? prepareItem = null) : ObservableCollection<T>
{
  protected override void InsertItem(int index, T item)
  {
    ensureEditable();
    prepareItem?.Invoke(item);
    base.InsertItem(index, item);
  }
  protected override void SetItem(int index, T item)
  {
    ensureEditable();
    prepareItem?.Invoke(item);
    base.SetItem(index, item);
  }
  protected override void RemoveItem(int index)
  {
    ensureEditable();
    base.RemoveItem(index);
  }
  protected override void ClearItems()
  {
    ensureEditable();
    base.ClearItems();
  }
  protected override void MoveItem(int oldIndex, int newIndex)
  {
    ensureEditable();
    base.MoveItem(oldIndex, newIndex);
  }
}
