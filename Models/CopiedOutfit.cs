using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

/// <summary>
/// Represents a copied outfit from the Outfits tab that can be used to create a distribution entry.
/// </summary>
public class CopiedOutfit
{
    /// <summary>
    /// Gets the outfit FormKey.
    /// </summary>
    public FormKey OutfitFormKey { get; init; }

    /// <summary>
    /// Gets the outfit EditorID for display purposes.
    /// </summary>
    public string? OutfitEditorId { get; init; }

    /// <summary>
    /// Gets a human-readable description for display purposes.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether this should be created as an override (keeping the original FormKey) rather than a new record.
    /// </summary>
    public bool IsOverride { get; init; }
}
