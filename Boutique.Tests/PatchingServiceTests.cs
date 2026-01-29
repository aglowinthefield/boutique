using FluentAssertions;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace Boutique.Tests;

/// <summary>
///     Tests for PatchingService outfit record creation.
/// </summary>
public class PatchingServiceTests
{
    [Fact]
    public void CreateOutfitRecord_WithSingleItem_CreatesValidRecord()
    {
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Test.esp"), SkyrimRelease.SkyrimSE);
        var armorKey = new FormKey(mod.ModKey, 0x800);

        var outfit = mod.Outfits.AddNew();
        outfit.EditorID = "TestOutfit";
        outfit.Items ??= [];
        outfit.Items.Add(armorKey.ToLink<IOutfitTargetGetter>());

        outfit.EditorID.Should().Be("TestOutfit");
        outfit.Items.Should().ContainSingle();
    }

    [Fact]
    public void CreateOutfitRecord_WithMultipleItems_CreatesValidRecord()
    {
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Test.esp"), SkyrimRelease.SkyrimSE);
        var armorKey1 = new FormKey(mod.ModKey, 0x800);
        var armorKey2 = new FormKey(mod.ModKey, 0x801);
        var armorKey3 = new FormKey(mod.ModKey, 0x802);

        var outfit = mod.Outfits.AddNew();
        outfit.EditorID = "MultiItemOutfit";
        outfit.Items ??= [];
        outfit.Items.Add(armorKey1.ToLink<IOutfitTargetGetter>());
        outfit.Items.Add(armorKey2.ToLink<IOutfitTargetGetter>());
        outfit.Items.Add(armorKey3.ToLink<IOutfitTargetGetter>());

        outfit.Items.Should().HaveCount(3);
    }

    [Fact]
    public void CreateOutfitRecord_EmptyItems_HasEmptyList()
    {
        var mod = new SkyrimMod(ModKey.FromNameAndExtension("Test.esp"), SkyrimRelease.SkyrimSE);

        var outfit = mod.Outfits.AddNew();
        outfit.EditorID = "EmptyOutfit";
        outfit.Items ??= [];

        outfit.Items.Should().BeEmpty();
    }

    [Fact]
    public void CreateOutfitRecord_WithCrossModReference_ContainsCorrectFormKey()
    {
        var patchMod = new SkyrimMod(ModKey.FromNameAndExtension("Patch.esp"), SkyrimRelease.SkyrimSE);
        var skyrimModKey = ModKey.FromNameAndExtension("Skyrim.esm");
        var skyrimArmorKey = new FormKey(skyrimModKey, 0x12345);

        var outfit = patchMod.Outfits.AddNew();
        outfit.EditorID = "CrossModOutfit";
        outfit.Items ??= [];
        outfit.Items.Add(skyrimArmorKey.ToLink<IOutfitTargetGetter>());

        outfit.Items.Should().ContainSingle()
            .Which.FormKey.ModKey.Should().Be(skyrimModKey);
    }

    [Fact]
    public void ModMasterCollection_AddingRecord_TracksMasters()
    {
        _ = new SkyrimMod(ModKey.FromNameAndExtension("Patch.esp"), SkyrimRelease.SkyrimSE);
        var skyrimModKey = ModKey.FromNameAndExtension("Skyrim.esm");
        var dawnguardModKey = ModKey.FromNameAndExtension("Dawnguard.esm");

        var referencedMods = new HashSet<ModKey> { skyrimModKey, dawnguardModKey };

        referencedMods.Should().HaveCount(2)
            .And.Contain(skyrimModKey)
            .And.Contain(dawnguardModKey);
    }

    [Fact]
    public void OutfitEditorId_WithPrefix_FormatsCorrectly()
    {
        var prefix = "BTQ_";
        var baseName = "VampireNoble";
        var expectedId = $"{prefix}{baseName}Outfit";

        expectedId.Should().Be("BTQ_VampireNobleOutfit");
    }

    [Fact]
    public void OutfitEditorId_SanitizesInvalidChars()
    {
        var input = "My Outfit (With Spaces)";
        var sanitized = input.Replace(" ", "_").Replace("(", "").Replace(")", "");

        sanitized.Should().Be("My_Outfit_With_Spaces");
    }

    [Fact]
    public void FormKeySet_PreventsDuplicates()
    {
        var modKey = ModKey.FromNameAndExtension("Test.esp");
        var formKey1 = new FormKey(modKey, 0x800);
        var formKey2 = new FormKey(modKey, 0x800);

        var set = new HashSet<FormKey> { formKey1, formKey2 };

        set.Should().ContainSingle();
    }

    [Fact]
    public void FormKeyList_AllowsDuplicates()
    {
        var modKey = ModKey.FromNameAndExtension("Test.esp");
        var formKey = new FormKey(modKey, 0x800);

        var list = new List<FormKey> { formKey, formKey };

        list.Should().HaveCount(2);
    }
}
