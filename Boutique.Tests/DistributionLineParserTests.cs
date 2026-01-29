using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

public class DistributionLineParserTests
{
    #region SPID LineTargetsAllNpcs Tests

    [Fact]
    public void SpidLine_NoFilters_TargetsAllNpcs()
    {
        var file = CreateSpidFileViewModel("test.ini");
        var line = CreateLine("Outfit = MyOutfit", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeTrue();
    }

    [Fact]
    public void SpidLine_WithStringFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSpidFileViewModel("test.ini");
        var line = CreateLine("Outfit = MyOutfit|Serana", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeFalse();
    }

    [Fact]
    public void SpidLine_WithFormFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSpidFileViewModel("test.ini");
        var line = CreateLine("Outfit = MyOutfit|NONE|NordRace", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeFalse();
    }

    [Fact]
    public void SpidLine_WithKeywordFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSpidFileViewModel("test.ini");
        var line = CreateLine("Outfit = MyOutfit|ActorTypeNPC", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeFalse();
    }

    [Fact]
    public void SpidLine_WithTraitFilterOnly_TargetsAllNpcs()
    {
        var file = CreateSpidFileViewModel("test.ini");
        var line = CreateLine("Outfit = MyOutfit|NONE|NONE|NONE|F", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeTrue("trait filters are modifiers, not exclusions");
    }

    #endregion

    #region SkyPatcher LineTargetsAllNpcs Tests

    [Fact]
    public void SkyPatcherLine_NoFilters_TargetsAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = CreateLine("outfitDefault=Skyrim.esm|000ABC12", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeTrue();
    }

    [Fact]
    public void SkyPatcherLine_WithNpcFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = CreateLine("filterByNpcs=Skyrim.esm|000ABC12:outfitDefault=Skyrim.esm|000DEF34", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeFalse();
    }

    [Fact]
    public void SkyPatcherLine_WithFactionFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = CreateLine("filterByFactions=Skyrim.esm|000ABC12:outfitDefault=Skyrim.esm|000DEF34", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeFalse();
    }

    [Fact]
    public void SkyPatcherLine_WithRaceFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = CreateLine("filterByRaces=Skyrim.esm|000ABC12:outfitDefault=Skyrim.esm|000DEF34", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeFalse();
    }

    [Fact]
    public void SkyPatcherLine_WithKeywordFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = CreateLine("filterByKeywords=Skyrim.esm|000ABC12:outfitDefault=Skyrim.esm|000DEF34", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeFalse();
    }

    [Fact]
    public void SkyPatcherLine_WithGenderFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = CreateLine("filterByGender=female:outfitDefault=Skyrim.esm|000DEF34", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeFalse();
    }

    [Fact]
    public void SkyPatcherLine_WithEditorIdFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = CreateLine("filterByEditorIdContains=Bandit:outfitDefault=Skyrim.esm|000DEF34", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeFalse();
    }

    [Fact]
    public void SkyPatcherLine_WithModNameFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = CreateLine("filterByModNames=MyMod.esp:outfitDefault=Skyrim.esm|000DEF34", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeFalse();
    }

    [Fact]
    public void SkyPatcherLine_WithDefaultOutfitFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = CreateLine("filterByDefaultOutfits=Skyrim.esm|000ABC12:outfitDefault=Skyrim.esm|000DEF34", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeFalse();
    }

    [Fact]
    public void SkyPatcherLine_WithoutOutfitAssignment_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = CreateLine("formsToAdd=Skyrim.esm|000ABC12", false);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeFalse();
    }

    [Fact]
    public void SkyPatcherLine_WithSleepOutfit_NoFilters_TargetsAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = CreateLine("outfitSleep=Skyrim.esm|000ABC12", true);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        result.Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private static DistributionLine CreateLine(string rawText, bool isOutfitDistribution) =>
        new(
            1,
            rawText,
            DistributionLineKind.KeyValue,
            null,
            null,
            null,
            isOutfitDistribution,
            []);

    private static DistributionFileViewModel CreateSpidFileViewModel(string fileName) =>
        new(new DistributionFile(
            fileName,
            $"Data/{fileName}",
            fileName,
            DistributionFileType.Spid,
            [],
            0));

    private static DistributionFileViewModel CreateSkyPatcherFileViewModel(string fileName) =>
        new(new DistributionFile(
            fileName,
            $"Data/{fileName}",
            fileName,
            DistributionFileType.SkyPatcher,
            [],
            0));

    #endregion
}
