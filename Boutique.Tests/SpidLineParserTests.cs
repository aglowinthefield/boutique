using Boutique.Models;
using Boutique.Utilities;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Tests for the SpidLineParser which parses full SPID distribution syntax.
/// </summary>
public class SpidLineParserTests
{
    #region Basic Parsing

    [Fact]
    public void TryParse_SimpleEditorId_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = VampireOutfit", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal("VampireOutfit", filter.OutfitIdentifier);
        Assert.True(filter.StringFilters.IsEmpty);
        Assert.True(filter.FormFilters.IsEmpty);
        Assert.Equal(100, filter.Chance);
    }

    [Fact]
    public void TryParse_FormKeyWithTilde_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = 0x800~MyMod.esp", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal("0x800~MyMod.esp", filter.OutfitIdentifier);
    }

    [Fact]
    public void TryParse_FormKeyWithPipe_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = MyMod.esp|0x800", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal("MyMod.esp|0x800", filter.OutfitIdentifier);
    }

    [Fact]
    public void TryParseOutfit_NotOutfitLine_ReturnsFalse()
    {
        var result = SpidLineParser.TryParseOutfit("Spell = 0x800~MyMod.esp", out var filter);

        Assert.False(result);
        Assert.Null(filter);
    }

    [Fact]
    public void TryParse_SpellLine_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Spell = 0x800~MyMod.esp", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal(SpidFormType.Spell, filter.FormType);
        Assert.Equal("0x800~MyMod.esp", filter.FormIdentifier);
    }

    [Fact]
    public void TryParse_EmptyLine_ReturnsFalse()
    {
        var result = SpidLineParser.TryParse("", out var filter);

        Assert.False(result);
        Assert.Null(filter);
    }

    [Fact]
    public void TryParse_Comment_ReturnsFalse()
    {
        var result = SpidLineParser.TryParse("; Outfit = Something", out var filter);

        Assert.False(result);
        Assert.Null(filter);
    }

    #endregion

    #region String Filters (Position 2)

    [Fact]
    public void TryParse_WithNpcName_ParsesStringFilters()
    {
        var result = SpidLineParser.TryParse("Outfit = VampireOutfit|Serana", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal("VampireOutfit", filter.OutfitIdentifier);
        Assert.Single(filter.StringFilters.Expressions);
        Assert.Single(filter.StringFilters.Expressions[0].Parts);
        Assert.Equal("Serana", filter.StringFilters.Expressions[0].Parts[0].Value);
    }

    [Fact]
    public void TryParse_WithKeyword_ParsesStringFilters()
    {
        var result = SpidLineParser.TryParse("Outfit = VampireOutfit|ActorTypeNPC", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Single(filter.StringFilters.Expressions);
        Assert.Equal("ActorTypeNPC", filter.StringFilters.Expressions[0].Parts[0].Value);
    }

    [Fact]
    public void TryParse_WithMultipleOrFilters_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = VampireOutfit|Serana,Harkon,Valerica", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal(3, filter.StringFilters.Expressions.Count);
        Assert.Equal("Serana", filter.StringFilters.Expressions[0].Parts[0].Value);
        Assert.Equal("Harkon", filter.StringFilters.Expressions[1].Parts[0].Value);
        Assert.Equal("Valerica", filter.StringFilters.Expressions[2].Parts[0].Value);
    }

    [Fact]
    public void TryParse_WithAndFilters_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = BanditOutfit|ActorTypeNPC+Bandit", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Single(filter.StringFilters.Expressions);
        Assert.Equal(2, filter.StringFilters.Expressions[0].Parts.Count);
        Assert.Equal("ActorTypeNPC", filter.StringFilters.Expressions[0].Parts[0].Value);
        Assert.Equal("Bandit", filter.StringFilters.Expressions[0].Parts[1].Value);
    }

    [Fact]
    public void TryParse_WithNegatedFilter_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = GuardOutfit|*Guard+-Stormcloak", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal(2, filter.StringFilters.Expressions[0].Parts.Count);
        Assert.False(filter.StringFilters.Expressions[0].Parts[0].IsNegated);
        Assert.Equal("*Guard", filter.StringFilters.Expressions[0].Parts[0].Value);
        Assert.True(filter.StringFilters.Expressions[0].Parts[1].IsNegated);
        Assert.Equal("Stormcloak", filter.StringFilters.Expressions[0].Parts[1].Value);
    }

    [Fact]
    public void TryParse_WithWildcard_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = GuardOutfit|*Guard*", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.True(filter.StringFilters.Expressions[0].Parts[0].HasWildcard);
    }

    #endregion

    #region Form Filters (Position 3)

    [Fact]
    public void TryParse_WithFaction_ParsesFormFilters()
    {
        var result = SpidLineParser.TryParse("Outfit = VampireOutfit|NONE|VampireFaction", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.True(filter.StringFilters.IsEmpty);
        Assert.False(filter.FormFilters.IsEmpty);
        Assert.Equal("VampireFaction", filter.FormFilters.Expressions[0].Parts[0].Value);
    }

    [Fact]
    public void TryParse_WithRace_ParsesFormFilters()
    {
        var result = SpidLineParser.TryParse("Outfit = NordOutfit|NONE|NordRace", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Single(filter.FormFilters.Expressions);
        Assert.Equal("NordRace", filter.FormFilters.Expressions[0].Parts[0].Value);
    }

    [Fact]
    public void TryParse_WithKeywordAndFaction_ParsesBothFilters()
    {
        var result = SpidLineParser.TryParse("Outfit = VampireOutfit|ActorTypeNPC|VampireFaction", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Single(filter.StringFilters.Expressions);
        Assert.Single(filter.FormFilters.Expressions);
    }

    [Fact]
    public void TryParse_WithMultipleFactions_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = CriminalOutfit|NONE|CrimeFactionWhiterun,CrimeFactionRiften",
            out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal(2, filter.FormFilters.Expressions.Count);
    }

    #endregion

    #region Trait Filters (Position 5)

    [Fact]
    public void TryParse_WithFemale_ParsesTraitFilters()
    {
        var result = SpidLineParser.TryParse("Outfit = FemaleOutfit|NONE|NONE|NONE|F", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.True(filter.TraitFilters.IsFemale);
    }

    [Fact]
    public void TryParse_WithMale_ParsesTraitFilters()
    {
        var result = SpidLineParser.TryParse("Outfit = MaleOutfit|NONE|NONE|NONE|M", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.False(filter.TraitFilters.IsFemale);
    }

    [Fact]
    public void TryParse_WithMultipleTraits_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = UniqueOutfit|NONE|NONE|NONE|-U/M/-C", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.False(filter.TraitFilters.IsUnique); // -U = not unique
        Assert.False(filter.TraitFilters.IsFemale); // M = male
        Assert.False(filter.TraitFilters.IsChild); // -C = not child
    }

    [Fact]
    public void TryParse_WithUnique_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = BossOutfit|NONE|NONE|NONE|U", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.True(filter.TraitFilters.IsUnique);
    }

    #endregion

    #region Chance (Position 7)

    [Fact]
    public void TryParse_WithChance_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse("Outfit = RareOutfit|NONE|NONE|NONE|NONE|NONE|5", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal(5, filter.Chance);
    }

    [Fact]
    public void TryParse_NoChance_DefaultsTo100()
    {
        var result = SpidLineParser.TryParse("Outfit = CommonOutfit|NONE", out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal(100, filter.Chance);
    }

    #endregion

    #region Real-World Examples

    [Fact]
    public void TryParse_RealExample_VampireDistribution()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = 1_Obi_Druchii|ActorTypeNPC|VampireFaction|NONE|F|NONE|5",
            out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal("1_Obi_Druchii", filter.OutfitIdentifier);
        Assert.Single(filter.StringFilters.Expressions);
        Assert.Single(filter.FormFilters.Expressions);
        Assert.True(filter.TraitFilters.IsFemale);
        Assert.Equal(5, filter.Chance);
    }

    [Fact]
    public void TryParse_RealExample_BanditDistribution()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = BanditOutfit|ActorTypeNPC+*Bandit*|BanditFaction",
            out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal("BanditOutfit", filter.OutfitIdentifier);
        Assert.Single(filter.StringFilters.Expressions);
        Assert.Single(filter.FormFilters.Expressions);
    }

    [Fact]
    public void TryParse_RealExample_FormKeyWithFilters()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = 0x800~RequiredMod.esp|ActorTypeNPC|SomeFaction|NONE|F",
            out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal("0x800~RequiredMod.esp", filter.OutfitIdentifier);
        Assert.Single(filter.StringFilters.Expressions);
        Assert.True(filter.TraitFilters.IsFemale);
    }

    [Fact]
    public void TryParse_WithInlineComment_IgnoresComment()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = VampireOutfit|Serana ; This is a comment",
            out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal("VampireOutfit", filter.OutfitIdentifier);
        Assert.Equal("Serana", filter.StringFilters.Expressions[0].Parts[0].Value);
    }

    #endregion

    #region Helper Methods

    [Fact]
    public void GetSpecificNpcIdentifiers_WithoutLinkCache_ReturnsAllStringFilters()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit|Serana,ActorTypeNPC,Harkon", out var filter);

        var npcs = SpidLineParser.GetSpecificNpcIdentifiers(filter!);

        Assert.Equal(3, npcs.Count);
        Assert.Contains("Serana", npcs);
        Assert.Contains("Harkon", npcs);
        Assert.Contains("ActorTypeNPC", npcs);
    }

    [Fact]
    public void GetKeywordIdentifiers_WithoutLinkCache_ReturnsAllStringFilters()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit|ActorTypeNPC+VampireKeyword,Serana", out var filter);

        var keywords = SpidLineParser.GetKeywordIdentifiers(filter!);

        Assert.Equal(3, keywords.Count);
        Assert.Contains("ActorTypeNPC", keywords);
        Assert.Contains("VampireKeyword", keywords);
        Assert.Contains("Serana", keywords);
    }

    [Fact]
    public void GetFactionIdentifiers_WithoutLinkCache_ReturnsAllFormFilters()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit|NONE|VampireFaction,NordRace", out var filter);

        var factions = SpidLineParser.GetFactionIdentifiers(filter!);

        Assert.Equal(2, factions.Count);
        Assert.Contains("VampireFaction", factions);
        Assert.Contains("NordRace", factions);
    }

    [Fact]
    public void GetRaceIdentifiers_WithoutLinkCache_ReturnsAllFormFilters()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit|NONE|VampireFaction,NordRace", out var filter);

        var races = SpidLineParser.GetRaceIdentifiers(filter!);

        Assert.Equal(2, races.Count);
        Assert.Contains("NordRace", races);
        Assert.Contains("VampireFaction", races);
    }

    [Fact]
    public void GetClassIdentifiers_WithoutLinkCache_ReturnsAllFormFilters()
    {
        SpidLineParser.TryParse("Outfit = SoldierOutfit|NONE|CWSoldierClass,VampireFaction", out var filter);

        var classes = SpidLineParser.GetClassIdentifiers(filter!);

        Assert.Equal(2, classes.Count);
        Assert.Contains("CWSoldierClass", classes);
        Assert.Contains("VampireFaction", classes);
    }

    [Fact]
    public void TryParse_WithClassFilter_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = 0x80A~Stormcloak Bikini Armors Combined - SPID.esp|ActorTypeNPC|CWSoldierClass|NONE|F|NONE|100",
            out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal("0x80A~Stormcloak Bikini Armors Combined - SPID.esp", filter.OutfitIdentifier);
        Assert.Single(filter.StringFilters.Expressions);
        Assert.Single(filter.FormFilters.Expressions);
        Assert.Equal("CWSoldierClass", filter.FormFilters.Expressions[0].Parts[0].Value);
        Assert.True(filter.TraitFilters.IsFemale);
        Assert.Equal(100, filter.Chance);
    }

    [Fact]
    public void TryParse_WithClassAndFaction_ParsesBothFormFilters()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = TestOutfit|NONE|CWSoldierClass+SonsFaction",
            out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal(2, filter.FormFilters.Expressions[0].Parts.Count);
        Assert.Equal("CWSoldierClass", filter.FormFilters.Expressions[0].Parts[0].Value);
        Assert.Equal("SonsFaction", filter.FormFilters.Expressions[0].Parts[1].Value);
    }

    #endregion

    #region Targeting Description

    [Fact]
    public void TryParse_WithBareHexFormIdsInFormFilters_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = 0xB3E8D~Skyrim.esm|ActorTypeNPC|0x48362,-0x13BB6,-0x1A6D9|NONE|F|NONE|100",
            out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal("0xB3E8D~Skyrim.esm", filter.OutfitIdentifier);
        Assert.Single(filter.StringFilters.Expressions);
        Assert.Single(filter.FormFilters.Expressions);
        Assert.Equal("0x48362", filter.FormFilters.Expressions[0].Parts[0].Value);
        Assert.Equal(2, filter.FormFilters.GlobalExclusions.Count);
        Assert.Equal("0x13BB6", filter.FormFilters.GlobalExclusions[0].Value);
        Assert.True(filter.FormFilters.GlobalExclusions[0].IsNegated);
        Assert.Equal("0x1A6D9", filter.FormFilters.GlobalExclusions[1].Value);
        Assert.True(filter.FormFilters.GlobalExclusions[1].IsNegated);
        Assert.True(filter.TraitFilters.IsFemale);
        Assert.Equal(100, filter.Chance);
    }

    [Fact]
    public void TryParse_WithSpecificNpcFormIdInFormFilters_ParsesCorrectly()
    {
        var result = SpidLineParser.TryParse(
            "Outfit = 0xB3E8E~Skyrim.esm|ActorTypeNPC|0x1A6D9|NONE|F|NONE|100",
            out var filter);

        Assert.True(result);
        Assert.NotNull(filter);
        Assert.Equal("0xB3E8E~Skyrim.esm", filter.OutfitIdentifier);
        Assert.Single(filter.FormFilters.Expressions);
        Assert.Equal("0x1A6D9", filter.FormFilters.Expressions[0].Parts[0].Value);
        Assert.False(filter.FormFilters.Expressions[0].Parts[0].IsNegated);
    }

    #endregion

    #region Targeting Description

    [Fact]
    public void GetTargetingDescription_AllNpcs_ReturnsAllNpcs()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit", out var filter);

        var description = filter!.GetTargetingDescription();

        Assert.Equal("All NPCs", description);
    }

    [Fact]
    public void GetTargetingDescription_WithFilters_ReturnsDescription()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit|ActorTypeNPC|VampireFaction|NONE|F|NONE|5", out var filter);

        var description = filter!.GetTargetingDescription();

        Assert.Contains("Names/Keywords", description);
        Assert.Contains("Factions/Forms", description);
        Assert.Contains("Female", description);
        Assert.Contains("5%", description);
    }

    [Fact]
    public void TargetsAllNpcs_WithNoFilters_ReturnsTrue()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit", out var filter);

        Assert.True(filter!.TargetsAllNpcs);
    }

    [Fact]
    public void TargetsAllNpcs_WithFilters_ReturnsFalse()
    {
        SpidLineParser.TryParse("Outfit = VampireOutfit|Serana", out var filter);

        Assert.False(filter!.TargetsAllNpcs);
    }

    #endregion
}
