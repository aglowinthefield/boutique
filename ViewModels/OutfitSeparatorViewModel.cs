using System.Reactive;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public partial class OutfitSeparatorViewModel : ReactiveObject, IOutfitQueueItem
{
    private readonly Func<OutfitSeparatorViewModel, Task<bool>> _confirmAndRemove;

    [Reactive] private string _name = "Group";
    [Reactive] private bool _isExpanded = true;
    [Reactive] private bool _isVisible = true;

    public OutfitSeparatorViewModel(
        string name,
        Func<OutfitSeparatorViewModel, Task<bool>> confirmAndRemove,
        int? index = null)
    {
        _name = string.IsNullOrWhiteSpace(name) ? "Group" : name;
        _confirmAndRemove = confirmAndRemove ?? throw new ArgumentNullException(nameof(confirmAndRemove));
        Index = index ?? -1;

        RemoveSelfCommand = ReactiveCommand.CreateFromTask(async () => { await _confirmAndRemove(this); });
    }

    public int Index { get; set; }

    public string ItemId => $"sep:{Index}";

    public ReactiveCommand<Unit, Unit> RemoveSelfCommand { get; }
}

public interface IOutfitQueueItem
{
    string ItemId { get; }
    bool IsExpanded { get; set; }
    bool IsVisible { get; set; }
}
