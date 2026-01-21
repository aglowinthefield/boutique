using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

/// <summary>
///     Represents a match between a source armor and a target armor.
/// </summary>
public class ArmorMatch(
    IArmorGetter sourceArmor,
    IArmorGetter? targetArmor = null,
    bool isGlamOnly = false)
{
    public IArmorGetter SourceArmor { get; set; } = sourceArmor;
    public IArmorGetter? TargetArmor { get; set; } = targetArmor;
    public bool IsGlamOnly { get; set; } = isGlamOnly;
}
