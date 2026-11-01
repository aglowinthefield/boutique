using System.IO;
using Boutique.Models;
using Boutique.Services;
using Boutique.ViewModels;
using FluentAssertions;
using Serilog;
using Xunit;

namespace Boutique.Tests;

public sealed class DistributionDocumentTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), "Boutique-document-" + Guid.NewGuid());
  private DistributionDocument CreateDocument() => new(new DistributionFileBackupService(new LoggerConfiguration().CreateLogger(), Path.Combine(_root, "backups")));

  [Fact]
  public async Task ImmediateSaveWritesCurrentEditsAndCompletesWithCleanBaseline()
  {
    Directory.CreateDirectory(_root);
    var path = Path.Combine(_root, "test_DISTR.ini");
    var document = CreateDocument();
    document.Load(path, DistributionFileType.Spid, [new DistributionEntryViewModel(new DistributionEntry { Type = DistributionType.Keyword, KeywordToDistribute = "Before" })]);
    document.Entries[0].KeywordToDistribute = "After";
    document.HasUnsavedChanges.Should().BeTrue();
    string? saved = null;
    document.FileSaved += p => saved = p;

    await document.SaveAsync();

    (await File.ReadAllTextAsync(path)).Should().Contain("Keyword = After").And.NotContain("Before");
    document.HasUnsavedChanges.Should().BeFalse();
    saved.Should().Be(path);
  }

  [Fact]
  public async Task CancelledSavePreservesOriginalBytesAndEdits()
  {
    Directory.CreateDirectory(_root);
    var path = Path.Combine(_root, "test_DISTR.ini");
    await File.WriteAllTextAsync(path, "original bytes");
    var document = CreateDocument();
    document.Load(path, DistributionFileType.Spid, []);
    document.Entries.Add(new DistributionEntryViewModel(new DistributionEntry { Type = DistributionType.Keyword, KeywordToDistribute = "Edited" }));
    using var cancellation = new CancellationTokenSource();
    var previousContext = SynchronizationContext.Current;
    var heldContext = new HeldContext();
    SynchronizationContext.SetSynchronizationContext(heldContext);
    var saving = document.SaveAsync(cancellation.Token);
    SynchronizationContext.SetSynchronizationContext(previousContext);
    cancellation.Cancel();
    heldContext.Continue();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saving);
    (await File.ReadAllTextAsync(path)).Should().Be("original bytes");
    document.HasUnsavedChanges.Should().BeTrue();
    Directory.GetFiles(_root, "*.tmp").Should().BeEmpty();
  }

  [Fact]
  public void DiscardRestoresEntriesAndDestinationAfterFinalEntryRemoval()
  {
    var document = CreateDocument();
    document.Load("original_DISTR.ini", DistributionFileType.Spid,
      [new DistributionEntryViewModel(new DistributionEntry { Type = DistributionType.Keyword, KeywordToDistribute = "Original" })],
      [new DistributionParseError(2, "Spell = Retained", "SPID (preserved)")]);
    document.Entries.Clear();
    document.HasUnsavedChanges.Should().BeTrue();
    document.Content.Should().Contain("Spell = Retained").And.NotContain("Keyword = Original");
    document.Destination = "changed.ini";
    document.Discard();
    document.Entries.Should().ContainSingle().Which.KeywordToDistribute.Should().Be("Original");
    document.Destination.Should().Be("original_DISTR.ini");
    document.HasUnsavedChanges.Should().BeFalse();
  }

  [Fact]
  public async Task InFlightSaveRejectsEditingAndOverlappingSave()
  {
    Directory.CreateDirectory(_root);
    var document = CreateDocument();
    document.Load(Path.Combine(_root, "test_DISTR.ini"), DistributionFileType.Spid,
      [new DistributionEntryViewModel(new DistributionEntry { Type = DistributionType.Keyword, KeywordToDistribute = "Original" })]);
    var previousContext = SynchronizationContext.Current;
    var heldContext = new HeldContext();
    SynchronizationContext.SetSynchronizationContext(heldContext);
    var saving = document.SaveAsync();
    SynchronizationContext.SetSynchronizationContext(previousContext);
    Action edit = () => document.Entries[0].KeywordToDistribute = "Wrong";
    edit.Should().Throw<InvalidOperationException>();
    Action remove = () => document.Entries.Clear();
    remove.Should().Throw<InvalidOperationException>();
    await Assert.ThrowsAsync<InvalidOperationException>(() => document.SaveAsync());
    heldContext.Continue();
    await saving;
    document.Entries[0].KeywordToDistribute.Should().Be("Original");
  }

  [Fact]
  public void DocumentOwnsModelsAndStartsClean()
  {
    var document = CreateDocument();
    document.HasUnsavedChanges.Should().BeFalse();
    var model = new DistributionEntry { Type = DistributionType.Keyword, KeywordToDistribute = "Owned" };
    document.Load("test.ini", DistributionFileType.Spid, [new DistributionEntryViewModel(model)]);
    model.RawStringFilters = "external";
    document.Entries[0].Entry.RawStringFilters = "external";
    document.Entries[0].Entry.RawStringFilters.Should().BeNull();
    document.HasUnsavedChanges.Should().BeFalse();
  }

  [Fact]
  public void AddingAnUnfinishedEntryIsAnEdit()
  {
    var document = CreateDocument();
    document.Entries.Add(new DistributionEntryViewModel(new DistributionEntry()));
    document.HasUnsavedChanges.Should().BeTrue();
    document.Discard();
    document.Entries.Should().BeEmpty();
    document.HasUnsavedChanges.Should().BeFalse();
  }

  [Fact]
  public async Task SavingFinalDeletionRetainsUnparsedLinesAndNotifiesOnce()
  {
    var document = CreateDocument();
    var path = Path.Combine(_root, "empty_DISTR.ini");
    document.Load(path, DistributionFileType.Spid,
      [new DistributionEntryViewModel(new DistributionEntry { Type = DistributionType.Keyword, KeywordToDistribute = "Remove" })],
      [new DistributionParseError(2, "Spell = Retained", "SPID (preserved)")]);
    document.Entries.Clear();
    var notifications = 0;
    document.FileSaved += _ => notifications++;
    document.HasUnsavedChanges.Should().BeTrue();
    await document.SaveAsync();
    (await File.ReadAllTextAsync(path)).Should().Contain("Spell = Retained").And.NotContain("Keyword = Remove");
    document.HasUnsavedChanges.Should().BeFalse();
    notifications.Should().Be(1);
  }

  [Fact]
  public async Task FailedReplacementPreservesOriginalAndEditsAndRemovesStage()
  {
    Directory.CreateDirectory(_root);
    var path = Path.Combine(_root, "locked_DISTR.ini");
    await File.WriteAllTextAsync(path, "original bytes");
    var document = CreateDocument();
    document.Load(path, DistributionFileType.Spid, []);
    document.Entries.Add(new DistributionEntryViewModel(new DistributionEntry { Type = DistributionType.Keyword, KeywordToDistribute = "Edited" }));
    var notified = false;
    document.FileSaved += _ => notified = true;
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
      await Assert.ThrowsAnyAsync<IOException>(() => document.SaveAsync());
    (await File.ReadAllTextAsync(path)).Should().Be("original bytes");
    document.HasUnsavedChanges.Should().BeTrue();
    document.IsSaving.Should().BeFalse();
    notified.Should().BeFalse();
    Directory.GetFiles(_root, "*.tmp").Should().BeEmpty();
  }

  [Fact]
  public async Task BackupsRetainFifteenPreviousVersionsAndFailureIsNonblocking()
  {
    Directory.CreateDirectory(_root);
    var path = Path.Combine(_root, "backup_DISTR.ini");
    var backups = new DistributionFileBackupService(new LoggerConfiguration().CreateLogger(), Path.Combine(_root, "backups"));
    var document = new DistributionDocument(backups);
    document.Load(path, DistributionFileType.Spid,
      [new DistributionEntryViewModel(new DistributionEntry { Type = DistributionType.Keyword, KeywordToDistribute = "Version0" })]);
    for (var i = 0; i < 18; i++)
    {
      document.Entries[0].KeywordToDistribute = "Version" + i;
      await document.SaveAsync();
    }
    var retained = backups.ListBackups(path);
    retained.Should().HaveCount(15);
    (await File.ReadAllTextAsync(retained[0].FilePath)).Should().Contain("Version16");
    var blockedRoot = Path.Combine(_root, "not-a-directory");
    await File.WriteAllTextAsync(blockedRoot, "block");
    var noBackups = new DistributionDocument(new DistributionFileBackupService(new LoggerConfiguration().CreateLogger(), blockedRoot));
    noBackups.Load(path, DistributionFileType.Spid,
      [new DistributionEntryViewModel(new DistributionEntry { Type = DistributionType.Keyword, KeywordToDistribute = "StillSaved" })]);
    await noBackups.SaveAsync();
    (await File.ReadAllTextAsync(path)).Should().Contain("StillSaved");
    noBackups.HasUnsavedChanges.Should().BeFalse();
  }

  [Fact]
  public void PreviewCorrespondenceComesFromGeneratedLines()
  {
    var document = CreateDocument();
    document.Entries.Add(new DistributionEntryViewModel(new DistributionEntry()));
    var keyword = new DistributionEntryViewModel(new DistributionEntry { Type = DistributionType.Keyword, KeywordToDistribute = "Mapped" });
    document.Entries.Add(keyword);
    var preview = document.GeneratePreview();
    var mapping = preview.EntriesByLine.Should().ContainSingle().Subject;
    mapping.Value.Should().BeSameAs(keyword);
    preview.Content.Split('\n')[mapping.Key].Should().Contain("Keyword = Mapped");
  }

  private sealed class HeldContext : SynchronizationContext
  {
    private Action? _continuation;
    public override void Post(SendOrPostCallback callback, object? state) => _continuation = () => callback(state);
    public void Continue() => _continuation!();
  }

  public void Dispose()
  {
    if (Directory.Exists(_root)) Directory.Delete(_root, true);
  }
}
