using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;

namespace Boutique.ViewModels;

public class ArmorRecordViewModel : ReactiveObject
{
    private readonly ILinkCache? _linkCache;
    private readonly string _searchCache;
    private bool _isMapped;
    private bool _isSlotCompatible = true;

    public ArmorRecordViewModel(IArmorGetter armor, ILinkCache? linkCache = null)
    {
        Armor = armor;
        _linkCache = linkCache;
        FormIdSortable = armor.FormKey.ID;
        FormIdDisplay = $"0x{FormIdSortable:X8}";

        _searchCache = $"{DisplayName} {EditorID} {ModDisplayName} {FormIdDisplay} {SlotSummary}".ToLowerInvariant();
    }

    public IArmorGetter Armor { get; }

    public string EditorID => Armor.EditorID ?? "(No EditorID)";
    public string Name => Armor.Name?.String ?? "(Unnamed)";
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : EditorID;
    public float ArmorRating => Armor.ArmorRating;
    public float Weight => Armor.Weight;
    public uint Value => Armor.Value;
    public BipedObjectFlag SlotMask => Armor.BodyTemplate?.FirstPersonFlags ?? 0;
    public string SlotSummary => SlotMask == 0 ? "Unassigned" : SlotMask.ToString();
    public string ModDisplayName => Armor.FormKey.ModKey.FileName;
    public string FormIdDisplay { get; }

    public uint FormIdSortable { get; }

    public string Keywords
    {
        get
        {
            if (Armor.Keywords == null || !Armor.Keywords.Any())
                return "(No Keywords)";

            if (_linkCache == null)
                return $"({Armor.Keywords.Count} keywords)";

            var keywordNames = Armor.Keywords
                .Select(k =>
                {
                    if (_linkCache.TryResolve<IKeywordGetter>(k.FormKey, out var keyword))
                        return keyword.EditorID ?? "Unknown";
                    return "Unresolved";
                })
                .Take(5); // Limit display to first 5

            var result = string.Join(", ", keywordNames);
            if (Armor.Keywords.Count > 5)
                result += $", ... (+{Armor.Keywords.Count - 5} more)";

            return result;
        }
    }

    public bool HasEnchantment => Armor.ObjectEffect.FormKey != FormKey.Null;

    public string EnchantmentInfo
    {
        get
        {
            if (!HasEnchantment)
                return "None";

            if (_linkCache != null &&
                _linkCache.TryResolve<IObjectEffectGetter>(Armor.ObjectEffect.FormKey, out var enchantment))
                return enchantment.Name?.String ?? enchantment.EditorID ?? "Unknown Enchantment";

            return "Enchanted";
        }
    }

    public int SlotCompatibilityPriority => _isSlotCompatible ? 0 : 1;

    public bool IsSlotCompatible
    {
        get => _isSlotCompatible;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _isSlotCompatible, value))
                this.RaisePropertyChanged(nameof(SlotCompatibilityPriority));
        }
    }

    public string FormKeyString => Armor.FormKey.ToString();
    public string SummaryLine => $"{DisplayName} ({SlotSummary}) ({FormIdDisplay}) ({ModDisplayName})";

    public bool IsMapped
    {
        get => _isMapped;
        set => this.RaiseAndSetIfChanged(ref _isMapped, value);
    }

    public bool MatchesSearch(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return true;

        return _searchCache.Contains(searchTerm.Trim().ToLowerInvariant());
    }

    public bool SharesSlotWith(ArmorRecordViewModel other)
    {
        if (SlotMask == 0 || other.SlotMask == 0)
            return true;

        return (SlotMask & other.SlotMask) != 0;
    }

    public bool ConflictsWithSlot(ArmorRecordViewModel other)
    {
        if (SlotMask == 0 || other.SlotMask == 0)
            return false;

        return (SlotMask & other.SlotMask) != 0;
    }
}