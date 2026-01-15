using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

public record OutfitCreationRequest(
    string Name,
    string EditorId,
    IReadOnlyList<IArmorGetter> Pieces,
    FormKey? ExistingFormKey = null,
    bool IsOverride = false);
