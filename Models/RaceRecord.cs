using Mutagen.Bethesda.Plugins;

namespace Boutique.Models;

public sealed record RaceRecord(
    FormKey FormKey,
    string? EditorID,
    string? Name,
    ModKey ModKey)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : EditorID ?? "(No EditorID)";
    public string FormKeyString => FormKey.ToString();
    public string ModDisplayName => ModKey.FileName;
}

