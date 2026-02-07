using Mutagen.Bethesda.Plugins;

namespace Boutique.ViewModels;

/// <summary>
///   Interface for selectable record view models that can be used in filter criteria.
///   Provides common properties for selection state, form key, and search matching.
/// </summary>
public interface ISelectableRecordViewModel
{
  FormKey FormKey { get; }
  string EditorID { get; }
  string DisplayName { get; }
  string ModDisplayName { get; }
  string FormKeyString { get; }
  bool IsSelected { get; set; }
  bool IsExcluded { get; set; }
  bool MatchesSearch(string searchTerm);
}
