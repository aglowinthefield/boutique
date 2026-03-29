using Boutique.Models;
using Boutique.Utilities;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

public class SpidLineParserTests
{
    #region Basic Parsing

    [Fact]
    public void TryParse_SimpleEditorId_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = VampireOutfit", out var filter);

        result.Should().BeTrue();
        filter!.OutfitIdentifier.Should().Be("VampireOutfit");
        filter.StringFilters.IsEmpty.Should().BeTrue();
        filter.FormFilters.IsEmpty.Should().BeTrue();
        filter.Chance.Should().Be(100);
    }

    [Fact]
    public void TryParseOutfit_NotOutfitLine_ReturnsFalse()
    {
        var result = SpidLineParser.TryParseOutfit("Spell = 0x800~MyMod.esp", out var filter);

        result.Should().BeFalse();
        filter.Should().BeNull();
    }

    [Fact]
    public void TryParse_SpellLine_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Spell = 0x800~MyMod.esp", out var filter);

        result.Should().BeTrue();
        filter!.FormType.Should().Be(SpidFormType.Spell);
        filter.FormIdentifier.Should().Be("0x800~MyMod.esp");
    }

    [Fact]
    public void TryParse_ExclusiveGroupLine_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse(
            "ExclusiveGroup = Only One Sword|IronSword,SteelSword,DaedricSword",
            out var filter);

        result.Should().BeTrue();
        filter!.FormType.Should().Be(SpidFormType.ExclusiveGroup);
        filter.FormIdentifier.Should().Be("Only One Sword");
        filter.ExclusiveGroupForms.Should().ContainInOrder("IronSword", "SteelSword", "DaedricSword");
    }

    [Fact]
    public void TryParseExclusiveGroup_MissingForms_ReturnsFalse()
    {
        var result = SpidLineParser.TryParseExclusiveGroup("ExclusiveGroup = EmptyGroup", out var filter);

        result.Should().BeFalse();
        filter.Should().BeNull();
    }

    [Fact]
    public void TryParse_EmptyLine_ReturnsFalse()
    {
        var result = SpidLineParser.TryParse("", out var filter);

        result.Should().BeFalse();
        filter.Should().BeNull();
    }

    [Fact]
    public void TryParse_Comment_ReturnsFalse()
    {
        var result = SpidLineParser.TryParse("; Outfit = Something", out var filter);

        result.Should().BeFalse();
        filter.Should().BeNull();
    }

    #endregion

    #region FormKey Format Parsing

    [Theory]
    [InlineData("Outfit = 0x800~MyMod.esp", "0x800~MyMod.esp")]
    [InlineData("Outfit = MyMod.esp|0x800", "MyMod.esp|0x800")]
    [InlineData("Outfit = 0xFE000D65~Plugin.esl", "0xFE000D65~Plugin.esl")]
    [InlineData("Outfit = Skyrim.esm|0x000D3E05", "Skyrim.esm|0x000D3E05")]
    public void TryParse_FormKeyFormats_ExtractsCorrectIdentifier(string line, string expectedIdentifier)
    {
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.FormIdentifier.Should().Be(expectedIdentifier);
    }

    [Theory]
    [InlineData("Outfit = 0x800~MyMod.esp|ActorTypeNPC", "0x800~MyMod.esp", "ActorTypeNPC")]
    [InlineData("Outfit = MyMod.esp|0x800|ActorTypeNPC|VampireFaction", "MyMod.esp|0x800", "ActorTypeNPC")]
    public void TryParse_FormKeyWithFilters_SeparatesCorrectly(
        string line,
        string expectedIdentifier,
        string expectedFirstFilter)
    {
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.FormIdentifier.Should().Be(expectedIdentifier);
        filter.StringFilters.Expressions.Should().ContainSingle()
            .Which.Parts[0].Value.Should().Be(expectedFirstFilter);
    }

    #endregion

    #region String Filters (Position 2)

    [Fact]
    public void TryParse_WithNpcName_ParsesStringFilters()
    {
        var result = SpidLineParser.TryParse("Outfit = VampireOutfit|Serana", out var filter);

        result.Should().BeTrue();
        filter!.OutfitIdentifier.Should().Be("VampireOutfit");
        filter.StringFilters.Expressions.Should().ContainSingle()
            .Which.Parts.Should().ContainSingle()
            .Which.Value.Should().Be("Serana");
    }

    [Fact]
    public void TryParse_WithKeyword_ParsesStringFilters()
    {
        var result = SpidLineParser.TryParse("Outfit = VampireOutfit|ActorTypeNPC", out var filter);

        result.Should().BeTrue();
        filter!.StringFilters.Expressions.Should().ContainSingle()
            .Which.Parts[0].Value.Should().Be("ActorTypeNPC");
    }

    [Fact]
    public void TryParse_WithMultipleOrFilters_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = VampireOutfit|Serana,Harkon,Valerica", out var filter);

        result.Should().BeTrue();
        filter!.StringFilters.Expressions.Should().HaveCount(3)
            .And.SatisfyRespectively(
                e => e.Parts[0].Value.Should().Be("Serana"),
                e => e.Parts[0].Value.Should().Be("Harkon"),
                e => e.Parts[0].Value.Should().Be("Valerica"));
    }

    [Fact]
    public void TryParse_WithAndFilters_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = BanditOutfit|ActorTypeNPC+Bandit", out var filter);

        result.Should().BeTrue();
        filter!.StringFilters.Expressions.Should().ContainSingle()
            .Which.Parts.Should().HaveCount(2)
            .And.SatisfyRespectively(
                p => p.Value.Should().Be("ActorTypeNPC"),
                p => p.Value.Should().Be("Bandit"));
    }

    [Fact]
    public void TryParse_WithNegatedFilter_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = GuardOutfit|*Guard+-Stormcloak", out var filter);

        result.Should().BeTrue();
        var parts = filter!.StringFilters.Expressions[0].Parts;
        parts.Should().HaveCount(2);
        parts[0].Value.Should().Be("*Guard");
        parts[0].IsNegated.Should().BeFalse();
        parts[1].Value.Should().Be("Stormcloak");
        parts[1].IsNegated.Should().BeTrue();
    }

    [Fact]
    public void TryParse_WithWildcard_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = GuardOutfit|*Guard*", out var filter);

        result.Should().BeTrue();
        filter!.StringFilters.Expressions[0].Parts[0].HasWildcard.Should().BeTrue();
    }

    #endregion

    #region Global Exclusions

    [Theory]
    [InlineData("Outfit = Test|-Exclude1", 0, 1, "Exclude1")]
    [InlineData("Outfit = Test|Filter1,-Exclude1", 1, 1, "Exclude1")]
    [InlineData("Outfit = Test|Filter1,-Exclude1,-Exclude2", 1, 2, "Exclude1")]
    [InlineData("Outfit = Test|Filter1+Filter2,-Exclude1,-Exclude2,-Exclude3", 1, 3, "Exclude1")]
    public void TryParse_GlobalExclusions_CountsCorrectly(
        string line,
        int expectedExpressions,
        int expectedExclusions,
        string firstExclusionValue)
    {
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.StringFilters.Expressions.Should().HaveCount(expectedExpressions);
        filter.StringFilters.GlobalExclusions.Should().HaveCount(expectedExclusions);
        filter.StringFilters.GlobalExclusions[0].Value.Should().Be(firstExclusionValue);
        filter.StringFilters.GlobalExclusions[0].IsNegated.Should().BeTrue();
    }

    [Fact]
    public void TryParse_OnlyGlobalExclusions_NoPositiveExpressions()
    {
        var line = "Outfit = TestOutfit|-OnlyExclude";
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.StringFilters.Expressions.Should().BeEmpty();
        filter.StringFilters.GlobalExclusions.Should().ContainSingle()
            .Which.Value.Should().Be("OnlyExclude");
    }

    #endregion

    #region AND/OR Expressions

    [Theory]
    [InlineData("Outfit = Test|A,B,C", 3, new[] { 1, 1, 1 })]
    [InlineData("Outfit = Test|A+B,C", 2, new[] { 2, 1 })]
    [InlineData("Outfit = Test|A+B+C", 1, new[] { 3 })]
    [InlineData("Outfit = Test|A+B,C+D,E", 3, new[] { 2, 2, 1 })]
    public void TryParse_AndOrExpressions_CountsCorrectly(
        string line,
        int expectedExpressionCount,
        int[] expectedPartsPerExpression)
    {
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.StringFilters.Expressions.Should().HaveCount(expectedExpressionCount);

        for (var i = 0; i < expectedPartsPerExpression.Length; i++)
        {
            filter.StringFilters.Expressions[i].Parts.Should().HaveCount(expectedPartsPerExpression[i]);
        }
    }

    [Fact]
    public void TryParse_MixedAndWithNegation_ParsesCorrectly()
    {
        var line = "Outfit = Test|ActorTypeNPC+*Bandit+-Chief";
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        var parts = filter!.StringFilters.Expressions.Should().ContainSingle()
            .Which.Parts;

        parts.Should().HaveCount(3)
            .And.SatisfyRespectively(
                p => { p.Value.Should().Be("ActorTypeNPC"); p.IsNegated.Should().BeFalse(); },
                p => { p.Value.Should().Be("*Bandit"); p.IsNegated.Should().BeFalse(); },
                p => { p.Value.Should().Be("Chief"); p.IsNegated.Should().BeTrue(); });
    }

    #endregion

    #region Form Filters (Position 3)

    [Fact]
    public void TryParse_WithFaction_ParsesFormFilters()
    {
        var result = SpidLineParser.TryParse("Outfit = VampireOutfit|NONE|VampireFaction", out var filter);

        result.Should().BeTrue();
        filter!.StringFilters.IsEmpty.Should().BeTrue();
        filter.FormFilters.IsEmpty.Should().BeFalse();
        filter.FormFilters.Expressions[0].Parts[0].Value.Should().Be("VampireFaction");
    }

    [Fact]
    public void TryParse_WithRace_ParsesFormFilters()
    {
        var result = SpidLineParser.TryParse("Outfit = NordOutfit|NONE|NordRace", out var filter);

        result.Should().BeTrue();
        filter!.FormFilters.Expressions.Should().ContainSingle()
            .Which.Parts[0].Value.Should().Be("NordRace");
    }

    [Fact]
    public void TryParse_WithKeywordAndFaction_ParsesBothFilters()
    {
        var result = SpidLineParser.TryParse("Outfit = VampireOutfit|ActorTypeNPC|VampireFaction", out var filter);

        result.Should().BeTrue();
        filter!.StringFilters.Expressions.Should().ContainSingle();
        filter.FormFilters.Expressions.Should().ContainSingle();
    }

    [Fact]
    public void TryParse_WithMultipleFactions_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = CriminalOutfit|NONE|CrimeFactionWhiterun,CrimeFactionRiften",
            out var filter);

        result.Should().BeTrue();
        filter!.FormFilters.Expressions.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("Outfit = Test|NONE|VampireFaction", 1, 0)]
    [InlineData("Outfit = Test|NONE|VampireFaction,NordRace", 2, 0)]
    [InlineData("Outfit = Test|NONE|VampireFaction+NordRace", 1, 0)]
    [InlineData("Outfit = Test|NONE|0x48362,-0x13BB6", 1, 1)]
    public void TryParse_FormFilters_CountsCorrectly(
        string line,
        int expectedExpressions,
        int expectedExclusions)
    {
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.FormFilters.Expressions.Should().HaveCount(expectedExpressions);
        filter.FormFilters.GlobalExclusions.Should().HaveCount(expectedExclusions);
    }

    #endregion

    #region Level Filters (Position 4)

    [Fact]
    public void TryParse_WithSkillLevel_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse(
            "Keyword = MAGECORE_isMasterAlteration|MAGECORE_isMage+MAGECORE_isFemale|NONE|12(85/999)",
            out var filter);

        result.Should().BeTrue();
        filter!.LevelFilters.Should().Be("12(85/999)");
    }

    [Fact]
    public void TryParse_WithLevelRange_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse(
            "Keyword = MAGECORE_isMasterLevel|MAGECORE_isMage+MAGECORE_isFemale|NONE|40/999",
            out var filter);

        result.Should().BeTrue();
        filter!.LevelFilters.Should().Be("40/999");
    }

    #endregion

    #region Trait Filters (Position 5)

    [Theory]
    [InlineData("Outfit = Test|NONE|NONE|NONE|F", true, null, null)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|M", false, null, null)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|U", null, true, null)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|-U", null, false, null)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|C", null, null, true)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|-C", null, null, false)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|F/U", true, true, null)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|M/-U/-C", false, false, false)]
    public void TryParse_TraitFilters_ValuesCorrect(
        string line,
        bool? expectedFemale,
        bool? expectedUnique,
        bool? expectedChild)
    {
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.TraitFilters.IsFemale.Should().Be(expectedFemale);
        filter.TraitFilters.IsUnique.Should().Be(expectedUnique);
        filter.TraitFilters.IsChild.Should().Be(expectedChild);
    }

    #endregion

    #region Chance (Position 7)

    [Fact]
    public void TryParse_WithChance_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = RareOutfit|NONE|NONE|NONE|NONE|NONE|5", out var filter);

        result.Should().BeTrue();
        filter!.Chance.Should().Be(5);
    }

    [Fact]
    public void TryParse_NoChance_DefaultsTo100()
    {
        var result = SpidLineParser.TryParse("Outfit = CommonOutfit|NONE", out var filter);

        result.Should().BeTrue();
        filter!.Chance.Should().Be(100);
    }

    #endregion

    #region TargetsAllNpcs

    [Theory]
    [InlineData("Outfit = Test", true)]
    [InlineData("Outfit = Test|NONE", true)]
    [InlineData("Outfit = Test|NONE|NONE", true)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|F", true)]
    [InlineData("Outfit = Test|ActorTypeNPC", false)]
    [InlineData("Outfit = Test|NONE|VampireFaction", false)]
    [InlineData("Outfit = Test|-Exclude", false)]
    public void TryParse_TargetsAllNpcs_CorrectForFilters(string line, bool expectedTargetsAll)
    {
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.TargetsAllNpcs.Should().Be(expectedTargetsAll);
    }

    #endregion

    #region Targeting Description

    [Fact]
    public void GetTargetingDescription_AllNpcs_ReturnsAllNpcs()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit", out var filter);

        filter!.GetTargetingDescription().Should().Be("All NPCs");
    }

    [Fact]
    public void GetTargetingDescription_WithFilters_ReturnsDescription()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit|ActorTypeNPC|VampireFaction|NONE|F|NONE|5", out var filter);

        var description = filter!.GetTargetingDescription();
        description.Should().Contain("Names/Keywords");
        description.Should().Contain("Factions/Forms");
        description.Should().Contain("Female");
        description.Should().Contain("5%");
    }

    #endregion

    #region Real-World Examples

    [Fact]
    public void TryParse_RealExample_VampireDistribution()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = 1_Obi_Druchii|ActorTypeNPC|VampireFaction|NONE|F|NONE|5",
            out var filter);

        result.Should().BeTrue();
        filter!.OutfitIdentifier.Should().Be("1_Obi_Druchii");
        filter.StringFilters.Expressions.Should().ContainSingle();
        filter.FormFilters.Expressions.Should().ContainSingle();
        filter.TraitFilters.IsFemale.Should().BeTrue();
        filter.Chance.Should().Be(5);
    }

    [Fact]
    public void TryParse_RealExample_BanditDistribution()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = BanditOutfit|ActorTypeNPC+*Bandit*|BanditFaction",
            out var filter);

        result.Should().BeTrue();
        filter!.OutfitIdentifier.Should().Be("BanditOutfit");
        filter.StringFilters.Expressions.Should().ContainSingle();
        filter.FormFilters.Expressions.Should().ContainSingle();
    }

    [Fact]
    public void TryParse_RealExample_FormKeyWithFilters()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = 0x800~RequiredMod.esp|ActorTypeNPC|SomeFaction|NONE|F",
            out var filter);

        result.Should().BeTrue();
        filter!.OutfitIdentifier.Should().Be("0x800~RequiredMod.esp");
        filter.StringFilters.Expressions.Should().ContainSingle();
        filter.TraitFilters.IsFemale.Should().BeTrue();
    }

    [Fact]
    public void TryParse_RealExample_OutfitWithAndExpressionAndExclusion()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = MAGECOREMasterResearcherMagickaOutfit|MAGECORE_isFemale+MAGECORE_isMage+MAGECORE_isMasterLevel+MAGECORE_isGroupA,-MAGECORE_hasMasterSkill",
            out var filter);

        result.Should().BeTrue();
        filter!.FormType.Should().Be(SpidFormType.Outfit);
        filter.FormIdentifier.Should().Be("MAGECOREMasterResearcherMagickaOutfit");
        filter.StringFilters.Expressions.Should().ContainSingle()
            .Which.Parts.Should().HaveCount(4);
        filter.StringFilters.GlobalExclusions.Should().ContainSingle()
            .Which.Value.Should().Be("MAGECORE_hasMasterSkill");
    }

    [Fact]
    public void TryParse_WithInlineComment_IgnoresComment()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = VampireOutfit|Serana ; This is a comment",
            out var filter);

        result.Should().BeTrue();
        filter!.OutfitIdentifier.Should().Be("VampireOutfit");
        filter.StringFilters.Expressions[0].Parts[0].Value.Should().Be("Serana");
    }

    #endregion

    #region Helper Methods

    [Fact]
    public void GetSpecificNpcIdentifiers_WithoutLinkCache_ReturnsAllStringFilters()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit|Serana,ActorTypeNPC,Harkon", out var filter);

        var npcs = SpidLineParser.GetSpecificNpcIdentifiers(filter!);

        npcs.Should().HaveCount(3)
            .And.Contain(["Serana", "Harkon", "ActorTypeNPC"]);
    }

    [Fact]
    public void GetKeywordIdentifiers_WithoutLinkCache_ReturnsAllStringFilters()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit|ActorTypeNPC+VampireKeyword,Serana", out var filter);

        var keywords = SpidLineParser.GetKeywordIdentifiers(filter!);

        keywords.Should().HaveCount(3)
            .And.Contain(["ActorTypeNPC", "VampireKeyword", "Serana"]);
    }

    [Fact]
    public void GetFactionIdentifiers_WithoutLinkCache_ReturnsAllFormFilters()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit|NONE|VampireFaction,NordRace", out var filter);

        var factions = SpidLineParser.GetFactionIdentifiers(filter!);

        factions.Should().HaveCount(2)
            .And.Contain(["VampireFaction", "NordRace"]);
    }

    [Fact]
    public void GetRaceIdentifiers_WithoutLinkCache_ReturnsAllFormFilters()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit|NONE|VampireFaction,NordRace", out var filter);

        var races = SpidLineParser.GetRaceIdentifiers(filter!);

        races.Should().HaveCount(2)
            .And.Contain(["NordRace", "VampireFaction"]);
    }

    [Fact]
    public void GetClassIdentifiers_WithoutLinkCache_ReturnsAllFormFilters()
    {
        SpidLineParser.TryParse("Outfit = SoldierOutfit|NONE|CWSoldierClass,VampireFaction", out var filter);

        var classes = SpidLineParser.GetClassIdentifiers(filter!);

        classes.Should().HaveCount(2)
            .And.Contain(["CWSoldierClass", "VampireFaction"]);
    }

    [Fact]
    public void TryParse_WithClassFilter_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = 0x80A~Stormcloak Bikini Armors Combined - SPID.esp|ActorTypeNPC|CWSoldierClass|NONE|F|NONE|100",
            out var filter);

        result.Should().BeTrue();
        filter!.OutfitIdentifier.Should().Be("0x80A~Stormcloak Bikini Armors Combined - SPID.esp");
        filter.StringFilters.Expressions.Should().ContainSingle();
        filter.FormFilters.Expressions.Should().ContainSingle()
            .Which.Parts[0].Value.Should().Be("CWSoldierClass");
        filter.TraitFilters.IsFemale.Should().BeTrue();
        filter.Chance.Should().Be(100);
    }

    [Fact]
    public void TryParse_WithClassAndFaction_ParsesBothFormFilters()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = TestOutfit|NONE|CWSoldierClass+SonsFaction",
            out var filter);

        result.Should().BeTrue();
        filter!.FormFilters.Expressions[0].Parts.Should().HaveCount(2)
            .And.SatisfyRespectively(
                p => p.Value.Should().Be("CWSoldierClass"),
                p => p.Value.Should().Be("SonsFaction"));
    }

    #endregion

    #region FormFilter Parsing

    [Fact]
    public void TryParse_WithBareHexFormIdsInFormFilters_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = 0xB3E8D~Skyrim.esm|ActorTypeNPC|0x48362,-0x13BB6,-0x1A6D9|NONE|F|NONE|100",
            out var filter);

        result.Should().BeTrue();
        filter!.OutfitIdentifier.Should().Be("0xB3E8D~Skyrim.esm");
        filter.StringFilters.Expressions.Should().ContainSingle();
        filter.FormFilters.Expressions.Should().ContainSingle()
            .Which.Parts[0].Value.Should().Be("0x48362");
        filter.FormFilters.GlobalExclusions.Should().HaveCount(2)
            .And.AllSatisfy(e => e.IsNegated.Should().BeTrue())
            .And.SatisfyRespectively(
                e => e.Value.Should().Be("0x13BB6"),
                e => e.Value.Should().Be("0x1A6D9"));
        filter.TraitFilters.IsFemale.Should().BeTrue();
        filter.Chance.Should().Be(100);
    }

    [Fact]
    public void TryParse_WithSpecificNpcFormIdInFormFilters_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = 0xB3E8E~Skyrim.esm|ActorTypeNPC|0x1A6D9|NONE|F|NONE|100",
            out var filter);

        result.Should().BeTrue();
        filter!.OutfitIdentifier.Should().Be("0xB3E8E~Skyrim.esm");
        filter.FormFilters.Expressions.Should().ContainSingle()
            .Which.Parts[0].Should().Match<SpidFilterPart>(p =>
                p.Value == "0x1A6D9" && p.IsNegated == false);
    }

    #endregion
}
