using System.Reactive.Linq;
using Boutique.Services;
using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Boutique.ViewModels;

public partial class IconPickerViewModel : ReactiveObject
{
  [Reactive] private string _searchText = string.Empty;
  [Reactive] private string? _selectedIcon;

  public IconPickerViewModel(string? currentIcon)
  {
    _selectedIcon = currentIcon;

    FilteredIcons = this.WhenAnyValue(x => x.SearchText)
        .Throttle(TimeSpan.FromMilliseconds(150))
        .Select(filter =>
        {
          if (string.IsNullOrWhiteSpace(filter))
          {
            return IconCacheService.Icons;
          }

          return IconCacheService.Icons
                  .Where(icon => icon.Contains(filter, StringComparison.OrdinalIgnoreCase))
                  .ToList();
        })
        .ObserveOn(RxApp.MainThreadScheduler);
  }

  public IObservable<IReadOnlyList<string>> FilteredIcons { get; }

  public static Uri GetIconUri(string iconName) =>
      new($"pack://application:,,,/Assets/sprites/{iconName}", UriKind.Absolute);
}
