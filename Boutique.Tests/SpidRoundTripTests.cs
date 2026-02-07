using Boutique.Utilities;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Round-trip tests for SPID distribution line parsing and formatting.
///     These tests focus on edge cases and formatting behaviors not covered
///     by file-based tests in SpidFullFlowTests.
///     See also: SpidSemanticTests for semantic verification,
///               SpidFullFlowTests for file-based round-trip testing.
/// </summary>
public class SpidRoundTripTests
{
    #region Edge Cases - Formatting Behaviors

    [Fact]
    public void RoundTrip_DefaultChance100_NotIncludedInOutput()
    {
        var input = "Outfit = VampireOutfit";
        var parsed = SpidLineParser.TryParse(input, out var filter);

        parsed.Should().BeTrue();
        filter!.Chance.Should().Be(100);

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        formatted.Should().Be(input);
        formatted.Should().NotContain("|100");
    }

    [Fact]
    public void RoundTrip_EmptyTraitFilters_NotIncludedInOutput()
    {
        var input = "Outfit = VampireOutfit|Serana";
        var parsed = SpidLineParser.TryParse(input, out var filter);

        parsed.Should().BeTrue();
        filter!.TraitFilters.IsEmpty.Should().BeTrue();

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter);
        formatted.Should().Be(input);
    }

    [Theory]
    [InlineData("Outfit = VampireOutfit|NONE|VampireFaction")]
    [InlineData("Outfit = VampireOutfit|NONE|NONE|5")]
    [InlineData("Outfit = VampireOutfit|NONE|NONE|NONE|F")]
    public void RoundTrip_IntermediateNones_PreservedCorrectly(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        parsed.Should().BeTrue();

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
        formatted.Should().Be(input);
    }

    [Theory]
    [InlineData("Outfit = RareOutfit|NONE|NONE|NONE|NONE|NONE|5")]
    [InlineData("Outfit = RareOutfit|NONE|NONE|NONE|NONE|NONE|50")]
    [InlineData("Outfit = RareOutfit|NONE|NONE|NONE|NONE|NONE|1")]
    public void RoundTrip_ExplicitChance_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        parsed.Should().BeTrue();

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
        formatted.Should().Be(input);
    }

    #endregion

    #region FormKey Format Variations

    [Theory]
    [InlineData("Outfit = 0x800~MyMod.esp")]
    [InlineData("Outfit = 0xFE000D65~Obi_Armor.esp")]
    public void RoundTrip_TildeFormKey_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        parsed.Should().BeTrue();

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
        formatted.Should().Be(input);
    }

    [Theory]
    [InlineData("Outfit = MyMod.esp|0x800")]
    [InlineData("Outfit = Skyrim.esm|0x000D3E05")]
    public void RoundTrip_PipeFormKey_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        parsed.Should().BeTrue();

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
        formatted.Should().Be(input);
    }

    [Theory]
    [InlineData("Outfit = 0x800~MyMod.esp|ActorTypeNPC|VampireFaction")]
    [InlineData("Outfit = MyMod.esp|0x800|ActorTypeNPC|VampireFaction")]
    public void RoundTrip_FormKeyWithFilters_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        parsed.Should().BeTrue();

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
        formatted.Should().Be(input);
    }

    #endregion

    #region All Form Types

    [Theory]
    [InlineData("Outfit = TestOutfit")]
    [InlineData("Keyword = TestKeyword")]
    [InlineData("ExclusiveGroup = GroupName|A,B,-C")]
    [InlineData("Spell = TestSpell")]
    [InlineData("Perk = TestPerk")]
    [InlineData("Item = TestItem")]
    [InlineData("Shout = TestShout")]
    [InlineData("Package = TestPackage")]
    [InlineData("Faction = TestFaction")]
    [InlineData("SleepOutfit = TestSleepOutfit")]
    [InlineData("Skin = TestSkin")]
    public void RoundTrip_AllFormTypes_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        parsed.Should().BeTrue();

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
        formatted.Should().Be(input);
    }

    #endregion

    #region Complex Combined Lines

    [Fact]
    public void RoundTrip_AllPositionsFilled_PreservesLine()
    {
        var input = "Outfit = VampireOutfit|Serana,Harkon|VampireFaction|5/50|F/-U|NONE|25";
        var parsed = SpidLineParser.TryParse(input, out var filter);

        parsed.Should().BeTrue();
        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
        formatted.Should().Be(input);
    }

    [Fact]
    public void RoundTrip_VeryComplexLine_PreservesLine()
    {
        var input = "Outfit = 0x800~MyMod.esp|ActorTypeNPC+Bandit+-Chief|NordRace+BanditFaction|10/50|M/-U/-C|NONE|75";
        var parsed = SpidLineParser.TryParse(input, out var filter);

        parsed.Should().BeTrue();
        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
        formatted.Should().Be(input);
    }

    #endregion

    #region Global Exclusion Formatting

    [Theory]
    [InlineData("Outfit = TestOutfit|KeywordA+KeywordB,-ExcludeC")]
    [InlineData("Outfit = TestOutfit|KeywordA,-ExcludeB,-ExcludeC")]
    [InlineData("Outfit = TestOutfit|-OnlyExclude")]
    public void RoundTrip_GlobalExclusions_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        parsed.Should().BeTrue();

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
        formatted.Should().Be(input);
    }

    #endregion

    #region Trait Filter Formatting

    [Theory]
    [InlineData("Outfit = FemaleOutfit|NONE|NONE|NONE|F")]
    [InlineData("Outfit = MaleOutfit|NONE|NONE|NONE|M")]
    [InlineData("Outfit = UniqueOutfit|NONE|NONE|NONE|U")]
    [InlineData("Outfit = GenericOutfit|NONE|NONE|NONE|-U")]
    [InlineData("Outfit = FemaleOutfit|NONE|NONE|NONE|F/-U")]
    [InlineData("Outfit = FemaleOutfit|NONE|NONE|NONE|F/-U/-C")]
    [InlineData("Outfit = AllTraits|NONE|NONE|NONE|F/U/S/C/L/T/D")]
    public void RoundTrip_TraitFilters_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        parsed.Should().BeTrue();

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
        formatted.Should().Be(input);
    }

    #endregion

    #region Level Filter Formatting

    [Theory]
    [InlineData("Outfit = EliteOutfit|NONE|NONE|5")]
    [InlineData("Outfit = EliteOutfit|NONE|NONE|5/20")]
    [InlineData("Outfit = MageOutfit|NONE|NONE|14(50/50)")]
    [InlineData("Outfit = MageOutfit|NONE|NONE|12(85/999)")]
    public void RoundTrip_LevelFilters_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        parsed.Should().BeTrue();

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
        formatted.Should().Be(input);
    }

    #endregion

    #region Wildcard Formatting

    [Theory]
    [InlineData("Outfit = GuardOutfit|*Guard")]
    [InlineData("Outfit = GuardOutfit|*Guard*")]
    [InlineData("Outfit = GuardOutfit|Guard*")]
    [InlineData("Outfit = GuardOutfit|*Guard+-Stormcloak")]
    public void RoundTrip_Wildcards_PreservesLine(string input)
    {
        var parsed = SpidLineParser.TryParse(input, out var filter);
        parsed.Should().BeTrue();

        var formatted = DistributionFileFormatter.FormatSpidDistributionFilter(filter!);
        formatted.Should().Be(input);
    }

    #endregion
}
