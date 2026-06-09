using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace Boutique.Tests.Integration;

public class SkyrimEsmFactsTests
{
    private static ISkyrimModDisposableGetter Load(out string path)
    {
        var resolved = SkyrimTestData.ResolveEsmPath();
        Skip.If(resolved is null, SkyrimTestData.MissingMessage);
        path = resolved!;
        return SkyrimMod.CreateFromBinaryOverlay(path, SkyrimRelease.SkyrimSE);
    }

    [SkippableFact]
    public void Header_IsAMasterAndLocalizedWithNoMasters()
    {
        using var mod = Load(out _);

        mod.ModHeader.Flags.HasFlag(SkyrimModHeader.HeaderFlag.Master).Should().BeTrue();
        mod.ModHeader.Flags.HasFlag(SkyrimModHeader.HeaderFlag.Localized).Should().BeTrue();
        mod.ModHeader.MasterReferences.Should().BeEmpty("Skyrim.esm is the root master");
    }

    [SkippableFact]
    public void RecordCounts_AreInVanillaRanges()
    {
        using var mod = Load(out _);

        mod.Armors.Count.Should().BeGreaterThan(2000);
        mod.Weapons.Count.Should().BeGreaterThan(2000);
        mod.Outfits.Count.Should().BeGreaterThan(400);
        mod.Npcs.Count.Should().BeGreaterThan(5000);
        mod.Keywords.Count.Should().BeGreaterThan(800);
    }

    [SkippableFact]
    public void IronCuirass_HasExpectedVanillaStats()
    {
        using var mod = Load(out _);
        var linkCache = mod.ToImmutableLinkCache();

        linkCache.TryResolve<IArmorGetter>(SkyrimFormKeys.ArmorIronCuirass, out var armor)
            .Should().BeTrue();

        armor!.EditorID.Should().Be("ArmorIronCuirass");
        (armor as ITranslatedNamedGetter)?.Name?.String.Should().Be("Iron Armor");
        armor.ArmorRating.Should().Be(25);
        armor.Value.Should().Be(125);
        armor.Weight.Should().Be(30);
        armor.BodyTemplate!.ArmorType.Should().Be(ArmorType.HeavyArmor);
        armor.BodyTemplate.FirstPersonFlags.HasFlag(BipedObjectFlag.Body).Should().BeTrue();
    }

    [SkippableFact]
    public void IronCuirass_KeywordsResolveToExpectedEditorIds()
    {
        using var mod = Load(out _);
        var linkCache = mod.ToImmutableLinkCache();

        linkCache.TryResolve<IArmorGetter>(SkyrimFormKeys.ArmorIronCuirass, out var armor)
            .Should().BeTrue();

        var keywordEditorIds = (armor!.Keywords ?? [])
            .Select(k => linkCache.TryResolve<IKeywordGetter>(k.FormKey, out var kw) ? kw.EditorID : null)
            .Where(e => e is not null)
            .ToList();

        keywordEditorIds.Should().Contain(["ArmorHeavy", "ArmorMaterialIron", "ArmorCuirass"]);
    }

    [SkippableTheory]
    [InlineData("00000F", "Gold001")]
    [InlineData("00000A", "Lockpick")]
    [InlineData("013BBF", "Nazeem")]
    [InlineData("012E46", "ArmorIronGauntlets")]
    [InlineData("012E4B", "ArmorIronBoots")]
    public void CanonicalForms_ResolveToExpectedEditorIds(string id, string expectedEditorId)
    {
        using var mod = Load(out _);
        var linkCache = mod.ToImmutableLinkCache();

        linkCache.TryResolve(FormKey.Factory($"{id}:Skyrim.esm"), out var record)
            .Should().BeTrue();

        record!.EditorID.Should().Be(expectedEditorId);
    }
}
