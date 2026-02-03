namespace Boutique.Resources;

public static class LocalizationKeys
{
    public const string AppTitle = "AppTitle";

    public static class Tabs
    {
        public const string Distribution = "Tab_Distribution";
        public const string OutfitCreator = "Tab_OutfitCreator";
        public const string ArmorPatch = "Tab_ArmorPatch";
        public const string Settings = "Tab_Settings";
        public const string Create = "Tab_Create";
        public const string NPCs = "Tab_NPCs";
        public const string Outfits = "Tab_Outfits";
        public const string Factions = "Tab_Factions";
        public const string Keywords = "Tab_Keywords";
        public const string Races = "Tab_Races";
        public const string Classes = "Tab_Classes";
    }

    public static class Settings
    {
        public const string Title = "Settings_Title";
        public const string SkyrimRelease = "Settings_SkyrimRelease";
        public const string SkyrimDataPath = "Settings_SkyrimDataPath";
        public const string PatchFileName = "Settings_PatchFileName";
        public const string OutputPath = "Settings_OutputPath";
        public const string OutputPathTooltip = "Settings_OutputPathTooltip";
        public const string Detection = "Settings_Detection";
        public const string Theme = "Settings_Theme";
        public const string Language = "Settings_Language";
        public const string Tutorial = "Settings_Tutorial";
    }

    public static class Buttons
    {
        public const string Browse = "Button_Browse";
        public const string AutoDetect = "Button_AutoDetect";
        public const string RestartTutorial = "Button_RestartTutorial";
        public const string Refresh = "Button_Refresh";
        public const string Close = "Button_Close";
        public const string Save = "Button_Save";
        public const string Cancel = "Button_Cancel";
        public const string ClearAll = "Button_ClearAll";
        public const string ClearFilters = "Button_ClearFilters";
        public const string Preview = "Button_Preview";
        public const string ResetView = "Button_ResetView";
    }

    public static class Status
    {
        public const string Patch = "Status_Patch";
        public const string Ready = "Status_Ready";
        public const string Loading = "Status_Loading";
        public const string Initializing = "Status_Initializing";
        public const string Saving = "Status_Saving";
    }

    public static class Labels
    {
        public const string Search = "Label_Search";
        public const string File = "Label_File";
        public const string Format = "Label_Format";
        public const string Filename = "Label_Filename";
        public const string SavesAs = "Label_SavesAs";
        public const string Suggested = "Label_Suggested";
        public const string FilterPlaceholder = "Label_FilterPlaceholder";
    }

    public static class Headers
    {
        public const string Name = "Header_Name";
        public const string EditorID = "Header_EditorID";
        public const string FormKey = "Header_FormKey";
        public const string FormID = "Header_FormID";
        public const string Mod = "Header_Mod";
        public const string Slots = "Header_Slots";
        public const string Type = "Header_Type";
        public const string Armor = "Header_Armor";
        public const string NPCs = "Header_NPCs";
        public const string FinalOutfit = "Header_FinalOutfit";
        public const string Distributor = "Header_Distributor";
        public const string Targeting = "Header_Targeting";
        public const string Chance = "Header_Chance";
        public const string Conflict = "Header_Conflict";
        public const string Preview = "Header_Preview";
        public const string Copy = "Header_Copy";
        public const string Override = "Header_Override";
    }

    public static class ArmorPatch
    {
        public const string PluginSelection = "ArmorPatch_PluginSelection";
        public const string SourcePlugin = "ArmorPatch_SourcePlugin";
        public const string TargetPlugin = "ArmorPatch_TargetPlugin";
        public const string SourceArmors = "ArmorPatch_SourceArmors";
        public const string TargetArmors = "ArmorPatch_TargetArmors";
        public const string MappingPreview = "ArmorPatch_MappingPreview";
        public const string MapSelection = "ArmorPatch_MapSelection";
        public const string MarkGlamOnly = "ArmorPatch_MarkGlamOnly";
        public const string CreatePatch = "ArmorPatch_CreatePatch";
        public const string GlamOnlyZeroStats = "ArmorPatch_GlamOnlyZeroStats";
        public const string ArmorsFormat = "ArmorPatch_ArmorsFormat";
        public const string CandidatesFormat = "ArmorPatch_CandidatesFormat";
        public const string MappingsFormat = "ArmorPatch_MappingsFormat";
        public const string TotalMappingsFormat = "ArmorPatch_TotalMappingsFormat";
        public const string MappingsQueuedFormat = "ArmorPatch_MappingsQueuedFormat";
        public const string RemoveMapping = "ArmorPatch_RemoveMapping";
        public const string TypeToSearch = "ArmorPatch_TypeToSearch";
    }

    public static class OutfitCreator
    {
        public const string SourcePlugins = "OutfitCreator_SourcePlugins";
        public const string AvailableArmors = "OutfitCreator_AvailableArmors";
        public const string OutfitQueue = "OutfitCreator_OutfitQueue";
        public const string CreateNewOutfit = "OutfitCreator_CreateNewOutfit";
        public const string DropArmorsHere = "OutfitCreator_DropArmorsHere";
        public const string NoOutfitsQueued = "OutfitCreator_NoOutfitsQueued";
        public const string NoPiecesSelected = "OutfitCreator_NoPiecesSelected";
        public const string ExistingOutfitsFormat = "OutfitCreator_ExistingOutfitsFormat";
        public const string CopyExistingOutfits = "OutfitCreator_CopyExistingOutfits";
        public const string SaveOutfits = "OutfitCreator_SaveOutfits";
        public const string OutfitsQueuedFormat = "OutfitCreator_OutfitsQueuedFormat";
        public const string PreviewArmor = "OutfitCreator_PreviewArmor";
        public const string PreviewOutfit = "OutfitCreator_PreviewOutfit";
        public const string DuplicateOutfit = "OutfitCreator_DuplicateOutfit";
        public const string RemoveOutfit = "OutfitCreator_RemoveOutfit";
        public const string RemovePiece = "OutfitCreator_RemovePiece";
        public const string FilterPlugins = "OutfitCreator_FilterPlugins";
    }

    public static class Distribution
    {
        public const string DistributionEntries = "Distribution_DistributionEntries";
        public const string DistributionFilters = "Distribution_DistributionFilters";
        public const string AddEntry = "Distribution_AddEntry";
        public const string SaveFile = "Distribution_SaveFile";
        public const string PasteFilter = "Distribution_PasteFilter";
        public const string PasteFilterTooltip = "Distribution_PasteFilterTooltip";
        public const string PasteFilterFormat = "Distribution_PasteFilterFormat";
        public const string Assign = "Distribution_Assign";
        public const string Outfit = "Distribution_Outfit";
        public const string Keyword = "Distribution_Keyword";
        public const string UseChance = "Distribution_UseChance";
        public const string ChanceTooltip = "Distribution_ChanceTooltip";
        public const string RemoveEntry = "Distribution_RemoveEntry";
        public const string NPCs = "Distribution_NPCs";
        public const string Factions = "Distribution_Factions";
        public const string Keywords = "Distribution_Keywords";
        public const string Races = "Distribution_Races";
        public const string Classes = "Distribution_Classes";
        public const string Traits = "Distribution_Traits";
        public const string Gender = "Distribution_Gender";
        public const string Unique = "Distribution_Unique";
        public const string LevelSkill = "Distribution_LevelSkill";
        public const string LevelSkillTooltip = "Distribution_LevelSkillTooltip";
        public const string FilePreview = "Distribution_FilePreview";
        public const string ParseErrorsFormat = "Distribution_ParseErrorsFormat";
        public const string TargetEntryFormat = "Distribution_TargetEntryFormat";
        public const string TargetEntryNone = "Distribution_TargetEntryNone";
        public const string AddSelectedNpcs = "Distribution_AddSelectedNpcs";
        public const string AddSelectedFactions = "Distribution_AddSelectedFactions";
        public const string AddSelectedKeywords = "Distribution_AddSelectedKeywords";
        public const string AddSelectedRaces = "Distribution_AddSelectedRaces";
        public const string AddSelectedClasses = "Distribution_AddSelectedClasses";
        public const string SelectKeywordTooltip = "Distribution_SelectKeywordTooltip";
    }

    public static class NpcTab
    {
        public const string RefreshTooltip = "NPCs_RefreshTooltip";
        public const string HideVanilla = "NPCs_HideVanilla";
        public const string HideVanillaTooltip = "NPCs_HideVanillaTooltip";
        public const string SearchTooltip = "NPCs_SearchTooltip";
        public const string SpidFilters = "NPCs_SpidFilters";
        public const string Templated = "NPCs_Templated";
        public const string Age = "NPCs_Age";
        public const string Faction = "NPCs_Faction";
        public const string Race = "NPCs_Race";
        public const string Class = "NPCs_Class";
        public const string CopyFilter = "NPCs_CopyFilter";
        public const string CopyFilterTooltip = "NPCs_CopyFilterTooltip";
        public const string ShowingFormat = "NPCs_ShowingFormat";
        public const string DistributionFiles = "NPCs_DistributionFiles";
        public const string Winner = "NPCs_Winner";
        public const string ChanceFormat = "NPCs_ChanceFormat";
        public const string OutfitFormat = "NPCs_OutfitFormat";
        public const string OutfitContents = "NPCs_OutfitContents";
        public const string PreviewOutfit = "NPCs_PreviewOutfit";
        public const string FilterSyntaxPreview = "NPCs_FilterSyntaxPreview";
        public const string SpidFormat = "NPCs_SpidFormat";
        public const string SkyPatcherFormat = "NPCs_SkyPatcherFormat";
        public const string CopySyntaxHint = "NPCs_CopySyntaxHint";
        public const string NpcDetails = "NPCs_NpcDetails";
        public const string Level = "NPCs_Level";
        public const string Voice = "NPCs_Voice";
        public const string Combat = "NPCs_Combat";
        public const string Template = "NPCs_Template";
        public const string TemplateNone = "NPCs_TemplateNone";
        public const string Male = "NPCs_Male";
        public const string Female = "NPCs_Female";
        public const string TraitUnique = "NPCs_Trait_Unique";
        public const string TraitSummonable = "NPCs_Trait_Summonable";
        public const string TraitChild = "NPCs_Trait_Child";
        public const string TraitLeveled = "NPCs_Trait_Leveled";
        public const string RankFormat = "NPCs_RankFormat";
    }

    public static class OutfitsTab
    {
        public const string LoadOutfits = "Outfits_LoadOutfits";
        public const string LoadTooltip = "Outfits_LoadTooltip";
        public const string HideVanillaTooltip = "Outfits_HideVanillaTooltip";
        public const string SearchTooltip = "Outfits_SearchTooltip";
        public const string CopyToPatch = "Outfits_CopyToPatch";
        public const string CopyAsOverride = "Outfits_CopyAsOverride";
        public const string DistributedToNpcs = "Outfits_DistributedToNpcs";
    }

    public static class Restart
    {
        public const string Title = "Restart_Title";
        public const string Message = "Restart_Message";
        public const string Later = "Restart_Later";
        public const string QuitNow = "Restart_QuitNow";
    }

    public static class MissingMasters
    {
        public const string Title = "MissingMasters_Title";
        public const string Header = "MissingMasters_Header";
        public const string Description = "MissingMasters_Description";
        public const string OrphanedFormat = "MissingMasters_OrphanedFormat";
        public const string AddBack = "MissingMasters_AddBack";
        public const string CleanPatch = "MissingMasters_CleanPatch";
    }

    public static class Preview
    {
        public const string Title = "Preview_Title";
        public const string MissingAssetsWarning = "Preview_MissingAssetsWarning";
        public const string BodySlideHint = "Preview_BodySlideHint";
        public const string MissingAssets = "Preview_MissingAssets";
        public const string LightingDebug = "Preview_LightingDebug";
        public const string Ambient = "Preview_Ambient";
        public const string KeyFill = "Preview_KeyFill";
        public const string Rim = "Preview_Rim";
        public const string Frontal = "Preview_Frontal";
    }

    public static class Filters
    {
        public const string GenderTooltip = "Filter_GenderTooltip";
        public const string UniqueTooltip = "Filter_UniqueTooltip";
        public const string TemplatedTooltip = "Filter_TemplatedTooltip";
        public const string ChildTooltip = "Filter_ChildTooltip";
        public const string FactionTooltip = "Filter_FactionTooltip";
        public const string RaceTooltip = "Filter_RaceTooltip";
        public const string ClassTooltip = "Filter_ClassTooltip";
        public const string KeywordTooltip = "Filter_KeywordTooltip";
        public const string ResetTooltip = "Filter_ResetTooltip";
    }

    public static class Theme
    {
        public const string System = "Theme_System";
        public const string Light = "Theme_Light";
        public const string Dark = "Theme_Dark";
    }

    public static class Detection
    {
        public const string MO2DataPath = "Detection_MO2DataPath";
        public const string MO2GamePath = "Detection_MO2GamePath";
        public const string MO2VirtualStore = "Detection_MO2VirtualStore";
        public const string MO2USVFS = "Detection_MO2USVFS";
        public const string Mutagen = "Detection_Mutagen";
        public const string Failed = "Detection_Failed";
    }

    public static class Dialogs
    {
        public const string SelectDataFolder = "Dialog_SelectDataFolder";
        public const string SelectOutputFolder = "Dialog_SelectOutputFolder";
        public const string SelectDistributionFile = "Dialog_SelectDistributionFile";
    }

    public static class Refresh
    {
        public const string Tooltip = "Refresh_Tooltip";
    }

    public static class PatchNameCollision
    {
        public const string Title = "PatchNameCollision_Title";
        public const string Header = "PatchNameCollision_Header";
        public const string Message = "PatchNameCollision_Message";
        public const string Revert = "PatchNameCollision_Revert";
        public const string KeepName = "PatchNameCollision_KeepName";
    }
}
