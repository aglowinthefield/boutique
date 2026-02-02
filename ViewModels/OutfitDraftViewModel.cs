using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive;
using Boutique.Utilities;
using Mutagen.Bethesda.Plugins;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public partial class OutfitDraftViewModel : ReactiveObject, IOutfitQueueItem
{
    private readonly Func<OutfitDraftViewModel, Task> _duplicateDraft;
    private readonly ObservableCollection<ArmorRecordViewModel> _pieces;
    private readonly Func<OutfitDraftViewModel, Task> _previewDraft;
    private readonly Action<OutfitDraftViewModel> _removeDraft;
    private readonly Action<OutfitDraftViewModel, ArmorRecordViewModel> _removePiece;
    private string _editorId = string.Empty;

    [Reactive] private bool _isExpanded = true;
    [Reactive] private bool _isVisible = true;
    [Reactive] private FormKey? _formKey;

    private string _name = string.Empty;
    private string _previousValidName = "Outfit";

    public OutfitDraftViewModel(
        string name,
        string editorId,
        IEnumerable<ArmorRecordViewModel> pieces,
        Action<OutfitDraftViewModel> removeDraft,
        Action<OutfitDraftViewModel, ArmorRecordViewModel> removePiece,
        Func<OutfitDraftViewModel, Task> previewDraft,
        Func<OutfitDraftViewModel, Task>? duplicateDraft = null)
    {
        _removeDraft = removeDraft ?? throw new ArgumentNullException(nameof(removeDraft));
        _removePiece = removePiece ?? throw new ArgumentNullException(nameof(removePiece));
        _previewDraft = previewDraft ?? throw new ArgumentNullException(nameof(previewDraft));
        _duplicateDraft = duplicateDraft ?? (_ => Task.CompletedTask);

        SetNameInternal(string.IsNullOrWhiteSpace(name) ? editorId : name, false);

        _pieces = new ObservableCollection<ArmorRecordViewModel>(pieces);
        _pieces.CollectionChanged += PiecesOnCollectionChanged;
        Pieces = new ReadOnlyObservableCollection<ArmorRecordViewModel>(_pieces);

        this.WhenAnyValue(x => x.FormKey)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(FormIdDisplay));
                this.RaisePropertyChanged(nameof(Header));
            });

        RemovePieceCommand = ReactiveCommand.Create<ArmorRecordViewModel>(piece => _removePiece(this, piece));
        RemoveSelfCommand = ReactiveCommand.Create(() => _removeDraft(this));
        PreviewCommand = ReactiveCommand.CreateFromTask(
            () => _previewDraft(this),
            this.WhenAnyValue(x => x.HasPieces));
        DuplicateCommand = ReactiveCommand.CreateFromTask(
            () => _duplicateDraft(this),
            this.WhenAnyValue(x => x.HasPieces));
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string ItemId => $"draft:{EditorId}";

    public string Name
    {
        get => _name;
        set => SetNameInternal(value, true);
    }

    public string EditorId => _editorId;

    public ReadOnlyObservableCollection<ArmorRecordViewModel> Pieces { get; }

    public bool HasPieces => _pieces.Count > 0;

    public int PieceCount => _pieces.Count;

    public bool IsOverride { get; init; }

    public ModKey? OverrideSourceMod { get; init; }

    public string FormIdDisplay => FormKey.HasValue ? $"0x{FormKey.Value.ID:X8}" : "Pending";

    public string? OverrideDisplayText => IsOverride && FormKey.HasValue
        ? $"Overrides {FormIdDisplay} in {OverrideSourceMod?.FileName ?? FormKey.Value.ModKey.FileName}"
        : null;

    public string Header => $"{Name} ({EditorId}) — FormID {FormIdDisplay}";

    public ReactiveCommand<ArmorRecordViewModel, Unit> RemovePieceCommand { get; }

    public ReactiveCommand<Unit, Unit> RemoveSelfCommand { get; }

    public ReactiveCommand<Unit, Unit> PreviewCommand { get; }

    public ReactiveCommand<Unit, Unit> DuplicateCommand { get; }

    public IReadOnlyList<ArmorRecordViewModel> GetPieces() => _pieces.ToList();

    public void RemovePiece(ArmorRecordViewModel piece)
    {
        if (_pieces.Remove(piece))
        {
            this.RaisePropertyChanged(nameof(HasPieces));
        }
    }

    public (IReadOnlyList<ArmorRecordViewModel> added, IReadOnlyList<ArmorRecordViewModel> replaced) AddPieces(
        IEnumerable<ArmorRecordViewModel> newPieces)
    {
        var added = new List<ArmorRecordViewModel>();

        foreach (var piece in newPieces)
        {
            if (_pieces.Any(p => p.Armor.FormKey == piece.Armor.FormKey))
            {
                continue;
            }

            _pieces.Add(piece);
            added.Add(piece);
        }

        if (added.Count > 0)
        {
            this.RaisePropertyChanged(nameof(HasPieces));
        }

        return (added, []);
    }

    private void PiecesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.RaisePropertyChanged(nameof(HasPieces));
        this.RaisePropertyChanged(nameof(PieceCount));
    }

    public void RevertName() => SetNameInternal(_previousValidName, false);

    private void SetNameInternal(string? value, bool updateHistory)
    {
        var sanitized = Sanitize(value);
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = string.IsNullOrEmpty(_name) ? "Outfit" : _name;
        }

        if (sanitized == _name)
        {
            return;
        }

        if (updateHistory)
        {
            _previousValidName = _name;
        }

        this.RaiseAndSetIfChanged(ref _name, sanitized);
        _editorId = sanitized;
        this.RaisePropertyChanged(nameof(EditorId));
        this.RaisePropertyChanged(nameof(Header));

        if (!updateHistory)
        {
            _previousValidName = _name;
        }
    }

    private static string Sanitize(string? value) => InputPatterns.Identifier.Sanitize(value);
}
