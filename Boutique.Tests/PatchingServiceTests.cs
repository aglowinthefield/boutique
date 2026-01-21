using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace Boutique.Tests;

public class PatchingServiceTests
{
    private const uint MinimumEslFormId = 0x800;

    [Fact]
    public void NewSkyrimMod_SE_DefaultNextFormID_Is0x800()
    {
        var modKey = ModKey.FromFileName("TestPatch.esp");
        var mod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);

        Assert.Equal(MinimumEslFormId, mod.ModHeader.Stats.NextFormID);
    }

    [Fact]
    public void NewSkyrimMod_VR_DefaultNextFormID_Is0x800()
    {
        var modKey = ModKey.FromFileName("TestPatch.esp");
        var mod = new SkyrimMod(modKey, SkyrimRelease.SkyrimVR);

        Assert.Equal(MinimumEslFormId, mod.ModHeader.Stats.NextFormID);
    }

    [Fact]
    public void NewSkyrimMod_AfterSettingNextFormID_UsesNewValue()
    {
        var modKey = ModKey.FromFileName("TestPatch.esp");
        var mod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);

        mod.ModHeader.Stats.NextFormID = 0x900;

        Assert.Equal(0x900u, mod.ModHeader.Stats.NextFormID);
    }

    [Fact]
    public void NewSkyrimMod_AddNewOutfit_UsesNextFormID()
    {
        var modKey = ModKey.FromFileName("TestPatch.esp");
        var mod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);

        var outfit = mod.Outfits.AddNew();
        outfit.EditorID = "TestOutfit";

        Assert.Equal(MinimumEslFormId, outfit.FormKey.ID);
    }

    [Fact]
    public void NewSkyrimMod_AddMultipleOutfits_IncrementsFormID()
    {
        var modKey = ModKey.FromFileName("TestPatch.esp");
        var mod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);

        var outfit1 = mod.Outfits.AddNew();
        var outfit2 = mod.Outfits.AddNew();
        var outfit3 = mod.Outfits.AddNew();

        Assert.Equal(0x800u, outfit1.FormKey.ID);
        Assert.Equal(0x801u, outfit2.FormKey.ID);
        Assert.Equal(0x802u, outfit3.FormKey.ID);
    }

    [Fact]
    public void NewSkyrimMod_LowNextFormID_ProducesLowFormIDs()
    {
        var modKey = ModKey.FromFileName("TestPatch.esp");
        var mod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);

        mod.ModHeader.Stats.NextFormID = 0;

        var outfit1 = mod.Outfits.AddNew();
        var outfit2 = mod.Outfits.AddNew();

        Assert.Equal(0x0u, outfit1.FormKey.ID);
        Assert.Equal(0x1u, outfit2.FormKey.ID);
    }

    [Fact]
    public void EnsureMinimumFormId_WhenBelowMinimum_SetsTo0x800()
    {
        var modKey = ModKey.FromFileName("TestPatch.esp");
        var mod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);

        mod.ModHeader.Stats.NextFormID = 0x100;

        if (mod.ModHeader.Stats.NextFormID < MinimumEslFormId)
        {
            mod.ModHeader.Stats.NextFormID = MinimumEslFormId;
        }

        Assert.Equal(MinimumEslFormId, mod.ModHeader.Stats.NextFormID);
    }

    [Fact]
    public void EnsureMinimumFormId_WhenAtOrAboveMinimum_Unchanged()
    {
        var modKey = ModKey.FromFileName("TestPatch.esp");
        var mod = new SkyrimMod(modKey, SkyrimRelease.SkyrimSE);

        mod.ModHeader.Stats.NextFormID = 0x900;

        if (mod.ModHeader.Stats.NextFormID < MinimumEslFormId)
        {
            mod.ModHeader.Stats.NextFormID = MinimumEslFormId;
        }

        Assert.Equal(0x900u, mod.ModHeader.Stats.NextFormID);
    }
}
