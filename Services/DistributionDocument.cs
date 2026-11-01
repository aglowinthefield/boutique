using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;

namespace Boutique.Services;

public sealed record DistributionPreview(string Content, IReadOnlyDictionary<int, DistributionEntryViewModel> EntriesByLine);

/// <summary>Owns one editable distribution and its persisted baseline. No dialogs or navigation.</summary>
public sealed class DistributionDocument(DistributionFileBackupService backups, Func<DistributionEntry, DistributionEntryViewModel>? createEntry = null, DistributionFilePathService? paths = null)
{
  private string _baseline = DistributionFileFormatter.GenerateFileContent([], DistributionFileType.SkyPatcher);
  private string _baselineDestination = string.Empty;
  private DistributionFileType _baselineFormat = DistributionFileType.SkyPatcher;
  private List<DistributionEntry> _baselineEntries = [];
  private long _revision;
  private long _baselineRevision;
  private readonly HashSet<DistributionEntryViewModel> _trackedEntries = [];

  private void CaptureBaseline(string content)
  {
    _baseline = content;
    _baselineRevision = _revision;
    _baselineDestination = Destination;
    _baselineFormat = Format;
    _baselineEntries = Entries.Select(e => e.Entry.Copy()).ToList();
  }

  public void Discard()
  {
    if (IsSaving)
    {
      throw new InvalidOperationException("Cannot discard during a save.");
    }

    Load(_baselineDestination, _baselineFormat,
      _baselineEntries.Select(e => createEntry?.Invoke(e.Copy()) ?? new DistributionEntryViewModel(e.Copy())).ToList(), UnparsedLines);
  }

  private ObservableCollection<DistributionEntryViewModel>? _entries;
  public ObservableCollection<DistributionEntryViewModel> Entries
  {
    get
    {
      if (_entries != null)
      {
        return _entries;
      }

      _entries = new EditableDistributionCollection<DistributionEntryViewModel>(EnsureEditable, entry => entry.TakeDocumentOwnership());
      _entries.CollectionChanged += (_, _) =>
      {
        foreach (var entry in _trackedEntries.Where(e => !_entries.Contains(e)).ToList())
        {
          entry.EntryChanged -= OnEntryChanged;
          _trackedEntries.Remove(entry);
        }

        foreach (var entry in _entries)
        {
          if (_trackedEntries.Add(entry))
          {
            entry.EntryChanged += OnEntryChanged;
          }
        }

        _revision++;
      };
      return _entries;
    }
  }

  private void OnEntryChanged(object? sender, EventArgs e) => _revision++;

  private void EnsureEditable()
  {
    if (IsSaving)
    {
      throw new InvalidOperationException("Cannot edit a distribution during a save.");
    }
  }

  private string _destination = string.Empty;
  public string Destination
  {
    get => _destination;
    set
    {
      EnsureEditable();
      _destination = value;
    }
  }

  private DistributionFileType _format = DistributionFileType.SkyPatcher;
  public DistributionFileType Format
  {
    get => _format;
    set
    {
      EnsureEditable();
      var effective = Entries.Any(e => e.UseChance || e.Type != DistributionType.Outfit) ? DistributionFileType.Spid : value;
      if (_format == effective)
      {
        return;
      }

      _format = effective;
      Destination = paths?.BuildPathFromExistingFile(Destination, effective) ?? Destination;
    }
  }

  public IReadOnlyList<DistributionParseError> UnparsedLines { get; private set; } = [];
  public DistributionPreview GeneratePreview()
  {
    var correspondence = new Dictionary<int, DistributionEntryViewModel>();
    var content = DistributionFileFormatter.GenerateFileContent(Entries, EffectiveFormat, UnparsedLines, correspondence);
    return new DistributionPreview(content, correspondence);
  }

  public string Content => GeneratePreview().Content;
  public DistributionFileType EffectiveFormat => Entries.Any(e => e.UseChance || e.Type != DistributionType.Outfit) ? DistributionFileType.Spid : Format;
  public bool HasUnsavedChanges => _revision != _baselineRevision || Content != _baseline || Destination != _baselineDestination || Format != _baselineFormat;
  public bool IsSaving { get; private set; }
  public event Action<string>? FileSaved;

  public void Load(string destination, DistributionFileType format, IEnumerable<DistributionEntryViewModel> entries, IReadOnlyList<DistributionParseError>? unparsedLines = null)
  {
    EnsureEditable();
    _destination = destination;
    _format = format;
    Entries.Clear();
    foreach (var entry in entries)
    {
      Entries.Add(entry);
    }

    UnparsedLines = unparsedLines?.ToArray() ?? [];
    CaptureBaseline(Content);
  }

  public async Task SaveAsync(CancellationToken cancellationToken = default)
  {
    if (IsSaving)
    {
      throw new InvalidOperationException("A distribution save is already in progress.");
    }

    Format = EffectiveFormat;
    var content = Content;
    var snapshotEntries = Entries.Select(e => e.Entry.Copy()).ToList();
    var snapshotRevision = _revision;
    var snapshotFormat = Format;
    var snapshotDestination = Destination;
    var destination = Path.GetFullPath(Destination);
    IsSaving = true;
    foreach (var entry in Entries)
    {
      entry.IsDocumentSaving = true;
    }

    var staged = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
    try
    {
      await Task.Yield();
      cancellationToken.ThrowIfCancellationRequested();
      Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
      await File.WriteAllTextAsync(staged, content, Encoding.UTF8, cancellationToken);
      cancellationToken.ThrowIfCancellationRequested();
      backups.CreateBackup(destination);
      if (File.Exists(destination))
      {
        File.Replace(staged, destination, null);
      }
      else
      {
        File.Move(staged, destination);
      }

      _baseline = content;
      _baselineRevision = snapshotRevision;
      _baselineEntries = snapshotEntries;
      _baselineFormat = snapshotFormat;
      _baselineDestination = snapshotDestination;
    }
    finally
    {
      try
      {
        if (File.Exists(staged))
        {
          File.Delete(staged);
        }
      }
      finally
      {
        IsSaving = false;
        foreach (var entry in Entries)
        {
          entry.IsDocumentSaving = false;
        }
      }
    }

    FileSaved?.Invoke(destination);
  }
}
