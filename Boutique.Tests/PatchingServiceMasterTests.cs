using Boutique.Services;
using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Tests for patch master-list hygiene: collecting the masters a patch actually
///     depends on and pruning unused ones from the header.
/// </summary>
public class PatchingServiceMasterTests
{
    private static readonly ModKey SkyrimMaster = ModKey.FromFileName("Skyrim.esm");
    private static readonly ModKey ArmorMaster  = ModKey.FromNameAndExtension("ArmorMod.esp");
    private static readonly ModKey UnusedMaster = ModKey.FromNameAndExtension("UnusedMod.esp");
    private static readonly ModKey PatchKey     = ModKey.FromNameAndExtension("MyPatch.esp");

    private static (SkyrimMod PatchMod, FormKey ArmorFormKey) CreatePatchWithOutfit()
    {
        var patchMod = new SkyrimMod(PatchKey, SkyrimRelease.SkyrimSE);
        patchMod.ModHeader.MasterReferences.Add(new MasterReference { Master = SkyrimMaster });
        patchMod.ModHeader.MasterReferences.Add(new MasterReference { Master = ArmorMaster });
        patchMod.ModHeader.MasterReferences.Add(new MasterReference { Master = UnusedMaster });

        var armorFormKey = new FormKey(ArmorMaster, 0x800);
        var outfit = patchMod.Outfits.AddNew();
        outfit.EditorID = "OTFT_Test";
        outfit.Items = [new FormLink<IOutfitTargetGetter>(armorFormKey)];

        return (patchMod, armorFormKey);
    }

    [Fact]
    public void CollectRequiredMasters_IncludesRecordAndItemMasters_ExcludesSelf()
    {
        var (patchMod, _) = CreatePatchWithOutfit();

        var required = PatchingService.CollectRequiredMasters(patchMod, []);

        required.Should().Contain(ArmorMaster, "the outfit references armor from that master");
        required.Should().NotContain(PatchKey, "the patch itself is never its own master");
        required.Should().NotContain(UnusedMaster, "an unreferenced master is not required");
    }

    [Fact]
    public void CollectRequiredMasters_ExcludedOutfits_AreIgnored()
    {
        var (patchMod, _) = CreatePatchWithOutfit();
        var outfitFormKey = patchMod.Outfits.Single().FormKey;

        var required = PatchingService.CollectRequiredMasters(patchMod, [outfitFormKey]);

        required.Should().NotContain(ArmorMaster, "the only consumer of that master was excluded");
    }

    [Fact]
    public void CleanupMasterReferences_RemovesOnlyUnusedMasters()
    {
        var (patchMod, _) = CreatePatchWithOutfit();
        var required = new HashSet<ModKey> { SkyrimMaster, ArmorMaster };

        PatchingService.CleanupMasterReferences(patchMod, required);

        var remaining = patchMod.ModHeader.MasterReferences.Select(m => m.Master).ToList();
        remaining.Should().BeEquivalentTo([SkyrimMaster, ArmorMaster]);
    }

    [Fact]
    public void EnsureMasters_AddsMissingWithoutDuplicates()
    {
        var patchMod = new SkyrimMod(PatchKey, SkyrimRelease.SkyrimSE);
        patchMod.ModHeader.MasterReferences.Add(new MasterReference { Master = SkyrimMaster });

        PatchingService.EnsureMasters(
            patchMod,
            [SkyrimMaster, ArmorMaster, PatchKey, ModKey.Null]);

        var masters = patchMod.ModHeader.MasterReferences.Select(m => m.Master).ToList();
        masters.Should().BeEquivalentTo([SkyrimMaster, ArmorMaster]);
        masters.Count(m => m == SkyrimMaster).Should().Be(1, "existing masters must not be duplicated");
    }
}
