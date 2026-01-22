using Boutique.Models;
using Boutique.Utilities;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Serilog;

namespace Boutique.Services;

public class NpcScanningService
{
    private readonly ILogger _logger;
    private readonly MutagenService _mutagenService;

    public NpcScanningService(MutagenService mutagenService, ILogger logger)
    {
        _mutagenService = mutagenService;
        _logger = logger.ForContext<NpcScanningService>();
    }

    public async Task<IReadOnlyList<NpcRecord>> ScanNpcsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run<IReadOnlyList<NpcRecord>>(
            () =>
            {
                if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
                {
                    _logger.Warning("LinkCache not available for NPC scanning.");
                    return [];
                }

                var npcs = new List<NpcRecord>();

                try
                {
                    var npcRecords = linkCache.WinningOverrides<INpcGetter>();

                    foreach (var npc in npcRecords)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (npc.FormKey == FormKey.Null)
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(npc.EditorID))
                        {
                            continue;
                        }

                        var name = NpcDataExtractor.GetName(npc);
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
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<NpcFilterData>> ScanNpcsWithFilterDataAsync(
        CancellationToken cancellationToken = default)
    {
        return await Task.Run<IReadOnlyList<NpcFilterData>>(
            () =>
            {
                if (_mutagenService.LinkCache is not ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
                {
                    _logger.Warning("LinkCache not available for NPC scanning.");
                    return [];
                }

                var npcs = new List<NpcFilterData>();

                try
                {
                    var npcRecords = linkCache.WinningOverrides<INpcGetter>();

                    foreach (var npc in npcRecords)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (npc.FormKey == FormKey.Null)
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(npc.EditorID))
                        {
                            continue;
                        }

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
            },
            cancellationToken);
    }

    private NpcFilterData? BuildNpcFilterData(INpcGetter npc, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
    {
        try
        {
            var originalModKey = FindOriginalMaster(linkCache, npc.FormKey);
            var keywords = NpcDataExtractor.ExtractKeywords(npc, linkCache);
            var factions = NpcDataExtractor.ExtractFactions(npc, linkCache);
            var (raceFormKey, raceEditorId) = NpcDataExtractor.ExtractRace(npc, linkCache);
            var (classFormKey, classEditorId) = NpcDataExtractor.ExtractClass(npc, linkCache);
            var (combatStyleFormKey, combatStyleEditorId) = NpcDataExtractor.ExtractCombatStyle(npc, linkCache);
            var (voiceTypeFormKey, voiceTypeEditorId) = NpcDataExtractor.ExtractVoiceType(npc, linkCache);
            var (outfitFormKey, outfitEditorId) = NpcDataExtractor.ExtractDefaultOutfit(npc, linkCache);
            var (templateFormKey, templateEditorId) = NpcDataExtractor.ExtractTemplate(npc, linkCache);
            var (isFemale, isUnique, isSummonable, isLeveled) = NpcDataExtractor.ExtractTraits(npc);
            var isChild = NpcDataExtractor.IsChildRace(raceEditorId);
            var level = NpcDataExtractor.ExtractLevel(npc);

            return new NpcFilterData
            {
                FormKey = npc.FormKey,
                EditorId = npc.EditorID,
                Name = NpcDataExtractor.GetName(npc),
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

    private ModKey FindOriginalMaster(ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache, FormKey formKey)
    {
        try
        {
            var contexts = linkCache.ResolveAllContexts<INpc, INpcGetter>(formKey);
            var firstContext = contexts.FirstOrDefault();
            if (firstContext != null)
            {
                return firstContext.ModKey;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(
                ex,
                "Failed to resolve contexts for FormKey {FormKey}, falling back to FormKey.ModKey",
                formKey);
        }

        return formKey.ModKey;
    }
}
