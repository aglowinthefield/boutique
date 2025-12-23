using Boutique.Models;
using Boutique.ViewModels;
using Xunit;

namespace Boutique.Tests;

/// <summary>
/// Tests for DistributionEntryViewModel to verify trait filter changes
/// are properly propagated to the underlying Entry.
/// </summary>
public class DistributionEntryViewModelTests
{
    [Fact]
    public void Gender_WhenChangedToFemale_UpdatesEntryTraitFilters()
    {
        // Arrange
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        // Act
        vm.Gender = GenderFilter.Female;

        // Assert
        Assert.True(entry.TraitFilters.IsFemale);
    }

    [Fact]
    public void Gender_WhenChangedToMale_UpdatesEntryTraitFilters()
    {
        // Arrange
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        // Act
        vm.Gender = GenderFilter.Male;

        // Assert
        Assert.False(entry.TraitFilters.IsFemale);
    }

    [Fact]
    public void Gender_WhenChangedToAny_UpdatesEntryTraitFiltersToNull()
    {
        // Arrange
        var entry = new DistributionEntry { TraitFilters = new SpidTraitFilters { IsFemale = true } };
        var vm = new DistributionEntryViewModel(entry);

        // Act
        vm.Gender = GenderFilter.Any;

        // Assert
        Assert.Null(entry.TraitFilters.IsFemale);
    }

    [Fact]
    public void Unique_WhenChangedToUniqueOnly_UpdatesEntryTraitFilters()
    {
        // Arrange
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        // Act
        vm.Unique = UniqueFilter.UniqueOnly;

        // Assert
        Assert.True(entry.TraitFilters.IsUnique);
    }

    [Fact]
    public void Unique_WhenChangedToNonUniqueOnly_UpdatesEntryTraitFilters()
    {
        // Arrange
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        // Act
        vm.Unique = UniqueFilter.NonUniqueOnly;

        // Assert
        Assert.False(entry.TraitFilters.IsUnique);
    }

    [Fact]
    public void IsChild_WhenChanged_UpdatesEntryTraitFilters()
    {
        // Arrange
        var entry = new DistributionEntry();
        var vm = new DistributionEntryViewModel(entry);

        // Act
        vm.IsChild = true;

        // Assert
        Assert.True(entry.TraitFilters.IsChild);
    }

    [Fact]
    public void Gender_InitializesFromEntry_Female()
    {
        // Arrange
        var entry = new DistributionEntry { TraitFilters = new SpidTraitFilters { IsFemale = true } };

        // Act
        var vm = new DistributionEntryViewModel(entry);

        // Assert
        Assert.Equal(GenderFilter.Female, vm.Gender);
    }

    [Fact]
    public void Gender_InitializesFromEntry_Male()
    {
        // Arrange
        var entry = new DistributionEntry { TraitFilters = new SpidTraitFilters { IsFemale = false } };

        // Act
        var vm = new DistributionEntryViewModel(entry);

        // Assert
        Assert.Equal(GenderFilter.Male, vm.Gender);
    }

    [Fact]
    public void Gender_InitializesFromEntry_Any()
    {
        // Arrange
        var entry = new DistributionEntry { TraitFilters = new SpidTraitFilters { IsFemale = null } };

        // Act
        var vm = new DistributionEntryViewModel(entry);

        // Assert
        Assert.Equal(GenderFilter.Any, vm.Gender);
    }
}
