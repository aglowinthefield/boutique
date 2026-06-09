using FluentAssertions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Skyrim;
using Xunit;

namespace Boutique.Tests.Integration;

public class MutagenSkyrimEsmTests
{
    private static readonly FormKey IronCuirass = FormKey.Factory("012E49:Skyrim.esm");

    [SkippableFact]
    public void SkyrimEsm_LoadsAsOverlayWithExpectedModKey()
    {
        var path = SkyrimTestData.ResolveEsmPath();
        Skip.If(path is null, SkyrimTestData.MissingMessage);

        using var mod = SkyrimMod.CreateFromBinaryOverlay(path!, SkyrimRelease.SkyrimSE);

        mod.ModKey.ToString().Should().Be("Skyrim.esm");
        mod.Armors.Count.Should().BeGreaterThan(100);
        mod.Outfits.Count.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public void SkyrimEsm_ResolvesKnownArmorThroughLinkCache()
    {
        var path = SkyrimTestData.ResolveEsmPath();
        Skip.If(path is null, SkyrimTestData.MissingMessage);

        using var mod = SkyrimMod.CreateFromBinaryOverlay(path!, SkyrimRelease.SkyrimSE);
        var linkCache = mod.ToImmutableLinkCache();

        linkCache.TryResolve<IArmorGetter>(IronCuirass, out var armor).Should().BeTrue();

        var named = armor as ITranslatedNamedGetter;
        named?.Name?.String.Should().NotBeNullOrWhiteSpace();
    }

    [SkippableFact]
    public void SkyrimEsm_KnownArmorExposesKeywords()
    {
        var path = SkyrimTestData.ResolveEsmPath();
        Skip.If(path is null, SkyrimTestData.MissingMessage);

        using var mod = SkyrimMod.CreateFromBinaryOverlay(path!, SkyrimRelease.SkyrimSE);
        var linkCache = mod.ToImmutableLinkCache();

        linkCache.TryResolve<IArmorGetter>(IronCuirass, out var armor).Should().BeTrue();
        armor!.Keywords.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public void SkyrimEsm_ArmorsHaveResolvableNames()
    {
        var path = SkyrimTestData.ResolveEsmPath();
        Skip.If(path is null, SkyrimTestData.MissingMessage);

        using var mod = SkyrimMod.CreateFromBinaryOverlay(path!, SkyrimRelease.SkyrimSE);

        mod.Armors
            .Where(a => !string.IsNullOrWhiteSpace(a.Name?.String))
            .Should()
            .NotBeEmpty("Skyrim.esm ships with many named armor records");
    }
}
