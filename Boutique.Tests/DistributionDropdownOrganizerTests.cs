using Boutique.Models;
using Boutique.Utilities;
using Boutique.ViewModels;
using FluentAssertions;
using Xunit;

namespace Boutique.Tests;

public class DistributionDropdownOrganizerTests
{
    [Fact]
    public void Organize_EmptyList_ReturnsOnlyNewFileAction()
    {
        var result = DistributionDropdownOrganizer.Organize([]);

        Assert.Single(result.Items);
        var action = Assert.IsType<GroupedDropdownAction>(result.Items[0]);
        Assert.Equal(DistributionDropdownOrganizer.NewFileActionId, action.ActionId);
        Assert.Empty(result.GroupNames);
    }

    [Fact]
    public void Organize_SingleFile_NoGroupHeader_WhenNoModName()
    {
        var file = CreateFile("Test.ini", @"skse\plugins\SkyPatcher\npc\Test.ini");
        var result = DistributionDropdownOrganizer.Organize([file]);

        Assert.Equal(2, result.Items.Count);
        Assert.IsType<GroupedDropdownAction>(result.Items[0]);
        Assert.IsType<GroupedDropdownItem<DistributionFileInfo>>(result.Items[1]);

        var fileItem = (GroupedDropdownItem<DistributionFileInfo>)result.Items[1];
        Assert.Equal("Test.ini", fileItem.Value.FileName);
        Assert.Empty(result.GroupNames);
    }

    [Fact]
    public void Organize_FilesWithModName_AddsGroupHeader()
    {
        var file = CreateFile("Test.ini", @"skse\plugins\SkyPatcher\npc\MyMod\Test.ini");
        var result = DistributionDropdownOrganizer.Organize([file]);

        Assert.Equal(3, result.Items.Count);
        Assert.IsType<GroupedDropdownAction>(result.Items[0]);
        Assert.IsType<GroupedDropdownHeader>(result.Items[1]);
        Assert.IsType<GroupedDropdownItem<DistributionFileInfo>>(result.Items[2]);

        var header = (GroupedDropdownHeader)result.Items[1];
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

        var fileItems = result.Items.OfType<GroupedDropdownItem<DistributionFileInfo>>().ToList();
        Assert.Equal(2, fileItems.Count);

        Assert.Equal("Sentinel.esp.ini", fileItems.First(f => f.GroupName == "ModA").Value.UniquePath);
        Assert.Equal("Sentinel.esp.ini", fileItems.First(f => f.GroupName == "ModB").Value.UniquePath);
    }

    [Fact]
    public void Organize_UniqueFileNames_ShowsJustFileName()
    {
        var file1 = CreateFile("FileA.ini", @"skse\plugins\SkyPatcher\npc\ModA\FileA.ini");
        var file2 = CreateFile("FileB.ini", @"skse\plugins\SkyPatcher\npc\ModB\FileB.ini");

        var result = DistributionDropdownOrganizer.Organize([file1, file2]);

        var fileItems = result.Items.OfType<GroupedDropdownItem<DistributionFileInfo>>().ToList();

        Assert.Equal("FileA.ini", fileItems.First(f => f.GroupName == "ModA").Value.UniquePath);
        Assert.Equal("FileB.ini", fileItems.First(f => f.GroupName == "ModB").Value.UniquePath);
    }

    [Fact]
    public void Organize_MixedWithAndWithoutModName_UngroupedFirst()
    {
        var ungrouped = CreateFile("Direct.ini", @"skse\plugins\SkyPatcher\npc\Direct.ini");
        var grouped = CreateFile("Grouped.ini", @"skse\plugins\SkyPatcher\npc\ModA\Grouped.ini");

        var result = DistributionDropdownOrganizer.Organize([grouped, ungrouped]);

        Assert.Equal(4, result.Items.Count);
        Assert.IsType<GroupedDropdownAction>(result.Items[0]);
        Assert.IsType<GroupedDropdownItem<DistributionFileInfo>>(result.Items[1]);
        Assert.IsType<GroupedDropdownHeader>(result.Items[2]);
        Assert.IsType<GroupedDropdownItem<DistributionFileInfo>>(result.Items[3]);

        var firstFile = (GroupedDropdownItem<DistributionFileInfo>)result.Items[1];
        Assert.Equal("Direct.ini", firstFile.Value.FileName);
        Assert.Null(firstFile.GroupName);
    }

    [Fact]
    public void Organize_MultipleFilesInSameGroup_SortedAlphabetically()
    {
        var fileZ = CreateFile("Zebra.ini", @"skse\plugins\SkyPatcher\npc\ModA\Zebra.ini");
        var fileA = CreateFile("Alpha.ini", @"skse\plugins\SkyPatcher\npc\ModA\Alpha.ini");
        var fileM = CreateFile("Middle.ini", @"skse\plugins\SkyPatcher\npc\ModA\Middle.ini");

        var result = DistributionDropdownOrganizer.Organize([fileZ, fileA, fileM]);

        var fileItems = result.Items.OfType<GroupedDropdownItem<DistributionFileInfo>>().ToList();
        Assert.Equal(3, fileItems.Count);
        Assert.Equal("Alpha.ini", fileItems[0].Value.FileName);
        Assert.Equal("Middle.ini", fileItems[1].Value.FileName);
        Assert.Equal("Zebra.ini", fileItems[2].Value.FileName);
    }

    [Fact]
    public void Organize_GroupsSortedAlphabetically()
    {
        var fileZ = CreateFile("Test.ini", @"skse\plugins\SkyPatcher\npc\Zebra\Test.ini");
        var fileA = CreateFile("Test2.ini", @"skse\plugins\SkyPatcher\npc\Alpha\Test2.ini");

        var result = DistributionDropdownOrganizer.Organize([fileZ, fileA]);

        var headers = result.Items.OfType<GroupedDropdownHeader>().ToList();
        Assert.Equal(2, headers.Count);
        Assert.Equal("Alpha", headers[0].GroupName);
        Assert.Equal("Zebra", headers[1].GroupName);
    }

    [Fact]
    public void Organize_FolderAboveSKSE_UsesAsModName()
    {
        var file = CreateFile("Test.ini", @"SomeArmorMod\skse\plugins\SkyPatcher\npc\Test.ini");
        var result = DistributionDropdownOrganizer.Organize([file]);

        var headers = result.Items.OfType<GroupedDropdownHeader>().ToList();
        Assert.Single(headers);
        Assert.Equal("SomeArmorMod", headers[0].GroupName);
    }

    [Fact]
    public void Organize_NestedSubfolders_ShowsPathWithoutGroupPrefix()
    {
        var file1 = CreateFile("Config.ini", @"skse\plugins\SkyPatcher\npc\Sentinel\Guards\Config.ini");
        var file2 = CreateFile("Config.ini", @"skse\plugins\SkyPatcher\npc\Sentinel\Patrols\Config.ini");

        var result = DistributionDropdownOrganizer.Organize([file1, file2]);

        var fileItems = result.Items.OfType<GroupedDropdownItem<DistributionFileInfo>>().ToList();
        Assert.Equal(2, fileItems.Count);

        Assert.Equal("Guards/Config.ini", fileItems[0].Value.UniquePath);
        Assert.Equal("Patrols/Config.ini", fileItems[1].Value.UniquePath);
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
