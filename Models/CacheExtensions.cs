using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

/// <summary>
/// Extension methods to convert between domain models and cache DTOs.
/// </summary>
public static class CacheExtensions
{
    // ========================================================================
    // NpcFilterData <-> NpcFilterDataDto
    // ========================================================================

    public static NpcFilterDataDto ToDto(this NpcFilterData npc)
    {
        return new NpcFilterDataDto
        {
            FormKeyString = npc.FormKey.ToString(),
            EditorId = npc.EditorId,
            Name = npc.Name,
            SourceModFileName = npc.SourceMod.FileName,
            Keywords = npc.Keywords.ToList(),
            Factions = npc.Factions.Select(f => f.ToDto()).ToList(),
            RaceFormKeyString = npc.RaceFormKey?.ToString(),
            RaceEditorId = npc.RaceEditorId,
            ClassFormKeyString = npc.ClassFormKey?.ToString(),
            ClassEditorId = npc.ClassEditorId,
            CombatStyleFormKeyString = npc.CombatStyleFormKey?.ToString(),
            CombatStyleEditorId = npc.CombatStyleEditorId,
            VoiceTypeFormKeyString = npc.VoiceTypeFormKey?.ToString(),
            VoiceTypeEditorId = npc.VoiceTypeEditorId,
            DefaultOutfitFormKeyString = npc.DefaultOutfitFormKey?.ToString(),
            DefaultOutfitEditorId = npc.DefaultOutfitEditorId,
            IsFemale = npc.IsFemale,
            IsUnique = npc.IsUnique,
            IsSummonable = npc.IsSummonable,
            IsChild = npc.IsChild,
            IsLeveled = npc.IsLeveled,
            Level = npc.Level,
            TemplateFormKeyString = npc.TemplateFormKey?.ToString(),
            TemplateEditorId = npc.TemplateEditorId
        };
    }

    public static NpcFilterData FromDto(this NpcFilterDataDto dto)
    {
        return new NpcFilterData
        {
            FormKey = FormKey.TryFactory(dto.FormKeyString, out var fk) ? fk : FormKey.Null,
            EditorId = dto.EditorId,
            Name = dto.Name,
            SourceMod = ModKey.TryFromFileName(dto.SourceModFileName, out var mk) ? mk : ModKey.Null,
            Keywords = dto.Keywords.ToHashSet(),
            Factions = dto.Factions.Select(f => f.FromDto()).ToList(),
            RaceFormKey = TryParseFormKey(dto.RaceFormKeyString),
            RaceEditorId = dto.RaceEditorId,
            ClassFormKey = TryParseFormKey(dto.ClassFormKeyString),
            ClassEditorId = dto.ClassEditorId,
            CombatStyleFormKey = TryParseFormKey(dto.CombatStyleFormKeyString),
            CombatStyleEditorId = dto.CombatStyleEditorId,
            VoiceTypeFormKey = TryParseFormKey(dto.VoiceTypeFormKeyString),
            VoiceTypeEditorId = dto.VoiceTypeEditorId,
            DefaultOutfitFormKey = TryParseFormKey(dto.DefaultOutfitFormKeyString),
            DefaultOutfitEditorId = dto.DefaultOutfitEditorId,
            IsFemale = dto.IsFemale,
            IsUnique = dto.IsUnique,
            IsSummonable = dto.IsSummonable,
            IsChild = dto.IsChild,
            IsLeveled = dto.IsLeveled,
            Level = dto.Level,
            TemplateFormKey = TryParseFormKey(dto.TemplateFormKeyString),
            TemplateEditorId = dto.TemplateEditorId
        };
    }

    // ========================================================================
    // FactionMembership <-> FactionMembershipDto
    // ========================================================================

    public static FactionMembershipDto ToDto(this FactionMembership faction)
    {
        return new FactionMembershipDto
        {
            FactionFormKeyString = faction.FactionFormKey.ToString(),
            FactionEditorId = faction.FactionEditorId,
            Rank = faction.Rank
        };
    }

    public static FactionMembership FromDto(this FactionMembershipDto dto)
    {
        return new FactionMembership
        {
            FactionFormKey = FormKey.TryFactory(dto.FactionFormKeyString, out var fk) ? fk : FormKey.Null,
            FactionEditorId = dto.FactionEditorId,
            Rank = dto.Rank
        };
    }

    // ========================================================================
    // DistributionFile <-> DistributionFileDto
    // ========================================================================

    public static DistributionFileDto ToDto(this DistributionFile file)
    {
        return new DistributionFileDto
        {
            FileName = file.FileName,
            FullPath = file.FullPath,
            RelativePath = file.RelativePath,
            Type = (int)file.Type,
            Lines = file.Lines.Select(l => l.ToDto()).ToList(),
            OutfitDistributionCount = file.OutfitDistributionCount
        };
    }

    public static DistributionFile FromDto(this DistributionFileDto dto)
    {
        return new DistributionFile(
            dto.FileName,
            dto.FullPath,
            dto.RelativePath,
            (DistributionFileType)dto.Type,
            dto.Lines.Select(l => l.FromDto()).ToList(),
            dto.OutfitDistributionCount);
    }

    // ========================================================================
    // DistributionLine <-> DistributionLineDto
    // ========================================================================

    public static DistributionLineDto ToDto(this DistributionLine line)
    {
        return new DistributionLineDto
        {
            LineNumber = line.LineNumber,
            RawText = line.RawText,
            Kind = (int)line.Kind,
            SectionName = line.SectionName,
            Key = line.Key,
            Value = line.Value,
            IsOutfitDistribution = line.IsOutfitDistribution,
            OutfitFormKeys = line.OutfitFormKeys.ToList()
        };
    }

    public static DistributionLine FromDto(this DistributionLineDto dto)
    {
        return new DistributionLine(
            dto.LineNumber,
            dto.RawText,
            (DistributionLineKind)dto.Kind,
            dto.SectionName,
            dto.Key,
            dto.Value,
            dto.IsOutfitDistribution,
            dto.OutfitFormKeys);
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private static FormKey? TryParseFormKey(string? formKeyString)
    {
        if (string.IsNullOrEmpty(formKeyString))
            return null;

        return FormKey.TryFactory(formKeyString, out var fk) ? fk : null;
    }
}
