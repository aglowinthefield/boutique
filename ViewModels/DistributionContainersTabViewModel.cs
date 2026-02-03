using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Boutique.Models;
using Boutique.Services;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public partial class DistributionContainersTabViewModel : ReactiveObject
{
  private readonly GameDataCacheService _cacheService;

  [Reactive] private bool _isLoading;
  [Reactive] private string _statusMessage = string.Empty;
  [Reactive] private string _searchText = string.Empty;
  [Reactive] private ContainerRecordViewModel? _selectedContainer;
  [Reactive] private bool _hideEmptyContainers = true;
  [Reactive] private string? _selectedCell;
  [Reactive] private string _selectedRespawnsFilter = "Any";

  public DistributionContainersTabViewModel(GameDataCacheService cacheService)
  {
    _cacheService = cacheService;

    var filterPredicate = this.WhenAnyValue(
            x => x.SearchText,
            x => x.HideEmptyContainers,
            x => x.SelectedCell,
            x => x.SelectedRespawnsFilter)
        .Throttle(TimeSpan.FromMilliseconds(150))
        .Select(CreateFilter);

    _cacheService.AllContainers
        .ToObservableChangeSet()
        .Filter(filterPredicate)
        .Sort(SortExpressionComparer<ContainerRecordViewModel>.Ascending(c => c.DisplayName))
        .ObserveOn(RxApp.MainThreadScheduler)
        .Bind(out var filteredContainers)
        .Subscribe(_ => UpdateStatusMessage());

    FilteredContainers = filteredContainers;

    this.WhenAnyValue(x => x.SelectedContainer)
        .ObserveOn(RxApp.MainThreadScheduler)
        .Subscribe(container =>
        {
          SelectedContainerItems.Clear();
          if (container?.Items != null)
          {
            foreach (var item in container.Items)
            {
              SelectedContainerItems.Add(item);
            }
          }
        });

    UpdateStatusMessage();
    _cacheService.CacheLoaded += OnCacheLoaded;

    if (_cacheService.IsLoaded)
    {
      PopulateCellOptions();
    }
  }

  public ReadOnlyObservableCollection<ContainerRecordViewModel> FilteredContainers { get; }
  public ObservableCollection<ContainerContentItem> SelectedContainerItems { get; } = [];
  public ObservableCollection<string> AvailableCells { get; } = [];
  public IReadOnlyList<string> RespawnsFilterOptions { get; } = ["Any", "Yes", "No"];

  private void OnCacheLoaded(object? sender, EventArgs e)
  {
    PopulateCellOptions();
    UpdateStatusMessage();
  }

  private void PopulateCellOptions()
  {
    AvailableCells.Clear();
    AvailableCells.Add(string.Empty);

    var cells = _cacheService.AllContainers
        .SelectMany(c => c.CellPlacements)
        .Distinct()
        .OrderBy(c => c)
        .ToList();

    foreach (var cell in cells)
    {
      AvailableCells.Add(cell);
    }
  }

  private static Func<ContainerRecordViewModel, bool> CreateFilter(
      (string search, bool hideEmpty, string? cell, string respawns) args)
  {
    var (search, hideEmpty, cell, respawns) = args;
    var searchLower = search?.Trim().ToLowerInvariant() ?? string.Empty;

    return container =>
    {
      if (hideEmpty && container.ItemCount == 0)
      {
        return false;
      }

      if (!string.IsNullOrEmpty(cell) && !container.CellPlacements.Contains(cell))
      {
        return false;
      }

      if (respawns == "Yes" && !container.Respawns)
      {
        return false;
      }

      if (respawns == "No" && container.Respawns)
      {
        return false;
      }

      if (string.IsNullOrEmpty(searchLower))
      {
        return true;
      }

      return container.DisplayName.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                 container.EditorId.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                 container.ModName.Contains(searchLower, StringComparison.OrdinalIgnoreCase);
    };
  }

  private void UpdateStatusMessage()
  {
    var total = _cacheService.AllContainers.Count;
    var filtered = FilteredContainers.Count;
    StatusMessage = total == 0
        ? "No containers loaded"
        : $"{filtered:N0} of {total:N0} containers";
  }
}
