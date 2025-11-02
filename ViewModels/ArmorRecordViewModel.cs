using System.Linq;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;

namespace RequiemGlamPatcher.ViewModels;

public class ArmorRecordViewModel : ReactiveObject
{
    private readonly IArmorGetter _armor;
    private readonly ILinkCache? _linkCache;
    private readonly string _searchCache;
    private bool _isSlotCompatible = true;
    private readonly string _formIdDisplay;
    private readonly uint _formIdSortable;
    private bool _isMapped;

    public IArmorGetter Armor => _armor;

    public string EditorID => _armor.EditorID ?? "(No EditorID)";
    public string Name => _armor.Name?.String ?? "(Unnamed)";
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : EditorID;
    public float ArmorRating => _armor.ArmorRating;
    public float Weight => _armor.Weight;
    public uint Value => _armor.Value;
    public BipedObjectFlag SlotMask => _armor.BodyTemplate?.FirstPersonFlags ?? 0;
    public string SlotSummary => SlotMask == 0 ? "Unassigned" : SlotMask.ToString();
    public string ModDisplayName => _armor.FormKey.ModKey.FileName;
    public string FormIdDisplay => _formIdDisplay;
    public uint FormIdSortable => _formIdSortable;

    public string Keywords
    {
        get
        {
            if (_armor.Keywords == null || !_armor.Keywords.Any())
                return "(No Keywords)";

            if (_linkCache == null)
                return $"({_armor.Keywords.Count} keywords)";

            var keywordNames = _armor.Keywords
                .Select(k =>
                {
                    if (_linkCache.TryResolve<IKeywordGetter>(k.FormKey, out var keyword))
                        return keyword.EditorID ?? "Unknown";
                    return "Unresolved";
                })
                .Take(5); // Limit display to first 5

            var result = string.Join(", ", keywordNames);
            if (_armor.Keywords.Count > 5)
                result += $", ... (+{_armor.Keywords.Count - 5} more)";

            return result;
        }
    }

    public bool HasEnchantment => _armor.ObjectEffect.FormKey != FormKey.Null;

    public string EnchantmentInfo
    {
        get
        {
            if (!HasEnchantment)
                return "None";

            if (_linkCache != null && _linkCache.TryResolve<IObjectEffectGetter>(_armor.ObjectEffect.FormKey, out var enchantment))
            {
                return enchantment.Name?.String ?? enchantment.EditorID ?? "Unknown Enchantment";
            }

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
            {
                this.RaisePropertyChanged(nameof(SlotCompatibilityPriority));
            }
        }
    }

    public string FormKeyString => _armor.FormKey.ToString();
    public string SummaryLine => $"{DisplayName} ({SlotSummary}) ({FormIdDisplay}) ({ModDisplayName})";
    public bool IsMapped
    {
        get => _isMapped;
        set => this.RaiseAndSetIfChanged(ref _isMapped, value);
    }

    public ArmorRecordViewModel(IArmorGetter armor, ILinkCache? linkCache = null)
    {
        _armor = armor;
        _linkCache = linkCache;
        _formIdSortable = armor.FormKey.ID;
        _formIdDisplay = $"0x{_formIdSortable:X8}";

        _searchCache = $"{DisplayName} {EditorID} {ModDisplayName} {_formIdDisplay} {SlotSummary}".ToLowerInvariant();
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
}
