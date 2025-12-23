using Mutagen.Bethesda.Plugins;

namespace Boutique.ViewModels;

/// <summary>
/// Interface for selectable record view models that can be used in filter criteria.
/// Provides common properties for selection state, form key, and search matching.
/// </summary>
public interface ISelectableRecordViewModel
{
    /// <summary>
    /// The unique form key identifying this record.
    /// </summary>
    FormKey FormKey { get; }

    /// <summary>
    /// Whether this record is currently selected in the UI.
    /// </summary>
    bool IsSelected { get; set; }
}
