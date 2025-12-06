using System;
using System.Collections.Generic;
using System.Linq;
using Boutique.Models;
using Mutagen.Bethesda.Plugins;
using Serilog;

namespace Boutique.Services;

/// <summary>
/// Service for matching NPCs against SPID distribution filters.
/// Implements the full SPID filter matching logic including string filters,
/// form filters, level filters, and trait filters.
/// </summary>
public class SpidFilterMatchingService : ISpidFilterMatchingService
{
    private readonly ILogger _logger;

    public SpidFilterMatchingService(ILogger logger)
    {
        _logger = logger.ForContext<SpidFilterMatchingService>();
    }

    public bool NpcMatchesFilter(NpcFilterData npc, SpidDistributionFilter filter)
    {
        // All filter sections are multiplicative (AND logic)
        // An empty/NONE filter section means "match all"
        
        // 1. Check string filters (keywords, names, EditorIDs)
        if (!MatchesStringFilters(npc, filter.StringFilters))
            return false;
        
        // 2. Check form filters (race, class, faction, etc.)
        if (!MatchesFormFilters(npc, filter.FormFilters))
            return false;
        
        // 3. Check level filters
        if (!MatchesLevelFilters(npc, filter.LevelFilters))
            return false;
        
        // 4. Check trait filters (gender, unique, etc.)
        if (!MatchesTraitFilters(npc, filter.TraitFilters))
            return false;
        
        return true;
    }

    public IReadOnlyList<NpcFilterData> GetMatchingNpcs(IReadOnlyList<NpcFilterData> allNpcs, SpidDistributionFilter filter)
    {
        return allNpcs.Where(npc => NpcMatchesFilter(npc, filter)).ToList();
    }

    /// <summary>
    /// Matches string filters against NPC name, EditorID, and keywords.
    /// String filters use OR logic between expressions, AND logic within + combined expressions.
    /// </summary>
    private bool MatchesStringFilters(NpcFilterData npc, SpidFilterSection filters)
    {
        if (filters.IsEmpty)
            return true;
        
        // OR logic: at least one expression must match
        foreach (var expression in filters.Expressions)
        {
            if (MatchesStringExpression(npc, expression))
                return true;
        }
        
        return false;
    }

    /// <summary>
    /// Matches a single string expression (which may have AND-combined parts).
    /// </summary>
    private bool MatchesStringExpression(NpcFilterData npc, SpidFilterExpression expression)
    {
        // AND logic: all parts must match
        foreach (var part in expression.Parts)
        {
            if (!MatchesStringPart(npc, part))
                return false;
        }
        
        return true;
    }

    /// <summary>
    /// Matches a single string filter part against NPC data.
    /// Checks: NPC name, EditorID, keywords, and race keywords.
    /// Supports wildcards (*) and negation (-).
    /// </summary>
    private bool MatchesStringPart(NpcFilterData npc, SpidFilterPart part)
    {
        var value = part.Value;
        var hasWildcard = part.HasWildcard;
        
        bool matches;
        
        if (hasWildcard)
        {
            // Partial match - remove * and check if any string contains the value
            var searchValue = value.Replace("*", "");
            matches = PartialMatchesNpcStrings(npc, searchValue);
        }
        else
        {
            // Exact match against name, EditorID, or keywords
            matches = ExactMatchesNpcStrings(npc, value);
        }
        
        // Apply negation if needed
        return part.IsNegated ? !matches : matches;
    }

    /// <summary>
    /// Checks if value exactly matches NPC name, EditorID, or any keyword.
    /// </summary>
    private static bool ExactMatchesNpcStrings(NpcFilterData npc, string value)
    {
        // Check NPC name
        if (!string.IsNullOrWhiteSpace(npc.Name) && 
            npc.Name.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Check EditorID
        if (!string.IsNullOrWhiteSpace(npc.EditorId) && 
            npc.EditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Check keywords (NPC + race keywords are already combined)
        if (npc.Keywords.Contains(value))
            return true;
        
        // Check template EditorID
        if (!string.IsNullOrWhiteSpace(npc.TemplateEditorId) &&
            npc.TemplateEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;
        
        return false;
    }

    /// <summary>
    /// Checks if value partially matches NPC name, EditorID, or any keyword.
    /// </summary>
    private static bool PartialMatchesNpcStrings(NpcFilterData npc, string value)
    {
        // Check NPC name
        if (!string.IsNullOrWhiteSpace(npc.Name) && 
            npc.Name.Contains(value, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Check EditorID
        if (!string.IsNullOrWhiteSpace(npc.EditorId) && 
            npc.EditorId.Contains(value, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Check keywords
        foreach (var keyword in npc.Keywords)
        {
            if (keyword.Contains(value, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        
        // Check template EditorID
        if (!string.IsNullOrWhiteSpace(npc.TemplateEditorId) &&
            npc.TemplateEditorId.Contains(value, StringComparison.OrdinalIgnoreCase))
            return true;
        
        return false;
    }

    /// <summary>
    /// Matches form filters against NPC race, class, faction, combat style, outfit, voice type, etc.
    /// </summary>
    private bool MatchesFormFilters(NpcFilterData npc, SpidFilterSection filters)
    {
        if (filters.IsEmpty)
            return true;
        
        // OR logic: at least one expression must match
        foreach (var expression in filters.Expressions)
        {
            if (MatchesFormExpression(npc, expression))
                return true;
        }
        
        return false;
    }

    /// <summary>
    /// Matches a single form expression (which may have AND-combined parts).
    /// </summary>
    private bool MatchesFormExpression(NpcFilterData npc, SpidFilterExpression expression)
    {
        // AND logic: all parts must match
        foreach (var part in expression.Parts)
        {
            if (!MatchesFormPart(npc, part))
                return false;
        }
        
        return true;
    }

    /// <summary>
    /// Matches a single form filter part against NPC data.
    /// Checks: race, class, faction, combat style, outfit, voice type, specific NPC, plugin.
    /// </summary>
    private bool MatchesFormPart(NpcFilterData npc, SpidFilterPart part)
    {
        var value = part.Value;
        
        bool matches = MatchesFormValue(npc, value);
        
        // Apply negation if needed
        return part.IsNegated ? !matches : matches;
    }

    /// <summary>
    /// Checks if the value matches any of the NPC's form properties.
    /// </summary>
    private static bool MatchesFormValue(NpcFilterData npc, string value)
    {
        // Check if it's a plugin filter (ends with .esp/.esm/.esl)
        if (IsPluginFilter(value))
        {
            return string.Equals(npc.SourceMod.FileName, value, StringComparison.OrdinalIgnoreCase);
        }
        
        // Try to match as FormKey (0x12345 or ModKey|FormID)
        if (TryParseAsFormKey(value, out var formKey))
        {
            // Check specific NPC
            if (npc.FormKey == formKey)
                return true;
            
            // Check race
            if (npc.RaceFormKey == formKey)
                return true;
            
            // Check class
            if (npc.ClassFormKey == formKey)
                return true;
            
            // Check faction
            if (npc.Factions.Any(f => f.FactionFormKey == formKey))
                return true;
            
            // Check combat style
            if (npc.CombatStyleFormKey == formKey)
                return true;
            
            // Check voice type
            if (npc.VoiceTypeFormKey == formKey)
                return true;
            
            // Check outfit
            if (npc.DefaultOutfitFormKey == formKey)
                return true;
            
            // Check template
            if (npc.TemplateFormKey == formKey)
                return true;
            
            return false;
        }
        
        // Otherwise, match by EditorID
        
        // Check specific NPC by EditorID
        if (!string.IsNullOrWhiteSpace(npc.EditorId) &&
            npc.EditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Check race by EditorID
        if (!string.IsNullOrWhiteSpace(npc.RaceEditorId) &&
            npc.RaceEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Check class by EditorID
        if (!string.IsNullOrWhiteSpace(npc.ClassEditorId) &&
            npc.ClassEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Check faction by EditorID
        if (npc.Factions.Any(f => !string.IsNullOrWhiteSpace(f.FactionEditorId) &&
            f.FactionEditorId.Equals(value, StringComparison.OrdinalIgnoreCase)))
            return true;
        
        // Check combat style by EditorID
        if (!string.IsNullOrWhiteSpace(npc.CombatStyleEditorId) &&
            npc.CombatStyleEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Check voice type by EditorID
        if (!string.IsNullOrWhiteSpace(npc.VoiceTypeEditorId) &&
            npc.VoiceTypeEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Check outfit by EditorID
        if (!string.IsNullOrWhiteSpace(npc.DefaultOutfitEditorId) &&
            npc.DefaultOutfitEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Check template by EditorID
        if (!string.IsNullOrWhiteSpace(npc.TemplateEditorId) &&
            npc.TemplateEditorId.Equals(value, StringComparison.OrdinalIgnoreCase))
            return true;
        
        return false;
    }

    private static bool IsPluginFilter(string value)
    {
        return value.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith(".esl", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseAsFormKey(string value, out FormKey formKey)
    {
        formKey = FormKey.Null;
        
        if (string.IsNullOrWhiteSpace(value))
            return false;
        
        // Try to parse as FormKey directly
        if (FormKey.TryFactory(value, out formKey))
            return true;
        
        // Try ModKey|FormID format
        var pipeIndex = value.IndexOf('|');
        if (pipeIndex > 0)
        {
            var modPart = value[..pipeIndex];
            var formIdPart = value[(pipeIndex + 1)..];
            
            if (ModKey.TryFromNameAndExtension(modPart, out var modKey))
            {
                formIdPart = formIdPart.Replace("0x", "").Replace("0X", "");
                if (uint.TryParse(formIdPart, System.Globalization.NumberStyles.HexNumber, null, out var formId))
                {
                    formKey = new FormKey(modKey, formId);
                    return true;
                }
            }
        }
        
        // Try 0x12345~Plugin.esp format
        var tildeIndex = value.IndexOf('~');
        if (tildeIndex > 0)
        {
            var formIdPart = value[..tildeIndex];
            var modPart = value[(tildeIndex + 1)..];
            
            formIdPart = formIdPart.Replace("0x", "").Replace("0X", "");
            if (uint.TryParse(formIdPart, System.Globalization.NumberStyles.HexNumber, null, out var formId) &&
                ModKey.TryFromNameAndExtension(modPart, out var modKey))
            {
                formKey = new FormKey(modKey, formId);
                return true;
            }
        }
        
        return false;
    }

    /// <summary>
    /// Matches level filters against NPC level.
    /// </summary>
    private bool MatchesLevelFilters(NpcFilterData npc, string? levelFilters)
    {
        if (string.IsNullOrWhiteSpace(levelFilters) || 
            levelFilters.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            return true;
        
        // Parse level filter: can be "min", "min/max", or "min/" (open-ended)
        // Can also have skill filters like "14(50/50)"
        
        // For now, just handle basic level range
        var parts = levelFilters.Split(',', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            
            // Skip skill filters for now (they have parentheses)
            if (trimmed.Contains('('))
                continue;
            
            if (!ParseLevelRange(trimmed, out var minLevel, out var maxLevel))
                continue;
            
            // Check if NPC level is in range
            if (npc.Level < minLevel)
                return false;
            
            if (maxLevel.HasValue && npc.Level > maxLevel.Value)
                return false;
        }
        
        return true;
    }

    private static bool ParseLevelRange(string value, out int minLevel, out int? maxLevel)
    {
        minLevel = 0;
        maxLevel = null;
        
        var slashIndex = value.IndexOf('/');
        
        if (slashIndex < 0)
        {
            // Just a minimum level
            return int.TryParse(value, out minLevel);
        }
        
        var minPart = value[..slashIndex];
        var maxPart = slashIndex < value.Length - 1 ? value[(slashIndex + 1)..] : null;
        
        if (!int.TryParse(minPart, out minLevel))
            return false;
        
        if (!string.IsNullOrWhiteSpace(maxPart) && int.TryParse(maxPart, out var max))
        {
            maxLevel = max;
        }
        
        return true;
    }

    /// <summary>
    /// Matches trait filters against NPC traits.
    /// </summary>
    private static bool MatchesTraitFilters(NpcFilterData npc, SpidTraitFilters traits)
    {
        if (traits.IsEmpty)
            return true;
        
        // Check gender
        if (traits.IsFemale.HasValue && npc.IsFemale != traits.IsFemale.Value)
            return false;
        
        // Check unique
        if (traits.IsUnique.HasValue && npc.IsUnique != traits.IsUnique.Value)
            return false;
        
        // Check summonable
        if (traits.IsSummonable.HasValue && npc.IsSummonable != traits.IsSummonable.Value)
            return false;
        
        // Check child
        if (traits.IsChild.HasValue && npc.IsChild != traits.IsChild.Value)
            return false;
        
        // Check leveled
        if (traits.IsLeveled.HasValue && npc.IsLeveled != traits.IsLeveled.Value)
            return false;
        
        // Note: Teammate and Dead are runtime states, can't be checked statically
        // We'll skip these for now
        
        return true;
    }
}

