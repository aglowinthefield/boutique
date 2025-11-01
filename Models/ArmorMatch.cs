using Mutagen.Bethesda.Skyrim;

namespace RequiemGlamPatcher.Models;

/// <summary>
/// Represents a match between a source armor (from glam mod) and a target armor (from master ESP)
/// </summary>
public class ArmorMatch
{
    public IArmorGetter SourceArmor { get; set; }
    public IArmorGetter? TargetArmor { get; set; }
    public double MatchConfidence { get; set; }
    public bool IsManualMatch { get; set; }
    public bool IsSelected { get; set; }

    public ArmorMatch(IArmorGetter sourceArmor, IArmorGetter? targetArmor = null, double matchConfidence = 0.0, bool isManualMatch = false)
    {
        SourceArmor = sourceArmor;
        TargetArmor = targetArmor;
        MatchConfidence = matchConfidence;
        IsManualMatch = isManualMatch;
        IsSelected = false;
    }
}
