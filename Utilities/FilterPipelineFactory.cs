using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Boutique.ViewModels;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;

namespace Boutique.Utilities;

public static class FilterPipelineFactory
{
  public static IDisposable CreateSearchFilter<T>(
      IObservable<string?> searchTextObservable,
      ReadOnlyObservableCollection<T> sourceCollection,
      out ReadOnlyObservableCollection<T> filteredCollection,
      TimeSpan? throttle = null)
      where T : ISelectableRecordViewModel
  {
    var filterObservable = searchTextObservable
        .Throttle(throttle ?? TimeSpan.FromMilliseconds(200))
        .Select(text => text?.Trim().ToLowerInvariant() ?? string.Empty)
        .Select(term => new Func<T, bool>(item =>
            string.IsNullOrEmpty(term) || item.MatchesSearch(term)));

    var subscription = sourceCollection.ToObservableChangeSet()
        .Filter(filterObservable)
        .ObserveOn(RxApp.MainThreadScheduler)
        .Bind(out filteredCollection)
        .Subscribe();

    return subscription;
  }
}
