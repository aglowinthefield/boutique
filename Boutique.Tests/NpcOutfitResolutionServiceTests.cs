using Boutique.Models;
using Boutique.Services;
using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Tests for the NPC outfit distribution simulation core: winner selection ("last
///     qualifying distribution wins") and conflict marking, including the identical-row
///     edge case where value-equality comparisons would mark the wrong row.
/// </summary>
public class NpcOutfitResolutionServiceTests
{
    
    private static readonly FormKey Outfit1 = new(ModKey.FromNameAndExtension("Skyrim.esm"), 0x800);
    private static readonly FormKey Outfit2 = new(ModKey.FromNameAndExtension("Skyrim.esm"), 0x801);
    private static readonly FormKey NpcKey  = new(ModKey.FromNameAndExtension("Skyrim.esm"), 0x900);

    private static OutfitDistribution Distribution(
        string fileName,
        DistributionFileType fileType,
        FormKey outfit,
        int processingOrder,
        string? editorId = null) =>
        new(
            FilePath: $"Data\\{fileName}",
            FileName: fileName,
            FileType: fileType,
            OutfitFormKey: outfit,
            OutfitEditorId: editorId,
            ProcessingOrder: processingOrder,
            IsWinner: false);

    private static Dictionary<FormKey, List<OutfitDistribution>> SingleNpc(params OutfitDistribution[] distributions) =>
        new() { [NpcKey] = [.. distributions] };

    [Fact]
    public void LastIniDistributionByProcessingOrder_IsWinner()
    {
        var distributions = SingleNpc(
            Distribution("b_distr.ini", DistributionFileType.Spid, Outfit1, processingOrder: 1),
            Distribution("a_distr.ini", DistributionFileType.Spid, Outfit2, processingOrder: 2));

        var assignments = NpcOutfitResolutionService.BuildNpcOutfitAssignmentsCore(
            distributions,
            new Dictionary<FormKey, NpcOutfitResolutionService.NpcBasicInfo>());

        assignments.Should().ContainSingle();
        var assignment = assignments[0];
        assignment.FinalOutfitFormKey.Should().Be(Outfit2);
        assignment.Distributions.Should().ContainSingle(d => d.IsWinner)
                  .Which.OutfitFormKey.Should().Be(Outfit2);
    }

    [Fact]
    public void IdenticalRows_DifferentFiles_WinnerIsLaterFile()
    {
        // Two byte-identical distributions except file name/order: the later one must be
        // marked as the winner, not whichever row happens to compare equal first.
        var distributions = SingleNpc(
            Distribution("a_distr.ini", DistributionFileType.Spid, Outfit1, processingOrder: 1, "SameOutfit"),
            Distribution("b_distr.ini", DistributionFileType.Spid, Outfit1, processingOrder: 2, "SameOutfit"));

        var assignments = NpcOutfitResolutionService.BuildNpcOutfitAssignmentsCore(
            distributions,
            new Dictionary<FormKey, NpcOutfitResolutionService.NpcBasicInfo>());

        assignments[0].Distributions.Should().ContainSingle(d => d.IsWinner)
                      .Which.FileName.Should().Be("b_distr.ini");
    }

    [Fact]
    public void EspDistribution_LosesToAnyIniDistribution()
    {
        var distributions = SingleNpc(
            Distribution("Patch.esp", DistributionFileType.Esp, Outfit1, processingOrder: 1),
            Distribution("z_distr.ini", DistributionFileType.Spid, Outfit2, processingOrder: 2));

        var assignments = NpcOutfitResolutionService.BuildNpcOutfitAssignmentsCore(
            distributions,
            new Dictionary<FormKey, NpcOutfitResolutionService.NpcBasicInfo>());

        var assignment = assignments[0];
        assignment.FinalOutfitFormKey.Should().Be(Outfit2, "INI distributions always beat ESP-provided outfits");
        assignment.Distributions.Should().ContainSingle(d => d.IsWinner)
                  .Which.FileType.Should().Be(DistributionFileType.Spid);
    }

    [Fact]
    public void EspOnlyDistribution_IsWinnerWhenNoIniExists()
    {
        var distributions = SingleNpc(
            Distribution("Patch.esp", DistributionFileType.Esp, Outfit1, processingOrder: 1));

        var assignments = NpcOutfitResolutionService.BuildNpcOutfitAssignmentsCore(
            distributions,
            new Dictionary<FormKey, NpcOutfitResolutionService.NpcBasicInfo>());

        var assignment = assignments[0];
        assignment.FinalOutfitFormKey.Should().Be(Outfit1);
        assignment.Distributions.Should().ContainSingle(d => d.IsWinner);
    }

    [Fact]
    public void MultipleIniDistributions_AreMarkedAsConflict()
    {
        var distributions = SingleNpc(
            Distribution("a_distr.ini", DistributionFileType.Spid, Outfit1, processingOrder: 1),
            Distribution("b_distr.ini", DistributionFileType.Spid, Outfit2, processingOrder: 2));

        var assignments = NpcOutfitResolutionService.BuildNpcOutfitAssignmentsCore(
            distributions,
            new Dictionary<FormKey, NpcOutfitResolutionService.NpcBasicInfo>());

        assignments[0].HasConflict.Should().BeTrue();
    }

    [Fact]
    public void SingleDistribution_NoConflict()
    {
        var distributions = SingleNpc(
            Distribution("a_distr.ini", DistributionFileType.Spid, Outfit1, processingOrder: 1));

        var assignments = NpcOutfitResolutionService.BuildNpcOutfitAssignmentsCore(
            distributions,
            new Dictionary<FormKey, NpcOutfitResolutionService.NpcBasicInfo>());

        assignments[0].HasConflict.Should().BeFalse();
    }

    [Fact]
    public void NpcNotInLookup_UsesFormKeyOriginModAndNoNames()
    {
        var distributions = SingleNpc(
            Distribution("a_distr.ini", DistributionFileType.Spid, Outfit1, processingOrder: 1));

        var assignments = NpcOutfitResolutionService.BuildNpcOutfitAssignmentsCore(
            distributions,
            new Dictionary<FormKey, NpcOutfitResolutionService.NpcBasicInfo>());

        var assignment = assignments[0];
        assignment.EditorId.Should().BeNull();
        assignment.Name.Should().BeNull();
        assignment.SourceMod.Should().Be(NpcKey.ModKey);
    }

    [Fact]
    public void NpcInLookup_UsesLookupValues()
    {
        var distributions = SingleNpc(
            Distribution("a_distr.ini", DistributionFileType.Spid, Outfit1, processingOrder: 1));
        var lookup = new Dictionary<FormKey, NpcOutfitResolutionService.NpcBasicInfo>
        {
            [NpcKey] = new("YsoldaEditorId", "Ysolda", ModKey.FromNameAndExtension("Overhaul.esp"))
        };

        var assignments = NpcOutfitResolutionService.BuildNpcOutfitAssignmentsCore(distributions, lookup);

        var assignment = assignments[0];
        assignment.EditorId.Should().Be("YsoldaEditorId");
        assignment.Name.Should().Be("Ysolda");
        assignment.SourceMod.Should().Be(ModKey.FromNameAndExtension("Overhaul.esp"));
    }
}
