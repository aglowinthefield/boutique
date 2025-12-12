using Boutique.Models;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

public class NpcScanningService
{
    private readonly MutagenService _mutagenService;
    private readonly ILogger _logger;

    public NpcScanningService(MutagenService mutagenService, ILogger logger)
    {
        _mutagenService = mutagenService;
        _logger = logger.ForContext<NpcScanningService>();
    }

    public async Task<IReadOnlyList<NpcRecord>> ScanNpcsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run<IReadOnlyList<NpcRecord>>(() =>
        {
            if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
            {
                _logger.Warning("LinkCache not available for NPC scanning.");
                return Array.Empty<NpcRecord>();
            }

            var npcs = new List<NpcRecord>();

            try
            {
                var npcRecords = linkCache.WinningOverrides<INpcGetter>();

                foreach (var npc in npcRecords)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Filter out invalid NPCs
                    if (npc.FormKey == FormKey.Null)
                        continue;

                    // Skip NPCs without EditorID (likely invalid)
                    if (string.IsNullOrWhiteSpace(npc.EditorID))
                        continue;

                    var name = GetNpcName(npc);
                    
                    // Find the original master (topmost in load order) that first introduced this NPC
                    var originalModKey = FindOriginalMaster(linkCache, npc.FormKey);

                    var npcRecord = new NpcRecord(
                        npc.FormKey,
                        npc.EditorID,
                        name,
                        originalModKey);

                    npcs.Add(npcRecord);
                }

                _logger.Information("Scanned {Count} NPCs from modlist.", npcs.Count);
            }
            catch (OperationCanceledException)
            {
                _logger.Information("NPC scanning cancelled.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to scan NPCs.");
            }

            return npcs;
        }, cancellationToken);
    }
    
    public async Task<IReadOnlyList<NpcFilterData>> ScanNpcsWithFilterDataAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run<IReadOnlyList<NpcFilterData>>(() =>
        {
            if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
            {
                _logger.Warning("LinkCache not available for NPC scanning.");
                return Array.Empty<NpcFilterData>();
            }

            var npcs = new List<NpcFilterData>();

            try
            {
                var npcRecords = linkCache.WinningOverrides<INpcGetter>();

                foreach (var npc in npcRecords)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Filter out invalid NPCs
                    if (npc.FormKey == FormKey.Null)
                        continue;

                    // Skip NPCs without EditorID (likely invalid)
                    if (string.IsNullOrWhiteSpace(npc.EditorID))
                        continue;

                    var filterData = BuildNpcFilterData(npc, linkCache);
                    if (filterData != null)
                    {
                        npcs.Add(filterData);
                    }
                }

                _logger.Information("Scanned {Count} NPCs with filter data from modlist.", npcs.Count);
            }
            catch (OperationCanceledException)
            {
                _logger.Information("NPC scanning cancelled.");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to scan NPCs.");
            }

            return npcs;
        }, cancellationToken);
    }
    
    private NpcFilterData? BuildNpcFilterData(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        try
        {
            var originalModKey = FindOriginalMaster(linkCache, npc.FormKey);
            
            // Collect keywords from NPC and its race
            var keywords = CollectNpcKeywords(npc, linkCache);
            
            // Collect faction memberships
            var factions = CollectNpcFactions(npc, linkCache);
            
            // Get race info
            var (raceFormKey, raceEditorId) = ResolveRace(npc, linkCache);
            
            // Get class info
            var (classFormKey, classEditorId) = ResolveClass(npc, linkCache);
            
            // Get combat style
            var (combatStyleFormKey, combatStyleEditorId) = ResolveCombatStyle(npc, linkCache);
            
            // Get voice type
            var (voiceTypeFormKey, voiceTypeEditorId) = ResolveVoiceType(npc, linkCache);
            
            // Get default outfit
            var (outfitFormKey, outfitEditorId) = ResolveOutfit(npc, linkCache);
            
            // Get template info
            var (templateFormKey, templateEditorId) = ResolveTemplate(npc, linkCache);
            
            // Extract traits from NPC configuration flags
            var configuration = npc.Configuration;
            var isFemale = configuration.Flags.HasFlag(NpcConfiguration.Flag.Female);
            var isUnique = configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique);
            var isSummonable = configuration.Flags.HasFlag(NpcConfiguration.Flag.Summonable);
            var isLeveled = npc.Configuration.Level is PcLevelMult;
            
            // Check if child via race
            var isChild = IsChildRace(raceEditorId);
            
            // Get level
            var level = npc.Configuration.Level is NpcLevel npcLevel ? npcLevel.Level : (short)1;
            
            return new NpcFilterData
            {
                FormKey = npc.FormKey,
                EditorId = npc.EditorID,
                Name = GetNpcName(npc),
                SourceMod = originalModKey,
                Keywords = keywords,
                Factions = factions,
                RaceFormKey = raceFormKey,
                RaceEditorId = raceEditorId,
                ClassFormKey = classFormKey,
                ClassEditorId = classEditorId,
                CombatStyleFormKey = combatStyleFormKey,
                CombatStyleEditorId = combatStyleEditorId,
                VoiceTypeFormKey = voiceTypeFormKey,
                VoiceTypeEditorId = voiceTypeEditorId,
                DefaultOutfitFormKey = outfitFormKey,
                DefaultOutfitEditorId = outfitEditorId,
                IsFemale = isFemale,
                IsUnique = isUnique,
                IsSummonable = isSummonable,
                IsChild = isChild,
                IsLeveled = isLeveled,
                Level = level,
                TemplateFormKey = templateFormKey,
                TemplateEditorId = templateEditorId
            };
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to build filter data for NPC {EditorId}", npc.EditorID);
            return null;
        }
    }
    
    private HashSet<string> CollectNpcKeywords(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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
    
    private List<FactionMembership> CollectNpcFactions(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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
    
    private (FormKey? FormKey, string? EditorId) ResolveRace(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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
    
    private (FormKey? FormKey, string? EditorId) ResolveClass(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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
    
    private (FormKey? FormKey, string? EditorId) ResolveCombatStyle(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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
    
    private (FormKey? FormKey, string? EditorId) ResolveVoiceType(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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
    
    private (FormKey? FormKey, string? EditorId) ResolveOutfit(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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
    
    private (FormKey? FormKey, string? EditorId) ResolveTemplate(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
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
    
    private static bool IsChildRace(string? raceEditorId)
    {
        if (string.IsNullOrWhiteSpace(raceEditorId))
            return false;
        
        // Common child race patterns in Skyrim
        return raceEditorId.Contains("Child", StringComparison.OrdinalIgnoreCase) ||
               raceEditorId.Contains("DA13", StringComparison.OrdinalIgnoreCase); // Daedric child form
    }

    /// <summary>
    /// Finds the original master mod (topmost in load order) that first introduced the NPC,
    /// rather than the leaf-most mod that last edited it.
    /// </summary>
    private ModKey FindOriginalMaster(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, FormKey formKey)
    {
        try
        {
            // Resolve all contexts for this FormKey - they are returned in load order
            // The first context is the original master that first introduced the record
            var contexts = linkCache.ResolveAllContexts<INpc, INpcGetter>(formKey);
            
            // Get the first context (original master)
            var firstContext = contexts.FirstOrDefault();
            if (firstContext != null)
            {
                return firstContext.ModKey;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "Failed to resolve contexts for FormKey {FormKey}, falling back to FormKey.ModKey", formKey);
        }

        // Fallback to FormKey.ModKey if context resolution fails
        return formKey.ModKey;
    }

    private static string? GetNpcName(INpcGetter npc)
    {
        return npc.Name?.String;
    }
}

