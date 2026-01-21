using Boutique.Utilities;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Round-trip tests for SPID distribution line parsing and formatting.
///     Ensures that parsing a line and formatting it back produces equivalent output.
/// </summary>
public class SpidRoundTripTests
{
    #region Chance (Position 7)

    [Theory]
    [InlineData("Outfit = RareOutfit|NONE|NONE|NONE|NONE|NONE|5")]
    [InlineData("Outfit = RareOutfit|NONE|NONE|NONE|NONE|NONE|50")]
    [InlineData("Outfit = RareOutfit|NONE|NONE|NONE|NONE|NONE|1")]
    public void RoundTrip_Chance_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    #endregion

    #region Bulk File Round-Trip Tests

    [Fact]
    public void RoundTrip_MultipleLines_AllPreserved()
    {
        var lines = new[]
        {
            "Outfit = VampireOutfit|Serana", "Outfit = BanditOutfit|ActorTypeNPC+Bandit",
            "Outfit = GuardOutfit|*Guard+-Stormcloak", "Outfit = 0x800~MyMod.esp|NONE|VampireFaction|NONE|F|NONE|5",
            "Keyword = MyKeyword|ActorTypeNPC|NordRace"
        };

        foreach (var line in lines)
        {
            var parsed = SpidLineParser.TryParse(line, out var filter);
            Assert.True(parsed, $"Failed to parse: {line}");
            Assert.NotNull(filter);

            var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
            Assert.Equal(line, formatted);
        }
    }

    #endregion

    #region Simple Outfit Lines

    [Theory]
    [InlineData("Outfit = VampireOutfit")]
    [InlineData("Outfit = DefaultOutfit")]
    [InlineData("Outfit = FarmClothesOutfit01")]
    public void RoundTrip_SimpleEditorId_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = 0x800~MyMod.esp")]
    [InlineData("Outfit = 0x12345~Plugin.esp")]
    [InlineData("Outfit = 0xFE000D65~Obi_Armor.esp")]
    public void RoundTrip_TildeFormKey_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = MyMod.esp|0x800")]
    [InlineData("Outfit = Plugin.esp|0x12345")]
    [InlineData("Outfit = Skyrim.esm|0x000D3E05")]
    public void RoundTrip_PipeFormKey_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    #endregion

    #region String Filters (Position 2)

    [Theory]
    [InlineData("Outfit = VampireOutfit|Serana")]
    [InlineData("Outfit = VampireOutfit|Harkon")]
    [InlineData("Outfit = GuardOutfit|Balgruuf")]
    public void RoundTrip_SingleNpcName_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = VampireOutfit|Serana,Harkon,Valerica")]
    [InlineData("Outfit = GuardOutfit|Balgruuf,Ulfric,Elisif")]
    public void RoundTrip_MultipleNpcNames_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = BanditOutfit|ActorTypeNPC")]
    [InlineData("Outfit = VampireOutfit|VampireKeyword")]
    public void RoundTrip_SingleKeyword_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = BanditOutfit|ActorTypeNPC+Bandit")]
    [InlineData("Outfit = GuardOutfit|ActorTypeNPC+Guard+Warrior")]
    public void RoundTrip_AndCombinedKeywords_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = GuardOutfit|*Guard")]
    [InlineData("Outfit = GuardOutfit|*Guard*")]
    [InlineData("Outfit = GuardOutfit|Guard*")]
    public void RoundTrip_WildcardFilters_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = GuardOutfit|*Guard+-Stormcloak")]
    [InlineData("Outfit = BanditOutfit|Bandit+-Chief")]
    public void RoundTrip_NegatedFilters_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    #endregion

    #region Form Filters (Position 3)

    [Theory]
    [InlineData("Outfit = VampireOutfit|NONE|VampireFaction")]
    [InlineData("Outfit = GuardOutfit|NONE|CrimeFactionWhiterun")]
    public void RoundTrip_SingleFaction_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = NordOutfit|NONE|NordRace")]
    [InlineData("Outfit = ElfOutfit|NONE|HighElfRace")]
    public void RoundTrip_SingleRace_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = MageOutfit|NONE|NordRace+MageFaction")]
    [InlineData("Outfit = WarriorOutfit|NONE|NordRace+WarriorClass")]
    public void RoundTrip_CombinedFormFilters_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    #endregion

    #region Level Filters (Position 4)

    [Theory]
    [InlineData("Outfit = EliteOutfit|NONE|NONE|5")]
    [InlineData("Outfit = EliteOutfit|NONE|NONE|10")]
    public void RoundTrip_MinLevel_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = EliteOutfit|NONE|NONE|5/20")]
    [InlineData("Outfit = EliteOutfit|NONE|NONE|10/30")]
    public void RoundTrip_LevelRange_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = MageOutfit|NONE|NONE|14(50/50)")]
    [InlineData("Outfit = MageOutfit|NONE|NONE|12(85/999)")]
    public void RoundTrip_SkillFilter_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    #endregion

    #region Trait Filters (Position 5)

    [Theory]
    [InlineData("Outfit = FemaleOutfit|NONE|NONE|NONE|F")]
    [InlineData("Outfit = MaleOutfit|NONE|NONE|NONE|M")]
    public void RoundTrip_GenderFilter_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = UniqueOutfit|NONE|NONE|NONE|U")]
    [InlineData("Outfit = GenericOutfit|NONE|NONE|NONE|-U")]
    public void RoundTrip_UniqueFilter_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = FemaleOutfit|NONE|NONE|NONE|F/-U")]
    [InlineData("Outfit = FemaleOutfit|NONE|NONE|NONE|F/-U/-C")]
    [InlineData("Outfit = AllTraits|NONE|NONE|NONE|F/U/S/C/L/T/D")]
    public void RoundTrip_MultipleTraits_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    #endregion

    #region Complex Combined Lines

    [Theory]
    [InlineData("Outfit = 1_Obi_Druchii|ActorTypeNPC|VampireFaction|NONE|F|NONE|5")]
    public void RoundTrip_ComplexLine_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = VampireOutfit|Serana,Harkon|VampireFaction|5/50|F/-U|NONE|25")]
    public void RoundTrip_AllPositionsFilled_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = 0x800~MyMod.esp|ActorTypeNPC+Bandit+-Chief|NordRace+BanditFaction|10/50|M/-U/-C|NONE|75")]
    public void RoundTrip_VeryComplexLine_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    #endregion

    #region Other Form Types

    [Theory]
    [InlineData("Keyword = MyKeyword|ActorTypeNPC")]
    [InlineData("Keyword = VampireKeyword|NONE|VampireFaction")]
    public void RoundTrip_KeywordType_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Spell = FireballSpell|ActorTypeNPC")]
    [InlineData("Perk = ExtraDamage|NONE|WarriorFaction")]
    [InlineData("Item = SwordOfDestruction|NONE|NONE|50")]
    public void RoundTrip_OtherFormTypes_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void RoundTrip_DefaultChance100_NotIncludedInOutput()
    {
        var input = "Outfit = VampireOutfit";
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);
        Assert.Equal(100, filter.Chance);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
        Assert.DoesNotContain("|100", formatted);
    }

    [Fact]
    public void RoundTrip_EmptyTraitFilters_NotIncludedInOutput()
    {
        var input = "Outfit = VampireOutfit|Serana";
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);
        Assert.True(filter.TraitFilters.IsEmpty);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = VampireOutfit|NONE|VampireFaction")]
    [InlineData("Outfit = VampireOutfit|NONE|NONE|5")]
    [InlineData("Outfit = VampireOutfit|NONE|NONE|NONE|F")]
    public void RoundTrip_IntermediateNones_PreservedCorrectly(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    #endregion

    #region Wildcard and Exclusion Cases

    [Theory]
    [InlineData("Keyword = MAGECORE_isMage|*Conjurer,*Cryomancer,*Mage")]
    [InlineData("Keyword = TestKeyword|*Guard,*Soldier,*Warrior")]
    public void RoundTrip_WildcardOrFilters_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData(
        "Keyword = MAGECORE_isGroupB|MAGECORE_isMage+MAGECORE_isFemale,-MAGECORE_isGroupA|NONE|NONE|NONE|NONE|50")]
    [InlineData(
        "Keyword = MAGECORE_isGroupC|MAGECORE_isMage+MAGECORE_isFemale,-MAGECORE_isGroupA,-MAGECORE_isGroupB|NONE|NONE|NONE|NONE|25")]
    public void RoundTrip_GlobalExclusions_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Theory]
    [InlineData("Outfit = TestOutfit|KeywordA+KeywordB,-ExcludeC")]
    [InlineData("Outfit = TestOutfit|KeywordA,-ExcludeB,-ExcludeC")]
    [InlineData("Outfit = TestOutfit|-OnlyExclude")]
    public void RoundTrip_MixedPositiveAndNegative_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed);
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    #endregion

    #region Magecore Test Data Lines

    [Theory]
    [InlineData(
        "Keyword = MAGECORE_isMage|*Conjurer,*Cryomancer,*Electromancer,*Mage,*Necro,*Pyromancer,*Wizard,*Warlock,*Sorcerer")]
    [InlineData("Keyword = MAGECORE_isFemale|MAGECORE_isMage|NONE|NONE|F/-U/-C")]
    [InlineData("Keyword = MAGECORE_isGroupA|MAGECORE_isMage+MAGECORE_isFemale|NONE|NONE|NONE|NONE|33")]
    [InlineData(
        "Keyword = MAGECORE_isGroupB|MAGECORE_isMage+MAGECORE_isFemale,-MAGECORE_isGroupA|NONE|NONE|NONE|NONE|50")]
    [InlineData("Keyword = MAGECORE_isGroupC|MAGECORE_isMage+MAGECORE_isFemale,-MAGECORE_isGroupA,-MAGECORE_isGroupB")]
    public void RoundTrip_MagecoreKeywordLines_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed, $"Failed to parse: {input}");
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    [Fact]
    public void Parse_MagecoreWildcards_ParsesAllExpressions()
    {
        var input =
            "Keyword = MAGECORE_isMage|*Conjurer,*Cryomancer,*Electromancer,*Mage,*Necro,*Pyromancer,*Wizard,*Warlock,*Sorcerer";
        var parsed = SpidLineParser.TryParse(input, out var filter);

        Assert.True(parsed);
        Assert.NotNull(filter);
        Assert.Equal("MAGECORE_isMage", filter.FormIdentifier);
        Assert.Equal(9, filter.StringFilters.Expressions.Count);

        var values = filter.StringFilters.Expressions
            .SelectMany(e => e.Parts)
            .Select(p => p.Value)
            .ToList();
        Assert.Contains("*Conjurer", values);
        Assert.Contains("*Cryomancer", values);
        Assert.Contains("*Sorcerer", values);
    }

    [Fact]
    public void Parse_MagecoreGlobalExclusions_ParsesCorrectly()
    {
        var input =
            "Keyword = MAGECORE_isGroupC|MAGECORE_isMage+MAGECORE_isFemale,-MAGECORE_isGroupA,-MAGECORE_isGroupB|NONE|NONE|NONE|NONE|100";
        var parsed = SpidLineParser.TryParse(input, out var filter);

        Assert.True(parsed);
        Assert.NotNull(filter);
        Assert.Equal("MAGECORE_isGroupC", filter.FormIdentifier);
        Assert.Single(filter.StringFilters.Expressions);
        Assert.Equal(2, filter.StringFilters.Expressions[0].Parts.Count);
        Assert.Equal("MAGECORE_isMage", filter.StringFilters.Expressions[0].Parts[0].Value);
        Assert.Equal("MAGECORE_isFemale", filter.StringFilters.Expressions[0].Parts[1].Value);
        Assert.Equal(2, filter.StringFilters.GlobalExclusions.Count);
        Assert.Equal("MAGECORE_isGroupA", filter.StringFilters.GlobalExclusions[0].Value);
        Assert.Equal("MAGECORE_isGroupB", filter.StringFilters.GlobalExclusions[1].Value);
        Assert.Equal(100, filter.Chance);
    }

    [Theory]
    [InlineData(
        "Outfit = MAGECOREMasterResearcherMagickaOutfit|MAGECORE_isFemale+MAGECORE_isMage+MAGECORE_isMasterLevel+MAGECORE_isGroupA,-MAGECORE_hasMasterSkill")]
    [InlineData(
        "Outfit = MAGECOREExpertResearcherAlterationOutfit|MAGECORE_isFemale+MAGECORE_isMage+MAGECORE_isExpertAlteration+MAGECORE_isGroupA,-MAGECORE_reachMasterLevel,-MAGECORE_hasMasterSkill")]
    public void RoundTrip_MagecoreOutfitLines_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        Assert.True(parsed, $"Failed to parse: {input}");
        Assert.NotNull(filter);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        Assert.Equal(input, formatted);
    }

    #endregion
}
