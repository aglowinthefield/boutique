using System.Reactive;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public partial class OutfitSeparatorViewModel : ReactiveObject, IOutfitQueueItem
{
  private readonly   Func<OutfitSeparatorViewModel, Task<bool>> _confirmAndRemove;
  [Reactive] private int                                        _childCount;
  [Reactive] private string?                                    _icon;
  [Reactive] private bool                                       _isExpanded = true;
  [Reactive] private bool                                       _isVisible  = true;

  [Reactive] private string _name;

  public OutfitSeparatorViewModel(
    string name,
    Func<OutfitSeparatorViewModel, Task<bool>> confirmAndRemove,
    int? index = null,
    string? icon = null)
  {
    _name             = string.IsNullOrWhiteSpace(name) ? "Group" : name;
    _icon             = icon;
    _confirmAndRemove = confirmAndRemove ?? throw new ArgumentNullException(nameof(confirmAndRemove));
    Index             = index ?? -1;

    RemoveSelfCommand = ReactiveCommand.CreateFromTask(async () => { await _confirmAndRemove(this); });

    this.WhenAnyValue(x => x.Icon)
        .Subscribe(_ => this.RaisePropertyChanged(nameof(IconUri)));
  }

  public int Index { get; set; }

  public Uri? IconUri => string.IsNullOrEmpty(Icon)
                           ? null
                           : new Uri($"pack://application:,,,/Assets/sprites/{Icon}", UriKind.Absolute);

  public ReactiveCommand<Unit, Unit> RemoveSelfCommand { get; }

  public string ItemId => $"sep:{Index}";
}

public interface IOutfitQueueItem
{
  string ItemId { get; }
  bool IsExpanded { get; set; }
  bool IsVisible { get; set; }
}
