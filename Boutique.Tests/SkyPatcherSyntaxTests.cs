using Boutique.Utilities;
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

        Assert.Equal("Skyrim.esm|0x1234", result);
    }

    [Fact]
    public void ExtractFilterValue_FilterAtEnd_ReturnsValue()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValue(line, "outfitDefault");

        Assert.Equal("MyMod.esp|0x800", result);
    }

    [Fact]
    public void ExtractFilterValue_MissingFilter_ReturnsNull()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValue(line, "filterByFactions");

        Assert.Null(result);
    }

    [Fact]
    public void ExtractFilterValue_CaseInsensitive_ReturnsValue()
    {
        var line = "FILTERBYOUTFITS=MyMod.esp|0x100:OUTFITDEFAULT=MyMod.esp|0x200";
        var result = SkyPatcherSyntax.ExtractFilterValue(line, "filterByOutfits");

        Assert.Equal("MyMod.esp|0x100", result);
    }

    [Fact]
    public void ExtractFilterValue_WithSpaces_TrimsValue()
    {
        var line = "outfitDefault= MyMod.esp|0x800 :filterByNpcs=Test.esp|0x1";
        var result = SkyPatcherSyntax.ExtractFilterValue(line, "outfitDefault");

        Assert.Equal("MyMod.esp|0x800", result);
    }

    [Fact]
    public void ExtractFilterValue_EmptyLine_ReturnsNull()
    {
        var result = SkyPatcherSyntax.ExtractFilterValue("", "filterByNpcs");

        Assert.Null(result);
    }

    [Fact]
    public void ExtractFilterValue_FilterWithNoValue_ReturnsEmpty()
    {
        var line = "filterByNpcs=:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValue(line, "filterByNpcs");

        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region ExtractFilterValues - Multiple comma-separated values

    [Fact]
    public void ExtractFilterValues_SingleValue_ReturnsSingleItem()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValues(line, "filterByNpcs");

        Assert.Single(result);
        Assert.Equal("Skyrim.esm|0x1234", result[0]);
    }

    [Fact]
    public void ExtractFilterValues_MultipleValues_ReturnsAll()
    {
        var line = "filterByNpcs=Skyrim.esm|0x100,Skyrim.esm|0x200,Skyrim.esm|0x300:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValues(line, "filterByNpcs");

        Assert.Equal(3, result.Count);
        Assert.Equal("Skyrim.esm|0x100", result[0]);
        Assert.Equal("Skyrim.esm|0x200", result[1]);
        Assert.Equal("Skyrim.esm|0x300", result[2]);
    }

    [Fact]
    public void ExtractFilterValues_MissingFilter_ReturnsEmpty()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValues(line, "filterByFactions");

        Assert.Empty(result);
    }

    [Fact]
    public void ExtractFilterValues_WithSpaces_TrimsEachValue()
    {
        var line = "filterByNpcs= Skyrim.esm|0x100 , Skyrim.esm|0x200 :outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValues(line, "filterByNpcs");

        Assert.Equal(2, result.Count);
        Assert.Equal("Skyrim.esm|0x100", result[0]);
        Assert.Equal("Skyrim.esm|0x200", result[1]);
    }

    [Fact]
    public void ExtractFilterValues_EmptyValues_FiltersOut()
    {
        var line = "filterByNpcs=Skyrim.esm|0x100,,Skyrim.esm|0x200:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ExtractFilterValues(line, "filterByNpcs");

        Assert.Equal(2, result.Count);
    }

    #endregion

    #region ParseGenderFilter

    [Fact]
    public void ParseGenderFilter_Female_ReturnsTrue()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:filterByGender=female:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseGenderFilter(line);

        Assert.True(result);
    }

    [Fact]
    public void ParseGenderFilter_Male_ReturnsFalse()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:filterByGender=male:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseGenderFilter(line);

        Assert.False(result);
    }

    [Fact]
    public void ParseGenderFilter_NoFilter_ReturnsNull()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseGenderFilter(line);

        Assert.Null(result);
    }

    [Fact]
    public void ParseGenderFilter_CaseInsensitive_Female()
    {
        var line = "filterByGender=FEMALE:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseGenderFilter(line);

        Assert.True(result);
    }

    [Fact]
    public void ParseGenderFilter_CaseInsensitive_Male()
    {
        var line = "filterByGender=MALE:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseGenderFilter(line);

        Assert.False(result);
    }

    [Fact]
    public void ParseGenderFilter_InvalidValue_ReturnsNull()
    {
        var line = "filterByGender=unknown:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseGenderFilter(line);

        Assert.Null(result);
    }

    #endregion

    #region ParseFormKeys - Extract and parse FormKeys from filter

    [Fact]
    public void ParseFormKeys_SingleFormKey_ParsesCorrectly()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseFormKeys(line, "filterByNpcs");

        Assert.Single(result);
        Assert.Equal("Skyrim.esm", result[0].ModKey.FileName);
        Assert.Equal(0x1234u, result[0].ID);
    }

    [Fact]
    public void ParseFormKeys_MultipleFormKeys_ParsesAll()
    {
        var line = "filterByNpcs=Skyrim.esm|0x100,Dawnguard.esm|0x200:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseFormKeys(line, "filterByNpcs");

        Assert.Equal(2, result.Count);
        Assert.Equal("Skyrim.esm", result[0].ModKey.FileName);
        Assert.Equal("Dawnguard.esm", result[1].ModKey.FileName);
    }

    [Fact]
    public void ParseFormKeys_MixedValidInvalid_ReturnsOnlyValid()
    {
        var line = "filterByNpcs=Skyrim.esm|0x100,InvalidNpc,Dawnguard.esm|0x200:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseFormKeys(line, "filterByNpcs");

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseFormKeys_TildeFormat_ParsesCorrectly()
    {
        var line = "outfitDefault=0x800~MyMod.esp";
        var result = SkyPatcherSyntax.ParseFormKeys(line, "outfitDefault");

        Assert.Single(result);
        Assert.Equal("MyMod.esp", result[0].ModKey.FileName);
        Assert.Equal(0x800u, result[0].ID);
    }

    [Fact]
    public void ParseFormKeys_MissingFilter_ReturnsEmpty()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";
        var result = SkyPatcherSyntax.ParseFormKeys(line, "filterByFactions");

        Assert.Empty(result);
    }

    #endregion

    #region HasFilter

    [Fact]
    public void HasFilter_ExistingFilter_ReturnsTrue()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";

        Assert.True(SkyPatcherSyntax.HasFilter(line, "filterByNpcs"));
        Assert.True(SkyPatcherSyntax.HasFilter(line, "outfitDefault"));
    }

    [Fact]
    public void HasFilter_MissingFilter_ReturnsFalse()
    {
        var line = "filterByNpcs=Skyrim.esm|0x1234:outfitDefault=MyMod.esp|0x800";

        Assert.False(SkyPatcherSyntax.HasFilter(line, "filterByFactions"));
        Assert.False(SkyPatcherSyntax.HasFilter(line, "filterByRaces"));
    }

    [Fact]
    public void HasFilter_CaseInsensitive_ReturnsTrue()
    {
        var line = "FILTERBYOUTFITS=MyMod.esp|0x100";

        Assert.True(SkyPatcherSyntax.HasFilter(line, "filterByOutfits"));
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

        Assert.Equal(2, npcs.Count);
        Assert.Equal(0x13BBFu, npcs[0].ID);
        Assert.Equal(0x1B07Au, npcs[1].ID);
        Assert.True(gender);
        Assert.Single(outfit);
        Assert.Equal(0x800u, outfit[0].ID);
    }

    [Fact]
    public void RealExample_FactionFilter_ParsesCorrectly()
    {
        var line = "filterByFactions=Skyrim.esm|0x000FDEAC:outfitDefault=MyMod.esp|0xFE000D65";

        var factions = SkyPatcherSyntax.ParseFormKeys(line, "filterByFactions");
        var outfit = SkyPatcherSyntax.ExtractFilterValue(line, "outfitDefault");

        Assert.Single(factions);
        Assert.Equal(0xFDEACu, factions[0].ID);
        Assert.Equal("MyMod.esp|0xFE000D65", outfit);
    }

    [Fact]
    public void RealExample_FormIdsWithoutHexPrefix_ParsesCorrectly()
    {
        var line = "filterByNpcs=Skyrim.esm|13BBF:outfitDefault=MyMod.esp|800";

        var npcs = SkyPatcherSyntax.ParseFormKeys(line, "filterByNpcs");
        var outfit = SkyPatcherSyntax.ParseFormKeys(line, "outfitDefault");

        Assert.Single(npcs);
        Assert.Equal(0x13BBFu, npcs[0].ID);
        Assert.Single(outfit);
        Assert.Equal(0x800u, outfit[0].ID);
    }

    [Fact]
    public void RealExample_LargeFormId_ParsesCorrectly()
    {
        var line = "outfitDefault=MyMod.esp|00ABCDEF";

        var outfit = SkyPatcherSyntax.ParseFormKeys(line, "outfitDefault");

        Assert.Single(outfit);
        Assert.Equal(0xABCDEFu, outfit[0].ID);
    }

    [Fact]
    public void RealExample_FactionsOrFilter_ParsesCorrectly()
    {
        var line = "filterByFactionsOr=Skyrim.esm|0x000FDEAC,Skyrim.esm|0x1BCC0:outfitDefault=MyMod.esp|0x800";

        var factions = SkyPatcherSyntax.ParseFormKeys(line, "filterByFactionsOr");

        Assert.Equal(2, factions.Count);
        Assert.Equal(0xFDEACu, factions[0].ID);
        Assert.Equal(0x1BCC0u, factions[1].ID);
    }

    [Fact]
    public void RealExample_EditorIdFactions_ExtractsValues()
    {
        var line = "filterByFactionsOr=DA16OrcAmbushFaction,DA16OrcDreamFaction:outfitDefault=TH_OrcScaling_OF";

        var values = SkyPatcherSyntax.ExtractFilterValuesWithVariants(line, "filterByFactions");

        Assert.Equal(2, values.Count);
        Assert.Equal("DA16OrcAmbushFaction", values[0]);
        Assert.Equal("DA16OrcDreamFaction", values[1]);
    }

    #endregion

    #region Filter variant methods

    [Fact]
    public void ExtractFilterValuesWithVariants_CombinesBothFormats()
    {
        var line =
            "filterByFactions=Skyrim.esm|0x100:filterByFactionsOr=Skyrim.esm|0x200,Skyrim.esm|0x300:outfitDefault=MyMod.esp|0x800";

        var values = SkyPatcherSyntax.ExtractFilterValuesWithVariants(line, "filterByFactions");

        Assert.Equal(3, values.Count);
        Assert.Equal("Skyrim.esm|0x100", values[0]);
        Assert.Equal("Skyrim.esm|0x200", values[1]);
        Assert.Equal("Skyrim.esm|0x300", values[2]);
    }

    [Fact]
    public void HasAnyVariant_Base_ReturnsTrue()
    {
        var line = "filterByFactions=Skyrim.esm|0x100:outfitDefault=MyMod.esp|0x800";

        Assert.True(SkyPatcherSyntax.HasAnyVariant(line, "filterByFactions"));
    }

    [Fact]
    public void HasAnyVariant_Or_ReturnsTrue()
    {
        var line = "filterByFactionsOr=Skyrim.esm|0x100:outfitDefault=MyMod.esp|0x800";

        Assert.True(SkyPatcherSyntax.HasAnyVariant(line, "filterByFactions"));
    }

    [Fact]
    public void HasAnyVariant_Excluded_ReturnsTrue()
    {
        var line = "filterByFactionsExcluded=Skyrim.esm|0x100:outfitDefault=MyMod.esp|0x800";

        Assert.True(SkyPatcherSyntax.HasAnyVariant(line, "filterByFactions"));
    }

    [Fact]
    public void HasAnyVariant_None_ReturnsFalse()
    {
        var line = "filterByNpcs=Skyrim.esm|0x100:outfitDefault=MyMod.esp|0x800";

        Assert.False(SkyPatcherSyntax.HasAnyVariant(line, "filterByFactions"));
    }

    #endregion

    #region Filter detection

    [Fact]
    public void GetAllFilterNames_ReturnsAllFilters()
    {
        var line = "filterByNpcs=Skyrim.esm|0x100:filterByFactions=Skyrim.esm|0x200:outfitDefault=MyMod.esp|0x800";

        var filters = SkyPatcherSyntax.GetAllFilterNames(line);

        Assert.Equal(3, filters.Count);
        Assert.Contains("filterByNpcs", filters);
        Assert.Contains("filterByFactions", filters);
        Assert.Contains("outfitDefault", filters);
    }

    [Fact]
    public void GetUnsupportedFilters_KnownFilters_ReturnsEmpty()
    {
        var line = "filterByFactionsOr=Faction1:filterByGender=female:outfitDefault=MyMod.esp|0x800";

        var unsupported = SkyPatcherSyntax.GetUnsupportedFilters(line);

        Assert.Empty(unsupported);
    }

    [Fact]
    public void GetUnsupportedFilters_UnknownFilter_ReturnsIt()
    {
        var line = "filterByCustomThing=Value:outfitDefault=MyMod.esp|0x800";

        var unsupported = SkyPatcherSyntax.GetUnsupportedFilters(line);

        Assert.Single(unsupported);
        Assert.Equal("filterByCustomThing", unsupported[0]);
    }

    [Fact]
    public void HasUnsupportedFilters_AllSupported_ReturnsFalse()
    {
        var line = "filterByFactionsOr=Faction1:outfitDefault=MyMod.esp|0x800";

        Assert.False(SkyPatcherSyntax.HasUnsupportedFilters(line));
    }

    [Fact]
    public void HasUnsupportedFilters_HasUnsupported_ReturnsTrue()
    {
        var line = "filterByUnknown=Value:outfitDefault=MyMod.esp|0x800";

        Assert.True(SkyPatcherSyntax.HasUnsupportedFilters(line));
    }

    #endregion
}
