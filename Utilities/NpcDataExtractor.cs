using Boutique.Models;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Utilities;

public static class NpcDataExtractor
{
    public static HashSet<string> ExtractKeywords(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Collect NPC's direct keywords
        if (npc.Keywords != null)
        {
            foreach (var keywordLink in npc.Keywords)
            {
                if (keywordLink.TryResolve(linkCache, out var keyword) && !string.IsNullOrWhiteSpace(keyword.EditorID))
                {
                    keywords.Add(keyword.EditorID);
                }
            }
        }

        // Collect race keywords
        if (npc.Race.TryResolve(linkCache, out var race) && race.Keywords != null)
        {
            foreach (var keywordLink in race.Keywords)
            {
                if (keywordLink.TryResolve(linkCache, out var keyword) && !string.IsNullOrWhiteSpace(keyword.EditorID))
                {
                    keywords.Add(keyword.EditorID);
                }
            }
        }

        return keywords;
    }

    public static List<FactionMembership> ExtractFactions(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        var factions = new List<FactionMembership>();

        if (npc.Factions == null)
            return factions;

        foreach (var factionRank in npc.Factions)
        {
            string? editorId = null;
            if (factionRank.Faction.TryResolve(linkCache, out var faction))
            {
                editorId = faction.EditorID;
            }

            factions.Add(new FactionMembership
            {
                FactionFormKey = factionRank.Faction.FormKey,
                FactionEditorId = editorId,
                Rank = factionRank.Rank
            });
        }

        return factions;
    }

    public static (FormKey? FormKey, string? EditorId) ExtractRace(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        if (npc.Race.IsNull)
            return (null, null);

        var formKey = npc.Race.FormKey;
        string? editorId = null;

        if (npc.Race.TryResolve(linkCache, out var race))
        {
            editorId = race.EditorID;
        }

        return (formKey, editorId);
    }

    public static (FormKey? FormKey, string? EditorId) ExtractClass(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        if (npc.Class.IsNull)
            return (null, null);

        var formKey = npc.Class.FormKey;
        string? editorId = null;

        if (npc.Class.TryResolve(linkCache, out var npcClass))
        {
            editorId = npcClass.EditorID;
        }

        return (formKey, editorId);
    }

    public static (FormKey? FormKey, string? EditorId) ExtractCombatStyle(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        if (npc.CombatStyle.IsNull)
            return (null, null);

        var formKey = npc.CombatStyle.FormKey;
        string? editorId = null;

        if (npc.CombatStyle.TryResolve(linkCache, out var combatStyle))
        {
            editorId = combatStyle.EditorID;
        }

        return (formKey, editorId);
    }

    public static (FormKey? FormKey, string? EditorId) ExtractVoiceType(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        if (npc.Voice.IsNull)
            return (null, null);

        var formKey = npc.Voice.FormKey;
        string? editorId = null;

        if (npc.Voice.TryResolve(linkCache, out var voice))
        {
            editorId = voice.EditorID;
        }

        return (formKey, editorId);
    }

    public static (FormKey? FormKey, string? EditorId) ExtractDefaultOutfit(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        if (npc.DefaultOutfit.IsNull)
            return (null, null);

        var formKey = npc.DefaultOutfit.FormKey;
        string? editorId = null;

        if (npc.DefaultOutfit.TryResolve(linkCache, out var outfit))
        {
            editorId = outfit.EditorID;
        }

        return (formKey, editorId);
    }

    public static (FormKey? FormKey, string? EditorId) ExtractTemplate(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        if (npc.Template.IsNull)
            return (null, null);

        var formKey = npc.Template.FormKey;
        string? editorId = null;

        // Template can be either an NPC or a LeveledNpc
        if (linkCache.TryResolve<INpcGetter>(formKey, out var templateNpc))
        {
            editorId = templateNpc.EditorID;
        }
        else if (linkCache.TryResolve<ILeveledNpcGetter>(formKey, out var templateLvln))
        {
            editorId = templateLvln.EditorID;
        }

        return (formKey, editorId);
    }

    public static string? GetName(INpcGetter npc) => npc.Name?.String;

    public static bool IsChildRace(string? raceEditorId)
    {
        if (string.IsNullOrWhiteSpace(raceEditorId))
            return false;

        // Common child race patterns in Skyrim
        return raceEditorId.Contains("Child", StringComparison.OrdinalIgnoreCase) ||
               raceEditorId.Contains("DA13", StringComparison.OrdinalIgnoreCase); // Daedric child form
    }

    public static short ExtractLevel(INpcGetter npc) => npc.Configuration.Level is NpcLevel npcLevel ? npcLevel.Level : (short)1;

    public static (bool IsFemale, bool IsUnique, bool IsSummonable, bool IsLeveled) ExtractTraits(INpcGetter npc)
    {
        var config = npc.Configuration;
        return (
            IsFemale: config.Flags.HasFlag(NpcConfiguration.Flag.Female),
            IsUnique: config.Flags.HasFlag(NpcConfiguration.Flag.Unique),
            IsSummonable: config.Flags.HasFlag(NpcConfiguration.Flag.Summonable),
            IsLeveled: config.Level is PcLevelMult
        );
    }
}
