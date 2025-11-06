using Mutagen.Bethesda.Skyrim;

namespace Boutique.Models;

/// <summary>
/// Represents a match between a source armor (from glam mod) and a target armor (from master ESP)
/// </summary>
public class ArmorMatch(
    IArmorGetter sourceArmor,
    IArmorGetter? targetArmor = null,
    double matchConfidence = 0.0,
    bool isManualMatch = false,
    bool isGlamOnly = false)
{
    public IArmorGetter SourceArmor { get; set; } = sourceArmor;
    public IArmorGetter? TargetArmor { get; set; } = targetArmor;
    public double MatchConfidence { get; set; } = matchConfidence;
    public bool IsManualMatch { get; set; } = isManualMatch;
    public bool IsGlamOnly { get; set; } = isGlamOnly;
}
