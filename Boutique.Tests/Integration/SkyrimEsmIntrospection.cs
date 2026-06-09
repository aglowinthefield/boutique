using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Skyrim;
using Xunit;
using Xunit.Abstractions;

namespace Boutique.Tests.Integration;

public class SkyrimEsmIntrospection(ITestOutputHelper output)
{
    [SkippableFact]
    public void Dump_HeaderAndRecordCounts()
    {
        var path = SkyrimTestData.ResolveEsmPath();
        Skip.If(path is null, SkyrimTestData.MissingMessage);

        using var mod = SkyrimMod.CreateFromBinaryOverlay(path!, SkyrimRelease.SkyrimSE);

        output.WriteLine($"Path          : {path}");
        output.WriteLine($"ModKey        : {mod.ModKey}");
        output.WriteLine($"Master?       : {mod.ModHeader.Flags.HasFlag(SkyrimModHeader.HeaderFlag.Master)}");
        output.WriteLine($"Localized?    : {mod.ModHeader.Flags.HasFlag(SkyrimModHeader.HeaderFlag.Localized)}");
        output.WriteLine($"Author        : {mod.ModHeader.Author}");
        output.WriteLine($"Masters       : {string.Join(", ", mod.ModHeader.MasterReferences.Select(m => m.Master.FileName))}");
        output.WriteLine($"NextFormID    : 0x{mod.ModHeader.Stats.NextFormID:X8}");
        output.WriteLine(string.Empty);

        output.WriteLine("Record counts:");
        output.WriteLine($"  Armors      : {mod.Armors.Count}");
        output.WriteLine($"  ArmorAddons : {mod.ArmorAddons.Count}");
        output.WriteLine($"  Weapons     : {mod.Weapons.Count}");
        output.WriteLine($"  Outfits     : {mod.Outfits.Count}");
        output.WriteLine($"  Npcs        : {mod.Npcs.Count}");
        output.WriteLine($"  Keywords    : {mod.Keywords.Count}");
        output.WriteLine($"  Factions    : {mod.Factions.Count}");
        output.WriteLine($"  LeveledItems: {mod.LeveledItems.Count}");
        output.WriteLine($"  Cobjs       : {mod.ConstructibleObjects.Count}");
    }

    [SkippableFact]
    public void Dump_IronCuirassDetails()
    {
        var path = SkyrimTestData.ResolveEsmPath();
        Skip.If(path is null, SkyrimTestData.MissingMessage);

        using var mod = SkyrimMod.CreateFromBinaryOverlay(path!, SkyrimRelease.SkyrimSE);
        var linkCache = mod.ToImmutableLinkCache();

        var iron = FormKey.Factory("012E49:Skyrim.esm");
        Skip.IfNot(linkCache.TryResolve<IArmorGetter>(iron, out var armor), "Iron Cuirass not resolvable");

        output.WriteLine($"FormKey       : {armor!.FormKey}");
        output.WriteLine($"EditorID      : {armor.EditorID}");
        output.WriteLine($"Name          : {(armor as ITranslatedNamedGetter)?.Name?.String}");
        output.WriteLine($"ArmorRating   : {armor.ArmorRating}");
        output.WriteLine($"Value         : {armor.Value}");
        output.WriteLine($"Weight        : {armor.Weight}");
        output.WriteLine($"BodyTemplate  : {armor.BodyTemplate?.FirstPersonFlags}");
        output.WriteLine($"ArmorType     : {armor.BodyTemplate?.ArmorType}");

        output.WriteLine("Keywords:");
        foreach (var kwLink in armor.Keywords ?? [])
        {
            var edid = linkCache.TryResolve<IKeywordGetter>(kwLink.FormKey, out var kw) ? kw.EditorID : "<unresolved>";
            output.WriteLine($"  {kwLink.FormKey} -> {edid}");
        }
    }

    [SkippableFact]
    public void Dump_SampleOutfitsAndKeywords()
    {
        var path = SkyrimTestData.ResolveEsmPath();
        Skip.If(path is null, SkyrimTestData.MissingMessage);

        using var mod = SkyrimMod.CreateFromBinaryOverlay(path!, SkyrimRelease.SkyrimSE);

        output.WriteLine("First 10 outfits:");
        foreach (var outfit in mod.Outfits.Take(10))
        {
            var pieces = outfit.Items?.Count ?? 0;
            output.WriteLine($"  {outfit.FormKey.ID:X8}  {outfit.EditorID}  ({pieces} items)");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("Sample armor keywords (first 15 starting with 'Armor'):");
        var armorKeywords = mod.Keywords
            .Where(k => k.EditorID?.StartsWith("Armor", StringComparison.Ordinal) == true)
            .Take(15);
        foreach (var kw in armorKeywords)
        {
            output.WriteLine($"  {kw.FormKey.ID:X8}  {kw.EditorID}");
        }
    }
}
