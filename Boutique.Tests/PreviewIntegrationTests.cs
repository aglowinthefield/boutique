using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Boutique.Services;
using Boutique.ViewModels;
using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace Boutique.Tests;

public class PreviewIntegrationTests
{
    [Fact]
    public async Task OutfitDraftManager_PreviewDelegate_IsInvokedWhenPreviewCommandExecuted()
    {
        var loggingService = new TestLoggingService();
        var manager = new OutfitDraftManager(loggingService);

        var previewInvoked = false;
        OutfitDraftViewModel? previewedDraft = null;

        manager.PreviewDraftAsync = draft =>
        {
            previewInvoked = true;
            previewedDraft = draft;
            return Task.CompletedTask;
        };

        var mockArmor = CreateMockArmorViewModel();
        var draft = await manager.CreateDraftAsync([mockArmor], "TestOutfit");

        draft.Should().NotBeNull();
        await draft!.PreviewCommand.Execute().ToTask();

        previewInvoked.Should().BeTrue("the preview delegate should be invoked when PreviewCommand executes");
        previewedDraft.Should().BeSameAs(draft, "the draft passed to preview should be the same instance");
    }

    [Fact]
    public async Task OutfitDraftManager_WithoutPreviewDelegate_DoesNotThrow()
    {
        var loggingService = new TestLoggingService();
        var manager = new OutfitDraftManager(loggingService);

        var mockArmor = CreateMockArmorViewModel();
        var draft = await manager.CreateDraftAsync([mockArmor], "TestOutfit");

        draft.Should().NotBeNull();

        var act = async () => await draft!.PreviewCommand.Execute().ToTask();
        await act.Should().NotThrowAsync("preview should gracefully handle missing delegate");
    }

    [Fact]
    public async Task OutfitDraftManager_PreviewCommand_IsDisabledWhenNoPieces()
    {
        var loggingService = new TestLoggingService();
        var manager = new OutfitDraftManager(loggingService);

        var previewInvoked = false;
        manager.PreviewDraftAsync = _ =>
        {
            previewInvoked = true;
            return Task.CompletedTask;
        };

        var mockArmor = CreateMockArmorViewModel();
        var draft = await manager.CreateDraftAsync([mockArmor], "TestOutfit");
        draft.Should().NotBeNull();

        draft!.RemovePiece(mockArmor);
        draft.HasPieces.Should().BeFalse();

        var canExecute = await draft.PreviewCommand.CanExecute.FirstAsync();
        canExecute.Should().BeFalse("preview should be disabled when draft has no pieces");
        previewInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task OutfitDraftManager_DuplicateCommand_CreatesNewDraft()
    {
        var loggingService = new TestLoggingService();
        var manager = new OutfitDraftManager(loggingService);
        manager.RequestNameAsync = tuple => Task.FromResult<string?>("DuplicatedOutfit");

        var mockArmor = CreateMockArmorViewModel();
        var original = await manager.CreateDraftAsync([mockArmor], "OriginalOutfit");
        original.Should().NotBeNull();

        manager.Drafts.Should().HaveCount(1);

        await original!.DuplicateCommand.Execute().ToTask();

        manager.Drafts.Should().HaveCount(2, "duplicate should create a new draft");
        manager.Drafts.Should().Contain(d => d.EditorId != original.EditorId, "duplicate should have a different name");
    }

    [Fact]
    public async Task OutfitDraftManager_RemoveSelfCommand_RemovesDraft()
    {
        var loggingService = new TestLoggingService();
        var manager = new OutfitDraftManager(loggingService);

        var mockArmor = CreateMockArmorViewModel();
        var draft = await manager.CreateDraftAsync([mockArmor], "TestOutfit");
        draft.Should().NotBeNull();

        manager.Drafts.Should().HaveCount(1);

        draft!.RemoveSelfCommand.Execute().Subscribe();

        manager.Drafts.Should().BeEmpty("draft should be removed after RemoveSelfCommand");
    }

    private static ArmorRecordViewModel CreateMockArmorViewModel()
    {
        var modKey = ModKey.FromNameAndExtension("Test.esp");
        var formKey = new FormKey(modKey, 0x800);
        var armor = new Armor(formKey, SkyrimRelease.SkyrimSE)
        {
            EditorID = "TestArmor",
            Name = "Test Armor"
        };

        return new ArmorRecordViewModel(armor, linkCache: null);
    }

    private sealed class TestLoggingService : ILoggingService
    {
        public Serilog.ILogger Logger { get; } = Serilog.Log.Logger;
        public string LogDirectory { get; } = Path.GetTempPath();
        public string LogFilePattern { get; } = "test-*.log";
        public bool IsDebugEnabled { get; set; }

        public Serilog.ILogger ForContext<T>() => Logger;
        public void Flush() { }
        public void Dispose() { }
    }
}
