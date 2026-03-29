using Boutique.Services;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

public class OutfitExtractionTests
{
    #region SPID - ExtractSpidOutfitIdentifier - EditorID formats

    [Fact]
    public void ExtractSpidOutfitIdentifier_PlainEditorId_ReturnsEditorId()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier("1_Obi_Druchii");
        result.Should().Be("1_Obi_Druchii");
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_EditorIdWithFilters_ReturnsOnlyEditorId()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier(
            "1_Obi_Druchii|ActorTypeNPC|VampireFaction|NONE|F|NONE|5");
        result.Should().Be("1_Obi_Druchii");
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_EditorIdWithSingleFilter_ReturnsOnlyEditorId()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier("SomeOutfit|NONE");
        result.Should().Be("SomeOutfit");
    }

    [Theory]
    [InlineData("OutfitVampire", "OutfitVampire")]
    [InlineData("1_MyCustomOutfit", "1_MyCustomOutfit")]
    [InlineData("Armor_Nordic_Set", "Armor_Nordic_Set")]
    public void ExtractSpidOutfitIdentifier_VariousEditorIds_ReturnsCorrectly(string input, string expected)
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier(input);
        result.Should().Be(expected);
    }

    #endregion

    #region SPID - ExtractSpidOutfitIdentifier - FormKey with tilde

    [Fact]
    public void ExtractSpidOutfitIdentifier_TildeFormKey_ReturnsFullFormKey()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier("0x12345~MyMod.esp");
        result.Should().Be("0x12345~MyMod.esp");
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_TildeFormKeyWithFilters_ReturnsOnlyFormKey()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier(
            "0x800~RequiredMod.esp|ActorTypeNPC|SomeFaction");
        result.Should().Be("0x800~RequiredMod.esp");
    }

    [Theory]
    [InlineData("0xABC~Plugin.esm", "0xABC~Plugin.esm")]
    [InlineData("0x00012345~Skyrim.esm", "0x00012345~Skyrim.esm")]
    [InlineData("123456~MyMod.esl", "123456~MyMod.esl")]
    public void ExtractSpidOutfitIdentifier_TildeFormats_ReturnsCorrectFormKey(string input, string expected)
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_TildeFormKeyEsm_ReturnsFullFormKey()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier("0x800~Skyrim.esm|NPC");
        result.Should().Be("0x800~Skyrim.esm");
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_TildeFormKeyEsl_ReturnsFullFormKey()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier("0x800~LightPlugin.esl|NPC");
        result.Should().Be("0x800~LightPlugin.esl");
    }

    #endregion

    #region SPID - ExtractSpidOutfitIdentifier - FormKey with pipe

    [Fact]
    public void ExtractSpidOutfitIdentifier_PipeFormKey_ReturnsFullFormKey()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier("MyMod.esp|0x12345");
        result.Should().Be("MyMod.esp|0x12345");
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_PipeFormKeyWithFilters_ReturnsOnlyFormKey()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier(
            "MyMod.esp|0x800|ActorTypeNPC|SomeFaction");
        result.Should().Be("MyMod.esp|0x800");
    }

    [Theory]
    [InlineData("Skyrim.esm|0xABCDE", "Skyrim.esm|0xABCDE")]
    [InlineData("DLC.esm|12345", "DLC.esm|12345")]
    [InlineData("Light.esl|0x1", "Light.esl|0x1")]
    public void ExtractSpidOutfitIdentifier_PipeFormats_ReturnsCorrectFormKey(string input, string expected)
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier(input);
        result.Should().Be(expected);
    }

    #endregion

    #region SPID - ExtractSpidOutfitKeys - Full line parsing

    [Fact]
    public void ExtractSpidOutfitKeys_EditorIdWithFilters_ExtractsEditorId()
    {
        var result = DistributionScannerService.ExtractSpidOutfitKeys(
            "Outfit = 1_Obi_Druchii|ActorTypeNPC|VampireFaction|NONE|F|NONE|5");

        result.Should().ContainSingle().Which.Should().Be("1_Obi_Druchii");
    }

    [Fact]
    public void ExtractSpidOutfitKeys_TildeFormKey_ExtractsFormKey()
    {
        var result = DistributionScannerService.ExtractSpidOutfitKeys(
            "Outfit = 0x800~RequiredMod.esp|NpcEditorId");

        result.Should().ContainSingle().Which.Should().Be("0x800~RequiredMod.esp");
    }

    [Fact]
    public void ExtractSpidOutfitKeys_MultipleOutfits_ExtractsAll()
    {
        var result = DistributionScannerService.ExtractSpidOutfitKeys(
            "Outfit = OutfitA|NONE, OutfitB|NONE, OutfitC");

        result.Should().HaveCount(3)
            .And.ContainInOrder("OutfitA", "OutfitB", "OutfitC");
    }

    [Fact]
    public void ExtractSpidOutfitKeys_PlainEditorId_ExtractsEditorId()
    {
        var result = DistributionScannerService.ExtractSpidOutfitKeys("Outfit = VampireOutfit");

        result.Should().ContainSingle().Which.Should().Be("VampireOutfit");
    }

    [Fact]
    public void ExtractSpidOutfitKeys_EmptyValue_ReturnsEmpty()
    {
        var result = DistributionScannerService.ExtractSpidOutfitKeys("Outfit = ");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSpidOutfitKeys_NoEquals_ReturnsEmpty()
    {
        var result = DistributionScannerService.ExtractSpidOutfitKeys("Outfit");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSpidOutfitKeys_WithInlineComment_IgnoresComment()
    {
        var result = DistributionScannerService.ExtractSpidOutfitKeys(
            "Outfit = SomeOutfit|NONE ; This is a comment");

        result.Should().ContainSingle().Which.Should().Be("SomeOutfit");
    }

    #endregion

    #region SPID - Edge cases

    [Fact]
    public void ExtractSpidOutfitIdentifier_WhitespaceOnly_ReturnsEmpty()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier("   ");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_Null_ReturnsEmpty()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier(null!);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_CommentOnly_ReturnsEmpty()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier("; comment");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSpidOutfitKeys_CaseVariations_HandlesCorrectly()
    {
        var result = DistributionScannerService.ExtractSpidOutfitKeys("OUTFIT = MyOutfit|NONE");

        result.Should().ContainSingle().Which.Should().Be("MyOutfit");
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_ModKeyWithNumbersAndUnderscores_Works()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier(
            "0x800~My_Cool_Mod_2024.esp|Filter");
        result.Should().Be("0x800~My_Cool_Mod_2024.esp");
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_ComplexEditorId_Works()
    {
        var result = DistributionScannerService.ExtractSpidOutfitIdentifier(
            "1_Requiem_Outfit_Vampire_Noble_Female|ActorTypeNPC");
        result.Should().Be("1_Requiem_Outfit_Vampire_Noble_Female");
    }

    #endregion

    #region SkyPatcher - ExtractSkyPatcherOutfitKeys - Main extraction

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithOutfitDefault_ExtractsFormKey()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|0x800");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithOutfitSleep_ReturnsEmpty()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "filterByNpcs=Skyrim.esm|0x1234:outfitSleep=MyMod.esp|0x900");

        result.Should().BeEmpty("outfitSleep is not currently extracted");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithBothDefaultAndFilterByOutfits_ExtractsBoth()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "filterByOutfits=MyMod.esp|0x800:outfitDefault=MyMod.esp|0x900");

        result.Should().HaveCount(2)
            .And.Contain(["MyMod.esp|0x800", "MyMod.esp|0x900"]);
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_NoOutfitFilters_ReturnsEmpty()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "filterByNpcs=Skyrim.esm|0x1234:formsToAdd=MyMod.esp|0x800");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_EmptyLine_ReturnsEmpty()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_NullLine_ThrowsException()
    {
        var act = () => DistributionScannerService.ExtractSkyPatcherOutfitKeys(null!);
        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_Comment_StillExtractsFormKey()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "; outfitDefault=MyMod.esp|0x800");

        result.Should().ContainSingle("comment filtering is done at call site, not in extraction");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_CaseInsensitive_ExtractsFormKey()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "OUTFITDEFAULT=MyMod.esp|0x800");

        result.Should().ContainSingle();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithHexPrefix_ExtractsFormKey()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=MyMod.esp|0x800");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|0x800");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithoutHexPrefix_ExtractsFormKey()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=MyMod.esp|800");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|800");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FormKeyWithEsm_ExtractsFormKey()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=Skyrim.esm|0xABCDE");

        result.Should().ContainSingle().Which.Should().Be("Skyrim.esm|0xABCDE");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FormKeyWithEsl_ExtractsFormKey()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=LightMod.esl|0x800");

        result.Should().ContainSingle().Which.Should().Be("LightMod.esl|0x800");
    }

    #endregion

    #region SkyPatcher - Edge cases

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithSpaces_ExtractsFormKey()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault= MyMod.esp|0x800 ");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|0x800");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_ComplexLine_ExtractsFormKey()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "filterByFactions=Skyrim.esm|0xFDEAC:filterByGender=female:outfitDefault=MyMod.esp|0xFE000D65");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|0xFE000D65");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_MultipleNpcFilters_ExtractsFormKey()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "filterByNpcs=Skyrim.esm|0x100,Skyrim.esm|0x200:outfitDefault=MyMod.esp|0x800");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|0x800");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_LongFormId_ExtractsFormKey()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=MyMod.esp|00ABCDEF");

        result.Should().ContainSingle().Which.Should().Be("MyMod.esp|00ABCDEF");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_WithSpacesInModName_ExtractsFormKey()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=My Cool Mod.esp|0x800");

        result.Should().ContainSingle().Which.Should().Be("My Cool Mod.esp|0x800");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_SleepOutfit_ReturnsEmpty()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "filterByNpcs=Skyrim.esm|0x13BBF:outfitSleep=Skyrim.esm|0xD3E06");

        result.Should().BeEmpty("outfitSleep is not currently extracted");
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_OnlyOutfitDefault_ExtractsSingle()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "outfitDefault=Skyrim.esm|0xD3E05");

        result.Should().ContainSingle().Which.Should().Be("Skyrim.esm|0xD3E05");
    }

    #endregion

    #region SkyPatcher - Non-outfit operations should be ignored

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FormsToAdd_ReturnsEmpty()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "formsToAdd=Skyrim.esm|0x59A71");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FormsToRemove_ReturnsEmpty()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "formsToRemove=Skyrim.esm|0x59A71");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FormsToReplace_ReturnsEmpty()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "formsToReplace=Skyrim.esm|0x59A71=Skyrim.esm|0x59A72");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_Clear_ReturnsEmpty()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "clear=true");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSkyPatcherOutfitKeys_FilterByOutfits_ExtractsOutfitKey()
    {
        var result = DistributionScannerService.ExtractSkyPatcherOutfitKeys(
            "filterByOutfits=Skyrim.esm|0x246EE7:formsToAdd=Skyrim.esm|0x59A71");

        result.Should().ContainSingle().Which.Should().Be("Skyrim.esm|0x246EE7");
    }

    #endregion
}
