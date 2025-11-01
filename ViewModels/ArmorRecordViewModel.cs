using System.Windows.Input;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using ReactiveUI;

namespace RequiemGlamPatcher.ViewModels;

public class ArmorRecordViewModel : ReactiveObject
{
    private readonly IArmorGetter _armor;
    private readonly ILinkCache? _linkCache;
    private bool _isSelected;

    public IArmorGetter Armor => _armor;

    public string EditorID => _armor.EditorID ?? "(No EditorID)";
    public string Name => _armor.Name?.String ?? "(Unnamed)";
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : EditorID;
    public float ArmorRating => _armor.ArmorRating;
    public float Weight => _armor.Weight;
    public uint Value => _armor.Value;

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

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public string FormKeyString => _armor.FormKey.ToString();

    public ArmorRecordViewModel(IArmorGetter armor, ILinkCache? linkCache = null)
    {
        _armor = armor;
        _linkCache = linkCache;
    }
}
