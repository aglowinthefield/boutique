using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

public record OutfitCreationRequest(
    string Name,
    string EditorId,
    IReadOnlyList<IArmorGetter> Pieces);