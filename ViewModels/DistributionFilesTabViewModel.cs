using System.Collections.ObjectModel;
using System.Reactive;
using Boutique.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Boutique.ViewModels;

public class DistributionFilesTabViewModel : ReactiveObject
{
    private readonly GameDataCacheService _cache;
    private readonly ILogger _logger;

    public DistributionFilesTabViewModel(
        GameDataCacheService cache,
        ILogger logger)
    {
        _cache = cache;
        _logger = logger.ForContext<DistributionFilesTabViewModel>();

        // ForceRefreshAsync invalidates cache; RefreshAsync uses cached data if available
        RefreshCommand = ReactiveCommand.CreateFromTask(ForceRefreshAsync);
        EnsureLoadedCommand = ReactiveCommand.CreateFromTask(RefreshAsync);

        // Subscribe to cache loaded event to populate files
        _cache.CacheLoaded += OnCacheLoaded;

        // If cache is already loaded, populate files immediately
        if (_cache.IsLoaded)
        {
            PopulateFilesFromCache();
        }
    }

    private void OnCacheLoaded(object? sender, EventArgs e) => PopulateFilesFromCache();

    private void PopulateFilesFromCache()
    {
        Files.Clear();
        foreach (var file in _cache.AllDistributionFiles)
        {
            Files.Add(file);
        }

        this.RaisePropertyChanged(nameof(Files));

        StatusMessage = Files.Count == 0
            ? "No outfit distribution files found."
            : $"Found {Files.Count} outfit distribution file(s).";

        _logger.Debug("Populated {Count} distribution files from cache.", Files.Count);
    }

    public ObservableCollection<DistributionFileViewModel> Files { get; } = [];

    [Reactive] public bool IsLoading { get; private set; }

    [Reactive] public string StatusMessage { get; private set; } = "Distribution files not loaded.";

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    /// <summary>
    /// Command to ensure files are loaded (uses cache if available, doesn't force refresh).
    /// Use for auto-load on startup.
    /// </summary>
    public ReactiveCommand<Unit, Unit> EnsureLoadedCommand { get; }

    public async Task RefreshAsync()
    {
        if (IsLoading)
            return;

        try
        {
            IsLoading = true;
            StatusMessage = "Loading distribution files...";

            // Wait for cache to load (uses cached data if available, doesn't invalidate)
            await _cache.EnsureLoadedAsync();

            // Cache load will trigger OnCacheLoaded which populates the files
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load distribution files.");
            Files.Clear();
            StatusMessage = $"Error loading distribution files: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Forces a reload of distribution files, invalidating the cache.
    /// Use when user explicitly wants to re-scan files from disk.
    /// </summary>
    public async Task ForceRefreshAsync()
    {
        if (IsLoading)
            return;

        try
        {
            IsLoading = true;
            StatusMessage = "Refreshing distribution files...";

            // Force reload (invalidates cache and re-scans from disk)
            await _cache.ReloadAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh distribution files.");
            Files.Clear();
            StatusMessage = $"Error loading distribution files: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
