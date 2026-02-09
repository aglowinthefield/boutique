using Boutique.Services;
using Boutique.Utilities;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Tests for SPID outfit identifier extraction from distribution lines.
/// </summary>
public class SpidOutfitExtractionTests
{
    #region ExtractSpidOutfitIdentifier - EditorID formats

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

    #region ExtractSpidOutfitIdentifier - FormKey with tilde

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

    #region ExtractSpidOutfitIdentifier - FormKey with pipe (ModKey|FormID)

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

    #region ExtractSpidOutfitKeys - Full line parsing

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

    #region Helper method tests

    [Theory]
    [InlineData("MyMod.esp", true)]
    [InlineData("Skyrim.esm", true)]
    [InlineData("Light.esl", true)]
    [InlineData("MYMOD.ESP", true)]
    [InlineData("MyMod", false)]
    [InlineData("esp", false)]
    [InlineData("", false)]
    [InlineData("ActorTypeNPC", false)]
    public void IsModKeyFileName_VariousInputs_ReturnsCorrectly(string input, bool expected)
    {
        var result = FormKeyHelper.IsModKeyFileName(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("0x12345", true)]
    [InlineData("0X12345", true)]
    [InlineData("12345", true)]
    [InlineData("ABCDEF", true)]
    [InlineData("0xABCDEF", true)]
    [InlineData("1", true)]
    [InlineData("12345678", true)]
    [InlineData("123456789", false)]
    [InlineData("GHIJK", false)]
    [InlineData("ActorTypeNPC", false)]
    [InlineData("", false)]
    public void LooksLikeFormId_VariousInputs_ReturnsCorrectly(string input, bool expected)
    {
        var result = FormKeyHelper.LooksLikeFormId(input);
        result.Should().Be(expected);
    }

    #endregion

    #region Edge cases

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
}
