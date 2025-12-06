using Boutique.Services;
using Xunit;

namespace Boutique.Tests;

/// <summary>
/// Tests for SkyPatcher outfit extraction from distribution lines.
/// </summary>
public class SkyPatcherOutfitExtractionTests
{
    #region ExtractSkyPatcherOutfitKeys - filterByOutfits format

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FilterByOutfits_SingleOutfit()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "filterByOutfits=MyMod.esp|0x12345:outfitDefault=OtherMod.esp|0x800");
        
        Assert.Contains("MyMod.esp|0x12345", result);
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FilterByOutfits_MultipleOutfits()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "filterByOutfits=Mod1.esp|0x100,Mod2.esp|0x200:outfitDefault=Mod3.esp|0x300");
        
        Assert.Contains("Mod1.esp|0x100", result);
        Assert.Contains("Mod2.esp|0x200", result);
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_OutfitDefault_ExtractsOutfit()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800");
        
        Assert.Contains("MyMod.esp|0x800", result);
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_BothMarkers_ExtractsAll()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "filterByOutfits=Mod1.esp|0x100:outfitDefault=Mod2.esp|0x200");
        
        Assert.Equal(2, result.Count);
        Assert.Contains("Mod1.esp|0x100", result);
        Assert.Contains("Mod2.esp|0x200", result);
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_NoOutfitMarkers_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "filterByNpcs=Skyrim.esm|0x1234:filterByFactions=VampireFaction");
        
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_EmptyValue_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys("");
        Assert.Empty(result);
    }

    #endregion

    #region ExtractSkyPatcherOutfitKeys - Tilde format support

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_TildeFormat_ExtractsAndNormalizes()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=0x800~MyMod.esp");
        
        Assert.Single(result);
        // The normalization should produce ModKey|FormID format
        Assert.Contains("MyMod.esp|0x800", result);
    }

    #endregion

    #region Edge cases

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_CaseInsensitive_Works()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "FILTERBYOUTFITS=MyMod.esp|0x100:OUTFITDEFAULT=MyMod.esp|0x200");
        
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithSpaces_HandlesCorrectly()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault= MyMod.esp|0x800 ");
        
        Assert.Single(result);
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_MultipleColonSections_ParsesAll()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "section1:filterByOutfits=Mod1.esp|0x100:section2:outfitDefault=Mod2.esp|0x200");
        
        Assert.Contains("Mod1.esp|0x100", result);
        Assert.Contains("Mod2.esp|0x200", result);
    }

    #endregion
}

