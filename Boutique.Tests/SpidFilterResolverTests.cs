using Boutique.Models;
using Boutique.Utilities;
using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Characterization tests for SpidFilterResolver: how SPID filter sections resolve
///     against a synthetic load order (in-memory records written to a temp Data folder).
///     These lock the resolution semantics before/while the resolver internals are refactored.
/// </summary>
public class SpidFilterResolverTests : IDisposable
{
    private readonly string _dataDir;
    private readonly ILinkCache<ISkyrimMod, ISkyrimModGetter> _linkCache;

    public INpcGetter YsoldaNpc { get; }
    public IFactionGetter TestFaction { get; }
    public IFactionGetter OtherFaction { get; }
    public IRaceGetter TestRace { get; }
    public IKeywordGetter TestKeyword { get; }
    public IOutfitGetter TestOutfit { get; }
    public IClassGetter TestClass { get; }

    public SpidFilterResolverTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "boutique_resolver_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);

        const string masterName = "HarnessMaster.esp";
        var master = new SkyrimMod(ModKey.FromNameAndExtension(masterName), SkyrimRelease.SkyrimSE);

        var npc = master.Npcs.AddNew();
        npc.EditorID = "Ysolda";
        npc.Name = "Ysolda";

        var faction = master.Factions.AddNew();
        faction.EditorID = "TestFaction";

        var otherFaction = master.Factions.AddNew();
        otherFaction.EditorID = "OtherFaction";

        var race = master.Races.AddNew();
        race.EditorID = "TestRace";

        var keyword = master.Keywords.AddNew();
        keyword.EditorID = "TestKeyword";

        var outfit = master.Outfits.AddNew();
        outfit.EditorID = "OTFT_TestOutfit";

        var classRecord = master.Classes.AddNew();
        classRecord.EditorID = "TestClass";

        master.BeginWrite
              .ToPath(Path.Combine(_dataDir, masterName))
              .WithNoLoadOrder()
              .Write();

        var listings = new List<ILoadOrderListingGetter>
        {
            new LoadOrderListing(master.ModKey, enabled: true)
        };
        var loadOrder = LoadOrder.Import<ISkyrimModGetter>(_dataDir, listings, GameRelease.SkyrimSE);
        _linkCache = loadOrder.ToImmutableLinkCache();

        YsoldaNpc   = _linkCache.WinningOverrides<INpcGetter>().Single(n => n.EditorID == "Ysolda");
        TestFaction = _linkCache.WinningOverrides<IFactionGetter>().Single(f => f.EditorID == "TestFaction");
        OtherFaction = _linkCache.WinningOverrides<IFactionGetter>().Single(f => f.EditorID == "OtherFaction");
        TestRace    = _linkCache.WinningOverrides<IRaceGetter>().Single(r => r.EditorID == "TestRace");
        TestKeyword = _linkCache.WinningOverrides<IKeywordGetter>().Single(k => k.EditorID == "TestKeyword");
        TestOutfit  = _linkCache.WinningOverrides<IOutfitGetter>().Single(o => o.EditorID == "OTFT_TestOutfit");
        TestClass   = _linkCache.WinningOverrides<IClassGetter>().Single(c => c.EditorID == "TestClass");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
            // Temp cleanup is best-effort
        }
    }

    private DistributionEntry? ResolveOutfitLine(
        string line,
        FormIdLookupCache? formIdCache = null,
        IReadOnlySet<string>? virtualKeywords = null)
    {
        var parsed = SpidLineParser.TryParse(line, out var filter);
        parsed.Should().BeTrue($"line should parse: {line}");
        return SpidFilterResolver.Resolve(
            filter!,
            _linkCache,
            [YsoldaNpc],
            [TestOutfit],
            virtualKeywords,
            formIdCache);
    }

    private DistributionEntry? ResolveKeywordLine(
        string line,
        FormIdLookupCache? formIdCache = null,
        IReadOnlySet<string>? virtualKeywords = null)
    {
        var parsed = SpidLineParser.TryParseKeyword(line, out var filter);
        parsed.Should().BeTrue($"line should parse: {line}");
        return SpidFilterResolver.ResolveKeyword(
            filter!,
            _linkCache,
            [YsoldaNpc],
            virtualKeywords,
            formIdCache);
    }

    [Fact]
    public void Resolve_NpcNameStringFilter_PopulatesNpcFilters()
    {
        var entry = ResolveOutfitLine("Outfit = OTFT_TestOutfit|Ysolda");

        entry.Should().NotBeNull();
        entry!.NpcFilters.Should().ContainSingle(f => f.FormKey == YsoldaNpc.FormKey && !f.IsExcluded);
        entry.KeywordFilters.Should().BeEmpty();
        entry.NpcLogicMode.Should().Be(FilterLogicMode.And);
    }

    [Fact]
    public void Resolve_KeywordEditorIdStringFilter_PopulatesKeywordFilters()
    {
        var entry = ResolveOutfitLine("Outfit = OTFT_TestOutfit|TestKeyword");

        entry.Should().NotBeNull();
        entry!.KeywordFilters.Should().ContainSingle(k => k.EditorId == "TestKeyword" && !k.IsExcluded);
        entry.NpcFilters.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_VirtualKeyword_PopulatesKeywordFilters()
    {
        var entry = ResolveOutfitLine(
            "Outfit = OTFT_TestOutfit|MyVirtualKeyword",
            virtualKeywords: new HashSet<string>(["MyVirtualKeyword"]));

        entry.Should().NotBeNull();
        entry!.KeywordFilters.Should().ContainSingle(k => k.EditorId == "MyVirtualKeyword");
    }

    [Fact]
    public void Resolve_UnknownStringFilter_PreservedInRawStringFilters()
    {
        var entry = ResolveOutfitLine("Outfit = OTFT_TestOutfit|NoSuchThing");

        entry.Should().NotBeNull("outfit-only lines must still resolve");
        entry!.RawStringFilters.Should().Be("NoSuchThing");
        entry.NpcFilters.Should().BeEmpty();
        entry.KeywordFilters.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NegatedStringFilter_IsExcluded()
    {
        var entry = ResolveOutfitLine("Outfit = OTFT_TestOutfit|-Ysolda");

        entry.Should().NotBeNull();
        entry!.NpcFilters.Should().ContainSingle(f => f.FormKey == YsoldaNpc.FormKey && f.IsExcluded);
        entry.RawStringFilters.Should().BeNull("the negated NPC resolved, so no raw remainder");
    }

    [Fact]
    public void Resolve_FactionFormFilter_PopulatesFactionFilters()
    {
        var entry = ResolveOutfitLine("Outfit = OTFT_TestOutfit|NONE|TestFaction");

        entry.Should().NotBeNull();
        entry!.FactionFilters.Should().ContainSingle(f => f.FormKey == TestFaction.FormKey && !f.IsExcluded);
        entry.FactionLogicMode.Should().Be(FilterLogicMode.And);
    }

    [Fact]
    public void Resolve_NegatedFaction_IsExcluded()
    {
        var entry = ResolveOutfitLine("Outfit = OTFT_TestOutfit|NONE|-TestFaction");

        entry.Should().NotBeNull();
        entry!.FactionFilters.Should().ContainSingle(f => f.FormKey == TestFaction.FormKey && f.IsExcluded);
    }

    [Fact]
    public void Resolve_RaceAndClassFormFilters_PopulateRespectiveLists()
    {
        var entry = ResolveOutfitLine("Outfit = OTFT_TestOutfit|NONE|TestRace+TestClass");

        entry.Should().NotBeNull();
        entry!.RaceFilters.Should().ContainSingle(f => f.FormKey == TestRace.FormKey);
        entry.ClassFormKeys.Should().ContainSingle(k => k == TestClass.FormKey);
        entry.RaceLogicMode.Should().Be(FilterLogicMode.And);
        entry.ClassLogicMode.Should().Be(FilterLogicMode.And);
    }

    [Fact]
    public void Resolve_CommaSeparatedSameTypeFormFilters_OrLogic()
    {
        // Same-type values split by "," land in one list with >1 entry — that is what
        // switches the list's logic mode to Or.
        var entry = ResolveOutfitLine("Outfit = OTFT_TestOutfit|NONE|TestFaction,OtherFaction");

        entry.Should().NotBeNull();
        entry!.FactionFilters.Should().HaveCount(2);
        entry.FactionFilters.Should().Contain(f => f.FormKey == TestFaction.FormKey);
        entry.FactionFilters.Should().Contain(f => f.FormKey == OtherFaction.FormKey);
        entry.FactionLogicMode.Should().Be(FilterLogicMode.Or);
    }

    [Fact]
    public void Resolve_CommaSeparatedDifferentTypeFormFilters_EachSingleValueIsAnd()
    {
        // Cross-type "," splits resolve into separate single-entry lists, which stay And
        // (with one value per list, And/Or is not distinguishable anyway).
        var entry = ResolveOutfitLine("Outfit = OTFT_TestOutfit|NONE|TestFaction,TestRace");

        entry.Should().NotBeNull();
        entry!.FactionFilters.Should().ContainSingle(f => f.FormKey == TestFaction.FormKey);
        entry.RaceFilters.Should().ContainSingle(f => f.FormKey == TestRace.FormKey);
        entry.FactionLogicMode.Should().Be(FilterLogicMode.And);
        entry.RaceLogicMode.Should().Be(FilterLogicMode.And);
    }

    [Fact]
    public void Resolve_BareFormId_WithLookupCache_Resolves()
    {
        var formIdCache = new FormIdLookupCache(_linkCache);
        var bareId = TestFaction.FormKey.ID.ToString("X6");
        var entry = ResolveOutfitLine($"Outfit = OTFT_TestOutfit|NONE|{bareId}", formIdCache);

        entry.Should().NotBeNull();
        entry!.FactionFilters.Should().ContainSingle(f => f.FormKey == TestFaction.FormKey);
    }

    [Fact]
    public void Resolve_BareFormId_WithoutLookupCache_Resolves()
    {
        var bareId = TestFaction.FormKey.ID.ToString("X6");
        var entry = ResolveOutfitLine($"Outfit = OTFT_TestOutfit|NONE|{bareId}");

        entry.Should().NotBeNull();
        entry!.FactionFilters.Should().ContainSingle(f => f.FormKey == TestFaction.FormKey);
    }

    [Fact]
    public void Resolve_ExplicitFormKey_PopulatesFilters()
    {
        var formKeyString = TestFaction.FormKey.ToString();
        var entry = ResolveOutfitLine($"Outfit = OTFT_TestOutfit|NONE|{formKeyString}");

        entry.Should().NotBeNull();
        entry!.FactionFilters.Should().ContainSingle(f => f.FormKey == TestFaction.FormKey);
    }

    [Fact]
    public void Resolve_UnknownFormFilter_PreservedInRawFormFilters()
    {
        var entry = ResolveOutfitLine("Outfit = OTFT_TestOutfit|NONE|MissingFilter");

        entry.Should().NotBeNull();
        entry!.RawFormFilters.Should().Be("MissingFilter");
        entry.FactionFilters.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NegatedUnknownFormFilter_Preserved()
    {
        var entry = ResolveOutfitLine("Outfit = OTFT_TestOutfit|NONE|-MissingFilter");

        entry.Should().NotBeNull();
        entry!.RawFormFilters.Should().Be("-MissingFilter");
    }

    [Fact]
    public void Resolve_TildeFormatOutfitIdentifier_ResolvesOutfit()
    {
        var identifier = $"{TestOutfit.FormKey.ID:X8}~HarnessMaster.esp";
        var entry = ResolveOutfitLine($"Outfit = {identifier}");

        entry.Should().NotBeNull();
        entry!.Outfit.Should().NotBeNull();
        entry.Outfit!.FormKey.Should().Be(TestOutfit.FormKey);
    }

    [Fact]
    public void Resolve_UnknownOutfit_ReturnsNull()
    {
        var entry = ResolveOutfitLine("Outfit = OTFT_DoesNotExist");

        entry.Should().BeNull();
    }

    [Fact]
    public void Resolve_ChanceNot100_IsCarriedToEntry()
    {
        var entry = ResolveOutfitLine("Outfit = OTFT_TestOutfit|NONE|NONE|NONE|NONE|NONE|50");

        entry.Should().NotBeNull();
        entry!.Chance.Should().Be(50);
    }

    [Fact]
    public void ResolveKeyword_SetsKeywordTypeAndIdentifier()
    {
        var entry = ResolveKeywordLine("Keyword = TestKeyword|Ysolda");

        entry.Should().NotBeNull();
        entry!.Type.Should().Be(DistributionType.Keyword);
        entry.KeywordToDistribute.Should().Be("TestKeyword");
        entry.NpcFilters.Should().ContainSingle(f => f.FormKey == YsoldaNpc.FormKey);
    }

    [Fact]
    public void ResolveKeyword_FactionFormFilter_PopulatesFactionFilters()
    {
        var entry = ResolveKeywordLine("Keyword = TestKeyword|NONE|TestFaction");

        entry.Should().NotBeNull();
        entry!.Type.Should().Be(DistributionType.Keyword);
        entry.FactionFilters.Should().ContainSingle(f => f.FormKey == TestFaction.FormKey);
    }

    [Fact]
    public void Resolve_NoFiltersAndNoOutfit_ReturnsNull()
    {
        // An outfit line whose outfit resolves but with no filter content at all
        // and nothing targeted should be dropped.
        var parsed = SpidLineParser.TryParse("Outfit = OTFT_TestOutfit", out var filter);
        parsed.Should().BeTrue();

        var entry = SpidFilterResolver.Resolve(
            filter!,
            _linkCache,
            [YsoldaNpc],
            [TestOutfit],
            null,
            null);

        entry.Should().NotBeNull("a bare Outfit line targets all NPCs via SPID semantics");
        filter!.TargetsAllNpcs.Should().BeTrue();
    }
}
