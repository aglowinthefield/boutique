using Boutique.Models;
using Boutique.Services;
using Boutique.ViewModels;
using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace Boutique.Tests;

public class DistributionConflictMatchingTests
{
  [Fact]
  public void FactionOnlyEntry_UsesSamePopulationAsPreviewAndIntraFile()
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Matching.esp"), SkyrimRelease.SkyrimSE);
    var faction = mod.Factions.AddNew("Guards");
    var first = mod.Outfits.AddNew("First");
    var second = mod.Outfits.AddNew("Second");
    var guard = mod.Npcs.AddNew("Guard");
    var civilian = mod.Npcs.AddNew("Civilian");
    using var cache = mod.ToImmutableLinkCache();
    var npcs = new[] { Npc(guard.FormKey, "Guard", faction.FormKey), Npc(civilian.FormKey, "Civilian") };
    var entry = new DistributionEntry { Outfit = first, FactionFilters = [new(faction.FormKey)] };
    var vm = new DistributionEntryViewModel(entry);
    var other = new DistributionEntryViewModel(new DistributionEntry { Outfit = second, FactionFilters = [new(faction.FormKey)] });

    var result = DistributionConflictDetectionService.DetectConflicts([vm],
      [File("Z_DISTR.ini", "Outfit = Second|NONE|Guards|NONE|NONE|NONE|100")], "A_DISTR.ini", cache, npcs);

    result.Conflicts.Should().ContainSingle().Which.NpcFormKey.Should().Be(guard.FormKey);
    FilterMatchingService.GetMatchingNpcsForEntry(npcs, entry).Should().ContainSingle().Which.FormKey.Should().Be(guard.FormKey);
    DistributionConflictDetectionService.DetectIntraFile([vm, other], npcs).TotalOverlappingNpcCount.Should().Be(1);
  }

  [Theory]
  [InlineData("raw form")]
  [InlineData("perk")]
  [InlineData("form list")]
  public void PartiallyEvaluatedEntry_DoesNotBecomeAConfirmedConflict(string unsupported)
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Matching.esp"), SkyrimRelease.SkyrimSE);
    var first = mod.Outfits.AddNew("First");
    var second = mod.Outfits.AddNew("Second");
    var guard = mod.Npcs.AddNew("Guard");
    using var cache = mod.ToImmutableLinkCache();
    var npcs = new[] { Npc(guard.FormKey, "Guard") };
    var partial = new DistributionEntry { Outfit = first, NpcFilters = [new(guard.FormKey)] };
    if (unsupported == "raw form") partial.RawFormFilters = "-Guard";
    if (unsupported == "perk") partial.PerkFormKeys = [new(mod.ModKey, 0x1234)];
    if (unsupported == "form list") partial.FormListFormKeys = [new(mod.ModKey, 0x1234)];
    var other = new DistributionEntry { Outfit = second, NpcFilters = [new(guard.FormKey)] };

    // Preview can explain a candidate, but unevaluated restrictions cannot prove an overlap.
    FilterMatchingService.GetMatchingNpcsForEntry(npcs, partial)
      .Should().ContainSingle().Which.Criteria.Should().Contain("Unresolved:");
    DistributionConflictDetectionService.DetectIntraFile([new(partial), new(other)], npcs)
      .TotalOverlappingNpcCount.Should().Be(0);
    var result = DistributionConflictDetectionService.DetectConflicts([new(partial)],
      [File("Z.ini", "Outfit = Second|NONE|NONE|NONE|NONE|NONE|100")], "A.ini", cache, npcs);
    result.Conflicts.Should().BeEmpty();
    result.Overlaps.Should().BeEmpty();
  }

  [Fact]
  public async Task AwaitedAnalysisOwnsTheInputsCapturedBeforeItsFirstYield()
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Matching.esp"), SkyrimRelease.SkyrimSE);
    var guard = mod.Npcs.AddNew("Guard");
    using var cache = mod.ToImmutableLinkCache();
    var npc = Npc(guard.FormKey, "Guard");
    List<NpcFilterData> npcs = [npc];
    var entry = new DistributionEntryViewModel(new DistributionEntry { RawStringFilters = "Ready" });
    List<DistributionEntryViewModel> entries = [entry];
    List<DistributionFileViewModel> files = [File("Z.ini", "Outfit = Any|Guard|NONE|NONE|NONE|NONE|100")];
    var keywords = new Dictionary<FormKey, HashSet<string>> { [guard.FormKey] = new() { "Ready" } };
    var context = new HeldContext();
    var previousContext = SynchronizationContext.Current;
    Task<ConflictDetectionResult> pending;
    try
    {
      SynchronizationContext.SetSynchronizationContext(context);
      pending = DistributionConflictDetectionService.DetectConflictsAsync(entries, files, "A.ini", cache, npcs, keywords);
    }
    finally { SynchronizationContext.SetSynchronizationContext(previousContext); }

    pending.IsCompleted.Should().BeFalse();
    entry.RawStringFilters = "Other";
    entries.Clear();
    files.Clear();
    npcs.Clear();
    keywords[guard.FormKey].Clear();
    npc.MatchKeys!.Clear();
    context.Continue();

    var result = await pending;
    result.Conflicts.Should().ContainSingle().Which.NpcFormKey.Should().Be(guard.FormKey);
    result.HasConflicts.Should().BeTrue();
    result.SuggestedFileName.Should().Be("ZZA.ini");
  }

  private sealed class HeldContext : SynchronizationContext
  {
    private Action? _continuation;
    public override void Post(SendOrPostCallback callback, object? state) => _continuation = () => callback(state);
    public void Continue() => _continuation!();
  }

  [Fact]
  public void SkyPatcherFilters_RespectFactionKeywordAndNpcExclusions()
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Matching.esp"), SkyrimRelease.SkyrimSE);
    var faction = mod.Factions.AddNew("Guards");
    mod.Outfits.AddNew("Uniform");
    var guard = mod.Npcs.AddNew("Guard");
    var excluded = mod.Npcs.AddNew("Excluded");
    using var cache = mod.ToImmutableLinkCache();
    var npcs = new[] { Npc(guard.FormKey, "Guard", faction.FormKey), Npc(excluded.FormKey, "Excluded", faction.FormKey) };
    var keywords = npcs.ToDictionary(n => n.FormKey, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "VirtualGuard" });
    var entry = new DistributionEntry { KeywordFilters = [new("VirtualGuard")] };
    var result = DistributionConflictDetectionService.DetectConflicts([new(entry)],
      [File("Z.ini", "filterByFactions=Guards:filterByKeywords=VirtualGuard:filterByNpcsExcluded=Excluded:filterByGender=male:restrictToFlags=unique:outfitDefault=Uniform", DistributionFileType.SkyPatcher)],
      "A.ini", cache, npcs, keywords);
    result.Conflicts.Should().ContainSingle().Which.NpcFormKey.Should().Be(guard.FormKey);
  }

  [Theory]
  [InlineData("filterByFactionsExcluded=Guards", false)]
  [InlineData("filterByFactions=Guards:filterByRaces=MissingRace", false)]
  [InlineData("filterByGender=unknown", false)]
  [InlineData("restrictToFlags=unknown", false)]
  [InlineData("filterByUnsupported=anything", false)]
  [InlineData("filterByFactionsOr=MissingFaction,Guards", false)]
  [InlineData("filterByKeywords=VirtualGuard:filterByKeywordsExcluded=VirtualGuard", false)]
  [InlineData("filterByClassExclude=Warrior", false)]
  [InlineData("filterByRaces=Nord:filterByClass=Warrior:filterByDefaultOutfits=Uniform", true)]
  public void SkyPatcher_DoesNotBroadenUnresolvedOrExcludedFilters(string filters, bool matches)
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Matching.esp"), SkyrimRelease.SkyrimSE);
    var faction = mod.Factions.AddNew("Guards");
    var race = mod.Races.AddNew("Nord");
    var cls = mod.Classes.AddNew("Warrior");
    var outfit = mod.Outfits.AddNew("Uniform");
    var guard = mod.Npcs.AddNew("Guard");
    using var cache = mod.ToImmutableLinkCache();
    var npcs = new[] { Npc(guard.FormKey, "Guard", faction.FormKey, race.FormKey, cls.FormKey, outfit.FormKey) };
    var keywords = new Dictionary<FormKey, HashSet<string>> { [guard.FormKey] = new(StringComparer.OrdinalIgnoreCase) { "VirtualGuard" } };
    FilterMatchingService.GetMatchingNpcsForDistributionLine(npcs, DistributionFileType.SkyPatcher,
      filters + ":outfitDefault=Uniform", cache, keywords).Count.Should().Be(matches ? 1 : 0);
  }

  [Fact]
  public void CompoundFilters_VirtualKeywordsRaceTraitsLevelsAndExclusionsAgreeAcrossSeams()
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Matching.esp"), SkyrimRelease.SkyrimSE);
    var race = mod.Races.AddNew("Nord");
    var first = mod.Outfits.AddNew("First");
    var second = mod.Outfits.AddNew("Second");
    var guard = mod.Npcs.AddNew("Guard");
    var excluded = mod.Npcs.AddNew("Excluded");
    var outsider = mod.Npcs.AddNew("Outsider");
    using var cache = mod.ToImmutableLinkCache();
    var npcs = new[] { Npc(guard.FormKey, "Guard", race: race.FormKey), Npc(excluded.FormKey, "Excluded", race: race.FormKey), Npc(outsider.FormKey, "Outsider") };
    var keywords = npcs.ToDictionary(n => n.FormKey, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "VirtualGuard", "Ready" });
    var entry = new DistributionEntry { Outfit = first, RawStringFilters = "VirtualGuard+Ready,-Excluded", RaceFilters = [new(race.FormKey)], TraitFilters = new() { IsFemale = false, IsUnique = true }, LevelFilters = "5/15" };
    var other = new DistributionEntry { Outfit = second, KeywordFilters = [new("VirtualGuard")] };
    var preview = FilterMatchingService.GetMatchingNpcsForEntry(npcs, entry, keywords);
    preview.Should().ContainSingle().Which.FormKey.Should().Be(guard.FormKey);
    DistributionConflictDetectionService.DetectIntraFile([new(entry), new(other)], npcs, keywords).TotalOverlappingNpcCount.Should().Be(1);
    var result = DistributionConflictDetectionService.DetectConflicts([new(entry)],
      [File("Z.ini", "Outfit = Second|VirtualGuard+Ready,-Excluded|Nord|5/15|M/U|NONE|100")], "A.ini", cache, npcs, keywords);
    result.Conflicts.Should().ContainSingle().Which.NpcFormKey.Should().Be(guard.FormKey);
  }

  [Fact]
  public void EveryExistingFile_ContributesToChanceAndFilenamePolicyWithoutInflatingNpcCounts()
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Matching.esp"), SkyrimRelease.SkyrimSE);
    var guard = mod.Npcs.AddNew("Guard");
    using var cache = mod.ToImmutableLinkCache();
    var npcs = new[] { Npc(guard.FormKey, "Guard") };
    DistributionEntryViewModel[] entries = [new(new DistributionEntry { KeywordFilters = [new("Ready")] })];
    var keywords = new Dictionary<FormKey, HashSet<string>> { [guard.FormKey] = new() { "Ready" } };
    DistributionFileViewModel[] files = [File("A.ini", "Outfit = Any|NONE|NONE|NONE|NONE|NONE|100"), File("ZZ.ini", "Outfit = Any|Guard|NONE|NONE|NONE|NONE|100"), File("ZZZ.ini", "Outfit = Any|Guard|NONE|NONE|NONE|NONE|25")];
    var result = DistributionConflictDetectionService.DetectConflicts(entries, files, "M.ini", cache, npcs, keywords);
    result.HasConflicts.Should().BeTrue();
    result.HasOverlaps.Should().BeTrue();
    result.Conflicts.Select(c => c.ExistingFileName).Should().BeEquivalentTo("A.ini", "ZZ.ini");
    result.Overlaps.Should().ContainSingle().Which.ExistingChance.Should().Be(25);
    result.ConflictSummary.Should().Contain("1 NPC(s)");
    result.SuggestedFileName.Should().Be("ZZZM.ini");
    var resolved = DistributionConflictDetectionService.DetectConflicts(entries, files, "ZZZZ.ini", cache, npcs, keywords);
    resolved.ConflictsResolvedByFilename.Should().BeTrue();
    resolved.OverlapsResolvedByFilename.Should().BeTrue();
  }

  [Theory]
  [InlineData("filterByKeywords=Required:filterByKeywordsOr=Alternative,Other", false)]
  [InlineData("filterByKeywords=Required:filterByKeywordsOr=Required,Other", true)]
  [InlineData("filterByFactions=Guards:filterByFactionsExcluded=Guards", false)]
  [InlineData("filterByFactions=Guards:unknownConstraint=Yes", false)]
  public void SkyPatcher_CombinesSeparateFilterClausesWithoutFlattening(string filters, bool matches)
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Matching.esp"), SkyrimRelease.SkyrimSE);
    var faction = mod.Factions.AddNew("Guards");
    mod.Outfits.AddNew("Uniform");
    var guard = mod.Npcs.AddNew("Guard");
    using var cache = mod.ToImmutableLinkCache();
    var keywords = new Dictionary<FormKey, HashSet<string>> { [guard.FormKey] = new() { "Required" } };
    FilterMatchingService.GetMatchingNpcsForDistributionLine([Npc(guard.FormKey, "Guard", faction.FormKey)],
      DistributionFileType.SkyPatcher, filters + ":outfitDefault=Uniform", cache, keywords)
      .Count.Should().Be(matches ? 1 : 0);
  }

  [Fact]
  public void MissingNpcSnapshot_ReportsLimitedCoverageInsteadOfAssuringNoConflicts()
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Matching.esp"), SkyrimRelease.SkyrimSE);
    using var cache = mod.ToImmutableLinkCache();
    var result = DistributionConflictDetectionService.DetectConflicts([new(new DistributionEntry { KeywordFilters = [new("Ready")] })],
      [File("Z.ini", "Outfit = Any|NONE|NONE|NONE|NONE|NONE|100")], "A.ini", cache, []);
    result.ConflictSummary.Should().Contain("unavailable");
    result.ConflictsResolvedByFilename.Should().BeFalse();
  }

  [Theory]
  [InlineData("garbage")]
  [InlineData("24(50/100)")]
  [InlineData("5/bad")]
  public void InvalidLevels_DoNotBroadenMatching(string level)
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Matching.esp"), SkyrimRelease.SkyrimSE);
    var guard = mod.Npcs.AddNew("Guard");
    using var cache = mod.ToImmutableLinkCache();
    var npcs = new[] { Npc(guard.FormKey, "Guard") };
    FilterMatchingService.GetMatchingNpcsForEntry(npcs, new DistributionEntry { LevelFilters = level }).Should().BeEmpty();
    FilterMatchingService.GetMatchingNpcsForDistributionLine(npcs, DistributionFileType.Spid,
      $"Outfit = Any|NONE|NONE|{level}|NONE|NONE|100", cache).Should().BeEmpty();
  }

  [Theory]
  [InlineData(DistributionType.Keyword)]
  [InlineData(DistributionType.ExclusiveGroup)]
  public void NonOutfitEntries_DoNotCreateOutfitConflicts(DistributionType type)
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Matching.esp"), SkyrimRelease.SkyrimSE);
    var guard = mod.Npcs.AddNew("Guard");
    using var cache = mod.ToImmutableLinkCache();
    var result = DistributionConflictDetectionService.DetectConflicts([new(new DistributionEntry { Type = type })],
      [File("Z.ini", "Outfit = Any|NONE|NONE|NONE|NONE|NONE|100")], "A.ini", cache, [Npc(guard.FormKey, "Guard")]);
    result.Conflicts.Should().BeEmpty();
  }

  [Fact]
  public void SkyPatcherRaceAlternatives_AreNotTreatedAsImpossibleConjunction()
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Matching.esp"), SkyrimRelease.SkyrimSE);
    var nord = mod.Races.AddNew("Nord");
    mod.Races.AddNew("Breton");
    mod.Outfits.AddNew("Uniform");
    var guard = mod.Npcs.AddNew("Guard");
    using var cache = mod.ToImmutableLinkCache();
    FilterMatchingService.GetMatchingNpcsForDistributionLine([Npc(guard.FormKey, "Guard", race: nord.FormKey)],
      DistributionFileType.SkyPatcher, "filterByRaces=Nord,Breton:outfitDefault=Uniform", cache)
      .Should().ContainSingle().Which.FormKey.Should().Be(guard.FormKey);
  }

  [Theory]
  [InlineData("Nord", "5/15", "M/U", true)]
  [InlineData("Breton", "5/15", "M/U", false)]
  [InlineData("Nord", "11/15", "M/U", false)]
  [InlineData("Nord", "5/15", "F/U", false)]
  [InlineData("Nord", "5/15", "M/-U", false)]
  public void ExistingSpidRules_ConstrainRaceLevelAndTraits(string race, string level, string traits, bool matches)
  {
    var mod = new SkyrimMod(ModKey.FromNameAndExtension("Matching.esp"), SkyrimRelease.SkyrimSE);
    var nord = mod.Races.AddNew("Nord");
    var guard = mod.Npcs.AddNew("Guard");
    using var cache = mod.ToImmutableLinkCache();
    var result = DistributionConflictDetectionService.DetectConflicts([new(new DistributionEntry { NpcFilters = [new(guard.FormKey)] })],
      [File("Z.ini", $"Outfit = Any|NONE|{race}|{level}|{traits}|NONE|100")], "A.ini", cache, [Npc(guard.FormKey, "Guard", race: nord.FormKey)]);
    result.Conflicts.Count.Should().Be(matches ? 1 : 0);
  }

  private static DistributionFileViewModel File(string name, string line, DistributionFileType type = DistributionFileType.Spid) =>
    new(new DistributionFile(name, name, name, type, [new DistributionLine(1, line, DistributionLineKind.KeyValue, true, [])], 1));

  private static NpcFilterData Npc(FormKey key, string name, FormKey? faction = null, FormKey? race = null, FormKey? cls = null, FormKey? outfit = null) => new()
  {
    FormKey = key, EditorId = name, Name = name, SourceMod = key.ModKey,
    Keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
    Factions = faction.HasValue ? [new FactionMembership { FactionFormKey = faction.Value, FactionEditorId = "Guards", Rank = 0 }] : [],
    RaceFormKey = race, RaceEditorId = race.HasValue ? "Nord" : null, ClassFormKey = cls, ClassEditorId = cls.HasValue ? "Warrior" : null,
    CombatStyleFormKey = null, CombatStyleEditorId = null, VoiceTypeFormKey = null, VoiceTypeEditorId = null,
    DefaultOutfitFormKey = outfit, DefaultOutfitEditorId = outfit.HasValue ? "Uniform" : null, WornArmorFormKey = null,
    IsFemale = false, IsUnique = true, IsSummonable = false, IsChild = false, IsLeveled = false, Level = 10,
    Locations = new HashSet<FormKey>(), TemplateFormKey = null, TemplateEditorId = null,
    MatchKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name, faction.HasValue ? "Guards" : name }
  };
}
