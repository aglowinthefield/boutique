using Boutique.Models;
using Boutique.Utilities;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Semantic tests for SPID parsing that verify the parsed data model has correct values,
///     not just that round-trips preserve strings. These tests catch regressions in parsing logic.
/// </summary>
public class SpidSemanticTests
{
    private static readonly string TestDataPath = Path.Combine(
        AppContext.BaseDirectory, "TestData", "Spid");

    private static readonly string MagecoreFilePath = Path.Combine(TestDataPath, "Magecore_General_DISTR.ini");

    #region Magecore File Integration Tests

    [Fact]
    public void Parse_MagecoreFile_AllNonCommentLinesParsed()
    {
        var lines = File.ReadAllLines(MagecoreFilePath);
        var nonCommentLines = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Where(l => !l.TrimStart().StartsWith(';'))
            .ToList();

        var parsed = nonCommentLines
            .Select(l => (Line: l, Success: SpidLineParser.TryParse(l, out var f), Filter: f))
            .ToList();

        var failures = parsed.Where(p => !p.Success).Select(p => p.Line).ToList();

        failures.Should().BeEmpty("all non-comment lines should parse successfully");
        parsed.Should().HaveCountGreaterThan(100, "Magecore file should have 100+ parseable lines");
    }

    [Fact]
    public void Parse_MagecoreFile_VirtualKeywordsDefinedBeforeUse()
    {
        var lines = File.ReadAllLines(MagecoreFilePath);
        var parsedLines = lines
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith(';'))
            .Select(l => SpidLineParser.TryParse(l, out var f) ? f : null)
            .Where(f => f != null)
            .ToList();

        var keywordLines = parsedLines.Where(f => f!.FormType == SpidFormType.Keyword).ToList();
        var outfitLines = parsedLines.Where(f => f!.FormType == SpidFormType.Outfit).ToList();

        keywordLines.Should().HaveCountGreaterThan(40, "Magecore should define 40+ virtual keywords");
        outfitLines.Should().HaveCountGreaterThan(70, "Magecore should distribute 70+ outfits");
    }

    [Fact]
    public void Parse_MageoreGroupC_HasTwoGlobalExclusions()
    {
        var line = "Keyword = MAGECORE_isGroupC|MAGECORE_isMage+MAGECORE_isFemale,-MAGECORE_isGroupA,-MAGECORE_isGroupB";
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.FormIdentifier.Should().Be("MAGECORE_isGroupC");
        filter.FormType.Should().Be(SpidFormType.Keyword);
        filter.StringFilters.Expressions.Should().ContainSingle()
            .Which.Parts.Should().SatisfyRespectively(
                p => p.Value.Should().Be("MAGECORE_isMage"),
                p => p.Value.Should().Be("MAGECORE_isFemale"));
        filter.StringFilters.GlobalExclusions.Should().HaveCount(2)
            .And.AllSatisfy(e => e.IsNegated.Should().BeTrue())
            .And.SatisfyRespectively(
                e => e.Value.Should().Be("MAGECORE_isGroupA"),
                e => e.Value.Should().Be("MAGECORE_isGroupB"));
    }

    [Fact]
    public void Parse_MagecoreIsMage_HasNineWildcardExpressions()
    {
        var line = "Keyword = MAGECORE_isMage|*Conjurer,*Cryomancer,*Electromancer,*Mage,*Necro,*Pyromancer,*Wizard,*Warlock,*Sorcerer";
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.FormIdentifier.Should().Be("MAGECORE_isMage");
        filter.StringFilters.Expressions.Should().HaveCount(9);

        var values = filter.StringFilters.Expressions
            .SelectMany(e => e.Parts)
            .Select(p => p.Value)
            .ToList();

        values.Should().AllSatisfy(v => v.Should().StartWith("*"));
        values.Should().Contain(["*Conjurer", "*Sorcerer", "*Wizard"]);
    }

    [Fact]
    public void Parse_MagecoreIsFemale_HasTraitFilters()
    {
        var line = "Keyword = MAGECORE_isFemale|MAGECORE_isMage|NONE|NONE|F/-U/-C";
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.FormIdentifier.Should().Be("MAGECORE_isFemale");
        filter.TraitFilters.IsFemale.Should().BeTrue();
        filter.TraitFilters.IsUnique.Should().BeFalse();
        filter.TraitFilters.IsChild.Should().BeFalse();
    }

    [Fact]
    public void Parse_MagecoreSkillLevel_ParsesCorrectly()
    {
        var line = "Keyword = MAGECORE_isMasterAlteration|MAGECORE_isMage+MAGECORE_isFemale|NONE|12(85/999)";
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.FormIdentifier.Should().Be("MAGECORE_isMasterAlteration");
        filter.LevelFilters.Should().Be("12(85/999)");
        filter.StringFilters.Expressions.Should().ContainSingle()
            .Which.Parts.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_MagecoreChanceFilter_ParsesCorrectly()
    {
        var line = "Keyword = MAGECORE_isGroupA|MAGECORE_isMage+MAGECORE_isFemale|NONE|NONE|NONE|NONE|33";
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.FormIdentifier.Should().Be("MAGECORE_isGroupA");
        filter.Chance.Should().Be(33);
    }

    [Fact]
    public void Parse_MagecoreOutfitWithExclusions_ParsesCorrectly()
    {
        var line = "Outfit = MAGECOREMasterResearcherMagickaOutfit|MAGECORE_isFemale+MAGECORE_isMage+MAGECORE_isMasterLevel+MAGECORE_isGroupA,-MAGECORE_hasMasterSkill";
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.FormType.Should().Be(SpidFormType.Outfit);
        filter.FormIdentifier.Should().Be("MAGECOREMasterResearcherMagickaOutfit");
        filter.StringFilters.Expressions.Should().ContainSingle()
            .Which.Parts.Should().HaveCount(4);
        filter.StringFilters.GlobalExclusions.Should().ContainSingle()
            .Which.Value.Should().Be("MAGECORE_hasMasterSkill");
    }

    [Fact]
    public void Parse_MagecoreLevelRange_ParsesCorrectly()
    {
        var line = "Keyword = MAGECORE_isMasterLevel|MAGECORE_isMage+MAGECORE_isFemale|NONE|40/999";
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.FormIdentifier.Should().Be("MAGECORE_isMasterLevel");
        filter.LevelFilters.Should().Be("40/999");
    }

    #endregion

    #region Global Exclusions Semantic Tests

    [Theory]
    [InlineData("Outfit = Test|-Exclude1", 0, 1, "Exclude1")]
    [InlineData("Outfit = Test|Filter1,-Exclude1", 1, 1, "Exclude1")]
    [InlineData("Outfit = Test|Filter1,-Exclude1,-Exclude2", 1, 2, "Exclude1")]
    [InlineData("Outfit = Test|Filter1+Filter2,-Exclude1,-Exclude2,-Exclude3", 1, 3, "Exclude1")]
    public void Parse_GlobalExclusions_CountsCorrectly(
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
    public void Parse_OnlyGlobalExclusions_NoPositiveExpressions()
    {
        var line = "Outfit = TestOutfit|-OnlyExclude";
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.StringFilters.Expressions.Should().BeEmpty();
        filter.StringFilters.GlobalExclusions.Should().ContainSingle()
            .Which.Value.Should().Be("OnlyExclude");
    }

    #endregion

    #region AND/OR Expression Semantic Tests

    [Theory]
    [InlineData("Outfit = Test|A,B,C", 3, new[] { 1, 1, 1 })]
    [InlineData("Outfit = Test|A+B,C", 2, new[] { 2, 1 })]
    [InlineData("Outfit = Test|A+B+C", 1, new[] { 3 })]
    [InlineData("Outfit = Test|A+B,C+D,E", 3, new[] { 2, 2, 1 })]
    public void Parse_AndOrExpressions_CountsCorrectly(
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
    public void Parse_MixedAndWithNegation_ParsesCorrectly()
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

    #region Trait Filters Semantic Tests

    [Theory]
    [InlineData("Outfit = Test|NONE|NONE|NONE|F", true, null, null)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|M", false, null, null)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|U", null, true, null)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|-U", null, false, null)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|C", null, null, true)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|-C", null, null, false)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|F/U", true, true, null)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|M/-U/-C", false, false, false)]
    public void Parse_TraitFilters_ValuesCorrect(
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

    #region FormKey Format Semantic Tests

    [Theory]
    [InlineData("Outfit = 0x800~MyMod.esp", "0x800~MyMod.esp")]
    [InlineData("Outfit = MyMod.esp|0x800", "MyMod.esp|0x800")]
    [InlineData("Outfit = 0xFE000D65~Plugin.esl", "0xFE000D65~Plugin.esl")]
    [InlineData("Outfit = Skyrim.esm|0x000D3E05", "Skyrim.esm|0x000D3E05")]
    public void Parse_FormKeyFormats_ExtractsCorrectIdentifier(string line, string expectedIdentifier)
    {
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.FormIdentifier.Should().Be(expectedIdentifier);
    }

    [Theory]
    [InlineData("Outfit = 0x800~MyMod.esp|ActorTypeNPC", "0x800~MyMod.esp", "ActorTypeNPC")]
    [InlineData("Outfit = MyMod.esp|0x800|ActorTypeNPC|VampireFaction", "MyMod.esp|0x800", "ActorTypeNPC")]
    public void Parse_FormKeyWithFilters_SeparatesCorrectly(
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

    #region Form Filters Semantic Tests

    [Theory]
    [InlineData("Outfit = Test|NONE|VampireFaction", 1, 0)]
    [InlineData("Outfit = Test|NONE|VampireFaction,NordRace", 2, 0)]
    [InlineData("Outfit = Test|NONE|VampireFaction+NordRace", 1, 0)]
    [InlineData("Outfit = Test|NONE|0x48362,-0x13BB6", 1, 1)]
    public void Parse_FormFilters_CountsCorrectly(
        string line,
        int expectedExpressions,
        int expectedExclusions)
    {
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.FormFilters.Expressions.Should().HaveCount(expectedExpressions);
        filter.FormFilters.GlobalExclusions.Should().HaveCount(expectedExclusions);
    }

    [Fact]
    public void Parse_FormFiltersWithHexExclusions_ParsesCorrectly()
    {
        var line = "Outfit = 0xB3E8D~Skyrim.esm|ActorTypeNPC|0x48362,-0x13BB6,-0x1A6D9|NONE|F|NONE|100";
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.FormFilters.Expressions.Should().ContainSingle()
            .Which.Parts[0].Value.Should().Be("0x48362");
        filter.FormFilters.GlobalExclusions.Should().HaveCount(2)
            .And.SatisfyRespectively(
                e => e.Value.Should().Be("0x13BB6"),
                e => e.Value.Should().Be("0x1A6D9"));
    }

    #endregion

    #region TargetsAllNpcs Semantic Tests

    [Theory]
    [InlineData("Outfit = Test", true)]
    [InlineData("Outfit = Test|NONE", true)]
    [InlineData("Outfit = Test|NONE|NONE", true)]
    [InlineData("Outfit = Test|NONE|NONE|NONE|F", true)]
    [InlineData("Outfit = Test|ActorTypeNPC", false)]
    [InlineData("Outfit = Test|NONE|VampireFaction", false)]
    [InlineData("Outfit = Test|-Exclude", false)]
    public void Parse_TargetsAllNpcs_CorrectForFilters(string line, bool expectedTargetsAll)
    {
        var success = SpidLineParser.TryParse(line, out var filter);

        success.Should().BeTrue();
        filter!.TargetsAllNpcs.Should().Be(expectedTargetsAll);
    }

    #endregion
}
