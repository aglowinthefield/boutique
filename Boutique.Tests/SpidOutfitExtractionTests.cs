using Boutique.Services;
using Xunit;

namespace Boutique.Tests;

/// <summary>
/// Tests for SPID outfit identifier extraction from distribution lines.
/// </summary>
public class SpidOutfitExtractionTests
{
    #region ExtractSpidOutfitIdentifier - EditorID formats

    [Fact]
    public void ExtractSpidOutfitIdentifier_PlainEditorId_ReturnsEditorId()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier("1_Obi_Druchii");
        Assert.Equal("1_Obi_Druchii", result);
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_EditorIdWithFilters_ReturnsOnlyEditorId()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier(
            "1_Obi_Druchii|ActorTypeNPC|VampireFaction|NONE|F|NONE|5");
        Assert.Equal("1_Obi_Druchii", result);
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_EditorIdWithSingleFilter_ReturnsOnlyEditorId()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier("SomeOutfit|NONE");
        Assert.Equal("SomeOutfit", result);
    }

    [Theory]
    [InlineData("OutfitVampire", "OutfitVampire")]
    [InlineData("1_MyCustomOutfit", "1_MyCustomOutfit")]
    [InlineData("Armor_Nordic_Set", "Armor_Nordic_Set")]
    public void ExtractSpidOutfitIdentifier_VariousEditorIds_ReturnsCorrectly(string input, string expected)
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region ExtractSpidOutfitIdentifier - FormKey with tilde

    [Fact]
    public void ExtractSpidOutfitIdentifier_TildeFormKey_ReturnsFullFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier("0x12345~MyMod.esp");
        Assert.Equal("0x12345~MyMod.esp", result);
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_TildeFormKeyWithFilters_ReturnsOnlyFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier(
            "0x800~RequiredMod.esp|ActorTypeNPC|SomeFaction");
        Assert.Equal("0x800~RequiredMod.esp", result);
    }

    [Theory]
    [InlineData("0xABC~Plugin.esm", "0xABC~Plugin.esm")]
    [InlineData("0x00012345~Skyrim.esm", "0x00012345~Skyrim.esm")]
    [InlineData("123456~MyMod.esl", "123456~MyMod.esl")]
    public void ExtractSpidOutfitIdentifier_TildeFormats_ReturnsCorrectFormKey(string input, string expected)
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_TildeFormKeyEsm_ReturnsFullFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier("0x800~Skyrim.esm|NPC");
        Assert.Equal("0x800~Skyrim.esm", result);
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_TildeFormKeyEsl_ReturnsFullFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier("0x800~LightPlugin.esl|NPC");
        Assert.Equal("0x800~LightPlugin.esl", result);
    }

    #endregion

    #region ExtractSpidOutfitIdentifier - FormKey with pipe (ModKey|FormID)

    [Fact]
    public void ExtractSpidOutfitIdentifier_PipeFormKey_ReturnsFullFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier("MyMod.esp|0x12345");
        Assert.Equal("MyMod.esp|0x12345", result);
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_PipeFormKeyWithFilters_ReturnsOnlyFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier(
            "MyMod.esp|0x800|ActorTypeNPC|SomeFaction");
        Assert.Equal("MyMod.esp|0x800", result);
    }

    [Theory]
    [InlineData("Skyrim.esm|0xABCDE", "Skyrim.esm|0xABCDE")]
    [InlineData("DLC.esm|12345", "DLC.esm|12345")]
    [InlineData("Light.esl|0x1", "Light.esl|0x1")]
    public void ExtractSpidOutfitIdentifier_PipeFormats_ReturnsCorrectFormKey(string input, string expected)
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region ExtractSpidOutfitKeys - Full line parsing

    [Fact]
    public void ExtractSpidOutfitKeys_EditorIdWithFilters_ExtractsEditorId()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitKeys(
            "Outfit = 1_Obi_Druchii|ActorTypeNPC|VampireFaction|NONE|F|NONE|5");
        
        Assert.Single(result);
        Assert.Equal("1_Obi_Druchii", result[0]);
    }

    [Fact]
    public void ExtractSpidOutfitKeys_TildeFormKey_ExtractsFormKey()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitKeys(
            "Outfit = 0x800~RequiredMod.esp|NpcEditorId");
        
        Assert.Single(result);
        Assert.Equal("0x800~RequiredMod.esp", result[0]);
    }

    [Fact]
    public void ExtractSpidOutfitKeys_MultipleOutfits_ExtractsAll()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitKeys(
            "Outfit = OutfitA|NONE, OutfitB|NONE, OutfitC");
        
        Assert.Equal(3, result.Count);
        Assert.Equal("OutfitA", result[0]);
        Assert.Equal("OutfitB", result[1]);
        Assert.Equal("OutfitC", result[2]);
    }

    [Fact]
    public void ExtractSpidOutfitKeys_PlainEditorId_ExtractsEditorId()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitKeys("Outfit = VampireOutfit");
        
        Assert.Single(result);
        Assert.Equal("VampireOutfit", result[0]);
    }

    [Fact]
    public void ExtractSpidOutfitKeys_EmptyValue_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitKeys("Outfit = ");
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractSpidOutfitKeys_NoEquals_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitKeys("Outfit");
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractSpidOutfitKeys_WithInlineComment_IgnoresComment()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitKeys(
            "Outfit = SomeOutfit|NONE ; This is a comment");
        
        Assert.Single(result);
        Assert.Equal("SomeOutfit", result[0]);
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
        var result = DistributionDiscoveryService.IsModKeyFileName(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("0x12345", true)]
    [InlineData("0X12345", true)]
    [InlineData("12345", true)]
    [InlineData("ABCDEF", true)]
    [InlineData("0xABCDEF", true)]
    [InlineData("1", true)]
    [InlineData("12345678", true)]
    [InlineData("123456789", false)] // Too long
    [InlineData("GHIJK", false)] // Not hex
    [InlineData("ActorTypeNPC", false)]
    [InlineData("", false)]
    public void LooksLikeFormId_VariousInputs_ReturnsCorrectly(string input, bool expected)
    {
        var result = DistributionDiscoveryService.LooksLikeFormId(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Edge cases

    [Fact]
    public void ExtractSpidOutfitIdentifier_WhitespaceOnly_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier("   ");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_Null_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier(null!);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_CommentOnly_ReturnsEmpty()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier("; comment");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractSpidOutfitKeys_CaseVariations_HandlesCorrectly()
    {
        // SPID is case-insensitive for the Outfit keyword
        var result = DistributionDiscoveryService.ExtractSpidOutfitKeys(
            "OUTFIT = MyOutfit|NONE");
        
        Assert.Single(result);
        Assert.Equal("MyOutfit", result[0]);
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_ModKeyWithNumbersAndUnderscores_Works()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier(
            "0x800~My_Cool_Mod_2024.esp|Filter");
        Assert.Equal("0x800~My_Cool_Mod_2024.esp", result);
    }

    [Fact]
    public void ExtractSpidOutfitIdentifier_ComplexEditorId_Works()
    {
        var result = DistributionDiscoveryService.ExtractSpidOutfitIdentifier(
            "1_Requiem_Outfit_Vampire_Noble_Female|ActorTypeNPC");
        Assert.Equal("1_Requiem_Outfit_Vampire_Noble_Female", result);
    }

    #endregion
}

