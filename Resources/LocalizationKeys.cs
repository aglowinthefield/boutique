namespace Boutique.Resources;

public static class LocalizationKeys
{
  public const string AppTitle = "AppTitle";

  public static class Tabs
  {
    public const string DistributionTab = "Tab_Distribution";
    public const string OutfitCreatorTab = "Tab_OutfitCreator";
    public const string ArmorPatchTab    = "Tab_ArmorPatch";
    public const string SettingsTab      = "Tab_Settings";
    public const string Create        = "Tab_Create";
    public const string Containers    = "Tab_Containers";
    public const string NPCs          = "Tab_NPCs";
    public const string Outfits       = "Tab_Outfits";
    public const string Factions      = "Tab_Factions";
    public const string Keywords      = "Tab_Keywords";
    public const string Races         = "Tab_Races";
    public const string Classes       = "Tab_Classes";
    public const string Locations     = "Tab_Locations";
    public const string ReportCard   = "Tab_ReportCard";
  }

  public static class Settings
  {
    public const string Title             = "Settings_Title";
    public const string SkyrimRelease     = "Settings_SkyrimRelease";
    public const string SkyrimDataPath    = "Settings_SkyrimDataPath";
    public const string PatchFileName     = "Settings_PatchFileName";
    public const string OutputPath        = "Settings_OutputPath";
    public const string OutputPathTooltip = "Settings_OutputPathTooltip";
    public const string DetectionMethod    = "Settings_Detection";
    public const string AppTheme          = "Settings_Theme";
    public const string Language          = "Settings_Language";
    public const string Tutorial          = "Settings_Tutorial";

    public static class SettingGroups
    {
      public const string GameConfiguration = "Settings_Group_GameConfiguration";
      public const string PatchSettings     = "Settings_Group_PatchSettings";
      public const string Appearance        = "Settings_Group_Appearance";
      public const string Advanced          = "Settings_Group_Advanced";
    }
  }

  public static class Buttons
  {
    public const string Browse          = "Button_Browse";
    public const string AutoDetect      = "Button_AutoDetect";
    public const string RestartTutorial = "Button_RestartTutorial";
    public const string RefreshData     = "Button_Refresh";
    public const string Close           = "Button_Close";
    public const string Save            = "Button_Save";
    public const string Cancel          = "Button_Cancel";
    public const string ClearAll        = "Button_ClearAll";
    public const string ClearFilters    = "Button_ClearFilters";
    public const string ShowPreview     = "Button_Preview";
    public const string ResetView       = "Button_ResetView";
  }

  public static class Status
  {
    public const string Patch        = "Status_Patch";
    public const string Ready        = "Status_Ready";
    public const string Loading      = "Status_Loading";
    public const string Initializing = "Status_Initializing";
    public const string Saving       = "Status_Saving";
  }

  public static class Labels
  {
    public const string Search            = "Label_Search";
    public const string File              = "Label_File";
    public const string Format            = "Label_Format";
    public const string Filename          = "Label_Filename";
    public const string SavesAs           = "Label_SavesAs";
    public const string Suggested         = "Label_Suggested";
    public const string FilterPlaceholder = "Label_FilterPlaceholder";
  }

  public static class Headers
  {
    public const string Name        = "Header_Name";
    public const string EditorID    = "Header_EditorID";
    public const string FormKey     = "Header_FormKey";
    public const string FormID      = "Header_FormID";
    public const string Mod         = "Header_Mod";
    public const string Slots       = "Header_Slots";
    public const string Type        = "Header_Type";
    public const string Armor       = "Header_Armor";
    public const string NPCs        = "Header_NPCs";
    public const string FinalOutfit = "Header_FinalOutfit";
    public const string Distributor = "Header_Distributor";
    public const string Targeting   = "Header_Targeting";
    public const string Chance      = "Header_Chance";
    public const string Conflict    = "Header_Conflict";
    public const string PreviewCol  = "Header_Preview";
    public const string Copy        = "Header_Copy";
    public const string Override    = "Header_Override";
  }

  public static class ArmorPatch
  {
    public const string PluginSelection      = "ArmorPatch_PluginSelection";
    public const string SourcePlugin         = "ArmorPatch_SourcePlugin";
    public const string TargetPlugin         = "ArmorPatch_TargetPlugin";
    public const string SourceArmors         = "ArmorPatch_SourceArmors";
    public const string TargetArmors         = "ArmorPatch_TargetArmors";
    public const string MappingPreview       = "ArmorPatch_MappingPreview";
    public const string MapSelection         = "ArmorPatch_MapSelection";
    public const string MarkGlamOnly         = "ArmorPatch_MarkGlamOnly";
    public const string CreatePatch          = "ArmorPatch_CreatePatch";
    public const string GlamOnlyZeroStats    = "ArmorPatch_GlamOnlyZeroStats";
    public const string ArmorsFormat         = "ArmorPatch_ArmorsFormat";
    public const string CandidatesFormat     = "ArmorPatch_CandidatesFormat";
    public const string MappingsFormat       = "ArmorPatch_MappingsFormat";
    public const string TotalMappingsFormat  = "ArmorPatch_TotalMappingsFormat";
    public const string MappingsQueuedFormat = "ArmorPatch_MappingsQueuedFormat";
    public const string RemoveMapping        = "ArmorPatch_RemoveMapping";
    public const string TypeToSearch         = "ArmorPatch_TypeToSearch";
  }

  public static class OutfitCreator
  {
    public const string SourcePlugins         = "OutfitCreator_SourcePlugins";
    public const string AvailableArmors       = "OutfitCreator_AvailableArmors";
    public const string OutfitQueue           = "OutfitCreator_OutfitQueue";
    public const string CreateNewOutfit       = "OutfitCreator_CreateNewOutfit";
    public const string DropArmorsHere        = "OutfitCreator_DropArmorsHere";
    public const string NoOutfitsQueued       = "OutfitCreator_NoOutfitsQueued";
    public const string NoPiecesSelected      = "OutfitCreator_NoPiecesSelected";
    public const string ExistingOutfitsFormat = "OutfitCreator_ExistingOutfitsFormat";
    public const string CopyExistingOutfits   = "OutfitCreator_CopyExistingOutfits";
    public const string SaveOutfits           = "OutfitCreator_SaveOutfits";
    public const string OutfitsQueuedFormat   = "OutfitCreator_OutfitsQueuedFormat";
    public const string PreviewArmor          = "OutfitCreator_PreviewArmor";
    public const string PreviewOutfit         = "OutfitCreator_PreviewOutfit";
    public const string DuplicateOutfit       = "OutfitCreator_DuplicateOutfit";
    public const string RemoveOutfit          = "OutfitCreator_RemoveOutfit";
    public const string RemovePiece           = "OutfitCreator_RemovePiece";
    public const string FilterPlugins         = "OutfitCreator_FilterPlugins";
  }

  public static class LeveledList
  {
    public const string OutfitsTab          = "LeveledList_OutfitsTab";
    public const string LeveledListsTab      = "LeveledList_LeveledListsTab";
    public const string CreateLeveledList    = "LeveledList_CreateLeveledList";
    public const string SaveLeveledLists     = "LeveledList_SaveLeveledLists";
    public const string ImportFromPlugin     = "LeveledList_ImportFromPlugin";
    public const string ImportTooltip        = "LeveledList_ImportTooltip";
    public const string NoListsQueued        = "LeveledList_NoListsQueued";
    public const string DropArmorsHere       = "LeveledList_DropArmorsHere";
    public const string CreateHint           = "LeveledList_CreateHint";
    public const string DistributionHint     = "LeveledList_DistributionHint";
    public const string UseAll               = "LeveledList_UseAll";
    public const string UseAllTooltip        = "LeveledList_UseAllTooltip";
    public const string RemoveList           = "LeveledList_RemoveList";
    public const string RemoveEntry          = "LeveledList_RemoveEntry";
    public const string LevelShort           = "LeveledList_LevelShort";
    public const string CountHeader          = "LeveledList_CountHeader";
    public const string NestListLabel        = "LeveledList_NestListLabel";
    public const string NestListTooltip      = "LeveledList_NestListTooltip";
    public const string AddListLabel         = "LeveledList_AddListLabel";
    public const string AddListTooltip       = "LeveledList_AddListTooltip";
    public const string RemoveFromOutfit     = "LeveledList_RemoveFromOutfit";
    public const string ListBadge            = "LeveledList_ListBadge";
    public const string SlotConflictTooltip  = "LeveledList_SlotConflictTooltip";
  }

  public static class Distribution
  {
    public const string DistributionEntries  = "Distribution_DistributionEntries";
    public const string DistributionFilters  = "Distribution_DistributionFilters";
    public const string AddEntry             = "Distribution_AddEntry";
    public const string SaveFile             = "Distribution_SaveFile";
    public const string PasteFilter          = "Distribution_PasteFilter";
    public const string PasteFilterTooltip   = "Distribution_PasteFilterTooltip";
    public const string PasteFilterFormat    = "Distribution_PasteFilterFormat";
    public const string Assign               = "Distribution_Assign";
    public const string Outfit               = "Distribution_Outfit";
    public const string Keyword              = "Distribution_Keyword";
    public const string UseChance            = "Distribution_UseChance";
    public const string ChanceTooltip        = "Distribution_ChanceTooltip";
    public const string RemoveEntry          = "Distribution_RemoveEntry";
    public const string NPCs                 = "Distribution_NPCs";
    public const string Factions             = "Distribution_Factions";
    public const string Keywords             = "Distribution_Keywords";
    public const string Races                = "Distribution_Races";
    public const string Classes              = "Distribution_Classes";
    public const string Locations            = "Distribution_Locations";
    public const string Traits               = "Distribution_Traits";
    public const string Gender               = "Distribution_Gender";
    public const string Unique               = "Distribution_Unique";
    public const string LevelSkill           = "Distribution_LevelSkill";
    public const string LevelSkillTooltip    = "Distribution_LevelSkillTooltip";
    public const string FilePreview          = "Distribution_FilePreview";
    public const string ParseErrorsFormat    = "Distribution_ParseErrorsFormat";
    public const string TargetEntryFormat    = "Distribution_TargetEntryFormat";
    public const string TargetEntryNone      = "Distribution_TargetEntryNone";
    public const string AddSelectedNpcs      = "Distribution_AddSelectedNpcs";
    public const string AddSelectedFactions  = "Distribution_AddSelectedFactions";
    public const string AddSelectedKeywords  = "Distribution_AddSelectedKeywords";
    public const string AddSelectedRaces     = "Distribution_AddSelectedRaces";
    public const string AddSelectedClasses   = "Distribution_AddSelectedClasses";
    public const string AddSelectedLocations = "Distribution_AddSelectedLocations";
    public const string SelectKeywordTooltip = "Distribution_SelectKeywordTooltip";
  }

  public static class NpcTab
  {
    public const string RefreshTooltip      = "NPCs_RefreshTooltip";
    public const string DistributionStatus        = "NPCs_DistributionStatus";
    public const string DistributionStatusTooltip = "NPCs_DistributionStatusTooltip";
    public const string SearchTooltip             = "NPCs_SearchTooltip";
    public const string SpidFilters         = "NPCs_SpidFilters";
    public const string Templated           = "NPCs_Templated";
    public const string Age                 = "NPCs_Age";
    public const string Faction             = "NPCs_Faction";
    public const string Race                = "NPCs_Race";
    public const string Class               = "NPCs_Class";
    public const string CopyFilter          = "NPCs_CopyFilter";
    public const string CopyFilterTooltip   = "NPCs_CopyFilterTooltip";
    public const string ShowingFormat       = "NPCs_ShowingFormat";
    public const string DistributionFiles   = "NPCs_DistributionFiles";
    public const string Winner              = "NPCs_Winner";
    public const string ChanceFormat        = "NPCs_ChanceFormat";
    public const string OutfitFormat        = "NPCs_OutfitFormat";
    public const string OutfitContents      = "NPCs_OutfitContents";
    public const string PreviewOutfit       = "NPCs_PreviewOutfit";
    public const string FilterSyntaxPreview = "NPCs_FilterSyntaxPreview";
    public const string SpidFormat          = "NPCs_SpidFormat";
    public const string SkyPatcherFormat    = "NPCs_SkyPatcherFormat";
    public const string CopySyntaxHint      = "NPCs_CopySyntaxHint";
    public const string NpcDetails          = "NPCs_NpcDetails";
    public const string Level               = "NPCs_Level";
    public const string Voice               = "NPCs_Voice";
    public const string Combat              = "NPCs_Combat";
    public const string Template            = "NPCs_Template";
    public const string TemplateNone        = "NPCs_TemplateNone";
    public const string Male                = "NPCs_Male";
    public const string Female              = "NPCs_Female";
    public const string TraitUnique         = "NPCs_Trait_Unique";
    public const string TraitSummonable     = "NPCs_Trait_Summonable";
    public const string TraitChild          = "NPCs_Trait_Child";
    public const string TraitLeveled        = "NPCs_Trait_Leveled";
    public const string RankFormat          = "NPCs_RankFormat";
  }

  public static class OutfitsTab
  {
    public const string LoadOutfits        = "Outfits_LoadOutfits";
    public const string LoadTooltip        = "Outfits_LoadTooltip";
    public const string HideVanilla        = "Outfits_HideVanilla";
    public const string HideVanillaTooltip = "Outfits_HideVanillaTooltip";
    public const string SearchTooltip      = "Outfits_SearchTooltip";
    public const string CopyToPatch        = "Outfits_CopyToPatch";
    public const string CopyAsOverride     = "Outfits_CopyAsOverride";
    public const string DistributedToNpcs  = "Outfits_DistributedToNpcs";
  }

  public static class Restart
  {
    public const string Title   = "Restart_Title";
    public const string Message = "Restart_Message";
    public const string Later   = "Restart_Later";
    public const string QuitNow = "Restart_QuitNow";
  }

  public static class MissingMasters
  {
    public const string Title          = "MissingMasters_Title";
    public const string Header         = "MissingMasters_Header";
    public const string Description    = "MissingMasters_Description";
    public const string OrphanedFormat = "MissingMasters_OrphanedFormat";
    public const string AddBack        = "MissingMasters_AddBack";
    public const string CleanPatch     = "MissingMasters_CleanPatch";
  }

  public static class Preview
  {
    public const string Title                = "Preview_Title";
    public const string MissingAssetsWarning = "Preview_MissingAssetsWarning";
    public const string BodySlideHint        = "Preview_BodySlideHint";
    public const string MissingAssets        = "Preview_MissingAssets";
    public const string LightingDebug        = "Preview_LightingDebug";
    public const string Ambient              = "Preview_Ambient";
    public const string KeyFill              = "Preview_KeyFill";
    public const string Rim                  = "Preview_Rim";
    public const string Frontal              = "Preview_Frontal";
  }

  public static class Filters
  {
    public const string GenderTooltip    = "Filter_GenderTooltip";
    public const string UniqueTooltip    = "Filter_UniqueTooltip";
    public const string TemplatedTooltip = "Filter_TemplatedTooltip";
    public const string ChildTooltip     = "Filter_ChildTooltip";
    public const string FactionTooltip   = "Filter_FactionTooltip";
    public const string RaceTooltip      = "Filter_RaceTooltip";
    public const string ClassTooltip     = "Filter_ClassTooltip";
    public const string KeywordTooltip   = "Filter_KeywordTooltip";
    public const string ResetTooltip     = "Filter_ResetTooltip";
  }

  public static class Theme
  {
    public const string System = "Theme_System";
    public const string Light  = "Theme_Light";
    public const string Dark   = "Theme_Dark";
  }

  public static class Detection
  {
    public const string MO2DataPath     = "Detection_MO2DataPath";
    public const string MO2GamePath     = "Detection_MO2GamePath";
    public const string MO2VirtualStore = "Detection_MO2VirtualStore";
    public const string MO2USVFS        = "Detection_MO2USVFS";
    public const string Mutagen         = "Detection_Mutagen";
    public const string Failed          = "Detection_Failed";
  }

  public static class Dialogs
  {
    public const string SelectDataFolder       = "Dialog_SelectDataFolder";
    public const string SelectOutputFolder     = "Dialog_SelectOutputFolder";
    public const string SelectDistributionFile = "Dialog_SelectDistributionFile";
  }

  public static class Refresh
  {
    public const string Tooltip = "Refresh_Tooltip";
  }

  public static class PatchNameCollision
  {
    public const string Title    = "PatchNameCollision_Title";
    public const string Header   = "PatchNameCollision_Header";
    public const string Message  = "PatchNameCollision_Message";
    public const string Revert   = "PatchNameCollision_Revert";
    public const string KeepName = "PatchNameCollision_KeepName";
  }

  public static class Messages
  {
    public const string Ready                         = "Msg_Ready";
    public const string Saving                        = "Msg_Saving";
    public const string ErrorGeneric                  = "Msg_ErrorGeneric";
    public const string ErrorTitle                    = "Msg_ErrorTitle";
    public const string InitializingMutagen           = "Msg_InitializingMutagen";
    public const string LoadedPluginsWithArmors       = "Msg_LoadedPluginsWithArmors";
    public const string LoadingArmorsFrom             = "Msg_LoadingArmorsFrom";
    public const string LoadedArmorsFrom              = "Msg_LoadedArmorsFrom";
    public const string LoadedArmorsFromOutfit        = "Msg_LoadedArmorsFromOutfit";
    public const string ErrorLoadingArmorsFrom        = "Msg_ErrorLoadingArmorsFrom";
    public const string ErrorLoadingPluginTitle       = "Msg_ErrorLoadingPluginTitle";
    public const string FailedLoadArmors              = "Msg_FailedLoadArmors";
    public const string MappedArmorsTo                = "Msg_MappedArmorsTo";
    public const string ErrorMappingArmors            = "Msg_ErrorMappingArmors";
    public const string MarkedGlamOnly                = "Msg_MarkedGlamOnly";
    public const string ErrorMarkingGlamOnly          = "Msg_ErrorMarkingGlamOnly";
    public const string ClearedMappings               = "Msg_ClearedMappings";
    public const string RemovedMappingFor             = "Msg_RemovedMappingFor";
    public const string GlamOnlySummary               = "Msg_GlamOnlySummary";
    public const string NotMapped                     = "Msg_NotMapped";
    public const string BuildingPreviewQuoted         = "Msg_BuildingPreviewQuoted";
    public const string PreviewReadyQuoted            = "Msg_PreviewReadyQuoted";
    public const string PreviewError                  = "Msg_PreviewError";
    public const string CreatingPatch                 = "Msg_CreatingPatch";
    public const string NoMappedArmors                = "Msg_NoMappedArmors";
    public const string OverwritePatchConfirm         = "Msg_OverwritePatchConfirm";
    public const string PatchCanceled                 = "Msg_PatchCanceled";
    public const string FailedToCreatePatch           = "Msg_FailedToCreatePatch";
    public const string UnexpectedError               = "Msg_UnexpectedError";
    public const string ErrorCreatingPatch            = "Msg_ErrorCreatingPatch";
    public const string SelectFilePath                = "Msg_SelectFilePath";
    public const string ConflictsDetectedHeader       = "Msg_ConflictsDetectedHeader";
    public const string ConflictsRenameNote           = "Msg_ConflictsRenameNote";
    public const string ConflictsZPrefixNote          = "Msg_ConflictsZPrefixNote";
    public const string ConflictsContinueFilename     = "Msg_ConflictsContinueFilename";
    public const string ConflictsTitle                = "Msg_ConflictsTitle";
    public const string SaveCancelled                 = "Msg_SaveCancelled";
    public const string OverwriteFileConfirm          = "Msg_OverwriteFileConfirm";
    public const string ConfirmOverwriteTitle         = "Msg_ConfirmOverwriteTitle";
    public const string SavingDistributionFile        = "Msg_SavingDistributionFile";
    public const string SavedDistributionFile         = "Msg_SavedDistributionFile";
    public const string ErrorSavingFile               = "Msg_ErrorSavingFile";
    public const string UnsavedChangesPrompt          = "Msg_UnsavedChangesPrompt";
    public const string UnsavedChangesTitle           = "Msg_UnsavedChangesTitle";
    public const string SavedFile                     = "Msg_SavedFile";
    public const string FileNotExist                  = "Msg_FileNotExist";
    public const string LoadingDistributionFile       = "Msg_LoadingDistributionFile";
    public const string LoadedDistributionEntries     = "Msg_LoadedDistributionEntries";
    public const string ParseErrorsSuffix             = "Msg_ParseErrorsSuffix";
    public const string ErrorLoadingFile              = "Msg_ErrorLoadingFile";
    public const string ErrorGeneratingContent        = "Msg_ErrorGeneratingContent";
    public const string SetDataPathScanNpcs           = "Msg_SetDataPathScanNpcs";
    public const string DataPathNotExist              = "Msg_DataPathNotExist";
    public const string InitializingSkyrim            = "Msg_InitializingSkyrim";
    public const string LoadingGameData               = "Msg_LoadingGameData";
    public const string LoadingGameDataPlugins        = "Msg_LoadingGameDataPlugins";
    public const string LoadedGameData                = "Msg_LoadedGameData";
    public const string ErrorScanningNpcs             = "Msg_ErrorScanningNpcs";
    public const string NoOutfitSelectedPreview       = "Msg_NoOutfitSelectedPreview";
    public const string InitializeBeforePreview       = "Msg_InitializeBeforePreview";
    public const string NoArmorPieces                 = "Msg_NoArmorPieces";
    public const string BuildingPreview               = "Msg_BuildingPreview";
    public const string PreviewReady                  = "Msg_PreviewReady";
    public const string FailedPreviewOutfit           = "Msg_FailedPreviewOutfit";
    public const string NoOutfitForNpc                = "Msg_NoOutfitForNpc";
    public const string CouldNotResolveOutfit         = "Msg_CouldNotResolveOutfit";
    public const string NoDistributionToPreview       = "Msg_NoDistributionToPreview";
    public const string BuildingOutfitPreview         = "Msg_BuildingOutfitPreview";
    public const string PreviewReadyWithCount         = "Msg_PreviewReadyWithCount";
    public const string FailedPreviewOutfits          = "Msg_FailedPreviewOutfits";
    public const string FailedPreviewGeneric          = "Msg_FailedPreviewGeneric";
    public const string LinkCacheNotAvailable         = "Msg_LinkCacheNotAvailable";
    public const string FoundNpcDistributions         = "Msg_FoundNpcDistributions";
    public const string LoadingNpcOutfits             = "Msg_LoadingNpcOutfits";
    public const string ErrorLoadingNpcOutfits        = "Msg_ErrorLoadingNpcOutfits";
    public const string RefreshingNpcOutfits          = "Msg_RefreshingNpcOutfits";
    public const string ErrorRefreshingNpcOutfits     = "Msg_ErrorRefreshingNpcOutfits";
    public const string NoFiltersActive               = "Msg_NoFiltersActive";
    public const string NoFiltersToCopy               = "Msg_NoFiltersToCopy";
    public const string FilterCopied                  = "Msg_FilterCopied";
    public const string SetDataPathLoadOutfits        = "Msg_SetDataPathLoadOutfits";
    public const string LoadingOutfits                = "Msg_LoadingOutfits";
    public const string LoadedOutfits                 = "Msg_LoadedOutfits";
    public const string ErrorLoadingOutfits           = "Msg_ErrorLoadingOutfits";
    public const string NoContainers                  = "Msg_NoContainers";
    public const string ContainersCount               = "Msg_ContainersCount";
    public const string CalculatingGrades             = "Msg_CalculatingGrades";
    public const string GradeSummary                  = "Msg_GradeSummary";
    public const string CalculationFailed             = "Msg_CalculationFailed";
    public const string NothingToSave                 = "Msg_NothingToSave";
    public const string ErrorSavingTitle              = "Msg_ErrorSavingTitle";
    public const string UnexpectedErrorSaving         = "Msg_UnexpectedErrorSaving";
    public const string ErrorSaving                   = "Msg_ErrorSaving";
    public const string MissingMastersCheckFailedTitle = "Msg_MissingMastersCheckFailedTitle";
    public const string CouldNotVerifyMasters         = "Msg_CouldNotVerifyMasters";
    public const string ErrorCleaningPatchTitle       = "Msg_ErrorCleaningPatchTitle";
    public const string SelectPluginImportLists       = "Msg_SelectPluginImportLists";
    public const string SelectSpecificPlugin          = "Msg_SelectSpecificPlugin";
    public const string ImportingLeveledLists         = "Msg_ImportingLeveledLists";
    public const string NoLeveledListsFound           = "Msg_NoLeveledListsFound";
    public const string ImportedLeveledLists          = "Msg_ImportedLeveledLists";
    public const string AllListsLoaded                = "Msg_AllListsLoaded";
    public const string FailedOpenLogFile             = "Msg_FailedOpenLogFile";
    public const string LogFileNotExist               = "Msg_LogFileNotExist";
    public const string LogFileNotFoundTitle          = "Msg_LogFileNotFoundTitle";
    public const string FailedOpenLogsFolder          = "Msg_FailedOpenLogsFolder";
    public const string LogsFolderNotExist            = "Msg_LogsFolderNotExist";
    public const string FolderNotFoundTitle           = "Msg_FolderNotFoundTitle";
    public const string FormatChangePrompt            = "Msg_FormatChangePrompt";
    public const string FormatChangeTitle             = "Msg_FormatChangeTitle";
    public const string WritingPatch                  = "Msg_WritingPatch";
    public const string RefreshingLoadOrder           = "Msg_RefreshingLoadOrder";
    public const string NoValidMatches                = "Msg_NoValidMatches";
    public const string PatchingArmor                 = "Msg_PatchingArmor";
    public const string PatchCreated                  = "Msg_PatchCreated";
    public const string WritingOutfit                 = "Msg_WritingOutfit";
    public const string WritingLeveledList            = "Msg_WritingLeveledList";
    public const string SavedTo                       = "Msg_SavedTo";
    public const string OutfitsCount                  = "Msg_OutfitsCount";
    public const string LeveledListsCount             = "Msg_LeveledListsCount";
    public const string ListJoin                      = "Msg_ListJoin";
    public const string ZeroRecords                   = "Msg_ZeroRecords";
    public const string MutagenNotInitialized         = "Msg_MutagenNotInitialized";
    public const string InvalidPluginName             = "Msg_InvalidPluginName";
    public const string CannotWriteFileLocked         = "Msg_CannotWriteFileLocked";
    public const string CannotWriteFile               = "Msg_CannotWriteFile";
    public const string CannotWriteUnknown            = "Msg_CannotWriteUnknown";
    public const string RemovedOutfitsMissingMasters  = "Msg_RemovedOutfitsMissingMasters";
    public const string PatchFileNotExist             = "Msg_PatchFileNotExist";
    public const string ErrorCleaningPatch            = "Msg_ErrorCleaningPatch";
    public const string UpdateDownloadFailed          = "Msg_UpdateDownloadFailed";
    public const string UpdateErrorTitle              = "Msg_UpdateErrorTitle";
    public const string LatestVersion                 = "Msg_LatestVersion";
    public const string NoUpdateTitle                 = "Msg_NoUpdateTitle";
    public const string AutosaveNoticeBody            = "Msg_AutosaveNoticeBody";
    public const string AutosaveNoticeTitle           = "Msg_AutosaveNoticeTitle";
  }

  public static class View
  {
    // Entry editor (DistributionEntryEditorView)
    public const string EntryDetails               = "View_EntryDetails";
    public const string Filters                    = "View_Filters";
    public const string SelectEntryHint            = "View_SelectEntryHint";
    public const string ExclusiveGroupNameTooltip  = "View_ExclusiveGroupNameTooltip";
    public const string ExclusiveGroupFormsTooltip = "View_ExclusiveGroupFormsTooltip";
    public const string FormsLabel                 = "View_FormsLabel";
    public const string ChanceLabel                = "View_ChanceLabel";
    public const string Traits                     = "View_Traits";
    public const string GenderLabel                = "View_GenderLabel";
    public const string UniqueLabel                = "View_UniqueLabel";
    public const string LevelLabel                 = "View_LevelLabel";
    public const string LevelNone                  = "View_LevelNone";
    public const string LevelLevel                 = "View_LevelLevel";
    public const string LevelSkillLevel            = "View_LevelSkillLevel";
    public const string LevelSkillWeight           = "View_LevelSkillWeight";
    public const string LevelRaw                   = "View_LevelRaw";
    public const string Min                        = "View_Min";
    public const string Max                        = "View_Max";
    public const string ToggleNegationTooltip      = "View_ToggleNegationTooltip";
    public const string AndOrLogicFormat           = "View_AndOrLogicFormat";
    public const string OutfitFilters              = "View_OutfitFilters";
    public const string RawFilters                 = "View_RawFilters";
    public const string StringLabel                = "View_StringLabel";
    public const string FormLabel                  = "View_FormLabel";
    public const string RawStringFiltersTooltip    = "View_RawStringFiltersTooltip";
    public const string RawFormFiltersTooltip      = "View_RawFormFiltersTooltip";
    public const string Results                    = "View_Results";
    public const string TotalNpcsFormat            = "View_TotalNpcsFormat";
    public const string Criteria                   = "View_Criteria";

    // Filter tabs / NPC / Outfits / Entry list / File preview
    public const string TargetFormat                 = "View_TargetFormat";
    public const string TargetNone                   = "View_TargetNone";
    public const string TargetOutfitsHint            = "View_TargetOutfitsHint";
    public const string AddSelectedOutfits           = "View_AddSelectedOutfits";
    public const string ShowingPrefix                = "View_ShowingPrefix";
    public const string OfSeparator                  = "View_OfSeparator";
    public const string NpcsSuffix                   = "View_NpcsSuffix";
    public const string OutfitFileTooltip            = "View_OutfitFileTooltip";
    public const string ContainsLeveledLists         = "View_ContainsLeveledLists";
    public const string ContainsLeveledListsTooltip  = "View_ContainsLeveledListsTooltip";
    public const string MoreActions                  = "View_MoreActions";
    public const string NpcCountFormat               = "View_NpcCountFormat";
    public const string UnresolvedFiltersTooltip     = "View_UnresolvedFiltersTooltip";
    public const string IntraFileOverlapsFormat      = "View_IntraFileOverlapsFormat";
    public const string ViewButton                   = "View_ViewButton";
    public const string ParseErrorsSuffix            = "View_ParseErrorsSuffix";
    public const string Plugin                       = "View_Plugin";
    public const string ArValFormat                  = "View_ArValFormat";

    // Report card (DistributionReportCardTabView)
    public const string Overall                 = "View_Overall";
    public const string NpcCoverage             = "View_NpcCoverage";
    public const string ModUtilization          = "View_ModUtilization";
    public const string OutfitVariety           = "View_OutfitVariety";
    public const string CalculateGrade          = "View_CalculateGrade";
    public const string CoverageSummaryFormat   = "View_CoverageSummaryFormat";
    public const string ClickCalculateHint      = "View_ClickCalculateHint";
    public const string SourceMods              = "View_SourceMods";
    public const string UnusedOutfitsFormat     = "View_UnusedOutfitsFormat";
    public const string UnusedArmorsFormat      = "View_UnusedArmorsFormat";
    public const string UnusedArmorsHint        = "View_UnusedArmorsHint";
    public const string NotApplicable           = "View_NotApplicable";

    // Containers tab (DistributionContainersTabView)
    public const string HideEmptyContainers = "View_HideEmptyContainers";
    public const string CellLabel           = "View_CellLabel";
    public const string RespawnsLabel       = "View_RespawnsLabel";
    public const string ItemsHeader         = "View_ItemsHeader";
    public const string MerchantHeader      = "View_MerchantHeader";
    public const string CellsHeader         = "View_CellsHeader";
    public const string RespawnsHeader      = "View_RespawnsHeader";
    public const string ItemHeader          = "View_ItemHeader";
    public const string CountHeader         = "View_CountHeader";
    public const string SelectContainer     = "View_SelectContainer";
    public const string SelectContainerHint = "View_SelectContainerHint";

    // Preview window (OutfitPreviewWindow)
    public const string PreviousOutfitTooltip = "View_PreviousOutfitTooltip";
    public const string NextOutfitTooltip     = "View_NextOutfitTooltip";
    public const string LoadingPreview        = "View_LoadingPreview";
    public const string LeveledItemsNotice    = "View_LeveledItemsNotice";
    public const string LeveledItemsHint      = "View_LeveledItemsHint";
    public const string Randomize             = "View_Randomize";
    public const string ToggleTabTooltip      = "View_ToggleTabTooltip";
    public const string ShowBaseBody          = "View_ShowBaseBody";
    public const string HideBaseBodyTooltip   = "View_HideBaseBodyTooltip";
    public const string OutfitCounterFormat   = "View_OutfitCounterFormat";
    public const string UnknownOutfit         = "View_UnknownOutfit";
    public const string FromSourceFormat      = "View_FromSourceFormat";
    public const string FromSourceWinnerFormat = "View_FromSourceWinnerFormat";
    public const string NoGeometry            = "View_NoGeometry";

    // Settings panel (SettingsPanelView)
    public const string AutoUpdate            = "View_AutoUpdate";
    public const string CheckUpdatesOnStartup = "View_CheckUpdatesOnStartup";
    public const string CheckNow              = "View_CheckNow";
    public const string Experimental          = "View_Experimental";
    public const string ShowContainersTab     = "View_ShowContainersTab";
    public const string DebugGroup            = "View_DebugGroup";
    public const string DebugLogging          = "View_DebugLogging";
    public const string EnableDebugLogging    = "View_EnableDebugLogging";
    public const string LogFiles              = "View_LogFiles";
    public const string OpenTodaysLog         = "View_OpenTodaysLog";
    public const string OpenLogsFolder        = "View_OpenLogsFolder";

    // Outfit creator (OutfitCreatorView)
    public const string DragToReorder       = "View_DragToReorder";
    public const string ClickChangeIcon     = "View_ClickChangeIcon";
    public const string ClickRename         = "View_ClickRename";
    public const string RemoveSeparator     = "View_RemoveSeparator";
    public const string TypeLabel           = "View_TypeLabel";
    public const string SlotLabel           = "View_SlotLabel";
    public const string AddSeparator        = "View_AddSeparator";
    public const string AddSeparatorTooltip = "View_AddSeparatorTooltip";
    public const string PiecesFormat        = "View_PiecesFormat";
    public const string ItemsCountFormat    = "View_ItemsCountFormat";

    // Update dialog (UpdateDialog)
    public const string UpdateAvailableTitle  = "View_UpdateAvailableTitle";
    public const string UpdateAvailableHeader = "View_UpdateAvailableHeader";
    public const string CurrentVersionLabel   = "View_CurrentVersionLabel";
    public const string LatestVersionLabel    = "View_LatestVersionLabel";
    public const string WhatsNew              = "View_WhatsNew";
    public const string SkipThisVersion       = "View_SkipThisVersion";
    public const string RemindMeLater         = "View_RemindMeLater";
    public const string UpdateNow             = "View_UpdateNow";

    // Misc dialogs
    public const string SelectIconTitle                 = "View_SelectIconTitle";
    public const string FilterIcons                     = "View_FilterIcons";
    public const string ClearIcon                       = "View_ClearIcon";
    public const string Ok                              = "View_Ok";
    public const string InputTitle                      = "View_InputTitle";
    public const string IntraFileOverlapsTitle          = "View_IntraFileOverlapsTitle";
    public const string IntraFileOverlapsDescription    = "View_IntraFileOverlapsDescription";
    public const string IntraFileOverlapsHeaderFormat   = "View_IntraFileOverlapsHeaderFormat";
    public const string ConfirmRemoveMessage            = "View_ConfirmRemoveMessage";
    public const string ConfirmRemoveTitle              = "View_ConfirmRemoveTitle";
    public const string PatchCreatedTitle               = "View_PatchCreatedTitle";
    public const string OverwritePatchTitle             = "View_OverwritePatchTitle";
    public const string ConfirmDeleteTitle              = "View_ConfirmDeleteTitle";
    public const string CreateOutfitTitle               = "View_CreateOutfitTitle";
    public const string MissingMastersSummaryFormat     = "View_MissingMastersSummaryFormat";
  }
}
