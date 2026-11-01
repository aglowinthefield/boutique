using Boutique.Models;
using Boutique.Services;
using Boutique.ViewModels;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace Boutique.Tests;

public sealed class ConnectedDraftLifecycleTests : IDisposable
{
  private readonly string _directory = Path.Combine(Path.GetTempPath(), "boutique-lifecycle-" + Guid.NewGuid());
  private readonly LoggingService _logging = new();

  public ConnectedDraftLifecycleTests() => Directory.CreateDirectory(_directory);

  [Fact]
  public async Task ConnectedDraftsSaveTwiceWithStableIdentitiesAndCleanReverts()
  {
    var environment = new SyntheticEnvironment();
    var lifecycle = new ConnectedDraftLifecycle(new PatchingService(null!, _logging, environment));
    lifecycle.SetOutputPath(Path.Combine(_directory, "Connected.esp"));
    var armor = new FormKey(ModKey.FromFileName("Source.esp"), 0x800);
    var child = lifecycle.AddList("Child", [new DraftEntry(new DraftReference(armor))]);
    var parent = lifecycle.AddList("Parent", [new DraftEntry(new DraftReference(DraftId: child))]);
    var outfit = lifecycle.AddOutfit("Outfit", [new DraftReference(DraftId: parent)]);

    var firstSave = await lifecycle.SaveAsync(SaveIntent.Explicit);
    Assert.True(firstSave.Status == ConnectedSaveStatus.Saved, firstSave.Message);
    var identity = lifecycle.GetDraft(child).OutputIdentity;
    Assert.NotNull(identity);
    Assert.False(lifecycle.HasUnsavedChanges);
    lifecycle.Rename(child, "Changed");
    lifecycle.Rename(child, "Child");
    Assert.False(lifecycle.HasUnsavedChanges);
    lifecycle.Rename(child, "Renamed");
    Assert.Equal(ConnectedSaveStatus.Saved, (await lifecycle.SaveAsync(SaveIntent.Explicit)).Status);
    Assert.Equal(identity, lifecycle.GetDraft(child).OutputIdentity);
    Assert.False(lifecycle.GetDraft(parent).IsDirty);
    Assert.False(lifecycle.GetDraft(outfit).IsDirty);
  }

  private (ConnectedDraftLifecycle lifecycle, SyntheticEnvironment environment, string path) Workspace()
  {
    var environment = new SyntheticEnvironment();
    var lifecycle = new ConnectedDraftLifecycle(new PatchingService(null!, _logging, environment));
    var path = Path.Combine(_directory, "Output.esp");
    lifecycle.SetOutputPath(path);
    return (lifecycle, environment, path);
  }

  private static readonly FormKey ArmorKey = new(ModKey.FromFileName("Source.esp"), 0x800);

  [Theory]
  [InlineData(DraftRecordKind.LeveledList)]
  [InlineData(DraftRecordKind.Outfit)]
  public async Task SourceImportsArePendingOverridesUntilPersisted(DraftRecordKind kind)
  {
    var (life, env, path) = Workspace();
    var source = new SkyrimMod(ModKey.FromFileName("Source.esp"), SkyrimRelease.SkyrimSE);
    var armor = source.Armors.AddNew("Armor");
    var list = source.LeveledItems.AddNew("Imported");
    list.Entries = [new() { Data = new() { Reference = armor.FormKey.ToLink<IItemGetter>(), Count = 2, Level = 3 } }];
    list.ChanceNone = new Noggog.Percent(0.37);
    var outfit = source.Outfits.AddNew("ImportedOutfit");
    outfit.Items = [armor.FormKey.ToLink<IOutfitTargetGetter>()];
    var key = kind == DraftRecordKind.LeveledList ? list.FormKey : outfit.FormKey;
    env.LoadedPlugins = [source];
    var id = life.Import(kind, key);
    var originalName = life.GetDraft(id).EditorId;
    Assert.Null(life.GetDraft(id).OutputIdentity);
    Assert.True(life.HasUnsavedChanges);
    life.Rename(id, "Changed");
    life.Rename(id, originalName);
    Assert.True(life.HasUnsavedChanges);
    Assert.False(File.Exists(path));
    var saved = await life.SaveAsync(SaveIntent.Explicit);
    Assert.True(saved.Status == ConnectedSaveStatus.Saved, saved.Message);
    Assert.Equal(key, life.GetDraft(id).OutputIdentity!.FormKey);
    Assert.False(life.HasUnsavedChanges);
    var output = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
    if (kind == DraftRecordKind.LeveledList)
    {
      Assert.Equal(list.ChanceNone, output.LeveledItems.Single().ChanceNone);
      Assert.Equal(2, output.LeveledItems.Single().Entries![0].Data!.Count);
    }
    else Assert.Equal(armor.FormKey, output.Outfits.Single().Items![0].FormKey);
    var bytes = File.ReadAllBytes(path);
    Assert.Equal(ConnectedSaveStatus.NothingToSave, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    Assert.Equal(bytes, File.ReadAllBytes(path));
  }

  [Fact]
  public async Task EmptyPlaceholderExcludedButEmptyPersistedRecordBlocks()
  {
    var (life, _, path) = Workspace();
    var empty = life.AddList("Empty", []);
    Assert.Equal(ConnectedSaveStatus.NothingToSave, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    var id = life.AddOutfit("Outfit", [new(ArmorKey)]);
    Assert.Equal(ConnectedSaveStatus.Saved, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    Assert.Null(life.GetDraft(empty).OutputIdentity);
    var bytes = File.ReadAllBytes(path);
    life.SetEntries(id, []);
    Assert.True(life.HasUnsavedChanges);
    Assert.Equal(ConnectedSaveStatus.WriteFailed, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    Assert.Equal(bytes, File.ReadAllBytes(path));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task WorkspaceRemovalNeverDeletesOutput(bool list)
  {
    var (life, _, path) = Workspace();
    var id = list ? life.AddList("Target", [new(new(ArmorKey))]) : life.AddOutfit("Target", [new(ArmorKey)]);
    await life.SaveAsync(SaveIntent.Explicit);
    var bytes = File.ReadAllBytes(path);
    life.RemoveDraft(id);
    Assert.False(life.HasPendingDeletions);
    Assert.Equal(ConnectedSaveStatus.NothingToSave, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    Assert.Equal(bytes, File.ReadAllBytes(path));
  }

  [Fact]
  public async Task UnsavedIncomingReferenceBlocksRemovalButSavedRemovalRetainsKey()
  {
    var (life, _, _) = Workspace();
    var child = life.AddList("Child", [new(new(ArmorKey))]);
    var parent = life.AddList("Parent", [new(new(DraftId: child))]);
    Assert.Contains("Parent", Assert.Throws<InvalidOperationException>(() => life.RemoveDraft(child)).Message);
    await life.SaveAsync(SaveIntent.Explicit);
    var key = life.GetDraft(child).OutputIdentity!.FormKey;
    life.RemoveDraft(child);
    Assert.Equal(key, life.GetDraft(parent).Entries[0].Reference.FormKey);
    Assert.False(life.HasUnsavedChanges);
  }

  [Fact]
  public async Task QueuedDeletionDefersAutosaveAndExplicitReferenceRemovalAllowsCommit()
  {
    var (life, _, path) = Workspace();
    var child = life.AddList("Child", [new(new(ArmorKey))]);
    var parent = life.AddList("Parent", [new(new(DraftId: child)), new(new(ArmorKey))]);
    await life.SaveAsync(SaveIntent.Explicit);
    life.QueueOutputRecordDeletion(child);
    var bytes = File.ReadAllBytes(path);
    Assert.Equal(ConnectedSaveStatus.AutosaveDeferred, (await life.SaveAsync(SaveIntent.Autosave)).Status);
    var blocked = await life.SaveAsync(SaveIntent.Explicit);
    Assert.Contains("Parent", blocked.Message);
    Assert.True(life.HasPendingDeletions);
    Assert.Equal(bytes, File.ReadAllBytes(path));
    life.SetEntries(parent, [life.GetDraft(parent).Entries[1]]);
    var saved = await life.SaveAsync(SaveIntent.Explicit);
    Assert.True(saved.Status == ConnectedSaveStatus.Saved, saved.Message);
    Assert.False(life.HasPendingDeletions);
    Assert.Single(SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE).LeveledItems);
    life.Rename(parent, "Again");
    Assert.Equal(ConnectedSaveStatus.Saved, (await life.SaveAsync(SaveIntent.Explicit)).Status);
  }

  [Fact]
  public async Task DeletingReferrerAndTargetTogetherIsValid()
  {
    var (life, _, _) = Workspace();
    var child = life.AddList("Child", [new(new(ArmorKey))]);
    var outfit = life.AddOutfit("Referrer", [new(DraftId: child)]);
    await life.SaveAsync(SaveIntent.Explicit);
    life.QueueOutputRecordDeletion(child);
    life.QueueOutputRecordDeletion(outfit);
    var result = await life.SaveAsync(SaveIntent.Explicit);
    Assert.True(result.Status == ConnectedSaveStatus.Saved, result.Message);
    Assert.Empty(life.Drafts);
  }

  [Fact]
  public async Task ClosedRawOutputAndLoadedUnresolvedReferencesBlockDeletion()
  {
    var (life, env, _) = Workspace();
    var target = life.AddOutfit("Target", [new(ArmorKey)]);
    var child = life.AddList("Child", [new(new(ArmorKey))]);
    var parent = life.AddList("Closed", [new(new(DraftId: child))]);
    await life.SaveAsync(SaveIntent.Explicit);
    life.RemoveDraft(parent);
    life.QueueOutputRecordDeletion(child);
    Assert.Contains("Closed", (await life.SaveAsync(SaveIntent.Explicit)).Message);
    var plugin = new SkyrimMod(ModKey.FromFileName("Loaded.esp"), SkyrimRelease.SkyrimSE);
    var npc = plugin.Npcs.AddNew();
    npc.EditorID = "UnresolvedNpc";
    npc.DefaultOutfit.SetTo(life.GetDraft(target).OutputIdentity!.FormKey);
    env.LoadedPlugins = [plugin];
    life.QueueOutputRecordDeletion(target);
    Assert.Contains("UnresolvedNpc", (await life.SaveAsync(SaveIntent.Explicit)).Message);
  }

  [Fact]
  public async Task CommitSurvivesRefreshFailureAndRetryNeverWrites()
  {
    var (life, env, path) = Workspace();
    var id = life.AddList("List", [new(new(ArmorKey))]);
    env.Refresh = () => throw new IOException("Refresh exploded");
    Assert.Equal(ConnectedSaveStatus.SavedButRefreshFailed, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    Assert.NotNull(life.GetDraft(id).OutputIdentity);
    Assert.False(life.GetDraft(id).IsDirty);
    Assert.Throws<InvalidOperationException>(() => life.Rename(id, "Blocked"));
    Assert.Equal(ConnectedSaveStatus.Blocked, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    var bytes = File.ReadAllBytes(path);
    var timestamp = File.GetLastWriteTimeUtc(path);
    env.Refresh = () => Task.CompletedTask;
    Assert.Equal(ConnectedSaveStatus.Saved, (await life.RetryRefreshAsync()).Status);
    Assert.Equal(bytes, File.ReadAllBytes(path));
    Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
    Assert.True(life.CanMutate);
  }

  [Fact]
  public async Task SuspendedRefreshFreezesEveryLifecycleMutationAndOverlappingSave()
  {
    var (life, env, _) = Workspace();
    var id = life.AddList("List", [new(new(ArmorKey))]);
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    env.Refresh = async () => { entered.SetResult(); await resume.Task; };
    var saving = life.SaveAsync(SaveIntent.Explicit);
    await entered.Task;
    Assert.Throws<InvalidOperationException>(() => life.Rename(id, "No"));
    Assert.Throws<InvalidOperationException>(() => life.SetUseAll(id, false));
    Assert.Throws<InvalidOperationException>(() => life.SetEntries(id, []));
    Assert.Throws<InvalidOperationException>(() => life.RemoveDraft(id));
    Assert.Throws<InvalidOperationException>(() => life.QueueOutputRecordDeletion(id));
    Assert.Throws<InvalidOperationException>(() => life.AddOutfit("No", []));
    Assert.Throws<InvalidOperationException>(() => life.Import(DraftRecordKind.Outfit, ArmorKey));
    Assert.Throws<InvalidOperationException>(() => life.SetOutputPath("Elsewhere.esp"));
    Assert.Equal(ConnectedSaveStatus.Blocked, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    resume.SetResult();
    Assert.Equal(ConnectedSaveStatus.Saved, (await saving).Status);
  }

  [Fact]
  public async Task LockedDestinationRetainsOriginalBytesAndDirtyState()
  {
    var (life, _, path) = Workspace();
    var id = life.AddList("List", [new(new(ArmorKey))]);
    await life.SaveAsync(SaveIntent.Explicit);
    var bytes = File.ReadAllBytes(path);
    life.Rename(id, "Changed");
    using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
      Assert.Equal(ConnectedSaveStatus.WriteFailed, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    Assert.Equal(bytes, File.ReadAllBytes(path));
    Assert.True(life.GetDraft(id).IsDirty);
  }

  [Fact]
  public async Task ExternalEditIsNotAcceptedByRefreshRecovery()
  {
    var (life, env, path) = Workspace();
    life.AddList("List", [new(new(ArmorKey))]);
    env.Refresh = () => throw new IOException("No refresh");
    await life.SaveAsync(SaveIntent.Explicit);
    var output = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
    output.LeveledItems.First().EditorID = "External";
    output.WriteToBinary(path);
    env.Refresh = () => Task.CompletedTask;
    Assert.Equal(ConnectedSaveStatus.SavedButRefreshFailed, (await life.RetryRefreshAsync()).Status);
    Assert.False(life.CanMutate);
  }

  [Fact]
  public async Task RawListSlotsDuplicatesFlagsAndNonUiFieldsSurviveEditsAndRebasing()
  {
    var path = Path.Combine(_directory, "Raw.esp");
    var raw = new SkyrimMod(ModKey.FromFileName("Raw.esp"), SkyrimRelease.SkyrimSE);
    var list = raw.LeveledItems.AddNew();
    list.EditorID = "Raw";
    list.ChanceNone = new Noggog.Percent(0.37);
    list.Flags = (LeveledItem.Flag)1 | LeveledItem.Flag.UseAll;
    list.Entries = [
      new() { Data = new() { Reference = ArmorKey.ToLink<IItemGetter>(), Count = 2, Level = 3 } },
      new() { Data = new() { Reference = ArmorKey.ToLink<IItemGetter>(), Count = 4, Level = 5 } },
      new() { Data = new() { Reference = FormKey.Null.ToLink<IItemGetter>(), Count = 6, Level = 7 } }];
    raw.WriteToBinary(path);
    var life = new ConnectedDraftLifecycle(new PatchingService(null!, _logging, new SyntheticEnvironment()));
    life.SetOutputPath(path);
    var id = life.Import(DraftRecordKind.LeveledList, list.FormKey);
    Assert.False(life.HasUnsavedChanges);
    life.Rename(id, "Renamed");
    life.SetUseAll(id, false);
    Assert.Equal(ConnectedSaveStatus.Saved, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    var state = life.GetDraft(id);
    life.SetEntries(id, [state.Entries[1], state.Entries[2]]);
    var result = await life.SaveAsync(SaveIntent.Explicit);
    Assert.True(result.Status == ConnectedSaveStatus.Saved, result.Message);
    state = life.GetDraft(id);
    life.SetEntries(id, [state.Entries[0] with { Count = 8 }, state.Entries[1]]);
    Assert.Equal(ConnectedSaveStatus.Saved, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    var saved = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE).LeveledItems.Single();
    Assert.Equal(new Noggog.Percent(0.37), saved.ChanceNone);
    Assert.Equal((LeveledItem.Flag)1, saved.Flags);
    Assert.Equal(2, saved.Entries!.Count);
    Assert.Equal(8, saved.Entries[0].Data!.Count);
    Assert.Equal(5, saved.Entries[0].Data!.Level);
    Assert.Equal(FormKey.Null, saved.Entries[1].Data!.Reference.FormKey);
    Assert.Equal(6, saved.Entries[1].Data!.Count);
  }

  [Fact]
  public async Task BoundConnectedWorkspaceRemainsCleanAndReconcilesDeletedRows()
  {
    var (life, _, _) = Workspace();
    using var lists = new LeveledListDraftManager(_logging, life);
    using var outfits = new OutfitDraftManager(_logging, life);
    var armor = new ArmorRecordViewModel(new Armor(ArmorKey, SkyrimRelease.SkyrimSE), null);
    var child = lists.CreateDraft([armor])!;
    var parent = lists.CreateDraft([armor])!;
    lists.AddNestedList(parent, child);
    var outfit = (await outfits.CreateDraftAsync([armor], "Outfit"))!;
    outfits.AddLeveledListToDraft(outfit, new LeveledListReference(parent));
    var result = await life.SaveAsync(SaveIntent.Explicit);
    Assert.True(result.Status == ConnectedSaveStatus.Saved, result.Message);
    Assert.False(life.HasUnsavedChanges);
    child.EditorId = "Renamed";
    Assert.True(life.HasUnsavedChanges);
    Assert.Equal(ConnectedSaveStatus.Saved, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    Assert.False(life.HasUnsavedChanges);
    life.QueueOutputRecordDeletion(outfit.Id);
    Assert.Equal(ConnectedSaveStatus.Saved, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    Assert.Empty(outfits.Drafts);
    Assert.False(life.HasUnsavedChanges);
    Assert.Equal(ConnectedSaveStatus.NothingToSave, (await life.SaveAsync(SaveIntent.Explicit)).Status);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task DuplicatePreservesConnectedReferencesAndSchedulesAutosave(bool listOnly)
  {
    var (life, _, path) = Workspace();
    using var outfits = new OutfitDraftManager(_logging, life);
    using var lists = new LeveledListDraftManager(_logging, life);
    var armor = new ArmorRecordViewModel(new Armor(ArmorKey, SkyrimRelease.SkyrimSE), null);
    var child = lists.CreateDraft([armor]);
    Assert.NotNull(child);
    var original = (await outfits.CreateDraftAsync([armor], "Original"))!;
    outfits.AddLeveledListToDraft(original, new LeveledListReference(child));
    if (listOnly) original.RemovePiece(armor);
    outfits.RequestNameAsync = _ => Task.FromResult<string?>("Duplicate");
    var notifications = 0;
    outfits.DraftModified += () => notifications++;
    var copy = await outfits.DuplicateDraftAsync(original);
    Assert.NotNull(copy);
    Assert.Single(copy.GetLeveledListReferences());
    Assert.Equal(listOnly ? 0 : 1, copy.GetPieces().Count);
    Assert.Equal(1, notifications);
    var result = await life.SaveAsync(SaveIntent.Autosave);
    Assert.True(result.Status == ConnectedSaveStatus.Saved, result.Message);
    var output = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE);
    Assert.Contains(output.Outfits.Single(o => o.EditorID == "Duplicate").Items!,
      item => item.FormKey == child.FormKey);
  }

  [Fact]
  public async Task CreatedOutfitNotifiesOnlyAfterFullyAddedToWorkspace()
  {
    var (life, _, _) = Workspace();
    using var outfits = new OutfitDraftManager(_logging, life);
    var notifications = 0;
    outfits.DraftModified += () =>
    {
      Assert.Single(outfits.Drafts);
      Assert.Single(life.Drafts);
      Assert.True(life.HasUnsavedChanges);
      notifications++;
    };
    var armor = new ArmorRecordViewModel(new Armor(ArmorKey, SkyrimRelease.SkyrimSE), null);
    await outfits.CreateDraftAsync([armor], "NewOutfit");
    Assert.Equal(1, notifications);
  }

  [Fact]
  public async Task DuplicateRejectsOpaqueImportedProjectionWithoutCreatingPartialDraft()
  {
    var (life, env, _) = Workspace();
    var source = new SkyrimMod(ModKey.FromFileName("Source.esp"), SkyrimRelease.SkyrimSE);
    var armor = source.Armors.AddNew("Visible");
    var record = source.Outfits.AddNew("Opaque");
    record.Items = [armor.FormKey.ToLink<IOutfitTargetGetter>(), new FormKey(source.ModKey, 0xFFFFFF).ToLink<IOutfitTargetGetter>()];
    env.LoadedPlugins = [source];
    using var outfits = new OutfitDraftManager(_logging, life);
    var original = outfits.CreateOverrideDraft(record, [new ArmorRecordViewModel(armor, null)], source.ModKey)!;
    outfits.RequestNameAsync = _ => Task.FromResult<string?>("PartialCopy");
    Assert.Null(await outfits.DuplicateDraftAsync(original));
    Assert.Single(life.Drafts);
    Assert.Single(outfits.Drafts);
  }

  [Fact]
  public async Task OpaqueUiProjectionRefusesContentEditsButAllowsSafeRename()
  {
    var (life, env, path) = Workspace();
    var source = new SkyrimMod(ModKey.FromFileName("Source.esp"), SkyrimRelease.SkyrimSE);
    var list = source.LeveledItems.AddNew();
    list.EditorID = "Opaque";
    list.ChanceNone = new Noggog.Percent(0.42);
    list.Entries = [new() { Data = new() { Reference = ArmorKey.ToLink<IItemGetter>(), Count = 5, Level = 2 } }];
    env.LoadedPlugins = [source];
    var vm = new LeveledListDraftViewModel("Opaque", _ => { }) { FormKey = list.FormKey };
    life.Bind(vm);
    Assert.True(life.HasUnsavedChanges);
    Assert.Throws<InvalidOperationException>(() => vm.AddArmorEntry(new ArmorRecordViewModel(new Armor(ArmorKey, SkyrimRelease.SkyrimSE), null)));
    vm.EditorId = "Renamed";
    var result = await life.SaveAsync(SaveIntent.Explicit);
    Assert.True(result.Status == ConnectedSaveStatus.Saved, result.Message);
    var output = SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE).LeveledItems.Single();
    Assert.Equal(new Noggog.Percent(0.42), output.ChanceNone);
    Assert.Equal(5, output.Entries![0].Data!.Count);
  }

  [Fact]
  public async Task InvalidDraftReferenceNeverSilentlySkipsOrFallsBack()
  {
    var (life, _, path) = Workspace();
    life.AddList("Invalid", [new(new(ArmorKey, Guid.NewGuid()))]);
    var result = await life.SaveAsync(SaveIntent.Explicit);
    Assert.Equal(ConnectedSaveStatus.WriteFailed, result.Status);
    Assert.Contains("Unresolved draft reference", result.Message);
    Assert.False(File.Exists(path));
  }

  [Fact]
  public async Task IncompleteLoadedScanBlocksBeforeWriting()
  {
    var (life, env, path) = Workspace();
    life.AddList("List", [new(new(ArmorKey))]);
    env.ScanUnavailable = true;
    var result = await life.SaveAsync(SaveIntent.Explicit);
    Assert.Equal(ConnectedSaveStatus.WriteFailed, result.Status);
    Assert.False(File.Exists(path));
  }

  [Fact]
  public async Task FailedFirstWriteCanRecoverEnvironmentWithoutRequiringAnUncommittedOutput()
  {
    var (life, env, path) = Workspace();
    var id = life.AddList("List", [new(new(ArmorKey))]);
    env.ReleaseAction = () => throw new IOException("Handle release failed");
    Assert.Equal(ConnectedSaveStatus.WriteFailed, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    Assert.False(File.Exists(path));
    Assert.True(life.NeedsRefresh);
    env.ReleaseAction = () => { };
    Assert.Equal(ConnectedSaveStatus.Saved, (await life.RetryRefreshAsync()).Status);
    Assert.False(File.Exists(path));
    Assert.True(life.CanMutate);
    Assert.True(life.GetDraft(id).IsDirty);
    Assert.Equal(ConnectedSaveStatus.Saved, (await life.SaveAsync(SaveIntent.Explicit)).Status);
  }

  [Fact]
  public async Task LosingLoadedRawOverrideStillBlocksDeletion()
  {
    var (life, env, _) = Workspace();
    var id = life.AddOutfit("Target", [new(ArmorKey)]);
    await life.SaveAsync(SaveIntent.Explicit);
    var losing = new SkyrimMod(ModKey.FromFileName("Losing.esp"), SkyrimRelease.SkyrimSE);
    var npc = losing.Npcs.AddNew();
    npc.EditorID = "LosingDependent";
    npc.DefaultOutfit.SetTo(life.GetDraft(id).OutputIdentity!.FormKey);
    var winning = new SkyrimMod(ModKey.FromFileName("Winning.esp"), SkyrimRelease.SkyrimSE);
    winning.Npcs.GetOrAddAsOverride(npc).DefaultOutfit.SetTo(FormKey.Null);
    env.LoadedPlugins = [winning, losing];
    life.QueueOutputRecordDeletion(id);
    Assert.Contains("LosingDependent", (await life.SaveAsync(SaveIntent.Explicit)).Message);
    Assert.True(life.HasPendingDeletions);
  }

  [Fact]
  public async Task ActualInactiveOutputIsAuthoritativeOverStaleLoadedOutput()
  {
    var (life, env, path) = Workspace();
    var child = life.AddList("Child", [new(new(ArmorKey))]);
    var parent = life.AddList("Parent", [new(new(DraftId: child)), new(new(ArmorKey))]);
    await life.SaveAsync(SaveIntent.Explicit);
    // Stale loaded output still has the link, but the candidate explicitly removes it.
    env.LoadedPlugins = [SkyrimMod.CreateFromBinary(path, SkyrimRelease.SkyrimSE)];
    life.QueueOutputRecordDeletion(child);
    life.SetEntries(parent, [life.GetDraft(parent).Entries[1]]);
    Assert.Equal(ConnectedSaveStatus.Saved, (await life.SaveAsync(SaveIntent.Explicit)).Status);
    var reopened = new ConnectedDraftLifecycle(new PatchingService(null!, _logging, env));
    reopened.SetOutputPath(path);
    var imported = reopened.Import(DraftRecordKind.LeveledList, life.GetDraft(parent).OutputIdentity!.FormKey);
    Assert.Single(reopened.GetDraft(imported).Entries);
    Assert.False(reopened.HasUnsavedChanges);
  }

  public void Dispose()
  {
    _logging.Dispose();
    Directory.Delete(_directory, true);
  }

  private sealed class SyntheticEnvironment : IConnectedDraftEnvironment
  {
    public SkyrimRelease Release => SkyrimRelease.SkyrimSE;
    private IReadOnlyList<ISkyrimModGetter> _loadedPlugins = [];
    public bool ScanUnavailable { get; set; }
    public IReadOnlyList<ISkyrimModGetter> LoadedPlugins { get => ScanUnavailable ? throw new IOException("Loaded scan unavailable") : _loadedPlugins; set => _loadedPlugins = value; }
    public Action ReleaseAction { get; set; } = () => { };
    public void ReleaseHandles() => ReleaseAction();
    public Func<Task> Refresh { get; set; } = () => Task.CompletedTask;
    public Task RefreshAsync(string outputPath) => Refresh();
  }
}
