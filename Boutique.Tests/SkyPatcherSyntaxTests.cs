using Boutique.Utilities;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

public class SkyPatcherSyntaxTests
{
    #region ExtractFilterValue - Single value extraction

    [Fact]
    public void ExtractFilterValue_ExistingFilter_ReturnsValue()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValue(line, "filterByNpcs");

        result.Should().Be("Skyrim.esm|0x1234");
    }

    [Fact]
    public void ExtractFilterValue_FilterAtEnd_ReturnsValue()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValue(line, "outfitDefault");

        result.Should().Be("MyMod.esp|0x800");
    }

    [Fact]
    public void ExtractFilterValue_MissingFilter_ReturnsNull()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValue(line, "filterByFactions");

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractFilterValue_CaseInsensitive_ReturnsValue()
    {
        var line = "FILTERBYOUTFITS=MyMod.esp|0x100:OUTFITDEFAULT=MyMod.esp|0x200";
        var result = SkyPatcherSyntax.ExtractFilterValue(line, "filterByOutfits");

        result.Should().Be("MyMod.esp|0x100");
    }

    [Fact]
    public void ExtractFilterValue_WithSpaces_TrimsValue()
    {
        var line = "outfitDefault= MyMod.esp|0x800 :filterByNpcs=Test.esp|0x1";
        var result = SkyPatcherSyntax.ExtractFilterValue(line, "outfitDefault");

        result.Should().Be("MyMod.esp|0x800");
    }

    [Fact]
    public void ExtractFilterValue_EmptyLine_ReturnsNull()
    {
        var result = SkyPatcherSyntax.ExtractFilterValue("", "filterByNpcs");
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractFilterValue_FilterWithNoValue_ReturnsEmpty()
    {
        var line = "filterByNpcs=:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValue(line, "filterByNpcs");

        result.Should().BeEmpty();
    }

    #endregion

    #region ExtractFilterValues - Multiple comma-separated values

    [Fact]
    public void ExtractFilterValues_SingleValue_ReturnsSingleItem()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValues(line, "filterByNpcs");

        result.Should().ContainSingle().Which.Should().Be("Skyrim.esm|0x1234");
    }

    [Fact]
    public void ExtractFilterValues_MultipleValues_ReturnsAll()
    {
        var line = "filterByNpcs=Skyrim.esm|0x100,Skyrim.esm|0x200,Skyrim.esm|0x300:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValues(line, "filterByNpcs");

        result.Should().HaveCount(3)
            .And.ContainInOrder("Skyrim.esm|0x100", "Skyrim.esm|0x200", "Skyrim.esm|0x300");
    }

    [Fact]
    public void ExtractFilterValues_MissingFilter_ReturnsEmpty()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValues(line, "filterByFactions");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractFilterValues_WithSpaces_TrimsEachValue()
    {
        var line = "filterByNpcs= Skyrim.esm|0x100 , Skyrim.esm|0x200 :outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValues(line, "filterByNpcs");

        result.Should().HaveCount(2)
            .And.ContainInOrder("Skyrim.esm|0x100", "Skyrim.esm|0x200");
    }

    [Fact]
    public void ExtractFilterValues_EmptyValues_FiltersOut()
    {
        var line = "filterByNpcs=Skyrim.esm|0x100,,Skyrim.esm|0x200:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValues(line, "filterByNpcs");

        result.Should().HaveCount(2);
    }

    #endregion

    #region ParseGenderFilter

    [Fact]
    public void ParseGenderFilter_Female_ReturnsTrue()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:filterByGender=female:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseGenderFilter(line);

        result.Should().BeTrue();
    }

    [Fact]
    public void ParseGenderFilter_Male_ReturnsFalse()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:filterByGender=male:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseGenderFilter(line);

        result.Should().BeFalse();
    }

    [Fact]
    public void ParseGenderFilter_NoFilter_ReturnsNull()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseGenderFilter(line);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseGenderFilter_CaseInsensitive_Female()
    {
        var line = "filterByGender=FEMALE:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseGenderFilter(line);

        result.Should().BeTrue();
    }

    [Fact]
    public void ParseGenderFilter_CaseInsensitive_Male()
    {
        var line = "filterByGender=MALE:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseGenderFilter(line);

        result.Should().BeFalse();
    }

    [Fact]
    public void ParseGenderFilter_InvalidValue_ReturnsNull()
    {
        var line = "filterByGender=unknown:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseGenderFilter(line);

        result.Should().BeNull();
    }

    #endregion

    #region ParseFormKeys - Extract and parse FormKeys from filter

    [Fact]
    public void ParseFormKeys_SingleFormKey_ParsesCorrectly()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseFormKeys(line, "filterByNpcs");

        result.Should().ContainSingle().Which.Should().Match<Mutagen.Bethesda.Plugins.FormKey>(fk =>
            fk.ModKey.FileName.String == "Skyrim.esm" && fk.ID == 0x1234u);
    }

    [Fact]
    public void ParseFormKeys_MultipleFormKeys_ParsesAll()
    {
        var line = "filterByNpcs=Skyrim.esm|0x100,Dawnguard.esm|0x200:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseFormKeys(line, "filterByNpcs");

        result.Should().HaveCount(2);
        result[0].ModKey.FileName.String.Should().Be("Skyrim.esm");
        result[1].ModKey.FileName.String.Should().Be("Dawnguard.esm");
    }

    [Fact]
    public void ParseFormKeys_MixedValidInvalid_ReturnsOnlyValid()
    {
        var line = "filterByNpcs=Skyrim.esm|0x100,InvalidNpc,Dawnguard.esm|0x200:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseFormKeys(line, "filterByNpcs");

        result.Should().HaveCount(2);
    }

    [Fact]
    public void ParseFormKeys_TildeFormat_ParsesCorrectly()
    {
        var line = "outfitDefault=0x800~MyMod.esp";
        var result = SkyPatcherSyntax.ParseFormKeys(line, "outfitDefault");

        result.Should().ContainSingle().Which.Should().Match<Mutagen.Bethesda.Plugins.FormKey>(fk =>
            fk.ModKey.FileName.String == "MyMod.esp" && fk.ID == 0x800u);
    }

    [Fact]
    public void ParseFormKeys_MissingFilter_ReturnsEmpty()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseFormKeys(line, "filterByFactions");

        result.Should().BeEmpty();
    }

    #endregion

    #region HasFilter

    [Fact]
    public void HasFilter_ExistingFilter_ReturnsTrue()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";

        SkyPatcherSyntax.HasFilter(line, "filterByNpcs").Should().BeTrue();
        SkyPatcherSyntax.HasFilter(line, "outfitDefault").Should().BeTrue();
    }

    [Fact]
    public void HasFilter_MissingFilter_ReturnsFalse()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";

        SkyPatcherSyntax.HasFilter(line, "filterByFactions").Should().BeFalse();
        SkyPatcherSyntax.HasFilter(line, "filterByRaces").Should().BeFalse();
    }

    [Fact]
    public void HasFilter_CaseInsensitive_ReturnsTrue()
    {
        var line = "FILTERBYOUTFITS=MyMod.esp|0x100";

        SkyPatcherSyntax.HasFilter(line, "filterByOutfits").Should().BeTrue();
    }

    #endregion

    #region Real-world examples

    [Fact]
    public void RealExample_ComplexSkyPatcherLine_ParsesCorrectly()
    {
        var line =
            "filterByNpcs=Skyrim.esm|0x13BBF,Skyrim.esm|0x1B07A:filterByGender=female:outfitDefault=MyMod.esp|0x800";

        var npcs = SkyPatcherSyntax.ParseFormKeys(line, "filterByNpcs");
        var gender = SkyPatcherSyntax.ParseGenderFilter(line);
        var outfit = SkyPatcherSyntax.ParseFormKeys(line, "outfitDefault");

        npcs.Should().HaveCount(2);
        npcs[0].ID.Should().Be(0x13BBFu);
        npcs[1].ID.Should().Be(0x1B07Au);
        gender.Should().BeTrue();
        outfit.Should().ContainSingle().Which.ID.Should().Be(0x800u);
    }

    [Fact]
    public void RealExample_FactionFilter_ParsesCorrectly()
    {
        var line = "filterByFactions=Skyrim.esm|0x000FDEAC:outfitDefault=MyMod.esp|0xFE000D65";

        var factions = SkyPatcherSyntax.ParseFormKeys(line, "filterByFactions");
        var outfit = SkyPatcherSyntax.ExtractFilterValue(line, "outfitDefault");

        factions.Should().ContainSingle().Which.ID.Should().Be(0xFDEACu);
        outfit.Should().Be("MyMod.esp|0xFE000D65");
    }

    [Fact]
    public void RealExample_FormIdsWithoutHexPrefix_ParsesCorrectly()
    {
        var line = "filterByNpcs=Skyrim.esm|13BBF:outfitDefault=MyMod.esp|800";

        var npcs = SkyPatcherSyntax.ParseFormKeys(line, "filterByNpcs");
        var outfit = SkyPatcherSyntax.ParseFormKeys(line, "outfitDefault");

        npcs.Should().ContainSingle().Which.ID.Should().Be(0x13BBFu);
        outfit.Should().ContainSingle().Which.ID.Should().Be(0x800u);
    }

    [Fact]
    public void RealExample_LargeFormId_ParsesCorrectly()
    {
        var line = "outfitDefault=MyMod.esp|00ABCDEF";

        var outfit = SkyPatcherSyntax.ParseFormKeys(line, "outfitDefault");

        outfit.Should().ContainSingle().Which.ID.Should().Be(0xABCDEFu);
    }

    [Fact]
    public void RealExample_FactionsOrFilter_ParsesCorrectly()
    {
        var line = "filterByFactionsOr=Skyrim.esm|0x000FDEAC,Skyrim.esm|0x1BCC0:outfitDefault=MyMod.esp|0x800";

        var factions = SkyPatcherSyntax.ParseFormKeys(line, "filterByFactionsOr");

        factions.Should().HaveCount(2);
        factions[0].ID.Should().Be(0xFDEACu);
        factions[1].ID.Should().Be(0x1BCC0u);
    }

    [Fact]
    public void RealExample_EditorIdFactions_ExtractsValues()
    {
        var line = "filterByFactionsOr=DA16OrcAmbushFaction,DA16OrcDreamFaction:outfitDefault=TH_OrcScaling_OF";

        var values = SkyPatcherSyntax.ExtractFilterValuesWithVariants(line, "filterByFactions");

        values.Should().HaveCount(2)
            .And.ContainInOrder("DA16OrcAmbushFaction", "DA16OrcDreamFaction");
    }

    #endregion

    #region Filter variant methods

    [Fact]
    public void ExtractFilterValuesWithVariants_CombinesBothFormats()
    {
        var line =
            "filterByFactions=Skyrim.esm|0x100:filterByFactionsOr=Skyrim.esm|0x200,Skyrim.esm|0x300:outfitDefault=MyMod.esp|0x800";

        var values = SkyPatcherSyntax.ExtractFilterValuesWithVariants(line, "filterByFactions");

        values.Should().HaveCount(3)
            .And.ContainInOrder("Skyrim.esm|0x100", "Skyrim.esm|0x200", "Skyrim.esm|0x300");
    }

    [Fact]
    public void HasAnyVariant_Base_ReturnsTrue()
    {
        var line = "filterByFactions=Skyrim.esm|0x100:outfitDefault=MyMod.esp|0x800";

        SkyPatcherSyntax.HasAnyVariant(line, "filterByFactions").Should().BeTrue();
    }

    [Fact]
    public void HasAnyVariant_Or_ReturnsTrue()
    {
        var line = "filterByFactionsOr=Skyrim.esm|0x100:outfitDefault=MyMod.esp|0x800";

        SkyPatcherSyntax.HasAnyVariant(line, "filterByFactions").Should().BeTrue();
    }

    [Fact]
    public void HasAnyVariant_Excluded_ReturnsTrue()
    {
        var line = "filterByFactionsExcluded=Skyrim.esm|0x100:outfitDefault=MyMod.esp|0x800";

        SkyPatcherSyntax.HasAnyVariant(line, "filterByFactions").Should().BeTrue();
    }

    [Fact]
    public void HasAnyVariant_None_ReturnsFalse()
    {
        var line = "filterByNpcs=Skyrim.esm|0x100:outfitDefault=MyMod.esp|0x800";

        SkyPatcherSyntax.HasAnyVariant(line, "filterByFactions").Should().BeFalse();
    }

    #endregion

    #region Filter detection

    [Fact]
    public void GetAllFilterNames_ReturnsAllFilters()
    {
        var line = "filterByNpcs=Skyrim.esm|0x100:filterByFactions=Skyrim.esm|0x200:outfitDefault=MyMod.esp|0x800";

        var filters = SkyPatcherSyntax.GetAllFilterNames(line);

        filters.Should().HaveCount(3)
            .And.Contain(["filterByNpcs", "filterByFactions", "outfitDefault"]);
    }

    [Fact]
    public void GetUnsupportedFilters_KnownFilters_ReturnsEmpty()
    {
        var line = "filterByFactionsOr=Faction1:filterByGender=female:outfitDefault=MyMod.esp|0x800";

        var unsupported = SkyPatcherSyntax.GetUnsupportedFilters(line);

        unsupported.Should().BeEmpty();
    }

    [Fact]
    public void GetUnsupportedFilters_UnknownFilter_ReturnsIt()
    {
        var line = "filterByCustomThing=Value:outfitDefault=MyMod.esp|0x800";

        var unsupported = SkyPatcherSyntax.GetUnsupportedFilters(line);

        unsupported.Should().ContainSingle().Which.Should().Be("filterByCustomThing");
    }

    [Fact]
    public void HasUnsupportedFilters_AllSupported_ReturnsFalse()
    {
        var line = "filterByFactionsOr=Faction1:outfitDefault=MyMod.esp|0x800";

        SkyPatcherSyntax.HasUnsupportedFilters(line).Should().BeFalse();
    }

    [Fact]
    public void HasUnsupportedFilters_HasUnsupported_ReturnsTrue()
    {
        var line = "filterByUnknown=Value:outfitDefault=MyMod.esp|0x800";

        SkyPatcherSyntax.HasUnsupportedFilters(line).Should().BeTrue();
    }

    #endregion
}
