using System.Collections.ObjectModel;
using Boutique.Models;
using Boutique.Services;
using Mutagen.Bethesda.Plugins;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Boutique.ViewModels;

public enum RankingCategory
{
  Faction,
  Class,
  Race
}

public partial class DistributionReportCardTabViewModel : ReactiveObject
{
  private readonly GameDataCacheService _cache;
  private readonly ILogger              _logger;
  private readonly IObservable<bool>    _notLoading;

  [Reactive] private bool   _isLoading;
  [Reactive] private string _statusMessage = string.Empty;
  [Reactive] private bool   _hasResults;

  [Reactive] private string _overallGrade        = "-";
  [Reactive] private string _npcCoverageGrade    = "-";
  [Reactive] private string _modUtilizationGrade = "-";
  [Reactive] private string _varietyGrade        = "-";
  [Reactive] private double _overallPercent;
  [Reactive] private double _npcCoveragePercent;
  [Reactive] private double _modUtilizationPercent;
  [Reactive] private double _varietyPercent;

  [Reactive] private int  _eligibleNpcCount;
  [Reactive] private int  _coveredNpcCount;
  [Reactive] private int  _modOutfitCount;
  [Reactive] private int  _usedModOutfitCount;
  [Reactive] private int  _uniqueOutfitCount;
  [Reactive] private bool _hasModOutfits;

  public DistributionReportCardTabViewModel(
    GameDataCacheService cache,
    ILogger logger)
  {
    _cache      = cache;
    _logger     = logger.ForContext<DistributionReportCardTabViewModel>();
    _notLoading = this.WhenAnyValue(vm => vm.IsLoading, loading => !loading);
  }

  public event EventHandler<(RankingCategory Category, string Label)>? RankingClicked;

  [ReactiveCommand]
  private void NavigateToFaction(UncoveredAttributeRanking ranking) =>
    RankingClicked?.Invoke(this, (RankingCategory.Faction, ranking.Label));

  [ReactiveCommand]
  private void NavigateToClass(UncoveredAttributeRanking ranking) =>
    RankingClicked?.Invoke(this, (RankingCategory.Class, ranking.Label));

  [ReactiveCommand]
  private void NavigateToRace(UncoveredAttributeRanking ranking) =>
    RankingClicked?.Invoke(this, (RankingCategory.Race, ranking.Label));

  public ObservableCollection<UncoveredAttributeRanking> UncoveredByFaction { get; } = [];
  public ObservableCollection<UncoveredAttributeRanking> UncoveredByClass { get; } = [];
  public ObservableCollection<UncoveredAttributeRanking> UncoveredByRace { get; } = [];
  public ObservableCollection<UncoveredAttributeRanking> UncoveredByMod { get; } = [];
  public ObservableCollection<UnusedOutfitGroup> UnusedOutfitGroups { get; } = [];

  [ReactiveCommand(CanExecute = nameof(_notLoading))]
  public async Task CalculateAsync()
  {
    IsLoading     = true;
    StatusMessage = "Calculating grades…";

    try
    {
      var result = await Task.Run(ComputeMetrics);

      NpcCoveragePercent    = result.NpcCoveragePercent;
      ModUtilizationPercent = result.ModUtilizationPercent;
      VarietyPercent        = result.VarietyPercent;
      OverallPercent        = result.OverallPercent;

      NpcCoverageGrade    = PercentToGrade(result.NpcCoveragePercent);
      ModUtilizationGrade = result.ModOutfitCount > 0 ? PercentToGrade(result.ModUtilizationPercent) : "–";
      VarietyGrade        = PercentToGrade(result.VarietyPercent);
      OverallGrade        = PercentToGrade(result.OverallPercent);
      HasModOutfits       = result.ModOutfitCount > 0;

      EligibleNpcCount  = result.EligibleNpcCount;
      CoveredNpcCount   = result.CoveredNpcCount;
      ModOutfitCount    = result.ModOutfitCount;
      UsedModOutfitCount = result.UsedModOutfitCount;
      UniqueOutfitCount = result.UniqueOutfitCount;

      ReplaceCollection(UncoveredByFaction, result.UncoveredByFaction);
      ReplaceCollection(UncoveredByClass, result.UncoveredByClass);
      ReplaceCollection(UncoveredByRace, result.UncoveredByRace);
      ReplaceCollection(UncoveredByMod, result.UncoveredByMod);

      UnusedOutfitGroups.Clear();
      foreach (var group in result.UnusedOutfitGroups)
      {
        UnusedOutfitGroups.Add(group);
      }

      HasResults    = true;
      StatusMessage = $"Grade: {OverallGrade} — " +
                      $"{result.CoveredNpcCount:N0}/{result.EligibleNpcCount:N0} NPCs covered, " +
                      $"{result.UsedModOutfitCount:N0}/{result.ModOutfitCount:N0} mod outfits used, " +
                      $"{result.UniqueOutfitCount:N0} unique outfits";

      _logger.Information(
        "Report card calculated: Overall={Overall}, Coverage={Coverage}%, Utilization={Utilization}%, Variety={Variety}%",
        OverallGrade,
        result.NpcCoveragePercent * 100,
        result.ModUtilizationPercent * 100,
        result.VarietyPercent * 100);
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "Failed to calculate report card");
      StatusMessage = "Calculation failed — check logs for details";
    }
    finally
    {
      IsLoading = false;
    }
  }

  private ReportCardResult ComputeMetrics()
  {
    var allNpcs        = _cache.AllNpcs.ToList();
    var allAssignments = _cache.AllNpcOutfitAssignments.ToList();
    var allOutfitRecords = _cache.AllOutfitRecords.ToList();
    var assignmentMap  = allAssignments.ToDictionary(a => a.NpcFormKey);

    var eligibleNpcs = allNpcs
      .Where(n => !n.IsTemplated && !n.IsLeveled && n.DefaultOutfitFormKey != null)
      .ToList();

    var coveredNpcs = eligibleNpcs
      .Where(n =>
        assignmentMap.TryGetValue(n.FormKey, out var a) &&
        a.FinalOutfitFormKey.HasValue &&
        a.FinalOutfitFormKey != n.DefaultOutfitFormKey)
      .ToList();

    double npcCoveragePercent = eligibleNpcs.Count > 0
      ? (double)coveredNpcs.Count / eligibleNpcs.Count
      : 0;

    var modOutfits = allOutfitRecords
      .Where(o => !IsVanillaOrCreationClub(o.FormKey.ModKey))
      .ToList();

    var usedOutfitKeys = new HashSet<FormKey>(
      allAssignments
        .SelectMany(a => a.Distributions)
        .Select(d => d.OutfitFormKey));

    var usedModOutfits = modOutfits
      .Where(o => usedOutfitKeys.Contains(o.FormKey))
      .ToList();

    bool hasModOutfits = modOutfits.Count > 0;
    double modUtilizationPercent = hasModOutfits
      ? (double)usedModOutfits.Count / modOutfits.Count
      : 0;

    var uniqueDistributedOutfits = coveredNpcs
      .Where(n => assignmentMap.TryGetValue(n.FormKey, out var a) && a.FinalOutfitFormKey.HasValue)
      .Select(n => assignmentMap[n.FormKey].FinalOutfitFormKey!.Value)
      .Distinct()
      .Count();

    double varietyPercent = coveredNpcs.Count > 0
      ? Math.Min(1.0, uniqueDistributedOutfits / (coveredNpcs.Count * 0.1))
      : 0;

    double overallPercent = hasModOutfits
      ? (npcCoveragePercent * 0.5) + (modUtilizationPercent * 0.3) + (varietyPercent * 0.2)
      : (npcCoveragePercent * 0.7) + (varietyPercent * 0.3);

    var coveredNpcSet = new HashSet<FormKey>(coveredNpcs.Select(n => n.FormKey));

    LogMetricsBreakdown(allNpcs, assignmentMap, eligibleNpcs, coveredNpcSet, allOutfitRecords, modOutfits, usedModOutfits);
    _logger.Debug(
      "Variety: UniqueOutfits={Unique}, CoveredNpcs={Covered}, Threshold={Threshold:F1}, Pct={Pct:P1}",
      uniqueDistributedOutfits,
      coveredNpcs.Count,
      coveredNpcs.Count * 0.1,
      varietyPercent);
    _logger.Debug(
      "Grades: Coverage={Coverage:P1} (×0.5), Utilization={Util:P1} (×0.3), Variety={Variety:P1} (×0.2), Overall={Overall:P1}",
      npcCoveragePercent,
      modUtilizationPercent,
      varietyPercent,
      overallPercent);

    var (byFaction, byClass, byRace, byMod) = BuildAttributeRankings(eligibleNpcs, coveredNpcSet);

    var usedModOutfitKeys = new HashSet<FormKey>(usedModOutfits.Select(o => o.FormKey));
    var unusedModOutfits  = modOutfits.Where(o => !usedModOutfitKeys.Contains(o.FormKey)).ToList();
    var unusedOutfitGroups = BuildUnusedOutfitGroups(unusedModOutfits);

    return new ReportCardResult(
      npcCoveragePercent,
      modUtilizationPercent,
      varietyPercent,
      overallPercent,
      eligibleNpcs.Count,
      coveredNpcs.Count,
      modOutfits.Count,
      usedModOutfits.Count,
      uniqueDistributedOutfits,
      byFaction,
      byClass,
      byRace,
      byMod,
      unusedOutfitGroups);
  }

  private void LogMetricsBreakdown(
    List<NpcFilterData> allNpcs,
    Dictionary<FormKey, NpcOutfitAssignmentViewModel> assignmentMap,
    List<NpcFilterData> eligibleNpcs,
    HashSet<FormKey> coveredNpcSet,
    List<OutfitRecordViewModel> allOutfitRecords,
    List<OutfitRecordViewModel> modOutfits,
    List<OutfitRecordViewModel> usedModOutfits)
  {
    _logger.Debug(
      "Report card inputs: AllNpcs={AllNpcs}, Assignments={Assignments}, OutfitRecords={Outfits}",
      allNpcs.Count,
      assignmentMap.Count,
      allOutfitRecords.Count);

    _logger.Debug(
      "NPC filtering: Templated={Templated}, Leveled={Leveled}, NoDefaultOutfit={NoOutfit}",
      allNpcs.Count(n => n.IsTemplated),
      allNpcs.Count(n => n.IsLeveled),
      allNpcs.Count(n => n.DefaultOutfitFormKey == null));

    var hasAssignment  = eligibleNpcs.Count(n => assignmentMap.ContainsKey(n.FormKey));
    var hasFinalOutfit = eligibleNpcs.Count(n =>
      assignmentMap.TryGetValue(n.FormKey, out var a) && a.FinalOutfitFormKey.HasValue);
    var sameAsDefault = eligibleNpcs.Count(n =>
      assignmentMap.TryGetValue(n.FormKey, out var a) && a.FinalOutfitFormKey == n.DefaultOutfitFormKey);

    _logger.Debug(
      "NPC coverage: Eligible={Eligible}, HasAssignment={HasAssignment}, HasFinalOutfit={HasFinal}, FinalSameAsDefault={SameAsDefault}, Covered={Covered}",
      eligibleNpcs.Count,
      hasAssignment,
      hasFinalOutfit,
      sameAsDefault,
      coveredNpcSet.Count);

    foreach (var g in allOutfitRecords.GroupBy(o => o.FormKey.ModKey).OrderByDescending(g => g.Count()))
    {
      _logger.Debug(
        "Outfit records from {Mod}: {Count} (excluded={Excluded})",
        g.Key,
        g.Count(),
        IsVanillaOrCreationClub(g.Key));
    }

    _logger.Debug(
      "Mod utilization: ModOutfits={ModOutfits}, Used={Used}",
      modOutfits.Count,
      usedModOutfits.Count);
  }

  private static (List<UncoveredAttributeRanking> ByFaction, List<UncoveredAttributeRanking> ByClass, List<UncoveredAttributeRanking> ByRace, List<UncoveredAttributeRanking> ByMod)
    BuildAttributeRankings(List<NpcFilterData> eligibleNpcs, HashSet<FormKey> coveredNpcSet)
  {
    const int maxRows = 15;

    var factionTotals    = new Dictionary<string, int>();
    var factionUncovered = new Dictionary<string, int>();
    foreach (var npc in eligibleNpcs)
    {
      bool isCovered = coveredNpcSet.Contains(npc.FormKey);
      foreach (var f in npc.Factions)
      {
        var label = f.FactionEditorId ?? f.FactionFormKey.ToString();
        factionTotals[label]    = factionTotals.GetValueOrDefault(label) + 1;
        if (!isCovered)
        {
          factionUncovered[label] = factionUncovered.GetValueOrDefault(label) + 1;
        }
      }
    }

    var byFaction = factionUncovered
      .Select(kvp => new UncoveredAttributeRanking(kvp.Key, kvp.Value, factionTotals[kvp.Key]))
      .OrderByDescending(r => r.UncoveredCount)
      .Take(maxRows)
      .ToList();

    var byClass = BuildRanking(
      eligibleNpcs,
      coveredNpcSet,
      n => n.ClassEditorId ?? "(No Class)",
      maxRows);

    var byRace = BuildRanking(
      eligibleNpcs,
      coveredNpcSet,
      n => n.RaceEditorId ?? "(No Race)",
      maxRows);

    var byMod = BuildRanking(
      eligibleNpcs,
      coveredNpcSet,
      n => n.SourceMod.FileName,
      maxRows);

    return (byFaction, byClass, byRace, byMod);
  }

  private static List<UncoveredAttributeRanking> BuildRanking(
    List<NpcFilterData> eligibleNpcs,
    HashSet<FormKey> coveredNpcSet,
    Func<NpcFilterData, string> labelSelector,
    int maxRows)
  {
    var totals    = new Dictionary<string, int>();
    var uncovered = new Dictionary<string, int>();

    foreach (var npc in eligibleNpcs)
    {
      var label = labelSelector(npc);
      totals[label] = totals.GetValueOrDefault(label) + 1;
      if (!coveredNpcSet.Contains(npc.FormKey))
      {
        uncovered[label] = uncovered.GetValueOrDefault(label) + 1;
      }
    }

    return uncovered
      .Select(kvp => new UncoveredAttributeRanking(kvp.Key, kvp.Value, totals[kvp.Key]))
      .OrderByDescending(r => r.UncoveredCount)
      .Take(maxRows)
      .ToList();
  }

  private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
  {
    target.Clear();
    foreach (var item in source)
    {
      target.Add(item);
    }
  }

  private static List<UnusedOutfitGroup> BuildUnusedOutfitGroups(List<OutfitRecordViewModel> unusedOutfits)
  {
    var groups = unusedOutfits
      .GroupBy(o => o.FormKey.ModKey.FileName)
      .Select(g => new UnusedOutfitGroup(
        g.Key,
        g.Count(),
        g.Select(o => o.EditorID).OrderBy(id => id).ToList()))
      .OrderByDescending(g => g.Count)
      .ToList();

    return groups;
  }

  internal static string PercentToGrade(double pct) => pct switch
  {
    >= 0.8 => "A",
    >= 0.6 => "B",
    >= 0.4 => "C",
    >= 0.2 => "D",
    _      => "F"
  };

  private static bool IsVanillaOrCreationClub(ModKey modKey) =>
    GameAssetLocator.VanillaModKeys.Contains(modKey) ||
    modKey.FileName.String.StartsWith("cc", StringComparison.OrdinalIgnoreCase);
}
