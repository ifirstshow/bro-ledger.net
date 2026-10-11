using BroLedger.Core.Models;
using BroLedger.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BroLedger.App.ViewModels;

/// <summary>
/// State + pure decisions behind the overview pane: the four toolbar toggles (Java
/// BrotherOverviewPane static properties + MainController.buildToolbar LedDef tables),
/// the overview column order (AppConfig.OverviewColumnOrder), the roster sort comparator
/// (BrotherOverviewPane.sortComparator) and the events the pane raises into the shell.
/// Visual construction lives in <c>BrotherOverviewPane</c>.
/// </summary>
internal partial class OverviewViewModel : ObservableObject
{
    // Java BrotherOverviewPane's nested enums (declaration order preserved; member names
    // match the Java enum constants because AnnotationService.UiState persists name()).
    public enum PotentialMode { CURRENT, BOTH, POTENTIAL, CURRENT_EXPECTED, EXPECTED }
    public enum ArmorMode { OFF, DURABILITY, FATIGUE }
    public enum WeaponTierMode { OFF, ON }

    // ---- four toggles ------------------------------------------------------

    /// <summary>One LedDef row of Java fancyToggle: description text + led colour class + value.</summary>
    public sealed record ToggleOption(string Desc, string LedClass, object Value);

    public sealed record ToggleSpec(string Title, IReadOnlyList<ToggleOption> Options);

    // Java MainController.buildToolbar L139-161 — verbatim LedDef tables.
    public static readonly IReadOnlyList<ToggleSpec> Toggles =
    [
        new("Weapon Tier",
        [
            new ToggleOption("Off", "led-red", WeaponTierMode.OFF),
            new ToggleOption("On", "led-green", WeaponTierMode.ON),
        ]),
        new("Armor Stats",
        [
            new ToggleOption("Off", "led-red", ArmorMode.OFF),
            new ToggleOption("Durability", "led-yellow", ArmorMode.DURABILITY),
            new ToggleOption("Fatigue", "led-yellow", ArmorMode.FATIGUE),
        ]),
        new("Perk Sorting",
        [
            new ToggleOption("Off", "led-red", PerkSortMode.Off),
            new ToggleOption("Commonality", "led-yellow", PerkSortMode.Commonality),
            new ToggleOption("Tier", "led-yellow", PerkSortMode.Tier),
            new ToggleOption("Tier + Cmn", "led-yellow", PerkSortMode.TierThenCommon),
        ]),
        new("Potential",
        [
            new ToggleOption("Off", "led-red", PotentialMode.CURRENT),
            new ToggleOption("Current + Pot.", "led-yellow", PotentialMode.BOTH),
            new ToggleOption("Pot. Only", "led-yellow", PotentialMode.POTENTIAL),
            new ToggleOption("Current + Exp.", "led-green", PotentialMode.CURRENT_EXPECTED),
            new ToggleOption("Exp. Only", "led-green", PotentialMode.EXPECTED),
        ]),
    ];

    [ObservableProperty] private PotentialMode _potential = PotentialMode.CURRENT;
    [ObservableProperty] private ArmorMode _armor = ArmorMode.OFF;
    [ObservableProperty] private WeaponTierMode _weaponTier = WeaponTierMode.OFF;
    [ObservableProperty] private PerkSortMode _perkSort = PerkSortMode.Off;

    public OverviewViewModel()
    {
        // Java MainController: every mode change persists the UiState (saveUiState listeners).
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Potential) or nameof(Armor) or nameof(WeaponTier) or nameof(PerkSort))
                SaveUiState();
        };
    }

    /// <summary>Click on the toggle at <paramref name="toggleIndex"/> advances to the next LedDef (wrapping).</summary>
    public void CycleNext(int toggleIndex)
    {
        var options = Toggles[toggleIndex].Options;
        object current = toggleIndex switch
        {
            0 => WeaponTier,
            1 => Armor,
            2 => PerkSort,
            _ => Potential,
        };
        var i = 0;
        while (i < options.Count && !Equals(options[i].Value, current)) i++;
        var next = options[(i + 1) % options.Count].Value;
        switch (toggleIndex)
        {
            case 0: WeaponTier = (WeaponTierMode)next; break;
            case 1: Armor = (ArmorMode)next; break;
            case 2: PerkSort = (PerkSortMode)next; break;
            default: Potential = (PotentialMode)next; break;
        }
    }

    /// <summary>Java MainController.restoreUiState — apply the persisted toggle names; invalid ones ignored.</summary>
    public void RestoreUiState(AnnotationService annotation)
    {
        var s = annotation.GetUiState();
        if (ParseEnum<PotentialMode>(s.PotentialMode) is { } pot) Potential = pot;
        if (ParseEnum<ArmorMode>(s.ArmorMode) is { } arm) Armor = arm;
        if (ParseEnum<WeaponTierMode>(s.WeaponTierMode) is { } wt) WeaponTier = wt;
        if (ParsePerkSort(s.PerkSortMode) is { } ps) PerkSort = ps;
    }

    private void SaveUiState()
    {
        AnnotationService.Instance.SetUiState(new AnnotationService.UiState
        {
            PotentialMode = Potential.ToString(),
            ArmorMode = Armor.ToString(),
            WeaponTierMode = WeaponTier.ToString(),
            PerkSortMode = FormatPerkSort(PerkSort),
        });
    }

    // Core's PerkSortMode is CamelCase while the Java enum constants (the on-disk format)
    // are SCREAMING_SNAKE — mapped explicitly so .broledger.json files stay compatible.
    private static string FormatPerkSort(PerkSortMode mode) => mode switch
    {
        PerkSortMode.Off => "OFF",
        PerkSortMode.Commonality => "COMMONALITY",
        PerkSortMode.Tier => "TIER",
        PerkSortMode.TierThenCommon => "TIER_THEN_COMMON",
        _ => "OFF",
    };

    private static PerkSortMode? ParsePerkSort(string? name) => name?.ToUpperInvariant() switch
    {
        "OFF" => PerkSortMode.Off,
        "COMMONALITY" => PerkSortMode.Commonality,
        "TIER" => PerkSortMode.Tier,
        "TIER_THEN_COMMON" => PerkSortMode.TierThenCommon,
        _ => null,
    };

    private static T? ParseEnum<T>(string? name) where T : struct, Enum
    {
        if (name != null && Enum.TryParse<T>(name, ignoreCase: true, out var value)
            && Enum.IsDefined(value)) return value;
        return null;
    }

    // ---- column order ------------------------------------------------------

    /// <summary>Default overview column IDs — Java doBuild's column construction order.</summary>
    public static IReadOnlyList<string> DefaultColumnIds() =>
    [
        "portrait", "name", "role", "level",
        .. Stat.Values.Select(s => "stat-" + s.Abbrev),
        "equip-Arm", "equip-Hlm", "equip-Wpn", "equip-Shd", "equip-Trk", "equip-Quv",
        "equip-P1", "equip-P2", "equip-P3", "equip-P4",
        "traits", "perks",
    ];

    /// <summary>
    /// Java doBuild's saved-order restore: saved IDs keep their order, columns absent from
    /// the saved list are appended at the end in default order.
    /// </summary>
    public static IReadOnlyList<string> RestoreColumnOrder(IReadOnlyList<string>? saved,
                                                           IReadOnlyList<string> defaults)
    {
        if (saved is not { Count: > 0 }) return defaults;
        var remaining = new LinkedList<string>(defaults);
        var restored = new List<string>(defaults.Count);
        foreach (var id in saved)
        {
            var node = remaining.Find(id);
            if (node == null) continue;
            restored.Add(node.Value);
            remaining.Remove(node);
        }
        restored.AddRange(remaining);
        return restored;
    }

    /// <summary>
    /// Java doBuild's columns listener validation: the 8 stat columns must stay within
    /// indices 4-11 and the 10 equipment columns within 12-21 (count == range width, so
    /// "every member inside the range" implies the whole group occupies exactly that range).
    /// </summary>
    public static bool IsColumnOrderValid(IReadOnlyList<string> ids)
    {
        if (ids.Count != DefaultColumnIds().Count) return false;
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i].StartsWith("stat-") && (i < 4 || i > 11)) return false;
            if (ids[i].StartsWith("equip-") && (i < 12 || i > 21)) return false;
        }
        return true;
    }

    /// <summary>Raised after a valid column reorder — the brother card mirrors stat order (T4 consumption).</summary>
    public event Action? ColumnOrderChanged;

    /// <summary>Java columns listener: validate, persist to AppConfig, announce. Invalid → rejected.</summary>
    public bool TryCommitColumnOrder(IReadOnlyList<string> ids)
    {
        if (!IsColumnOrderValid(ids)) return false;
        AppConfig.Instance.OverviewColumnOrder = [.. ids];
        AppConfig.Instance.Save();
        ColumnOrderChanged?.Invoke();
        return true;
    }

    // ---- events into the shell ---------------------------------------------

    /// <summary>Java row click → MainController showCard.</summary>
    public event Action<Brother>? ShowCardRequested;

    /// <summary>Java level cell click → guidePopup (T5 mounts the popup content itself).</summary>
    public event Action<Brother>? LevelGuideRequested;

    public void RequestShowCard(Brother b) => ShowCardRequested?.Invoke(b);

    public void RequestLevelGuide(Brother b) => LevelGuideRequested?.Invoke(b);

    // ---- roster sort -------------------------------------------------------

    /// <summary>
    /// Java BrotherOverviewPane.sortComparator: frontline brothers first (no role / null
    /// fingerprint counts as frontline), then by annotation sortIndex, unsorted last
    /// (stable — equals Java Comparator semantics for the MAX_VALUE ties).
    /// </summary>
    /// <param name="roleOf">role assigned to a brother (may be null)</param>
    /// <param name="sortIndexOf">persisted sortIndex, or null when never drag-sorted</param>
    public static IComparer<Brother> SortComparator(Func<Brother, Role?> roleOf,
                                                    Func<Brother, int?> sortIndexOf)
    {
        return Comparer<Brother>.Create((a, b) =>
        {
            var front = FrontlineKeyOf(a, roleOf).CompareTo(FrontlineKeyOf(b, roleOf));
            if (front != 0) return front;
            var ia = sortIndexOf(a);
            var ib = sortIndexOf(b);
            return (ia ?? int.MaxValue).CompareTo(ib ?? int.MaxValue);
        });
    }

    private static int FrontlineKeyOf(Brother b, Func<Brother, Role?> roleOf)
    {
        if (b.Fingerprint == null) return 0;
        return OverviewCalc.FrontlineKey(roleOf(b));
    }

    /// <summary>Java sortComparator(UiContext) — comparator wired to annotation + role lookup.</summary>
    public IComparer<Brother> SortComparatorFor(UiContext ctx) =>
        SortComparator(
            b =>
            {
                if (b.Fingerprint == null) return null;
                var roleId = ctx.Annotation.Get(b.Fingerprint).RoleId;
                return roleId != null ? ctx.Roles.GetById(roleId) : null;
            },
            b => b.Fingerprint == null ? null : ctx.Annotation.Get(b.Fingerprint).SortIndex);
}
