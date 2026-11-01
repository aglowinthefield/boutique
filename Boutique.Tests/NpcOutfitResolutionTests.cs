using System.IO;
using Boutique.Models;
using Boutique.Services;
using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Serilog;
using Xunit;

namespace Boutique.Tests;

public sealed class NpcOutfitResolutionTests : IDisposable
{
  private readonly string _directory = Path.Combine(Path.GetTempPath(), "BoutiqueResolutionTests", Guid.NewGuid().ToString("N"));
  private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();

  public NpcOutfitResolutionTests() => Directory.CreateDirectory(_directory);

  [Fact]
  public async Task KeywordOnlyFile_EnablesOutfitInAnotherDiscoveredFile()
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Resolution.esp"), SkyrimRelease.SkyrimSE);
    var outfit = mod.Outfits.AddNew();
    outfit.EditorID = "GuardOutfit";
    using var cache = mod.ToImmutableLinkCache();
    var guard = Npc(new FormKey(mod.ModKey, 0x900), "Whiterun Guard");
    var civilian = Npc(new FormKey(mod.ModKey, 0x901), "Merchant");
    await File.WriteAllTextAsync(Path.Combine(_directory, "Keywords_DISTR.ini"),
      "Keyword = BoutiqueGuard|*Guard|NONE|NONE|NONE|NONE|100");
    await File.WriteAllTextAsync(Path.Combine(_directory, "Outfits_DISTR.ini"),
      "Outfit = GuardOutfit|BoutiqueGuard|NONE|NONE|NONE|NONE|100");
    var files = await new DistributionScannerService(_logger).DiscoverAsync(_directory);
    files.Should().ContainSingle(file => file.KeywordDistributionCount == 1 && file.OutfitDistributionCount == 0);

    var resolution = new NpcOutfitResolutionService(new KeywordDistributionResolver(_logger), _logger);
    var result = await resolution.ResolveNpcOutfitsWithFiltersAsync(files, [guard, civilian], cache);

    result.SimulatedKeywordsByNpc[guard.FormKey].Should().Contain("BoutiqueGuard");
    result.SimulatedKeywordsByNpc[civilian.FormKey].Should().NotContain("BoutiqueGuard");
    var assignment = result.Assignments.Should().ContainSingle().Subject;
    assignment.NpcFormKey.Should().Be(guard.FormKey);
    assignment.FinalOutfitFormKey.Should().Be(outfit.FormKey);
    assignment.Distributions.Should().ContainSingle().Which.FileName.Should().Be("Outfits_DISTR.ini");
  }

  [Fact]
  public async Task KeywordDependencies_AreResolvedBeforeOutfitsEvenWhenProviderSortsLast()
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Resolution.esp"), SkyrimRelease.SkyrimSE);
    var outfit = mod.Outfits.AddNew();
    outfit.EditorID = "GuardOutfit";
    using var cache = mod.ToImmutableLinkCache();
    var guard = Npc(new FormKey(mod.ModKey, 0x900), "Whiterun Guard");
    await File.WriteAllTextAsync(Path.Combine(_directory, "A_Outfits_DISTR.ini"),
      "Outfit = GuardOutfit|ReadyGuard|NONE|NONE|NONE|NONE|100");
    await File.WriteAllTextAsync(Path.Combine(_directory, "M_Derived_DISTR.ini"),
      "Keyword = ReadyGuard|BaseGuard|NONE|NONE|NONE|NONE|100");
    await File.WriteAllTextAsync(Path.Combine(_directory, "Z_Seed_DISTR.ini"),
      "Keyword = BaseGuard|*Guard|NONE|NONE|NONE|NONE|100");
    var files = await new DistributionScannerService(_logger).DiscoverAsync(_directory);
    var resolution = new NpcOutfitResolutionService(new KeywordDistributionResolver(_logger), _logger);

    var result = await resolution.ResolveNpcOutfitsWithFiltersAsync(files, [guard], cache);

    result.SimulatedKeywordsByNpc[guard.FormKey].Should().Contain(["BaseGuard", "ReadyGuard"]);
    var assignment = result.Assignments.Should().ContainSingle().Subject;
    assignment.FinalOutfitFormKey.Should().Be(outfit.FormKey);
    assignment.Distributions.Should().ContainSingle().Which.ProcessingOrder.Should().Be(1);
  }

  [Fact]
  public async Task CompleteInputs_PreserveOutfitFileOrderAndEspFallbackProvenance()
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Resolution.esp"), SkyrimRelease.SkyrimSE);
    var original = mod.Outfits.AddNew("OriginalOutfit");
    var earlier = mod.Outfits.AddNew("EarlierOutfit");
    var later = mod.Outfits.AddNew("LaterOutfit");
    using var cache = mod.ToImmutableLinkCache();
    var guard = Npc(new FormKey(mod.ModKey, 0x900), "Whiterun Guard", original.FormKey);
    var civilian = Npc(new FormKey(mod.ModKey, 0x901), "Merchant", original.FormKey);
    await File.WriteAllTextAsync(Path.Combine(_directory, "A_Outfits_DISTR.ini"),
      "Outfit = EarlierOutfit|BoutiqueGuard|NONE|NONE|NONE|NONE|100");
    await File.WriteAllTextAsync(Path.Combine(_directory, "M_Outfits_DISTR.ini"),
      "Outfit = LaterOutfit|BoutiqueGuard|NONE|NONE|NONE|NONE|100");
    await File.WriteAllTextAsync(Path.Combine(_directory, "F_Keywords_DISTR.ini"),
      "Keyword = BoutiqueGuard|*Guard|NONE|NONE|NONE|NONE|100");
    var files = await new DistributionScannerService(_logger).DiscoverAsync(_directory);
    var resolution = new NpcOutfitResolutionService(new KeywordDistributionResolver(_logger), _logger);

    var result = await resolution.ResolveNpcOutfitsWithFiltersAsync(files, [guard, civilian], cache);

    var guardAssignment = result.Assignments.Single(assignment => assignment.NpcFormKey == guard.FormKey);
    guardAssignment.FinalOutfitFormKey.Should().Be(later.FormKey);
    guardAssignment.HasConflict.Should().BeTrue();
    guardAssignment.Distributions.Select(distribution => distribution.OutfitFormKey)
      .Should().Equal(original.FormKey, earlier.FormKey, later.FormKey);
    guardAssignment.Distributions.Select(distribution => distribution.ProcessingOrder).Should().Equal(0, 1, 2);
    guardAssignment.Distributions.Should().ContainSingle(distribution => distribution.IsWinner)
      .Which.OutfitFormKey.Should().Be(later.FormKey);
    var fallback = result.Assignments.Single(assignment => assignment.NpcFormKey == civilian.FormKey);
    fallback.FinalOutfitFormKey.Should().Be(original.FormKey);
    fallback.Distributions.Should().ContainSingle().Which.FileType.Should().Be(DistributionFileType.Esp);
  }

  [Theory]
  [InlineData(100, true)]
  [InlineData(0, false)]
  public async Task KeywordOnlyInputs_AreSimulatedWithoutInventingOutfitAssignments(int chance, bool applied)
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Resolution.esp"), SkyrimRelease.SkyrimSE);
    using var cache = mod.ToImmutableLinkCache();
    var guard = Npc(new FormKey(mod.ModKey, 0x900), "Whiterun Guard");
    await File.WriteAllTextAsync(Path.Combine(_directory, "Keywords_DISTR.ini"),
      $"Keyword = BoutiqueGuard|*Guard|NONE|NONE|NONE|NONE|{chance}");
    var files = await new DistributionScannerService(_logger).DiscoverAsync(_directory);
    var resolution = new NpcOutfitResolutionService(new KeywordDistributionResolver(_logger), _logger);

    var result = await resolution.ResolveNpcOutfitsWithFiltersAsync(files, [guard], cache);

    result.Assignments.Should().BeEmpty();
    result.SimulatedKeywordsByNpc[guard.FormKey].Contains("BoutiqueGuard").Should().Be(applied);
  }

  private static NpcFilterData Npc(FormKey key, string name, FormKey? defaultOutfit = null) => new()
  {
    FormKey = key,
    EditorId = name.Replace(" ", string.Empty),
    Name = name,
    SourceMod = key.ModKey,
    Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
    Factions = [],
    RaceFormKey = null,
    RaceEditorId = null,
    ClassFormKey = null,
    ClassEditorId = null,
    CombatStyleFormKey = null,
    CombatStyleEditorId = null,
    VoiceTypeFormKey = null,
    VoiceTypeEditorId = null,
    DefaultOutfitFormKey = defaultOutfit,
    DefaultOutfitEditorId = null,
    WornArmorFormKey = null,
    IsFemale = false,
    IsUnique = false,
    IsSummonable = false,
    IsChild = false,
    IsLeveled = false,
    Level = 1,
    Locations = new HashSet<FormKey>(),
    TemplateFormKey = null,
    TemplateEditorId = null,
    MatchKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name, name.Replace(" ", string.Empty) }
  };

  public void Dispose()
  {
    Directory.Delete(_directory, recursive: true);
    (_logger as IDisposable)?.Dispose();
  }
}
