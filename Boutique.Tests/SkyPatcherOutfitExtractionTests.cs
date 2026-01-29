using Boutique.Services;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Tests for SkyPatcher outfit identifier extraction from distribution lines.
/// </summary>
public class SkyPatcherOutfitExtractionTests
{
    #region ExtractSkyPatcherOutfitKeys - Main extraction

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithOutfitDefault_ExtractsFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|0x800");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithOutfitSleep_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "filterByNpcs=Skyrim.esm|0x1234:outfitSleep=MyMod.esp|0x900");

        result.Should().BeEmpty("outfitSleep is not currently extracted");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithBothDefaultAndFilterByOutfits_ExtractsBoth()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "filterByOutfits=MyMod.esp|0x800:outfitDefault=MyMod.esp|0x900");

        result.Should().HaveCount(2)
            .And.Contain(["MyMod.esp|0x800", "MyMod.esp|0x900"]);
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_NoOutfitFilters_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "filterByNpcs=Skyrim.esm|0x1234:formsToAdd=MyMod.esp|0x800");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_EmptyLine_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_NullLine_ThrowsException()
    {
        var act = () => DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(null!);
        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_Comment_StillExtractsFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "; outfitDefault=MyMod.esp|0x800");

        result.Should().ContainSingle("comment filtering is done at call site, not in extraction");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_CaseInsensitive_ExtractsFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "OUTFITDEFAULT=MyMod.esp|0x800");

        result.Should().ContainSingle();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithHexPrefix_ExtractsFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=MyMod.esp|0x800");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|0x800");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithoutHexPrefix_ExtractsFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=MyMod.esp|800");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|800");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FormKeyWithEsm_ExtractsFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=Skyrim.esm|0xABCDE");

        result.Should().ContainSingle().Which.Should().Be("Skyrim.esm|0xABCDE");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FormKeyWithEsl_ExtractsFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=LightMod.esl|0x800");

        result.Should().ContainSingle().Which.Should().Be("LightMod.esl|0x800");
    }

    #endregion

    #region Edge cases

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithSpaces_ExtractsFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault= MyMod.esp|0x800 ");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|0x800");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_ComplexLine_ExtractsFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "filterByFactions=Skyrim.esm|0xFDEAC:filterByGender=female:outfitDefault=MyMod.esp|0xFE000D65");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|0xFE000D65");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_MultipleNpcFilters_ExtractsFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "filterByNpcs=Skyrim.esm|0x100,Skyrim.esm|0x200:outfitDefault=MyMod.esp|0x800");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|0x800");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_LongFormId_ExtractsFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=MyMod.esp|00ABCDEF");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|00ABCDEF");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithSpacesInModName_ExtractsFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=My Cool Mod.esp|0x800");

        result.Should().ContainSingle().Which.Should().Be("My Cool Mod.esp|0x800");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_SleepOutfit_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "filterByNpcs=Skyrim.esm|0x13BBF:outfitSleep=Skyrim.esm|0xD3E06");

        result.Should().BeEmpty("outfitSleep is not currently extracted");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_OnlyOutfitDefault_ExtractsSingle()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=Skyrim.esm|0xD3E05");

        result.Should().ContainSingle().Which.Should().Be("Skyrim.esm|0xD3E05");
    }

    #endregion

    #region Non-outfit operations should be ignored

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FormsToAdd_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "formsToAdd=Skyrim.esm|0x59A71");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FormsToRemove_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "formsToRemove=Skyrim.esm|0x59A71");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FormsToReplace_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "formsToReplace=Skyrim.esm|0x59A71=Skyrim.esm|0x59A72");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_Clear_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "clear=true");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FilterByOutfits_ExtractsOutfitKey()
    {
        var result = DistributionDiscoveryService.ExtractSkyPatcherOutfitKeys(
            "filterByOutfits=Skyrim.esm|0x246EE7:formsToAdd=Skyrim.esm|0x59A71");

        result.Should().ContainSingle().Which.Should().Be("Skyrim.esm|0x246EE7");
    }

    #endregion
}
