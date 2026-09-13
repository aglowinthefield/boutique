using System.Collections.ObjectModel;
using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     SkyPatcher line generation and round-trip: format a populated entry with
///     DistributionFileFormatter, then parse the emitted line back with SkyPatcherSyntax
///     (the same extractor the distribution file editor uses) and verify the filters.
/// </summary>
public class SkyPatcherFormatterRoundTripTests
{
    private static readonly ModKey MasterKey = ModKey.FromNameAndExtension("HarnessMaster.esp");

    private static IOutfitGetter CreateOutfit()
    {
        var mod = new SkyrimMod(MasterKey, SkyrimRelease.SkyrimSE);
        var outfit = mod.Outfits.AddNew();
        outfit.EditorID = "OTFT_RoundTrip";
        return outfit;
    }

    private static DistributionEntryViewModel CreateEntryVm(
        IOutfitGetter outfit,
        Action<DistributionEntry>? configure = null)
    {
        var entry = new DistributionEntry { Outfit = outfit };
        configure?.Invoke(entry);
        return new DistributionEntryViewModel(entry);
    }

    [Fact]
    public void FactionKeywordGenderUnique_EmittedAndParseBack()
    {
        var outfit = CreateOutfit();
        var factionRecord = new FactionRecord(new FormKey(MasterKey, 0x800), "TestFaction", null, MasterKey);
        var keywordRecord = new KeywordRecord(new FormKey(MasterKey, 0x810), "TestKeyword", MasterKey);

        var vm = CreateEntryVm(outfit, entry =>
        {
            entry.FactionFilters.Add(new FormKeyFilter(factionRecord.FormKey));
            entry.KeywordFilters.Add(new KeywordFilter(keywordRecord.EditorID));
            entry.TraitFilters = new SpidTraitFilters { IsFemale = true, IsUnique = true };
        });
        vm.SelectedFactions = new ObservableCollection<FactionRecordViewModel>(
            [new FactionRecordViewModel(factionRecord)]);
        vm.SelectedKeywords = new ObservableCollection<KeywordRecordViewModel>(
            [new KeywordRecordViewModel(keywordRecord)]);
        vm.Gender = GenderFilter.Female;
        vm.Unique = UniqueFilter.UniqueOnly;

        var line = DistributionFileFormatter.FormatSkyPatcherLine(vm);

        SkyPatcherSyntax.ExtractFilterValues(line, "filterByFactions")
                        .Should().Contain("HarnessMaster.esp|00000800");
        SkyPatcherSyntax.ExtractFilterValues(line, "filterByKeywords")
                        .Should().Contain("HarnessMaster.esp|00000810");
        SkyPatcherSyntax.ParseGenderFilter(line).Should().Be(true, "female filter must parse back");
        SkyPatcherSyntax.ExtractFilterValue(line, "restrictToFlags").Should().Be("unique");
        SkyPatcherSyntax.ExtractFilterValue(line, "outfitDefault")
                        .Should().Contain("HarnessMaster.esp",
                                          "the outfit identifier must reference its defining master");
    }

    [Fact]
    public void ExcludedNpc_EmittedAsNpcsExcluded()
    {
        var outfit = CreateOutfit();
        var npcRecord = new NpcRecord(new FormKey(MasterKey, 0x900), "Ysolda", "Ysolda", MasterKey);

        var vm = CreateEntryVm(outfit, entry =>
        {
            entry.NpcFilters.Add(new FormKeyFilter(npcRecord.FormKey, IsExcluded: true));
        });
        var npcVm = new NpcRecordViewModel(npcRecord) { IsExcluded = true };
        vm.SelectedNpcs = new ObservableCollection<NpcRecordViewModel>([npcVm]);

        var line = DistributionFileFormatter.FormatSkyPatcherLine(vm);

        SkyPatcherSyntax.ExtractFilterValue(line, "filterByNpcsExcluded")
                        .Should().Contain("00000900", "the exclusion must survive formatting");
    }

    [Fact]
    public void OrLogicKeywords_EmittedWithKeywordsOrVariant()
    {
        var outfit = CreateOutfit();
        var kw1 = new KeywordRecord(new FormKey(MasterKey, 0x810), "KeywordOne", MasterKey);
        var kw2 = new KeywordRecord(new FormKey(MasterKey, 0x811), "KeywordTwo", MasterKey);

        var vm = CreateEntryVm(outfit, entry =>
        {
            entry.KeywordFilters.Add(new KeywordFilter("KeywordOne"));
            entry.KeywordFilters.Add(new KeywordFilter("KeywordTwo"));
            entry.KeywordLogicMode = FilterLogicMode.Or;
        });
        vm.SelectedKeywords = new ObservableCollection<KeywordRecordViewModel>(
        [
            new KeywordRecordViewModel(kw1),
            new KeywordRecordViewModel(kw2)
        ]);

        var line = DistributionFileFormatter.FormatSkyPatcherLine(vm);

        var values = SkyPatcherSyntax.ExtractFilterValuesWithVariants(line, "filterByKeywords");
        values.Should().Contain("HarnessMaster.esp|00000810");
        values.Should().Contain("HarnessMaster.esp|00000811");
        SkyPatcherSyntax.ExtractFilterValue(line, "filterByKeywordsOr")
                        .Should().NotBeNull("Or-mode keywords use the Or variant filter");
    }
}
