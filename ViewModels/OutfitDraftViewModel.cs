using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins;
using ReactiveUI;

namespace Boutique.ViewModels;

public class OutfitDraftViewModel : ReactiveObject
{
    private static readonly Regex AlphaOnlyRegex = new("[^A-Za-z]", RegexOptions.Compiled);

    private readonly ObservableCollection<ArmorRecordViewModel> _pieces;
    private readonly Action<OutfitDraftViewModel> _removeDraft;
    private readonly Action<OutfitDraftViewModel, ArmorRecordViewModel> _removePiece;
    private readonly Func<OutfitDraftViewModel, Task> _previewDraft;
    private string _name = string.Empty;
    private string _editorId = string.Empty;
    private string _previousValidName = "Outfit";
    private FormKey? _formKey;

    public OutfitDraftViewModel(
        string name,
        string editorId,
        IEnumerable<ArmorRecordViewModel> pieces,
        Action<OutfitDraftViewModel> removeDraft,
        Action<OutfitDraftViewModel, ArmorRecordViewModel> removePiece,
        Func<OutfitDraftViewModel, Task> previewDraft)
    {
        _removeDraft = removeDraft ?? throw new ArgumentNullException(nameof(removeDraft));
        _removePiece = removePiece ?? throw new ArgumentNullException(nameof(removePiece));
        _previewDraft = previewDraft ?? throw new ArgumentNullException(nameof(previewDraft));

        SetNameInternal(string.IsNullOrWhiteSpace(name) ? editorId : name, updateHistory: false);

        _pieces = new ObservableCollection<ArmorRecordViewModel>(pieces);
        _pieces.CollectionChanged += PiecesOnCollectionChanged;
        Pieces = new ReadOnlyObservableCollection<ArmorRecordViewModel>(_pieces);

        RemovePieceCommand = ReactiveCommand.Create<ArmorRecordViewModel>(piece => _removePiece(this, piece));
        RemoveSelfCommand = ReactiveCommand.Create(() => _removeDraft(this));
        PreviewCommand = ReactiveCommand.CreateFromTask(() => _previewDraft(this),
            this.WhenAnyValue(x => x.HasPieces));
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set => SetNameInternal(value, updateHistory: true);
    }

    public string EditorId => _editorId;

    public ReadOnlyObservableCollection<ArmorRecordViewModel> Pieces { get; }

    public bool HasPieces => _pieces.Count > 0;

    public FormKey? FormKey
    {
        get => _formKey;
        set
        {
            if (_formKey == value)
                return;

            this.RaiseAndSetIfChanged(ref _formKey, value);
            this.RaisePropertyChanged(nameof(FormIdDisplay));
            this.RaisePropertyChanged(nameof(Header));
        }
    }

    public string FormIdDisplay => _formKey.HasValue ? $"0x{_formKey.Value.ID:X8}" : "Pending";

    public string Header => $"{Name} ({EditorId}) — FormID {FormIdDisplay}";

    public ReactiveCommand<ArmorRecordViewModel, Unit> RemovePieceCommand { get; }

    public ReactiveCommand<Unit, Unit> RemoveSelfCommand { get; }

    public ReactiveCommand<Unit, Unit> PreviewCommand { get; }

    public IReadOnlyList<ArmorRecordViewModel> GetPieces() => _pieces.ToList();

    public void RemovePiece(ArmorRecordViewModel piece)
    {
        if (_pieces.Remove(piece))
        {
            this.RaisePropertyChanged(nameof(HasPieces));
        }
    }

    public (IReadOnlyList<ArmorRecordViewModel> added, IReadOnlyList<ArmorRecordViewModel> replaced) AddPieces(IEnumerable<ArmorRecordViewModel> newPieces)
    {
        var added = new List<ArmorRecordViewModel>();

        foreach (var piece in newPieces)
        {
            if (piece == null)
                continue;

            if (_pieces.Any(p => p.Armor.FormKey == piece.Armor.FormKey))
                continue;

            _pieces.Add(piece);
            added.Add(piece);
        }

        if (added.Count > 0)
        {
            this.RaisePropertyChanged(nameof(HasPieces));
        }

        return (added, Array.Empty<ArmorRecordViewModel>());
    }

    private void PiecesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.RaisePropertyChanged(nameof(HasPieces));
    }

    public void RevertName() => SetNameInternal(_previousValidName, updateHistory: false);

    private void SetNameInternal(string? value, bool updateHistory)
    {
        var sanitized = Sanitize(value);
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = string.IsNullOrEmpty(_name) ? "Outfit" : _name;
        }

        if (sanitized == _name)
            return;

        if (updateHistory)
        {
            _previousValidName = _name;
        }

        this.RaiseAndSetIfChanged(ref _name, sanitized);
        this.RaiseAndSetIfChanged(ref _editorId, sanitized);
        this.RaisePropertyChanged(nameof(Header));

        if (!updateHistory)
        {
            _previousValidName = _name;
        }
    }

    private static string Sanitize(string? value) => string.IsNullOrEmpty(value)
        ? string.Empty
        : AlphaOnlyRegex.Replace(value, string.Empty);
}
