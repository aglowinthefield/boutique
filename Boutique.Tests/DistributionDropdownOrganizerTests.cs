using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

public class DistributionDropdownOrganizerTests
{
    [Fact]
    public void Organize_EmptyList_ReturnsOnlyNewFileItem()
    {
        var result = DistributionDropdownOrganizer.Organize([]);

        result.Items.Should().ContainSingle()
            .Which.Should().BeOfType<DistributionNewFileItem>();
        result.GroupNames.Should().BeEmpty();
    }

    [Fact]
    public void Organize_SingleFile_NoGroupHeader_WhenNoModName()
    {
        var file = CreateFile("Test.ini", @"skse\plugins\SkyPatcher\npc\Test.ini");
        var result = DistributionDropdownOrganizer.Organize([file]);

        result.Items.Should().HaveCount(2);
        result.Items[0].Should().BeOfType<DistributionNewFileItem>();
        result.Items[1].Should().BeOfType<DistributionFileItem>()
            .Which.FileName.Should().Be("Test.ini");
        result.GroupNames.Should().BeEmpty();
    }

    [Fact]
    public void Organize_FilesWithModName_AddsGroupHeader()
    {
        var file = CreateFile("Test.ini", @"skse\plugins\SkyPatcher\npc\MyMod\Test.ini");
        var result = DistributionDropdownOrganizer.Organize([file]);

        result.Items.Should().HaveCount(3);
        result.Items[0].Should().BeOfType<DistributionNewFileItem>();
        result.Items[1].Should().BeOfType<DistributionGroupHeader>()
            .Which.GroupName.Should().Be("MyMod");
        result.Items[2].Should().BeOfType<DistributionFileItem>();
        result.GroupNames.Should().ContainSingle().Which.Should().Be("MyMod");
    }

    [Fact]
    public void Organize_DuplicateFileNames_ShowsUniquePathWithoutGroupPrefix()
    {
        var file1 = CreateFile("Sentinel.esp.ini", @"skse\plugins\SkyPatcher\npc\ModA\Sentinel.esp.ini");
        var file2 = CreateFile("Sentinel.esp.ini", @"skse\plugins\SkyPatcher\npc\ModB\Sentinel.esp.ini");

        var result = DistributionDropdownOrganizer.Organize([file1, file2]);

        var fileItems = result.Items.OfType<DistributionFileItem>().ToList();
        fileItems.Should().HaveCount(2);
        fileItems.First(f => f.GroupName == "ModA").UniquePath.Should().Be("Sentinel.esp.ini");
        fileItems.First(f => f.GroupName == "ModB").UniquePath.Should().Be("Sentinel.esp.ini");
    }

    [Fact]
    public void Organize_UniqueFileNames_ShowsJustFileName()
    {
        var file1 = CreateFile("FileA.ini", @"skse\plugins\SkyPatcher\npc\ModA\FileA.ini");
        var file2 = CreateFile("FileB.ini", @"skse\plugins\SkyPatcher\npc\ModB\FileB.ini");

        var result = DistributionDropdownOrganizer.Organize([file1, file2]);

        var fileItems = result.Items.OfType<DistributionFileItem>().ToList();
        fileItems.First(f => f.GroupName == "ModA").UniquePath.Should().Be("FileA.ini");
        fileItems.First(f => f.GroupName == "ModB").UniquePath.Should().Be("FileB.ini");
    }

    [Fact]
    public void Organize_MixedWithAndWithoutModName_UngroupedFirst()
    {
        var ungrouped = CreateFile("Direct.ini", @"skse\plugins\SkyPatcher\npc\Direct.ini");
        var grouped = CreateFile("Grouped.ini", @"skse\plugins\SkyPatcher\npc\ModA\Grouped.ini");

        var result = DistributionDropdownOrganizer.Organize([grouped, ungrouped]);

        result.Items.Should().HaveCount(4);
        result.Items[0].Should().BeOfType<DistributionNewFileItem>();
        result.Items[1].Should().BeOfType<DistributionFileItem>()
            .Which.Should().Match<DistributionFileItem>(f =>
                f.FileName == "Direct.ini" && f.GroupName == "");
        result.Items[2].Should().BeOfType<DistributionGroupHeader>();
        result.Items[3].Should().BeOfType<DistributionFileItem>();
    }

    [Fact]
    public void Organize_MultipleFilesInSameGroup_SortedAlphabetically()
    {
        var fileZ = CreateFile("Zebra.ini", @"skse\plugins\SkyPatcher\npc\ModA\Zebra.ini");
        var fileA = CreateFile("Alpha.ini", @"skse\plugins\SkyPatcher\npc\ModA\Alpha.ini");
        var fileM = CreateFile("Middle.ini", @"skse\plugins\SkyPatcher\npc\ModA\Middle.ini");

        var result = DistributionDropdownOrganizer.Organize([fileZ, fileA, fileM]);

        var fileItems = result.Items.OfType<DistributionFileItem>().ToList();
        fileItems.Should().HaveCount(3);
        fileItems.Select(f => f.FileName).Should().ContainInOrder("Alpha.ini", "Middle.ini", "Zebra.ini");
    }

    [Fact]
    public void Organize_GroupsSortedAlphabetically()
    {
        var fileZ = CreateFile("Test.ini", @"skse\plugins\SkyPatcher\npc\Zebra\Test.ini");
        var fileA = CreateFile("Test2.ini", @"skse\plugins\SkyPatcher\npc\Alpha\Test2.ini");

        var result = DistributionDropdownOrganizer.Organize([fileZ, fileA]);

        var headers = result.Items.OfType<DistributionGroupHeader>().ToList();
        headers.Should().HaveCount(2);
        headers.Select(h => h.GroupName).Should().ContainInOrder("Alpha", "Zebra");
    }

    [Fact]
    public void Organize_FolderAboveSKSE_UsesAsModName()
    {
        var file = CreateFile("Test.ini", @"SomeArmorMod\skse\plugins\SkyPatcher\npc\Test.ini");
        var result = DistributionDropdownOrganizer.Organize([file]);

        var headers = result.Items.OfType<DistributionGroupHeader>().ToList();
        headers.Should().ContainSingle()
            .Which.GroupName.Should().Be("SomeArmorMod");
    }

    [Fact]
    public void Organize_NestedSubfolders_ShowsPathWithoutGroupPrefix()
    {
        var file1 = CreateFile("Config.ini", @"skse\plugins\SkyPatcher\npc\Sentinel\Guards\Config.ini");
        var file2 = CreateFile("Config.ini", @"skse\plugins\SkyPatcher\npc\Sentinel\Patrols\Config.ini");

        var result = DistributionDropdownOrganizer.Organize([file1, file2]);

        var fileItems = result.Items.OfType<DistributionFileItem>().ToList();
        fileItems.Should().HaveCount(2);
        fileItems.Select(f => f.UniquePath).Should().Contain(["Guards/Config.ini", "Patrols/Config.ini"]);
    }

    private static DistributionFileViewModel CreateFile(string fileName, string relativePath)
    {
        var fullPath = Path.Combine(@"C:\Data", relativePath);
        var distributionFile = new DistributionFile(
            fileName,
            fullPath,
            relativePath,
            DistributionFileType.SkyPatcher,
            [],
            0);

        return new DistributionFileViewModel(distributionFile);
    }
}
