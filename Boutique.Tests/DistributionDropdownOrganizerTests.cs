using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using Xunit;

namespace Boutique.Tests;

public class DistributionDropdownOrganizerTests
{
    [Fact]
    public void Organize_EmptyList_ReturnsOnlyNewFileItem()
    {
        var result = DistributionDropdownOrganizer.Organize([]);

        Assert.Single(result.Items);
        Assert.IsType<DistributionNewFileItem>(result.Items[0]);
        Assert.Empty(result.GroupNames);
    }

    [Fact]
    public void Organize_SingleFile_NoGroupHeader_WhenNoModName()
    {
        // File directly in npc folder - no subfolder = no mod name
        var file = CreateFile("Test.ini", @"skse\plugins\SkyPatcher\npc\Test.ini");
        var result = DistributionDropdownOrganizer.Organize([file]);

        Assert.Equal(2, result.Items.Count);
        Assert.IsType<DistributionNewFileItem>(result.Items[0]);
        Assert.IsType<DistributionFileItem>(result.Items[1]);

        var fileItem = (DistributionFileItem)result.Items[1];
        Assert.Equal("Test.ini", fileItem.FileName);
        Assert.Empty(result.GroupNames);
    }

    [Fact]
    public void Organize_FilesWithModName_AddsGroupHeader()
    {
        // File in a subfolder - subfolder becomes mod name
        var file = CreateFile("Test.ini", @"skse\plugins\SkyPatcher\npc\MyMod\Test.ini");
        var result = DistributionDropdownOrganizer.Organize([file]);

        Assert.Equal(3, result.Items.Count);
        Assert.IsType<DistributionNewFileItem>(result.Items[0]);
        Assert.IsType<DistributionGroupHeader>(result.Items[1]);
        Assert.IsType<DistributionFileItem>(result.Items[2]);

        var header = (DistributionGroupHeader)result.Items[1];
        Assert.Equal("MyMod", header.GroupName);
        Assert.Single(result.GroupNames);
        Assert.Equal("MyMod", result.GroupNames[0]);
    }

    [Fact]
    public void Organize_DuplicateFileNames_ShowsUniquePathWithoutGroupPrefix()
    {
        var file1 = CreateFile("Sentinel.esp.ini", @"skse\plugins\SkyPatcher\npc\ModA\Sentinel.esp.ini");
        var file2 = CreateFile("Sentinel.esp.ini", @"skse\plugins\SkyPatcher\npc\ModB\Sentinel.esp.ini");

        var result = DistributionDropdownOrganizer.Organize([file1, file2]);

        var fileItems = result.Items.OfType<DistributionFileItem>().ToList();
        Assert.Equal(2, fileItems.Count);

        // Group prefix should be stripped since header shows it - just the filename remains
        Assert.Equal("Sentinel.esp.ini", fileItems.First(f => f.GroupName == "ModA").UniquePath);
        Assert.Equal("Sentinel.esp.ini", fileItems.First(f => f.GroupName == "ModB").UniquePath);
    }

    [Fact]
    public void Organize_UniqueFileNames_ShowsJustFileName()
    {
        var file1 = CreateFile("FileA.ini", @"skse\plugins\SkyPatcher\npc\ModA\FileA.ini");
        var file2 = CreateFile("FileB.ini", @"skse\plugins\SkyPatcher\npc\ModB\FileB.ini");

        var result = DistributionDropdownOrganizer.Organize([file1, file2]);

        var fileItems = result.Items.OfType<DistributionFileItem>().ToList();

        // Unique names should show just the filename
        Assert.Equal("FileA.ini", fileItems.First(f => f.GroupName == "ModA").UniquePath);
        Assert.Equal("FileB.ini", fileItems.First(f => f.GroupName == "ModB").UniquePath);
    }

    [Fact]
    public void Organize_MixedWithAndWithoutModName_UngroupedFirst()
    {
        var ungrouped = CreateFile("Direct.ini", @"skse\plugins\SkyPatcher\npc\Direct.ini");
        var grouped = CreateFile("Grouped.ini", @"skse\plugins\SkyPatcher\npc\ModA\Grouped.ini");

        var result = DistributionDropdownOrganizer.Organize([grouped, ungrouped]);

        // Order: NewFile, ungrouped file (no header), header for ModA, grouped file
        Assert.Equal(4, result.Items.Count);
        Assert.IsType<DistributionNewFileItem>(result.Items[0]);
        Assert.IsType<DistributionFileItem>(result.Items[1]); // Ungrouped file comes first
        Assert.IsType<DistributionGroupHeader>(result.Items[2]);
        Assert.IsType<DistributionFileItem>(result.Items[3]);

        var firstFile = (DistributionFileItem)result.Items[1];
        Assert.Equal("Direct.ini", firstFile.FileName);
        Assert.Equal("", firstFile.GroupName);
    }

    [Fact]
    public void Organize_MultipleFilesInSameGroup_SortedAlphabetically()
    {
        var fileZ = CreateFile("Zebra.ini", @"skse\plugins\SkyPatcher\npc\ModA\Zebra.ini");
        var fileA = CreateFile("Alpha.ini", @"skse\plugins\SkyPatcher\npc\ModA\Alpha.ini");
        var fileM = CreateFile("Middle.ini", @"skse\plugins\SkyPatcher\npc\ModA\Middle.ini");

        var result = DistributionDropdownOrganizer.Organize([fileZ, fileA, fileM]);

        var fileItems = result.Items.OfType<DistributionFileItem>().ToList();
        Assert.Equal(3, fileItems.Count);
        Assert.Equal("Alpha.ini", fileItems[0].FileName);
        Assert.Equal("Middle.ini", fileItems[1].FileName);
        Assert.Equal("Zebra.ini", fileItems[2].FileName);
    }

    [Fact]
    public void Organize_GroupsSortedAlphabetically()
    {
        var fileZ = CreateFile("Test.ini", @"skse\plugins\SkyPatcher\npc\Zebra\Test.ini");
        var fileA = CreateFile("Test2.ini", @"skse\plugins\SkyPatcher\npc\Alpha\Test2.ini");

        var result = DistributionDropdownOrganizer.Organize([fileZ, fileA]);

        var headers = result.Items.OfType<DistributionGroupHeader>().ToList();
        Assert.Equal(2, headers.Count);
        Assert.Equal("Alpha", headers[0].GroupName);
        Assert.Equal("Zebra", headers[1].GroupName);
    }

    [Fact]
    public void Organize_FolderAboveSKSE_UsesAsModeNam()
    {
        // When a mod installs SKSE folder, the folder above it is the mod name
        var file = CreateFile("Test.ini", @"SomeArmorMod\skse\plugins\SkyPatcher\npc\Test.ini");
        var result = DistributionDropdownOrganizer.Organize([file]);

        var headers = result.Items.OfType<DistributionGroupHeader>().ToList();
        Assert.Single(headers);
        Assert.Equal("SomeArmorMod", headers[0].GroupName);
    }

    [Fact]
    public void Organize_NestedSubfolders_ShowsPathWithoutGroupPrefix()
    {
        // Duplicate filenames in nested subfolders under same group
        var file1 = CreateFile("Config.ini", @"skse\plugins\SkyPatcher\npc\Sentinel\Guards\Config.ini");
        var file2 = CreateFile("Config.ini", @"skse\plugins\SkyPatcher\npc\Sentinel\Patrols\Config.ini");

        var result = DistributionDropdownOrganizer.Organize([file1, file2]);

        var fileItems = result.Items.OfType<DistributionFileItem>().ToList();
        Assert.Equal(2, fileItems.Count);

        // Group is "Sentinel", so paths should be relative to that
        Assert.Equal("Guards/Config.ini", fileItems[0].UniquePath);
        Assert.Equal("Patrols/Config.ini", fileItems[1].UniquePath);
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
