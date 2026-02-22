using System.Collections.ObjectModel;
using Boutique.Models;
using Boutique.Services;
using Mutagen.Bethesda.Plugins;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Serilog;

namespace Boutique.ViewModels;

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

  [Reactive] private int _eligibleNpcCount;
  [Reactive] private int _coveredNpcCount;
  [Reactive] private int _modOutfitCount;
  [Reactive] private int _usedModOutfitCount;
  [Reactive] private int _uniqueOutfitCount;

  public DistributionReportCardTabViewModel(
    GameDataCacheService cache,
    ILogger logger)
  {
    _cache      = cache;
    _logger     = logger.ForContext<DistributionReportCardTabViewModel>();
    _notLoading = this.WhenAnyValue(vm => vm.IsLoading, loading => !loading);
  }

  public ObservableCollection<NpcFactionGroup> UncoveredNpcGroups { get; } = [];
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
      ModUtilizationGrade = PercentToGrade(result.ModUtilizationPercent);
      VarietyGrade        = PercentToGrade(result.VarietyPercent);
      OverallGrade        = PercentToGrade(result.OverallPercent);

      EligibleNpcCount  = result.EligibleNpcCount;
      CoveredNpcCount   = result.CoveredNpcCount;
      ModOutfitCount    = result.ModOutfitCount;
      UsedModOutfitCount = result.UsedModOutfitCount;
      UniqueOutfitCount = result.UniqueOutfitCount;

      UncoveredNpcGroups.Clear();
      foreach (var group in result.UncoveredNpcGroups)
      {
        UncoveredNpcGroups.Add(group);
      }

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
    var allNpcs       = _cache.AllNpcs.ToList();
    var allAssignments = _cache.AllNpcOutfitAssignments.ToList();
    var allOutfitRecords = _cache.AllOutfitRecords.ToList();

    var assignmentMap = allAssignments.ToDictionary(a => a.NpcFormKey);

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
      .Where(o => !GameAssetLocator.VanillaModKeys.Contains(o.FormKey.ModKey))
      .ToList();

    var usedOutfitKeys = new HashSet<FormKey>(
      allAssignments
        .Where(a => a.FinalOutfitFormKey.HasValue)
        .Select(a => a.FinalOutfitFormKey!.Value));

    var usedModOutfits = modOutfits
      .Where(o => usedOutfitKeys.Contains(o.FormKey))
      .ToList();

    double modUtilizationPercent = modOutfits.Count > 0
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

    double overallPercent = (npcCoveragePercent * 0.5) +
                            (modUtilizationPercent * 0.3) +
                            (varietyPercent * 0.2);

    var uncoveredNpcs = eligibleNpcs
      .Where(n =>
        !assignmentMap.TryGetValue(n.FormKey, out var a) ||
        !a.FinalOutfitFormKey.HasValue ||
        a.FinalOutfitFormKey == n.DefaultOutfitFormKey)
      .ToList();

    var uncoveredNpcGroups = BuildUncoveredNpcGroups(uncoveredNpcs);

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
      uncoveredNpcGroups,
      unusedOutfitGroups);
  }

  private static List<NpcFactionGroup> BuildUncoveredNpcGroups(List<NpcFilterData> uncoveredNpcs)
  {
    var groups = new Dictionary<string, List<string>>();

    foreach (var npc in uncoveredNpcs)
    {
      var firstFaction = npc.Factions.Count > 0 ? npc.Factions[0] : null;
      var groupName = firstFaction?.FactionEditorId ?? npc.SourceMod.FileName;
      if (!groups.TryGetValue(groupName, out var list))
      {
        list = [];
        groups[groupName] = list;
      }

      list.Add(npc.DisplayName);
    }

    return groups
      .Select(kvp => new NpcFactionGroup(kvp.Key, kvp.Value.Count, kvp.Value))
      .OrderByDescending(g => g.Count)
      .ToList();
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
}
