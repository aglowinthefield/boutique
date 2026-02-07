using Boutique.Models;
using Boutique.ViewModels;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Tests for DistributionEntryViewModel to verify trait filter changes
///     are properly propagated to the underlying Entry.
/// </summary>
public class DistributionEntryViewModelTests
{
    [Fact]
    public void Gender_WhenChangedToFemale_UpdatesEntryTraitFilters()
    {
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        vm.Gender = GenderFilter.Female;

        entry.TraitFilters.IsFemale.Should().BeTrue();
    }

    [Fact]
    public void Gender_WhenChangedToMale_UpdatesEntryTraitFilters()
    {
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        vm.Gender = GenderFilter.Male;

        entry.TraitFilters.IsFemale.Should().BeFalse();
    }

    [Fact]
    public void Gender_WhenChangedToAny_UpdatesEntryTraitFiltersToNull()
    {
        var entry = new DistributionEntry { TraitFilters = new SpidTraitFilters { IsFemale = true } };

        _ = new DistributionEntryViewModel(entry)
        {
            Gender = GenderFilter.Any
        };

        entry.TraitFilters.IsFemale.Should().BeNull();
    }

    [Fact]
    public void Unique_WhenChangedToUniqueOnly_UpdatesEntryTraitFilters()
    {
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        vm.Unique = UniqueFilter.UniqueOnly;

        entry.TraitFilters.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Unique_WhenChangedToNonUniqueOnly_UpdatesEntryTraitFilters()
    {
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        vm.Unique = UniqueFilter.NonUniqueOnly;

        entry.TraitFilters.IsUnique.Should().BeFalse();
    }

    [Fact]
    public void IsChild_WhenChanged_UpdatesEntryTraitFilters()
    {
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        vm.IsChild = true;

        entry.TraitFilters.IsChild.Should().BeTrue();
    }

    [Fact]
    public void Gender_InitializesFromEntry_Female()
    {
        var entry = new DistributionEntry { TraitFilters = new SpidTraitFilters { IsFemale = true } };

        var vm = new DistributionEntryViewModel(entry);

        vm.Gender.Should().Be(GenderFilter.Female);
    }

    [Fact]
    public void Gender_InitializesFromEntry_Male()
    {
        var entry = new DistributionEntry { TraitFilters = new SpidTraitFilters { IsFemale = false } };

        var vm = new DistributionEntryViewModel(entry);

        vm.Gender.Should().Be(GenderFilter.Male);
    }

    [Fact]
    public void Gender_InitializesFromEntry_Any()
    {
        var entry = new DistributionEntry { TraitFilters = new SpidTraitFilters { IsFemale = null } };

        var vm = new DistributionEntryViewModel(entry);

        vm.Gender.Should().Be(GenderFilter.Any);
    }

    [Fact]
    public void LevelFilters_InitializesStructuredSkillMode_FromSpidSkillSyntax()
    {
        var entry = new DistributionEntry { LevelFilters = "14(50/50)" };

        var vm = new DistributionEntryViewModel(entry);

        vm.LevelFilterMode.Should().Be(LevelFilterMode.SkillLevel);
        vm.SelectedLevelSkill!.Index.Should().Be(14);
        vm.LevelFilterMin.Should().Be("50");
        vm.LevelFilterMax.Should().Be("50");
    }

    [Fact]
    public void LevelFilters_WhenUsingSkillWeightUi_RebuildsSpidSyntax()
    {
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        vm.LevelFilterMode = LevelFilterMode.SkillWeight;
        vm.SelectedLevelSkill = DistributionEntryViewModel.SkillFilterOptions.First(s => s.Index == 2);
        vm.LevelFilterMin = "2";
        vm.LevelFilterMax = "3";

        vm.LevelFilters.Should().Be("w2(2/3)");
        entry.LevelFilters.Should().Be("w2(2/3)");
    }

    [Fact]
    public void LevelFilters_UnsupportedSyntax_FallsBackToRawMode()
    {
        var entry = new DistributionEntry { LevelFilters = "14(50/50),5/10" };

        var vm = new DistributionEntryViewModel(entry);

        vm.LevelFilterMode.Should().Be(LevelFilterMode.Raw);
        vm.LevelFilters.Should().Be("14(50/50),5/10");
    }
}
