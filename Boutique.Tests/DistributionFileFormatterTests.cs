using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Tests for DistributionFileFormatter to ensure consistent file output.
/// </summary>
public class DistributionFileFormatterTests
{
    #region SPID Line Format Structure Tests

    [Fact]
    public void FormatSpidLine_ThrowsIfNoOutfit()
    {
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        var act = () => DistributionFileFormatter.FormatSpidLine(vm);
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region SkyPatcher Line Format Structure Tests

    [Fact]
    public void FormatSkyPatcherLine_ThrowsIfNoOutfit()
    {
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        var act = () => DistributionFileFormatter.FormatSkyPatcherLine(vm);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FormatSpidExclusiveGroupLine_FormatsCorrectly()
    {
        var entry = new DistributionEntry
        {
            Type = DistributionType.ExclusiveGroup,
            ExclusiveGroupName = "Primary Weapon",
            ExclusiveGroupForms = ["CoolSword", "DecentAxe", "OPBow"]
        };
        var vm = new DistributionEntryViewModel(entry);

        var line = DistributionFileFormatter.FormatSpidExclusiveGroupLine(vm);

        line.Should().Be("ExclusiveGroup = Primary Weapon|CoolSword,DecentAxe,OPBow");
    }

    #endregion

    #region FormatTraitFilters Tests

    [Fact]
    public void FormatTraitFilters_EmptyTraits_ReturnsNull()
    {
        var traits = new SpidTraitFilters();

        var result = DistributionFileFormatter.FormatTraitFilters(traits);

        result.Should().BeNull();
    }

    [Fact]
    public void FormatTraitFilters_FemaleOnly_ReturnsF()
    {
        var traits = new SpidTraitFilters { IsFemale = true };

        var result = DistributionFileFormatter.FormatTraitFilters(traits);

        result.Should().Be("F");
    }

    [Fact]
    public void FormatTraitFilters_MaleOnly_ReturnsM()
    {
        var traits = new SpidTraitFilters { IsFemale = false };

        var result = DistributionFileFormatter.FormatTraitFilters(traits);

        result.Should().Be("M");
    }

    [Fact]
    public void FormatTraitFilters_UniqueOnly_ReturnsU()
    {
        var traits = new SpidTraitFilters { IsUnique = true };

        var result = DistributionFileFormatter.FormatTraitFilters(traits);

        result.Should().Be("U");
    }

    [Fact]
    public void FormatTraitFilters_NonUnique_ReturnsNegativeU()
    {
        var traits = new SpidTraitFilters { IsUnique = false };

        var result = DistributionFileFormatter.FormatTraitFilters(traits);

        result.Should().Be("-U");
    }

    [Fact]
    public void FormatTraitFilters_ChildOnly_ReturnsC()
    {
        var traits = new SpidTraitFilters { IsChild = true };

        var result = DistributionFileFormatter.FormatTraitFilters(traits);

        result.Should().Be("C");
    }

    [Fact]
    public void FormatTraitFilters_NotChild_ReturnsNegativeC()
    {
        var traits = new SpidTraitFilters { IsChild = false };

        var result = DistributionFileFormatter.FormatTraitFilters(traits);

        result.Should().Be("-C");
    }

    [Fact]
    public void FormatTraitFilters_MultipleTraits_JoinsWithSlash()
    {
        var traits = new SpidTraitFilters { IsFemale = true, IsUnique = false, IsChild = false };

        var result = DistributionFileFormatter.FormatTraitFilters(traits);

        result.Should().Be("F/-U/-C");
    }

    [Fact]
    public void FormatTraitFilters_AllPositiveTraits_FormatsCorrectly()
    {
        var traits = new SpidTraitFilters
        {
            IsFemale = true,
            IsUnique = true,
            IsSummonable = true,
            IsChild = true,
            IsLeveled = true,
            IsTeammate = true,
            IsDead = true
        };

        var result = DistributionFileFormatter.FormatTraitFilters(traits);

        result.Should().Be("F/U/S/C/L/T/D");
    }

    [Fact]
    public void FormatTraitFilters_AllNegativeTraits_FormatsCorrectly()
    {
        var traits = new SpidTraitFilters
        {
            IsFemale = false,
            IsUnique = false,
            IsSummonable = false,
            IsChild = false,
            IsLeveled = false,
            IsTeammate = false,
            IsDead = false
        };

        var result = DistributionFileFormatter.FormatTraitFilters(traits);

        result.Should().Be("M/-U/-S/-C/-L/-T/-D");
    }

    #endregion

    #region GenerateFileContent Tests

    [Fact]
    public void GenerateFileContent_EmptyEntries_ReturnsHeaderOnly()
    {
        var entries = Array.Empty<DistributionEntryViewModel>();

        var result = DistributionFileFormatter.GenerateFileContent(entries, DistributionFileType.Spid);

        result.Should().Contain("; Distribution File");
        result.Should().Contain("; Generated by Boutique:");
        result.Should().Contain("; Last Modified:");
    }

    [Fact]
    public void GenerateFileContent_SpidFormat_ContainsHeader()
    {
        var entries = Array.Empty<DistributionEntryViewModel>();

        var result = DistributionFileFormatter.GenerateFileContent(entries, DistributionFileType.Spid);

        var lines = result.Split(Environment.NewLine);
        lines[0].Should().Be("; Distribution File");
        lines[1].Should().StartWith("; Generated by Boutique:");
        lines[2].Should().StartWith("; Last Modified:");
        lines[3].Should().BeEmpty();
    }

    [Fact]
    public void GenerateFileContent_SkyPatcherFormat_ContainsHeader()
    {
        var entries = Array.Empty<DistributionEntryViewModel>();

        var result = DistributionFileFormatter.GenerateFileContent(entries, DistributionFileType.SkyPatcher);

        var lines = result.Split(Environment.NewLine);
        lines[0].Should().Be("; Distribution File");
        lines[1].Should().StartWith("; Generated by Boutique:");
        lines[2].Should().StartWith("; Last Modified:");
    }

    [Fact]
    public void GenerateFileContent_NullOutfitEntry_IsSkipped()
    {
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        var result = DistributionFileFormatter.GenerateFileContent([vm], DistributionFileType.Spid);

        var outfitLines = result.Split(Environment.NewLine).Where(l => l.StartsWith("Outfit")).ToList();
        outfitLines.Should().BeEmpty();
    }

    #endregion
}
