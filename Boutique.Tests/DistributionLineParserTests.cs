using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using Xunit;

namespace Boutique.Tests;

public class DistributionLineParserTests
{
    #region SPID LineTargetsAllNpcs Tests

    [Fact]
    public void SpidLine_NoFilters_TargetsAllNpcs()
    {
        var file = CreateSpidFileViewModel("test.ini");
        var line = new DistributionLine("Outfit = MyOutfit", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.True(result);
    }

    [Fact]
    public void SpidLine_WithStringFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSpidFileViewModel("test.ini");
        var line = new DistributionLine("Outfit = MyOutfit|Serana", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.False(result);
    }

    [Fact]
    public void SpidLine_WithFormFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSpidFileViewModel("test.ini");
        var line = new DistributionLine("Outfit = MyOutfit|NONE|NordRace", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.False(result);
    }

    [Fact]
    public void SpidLine_WithKeywordFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSpidFileViewModel("test.ini");
        var line = new DistributionLine("Outfit = MyOutfit|ActorTypeNPC", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.False(result);
    }

    [Fact]
    public void SpidLine_WithTraitFilterOnly_TargetsAllNpcs()
    {
        // Trait filters (F/M/U etc) are evaluated but don't prevent "targets all"
        // because they're modifiers, not exclusions of all NPCs
        var file = CreateSpidFileViewModel("test.ini");
        var line = new DistributionLine("Outfit = MyOutfit|NONE|NONE|NONE|F", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        // This should return true because StringFilters and FormFilters are empty
        // Trait filters are separate from NPC targeting
        Assert.True(result);
    }

    #endregion

    #region SkyPatcher LineTargetsAllNpcs Tests

    [Fact]
    public void SkyPatcherLine_NoFilters_TargetsAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = new DistributionLine("outfitDefault=Skyrim.esm|000ABC12", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.True(result);
    }

    [Fact]
    public void SkyPatcherLine_WithNpcFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = new DistributionLine("filterByNpcs=Skyrim.esm|000ABC12:outfitDefault=Skyrim.esm|000DEF34", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.False(result);
    }

    [Fact]
    public void SkyPatcherLine_WithFactionFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = new DistributionLine("filterByFactions=Skyrim.esm|000ABC12:outfitDefault=Skyrim.esm|000DEF34", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.False(result);
    }

    [Fact]
    public void SkyPatcherLine_WithRaceFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = new DistributionLine("filterByRaces=Skyrim.esm|000ABC12:outfitDefault=Skyrim.esm|000DEF34", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.False(result);
    }

    [Fact]
    public void SkyPatcherLine_WithKeywordFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = new DistributionLine("filterByKeywords=Skyrim.esm|000ABC12:outfitDefault=Skyrim.esm|000DEF34", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.False(result);
    }

    [Fact]
    public void SkyPatcherLine_WithGenderFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = new DistributionLine("filterByGender=female:outfitDefault=Skyrim.esm|000DEF34", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.False(result);
    }

    [Fact]
    public void SkyPatcherLine_WithEditorIdFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = new DistributionLine("filterByEditorIdContains=Bandit:outfitDefault=Skyrim.esm|000DEF34", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.False(result);
    }

    [Fact]
    public void SkyPatcherLine_WithModNameFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = new DistributionLine("filterByModNames=MyMod.esp:outfitDefault=Skyrim.esm|000DEF34", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.False(result);
    }

    [Fact]
    public void SkyPatcherLine_WithDefaultOutfitFilter_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = new DistributionLine("filterByDefaultOutfits=Skyrim.esm|000ABC12:outfitDefault=Skyrim.esm|000DEF34", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.False(result);
    }

    [Fact]
    public void SkyPatcherLine_WithoutOutfitAssignment_DoesNotTargetAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        // This line has no outfit assignment, so it's not an outfit distribution
        var line = new DistributionLine("formsToAdd=Skyrim.esm|000ABC12", false, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.False(result);
    }

    [Fact]
    public void SkyPatcherLine_WithSleepOutfit_NoFilters_TargetsAllNpcs()
    {
        var file = CreateSkyPatcherFileViewModel("test.ini");
        var line = new DistributionLine("outfitSleep=Skyrim.esm|000ABC12", true, []);

        var result = DistributionLineParser.LineTargetsAllNpcs(file, line);

        Assert.True(result);
    }

    #endregion

    #region Helper Methods

    private static DistributionFileViewModel CreateSpidFileViewModel(string fileName) =>
        new(new DistributionFile
        {
            FileName = fileName,
            FullPath = $"Data/{fileName}",
            Type = DistributionFileType.Spid,
            Lines = []
        });

    private static DistributionFileViewModel CreateSkyPatcherFileViewModel(string fileName) =>
        new(new DistributionFile
        {
            FileName = fileName,
            FullPath = $"Data/{fileName}",
            Type = DistributionFileType.SkyPatcher,
            Lines = []
        });

    #endregion
}
