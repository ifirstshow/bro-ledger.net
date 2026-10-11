using BroLedger.Core.Models;

namespace BroLedger.Core.Services;

// Transcribed method-by-method from Java ui/OverviewCalc.java.
//
// Pure, side-effect-free helpers for the overview and card UI.
// All methods are static and directly unit-testable.
//
// Visibility mapping: Java package-private → internal (tests use InternalsVisibleTo).
// Renames forced by C# (method name may not equal a nested type name in the same class):
// levelBudget → GetLevelBudget, budgetState → GetBudgetState, targetState → GetTargetState.
public static class OverviewCalc
{
    // ---- budget model ------------------------------------------------------

    /// <summary>Snapshot of a brother's increase budget at a given point in time.</summary>
    internal sealed record LevelBudget(bool Post11, int Cap, int TotalBudget, int Used, int Free);

    /// <summary>
    /// Derives the level-increase budget for a brother.
    /// </summary>
    /// <param name="levelTotal"><c>Brother.LevelTotal</c></param>
    /// <param name="remainingLevels">pre-lv11 remaining level-ups (from <see cref="ExpectedStatsCalculator.RemainingLevels(int,int)"/>)</param>
    /// <param name="preIncreases"><c>BrotherAnnotation.StatIncreases</c>; may be null</param>
    /// <param name="post11Increases"><c>BrotherAnnotation.Post11Increases</c>; may be null</param>
    internal static LevelBudget GetLevelBudget(int levelTotal, int remainingLevels,
                                               int[]? preIncreases, int[]? post11Increases)
    {
        var post11 = levelTotal > 11;
        var cap = post11 ? Math.Max(0, levelTotal - 11) : remainingLevels;
        var totalBudget = 3 * cap;
        var used = SumOrZero(post11 ? post11Increases : preIncreases);
        return new LevelBudget(post11, cap, totalBudget, used, totalBudget - used);
    }

    /// <summary>Null-safe sum of an int array; returns 0 when arr is null.</summary>
    internal static int SumOrZero(int[]? arr) => arr?.Sum() ?? 0;

    /// <summary>
    /// True when every entry fits within <paramref name="cap"/>. A stat can reach the sum-matching
    /// total (e.g. via a temporarily higher cap while a Gifted perk was planned) while a
    /// single stat still exceeds the current cap — that stale state must not be treated
    /// as a valid, fully-allocated budget.
    /// </summary>
    internal static bool AllWithinCap(int[]? arr, int cap)
    {
        if (arr == null) return true;
        foreach (var v in arr) if (v > cap) return false;
        return true;
    }

    // ---- gifted perk bonus --------------------------------------------------

    private const string GiftedHexId = "9899E380";

    /// <summary>True when the brother already owns the Gifted perk.</summary>
    internal static bool HasGiftedPerk(Brother b) =>
        b.PerkIds.Any(id => GiftedHexId.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when Gifted is PLANNED in the perk plan and not yet owned.</summary>
    internal static bool IsGiftedPending(Brother? b, IReadOnlyDictionary<string, string>? planStatus)
    {
        if (b == null || HasGiftedPerk(b)) return false;
        if (planStatus == null) return false;
        return planStatus.TryGetValue(GiftedHexId, out var status) && status == "PLANNED";
    }

    /// <summary>
    /// Remaining pre-lv11 level-ups, plus one extra when a not-yet-owned Gifted perk
    /// is planned (Gifted grants a bonus level-up).
    /// </summary>
    internal static int EffectiveRemainingLevels(Brother? b, int remainingLevels,
                                                 IReadOnlyDictionary<string, string>? planStatus) =>
        remainingLevels + (IsGiftedPending(b, planStatus) ? 1 : 0);

    // ---- budget state & formatting ----------------------------------------

    internal enum BudgetState { Under, Exact, Over }

    /// <summary>Classifies a used/total pair as over-budget, exact, or under-budget.</summary>
    internal static BudgetState GetBudgetState(int used, int total)
    {
        if (used > total) return BudgetState.Over;
        if (used < total) return BudgetState.Under;
        return BudgetState.Exact;
    }

    /// <summary>
    /// Formats the "used/total" budget label text.
    /// Post-11 uses a "-X/-Y" prefix; pre-11 uses "X/Y".
    /// </summary>
    internal static string FormatBudgetLabel(bool post11, int used, int total) =>
        post11 ? $"-{used}/-{total}" : $"{used}/{total}";

    // ---- target / priority -------------------------------------------------

    internal enum TargetState { Met, Reachable, Unmet }

    /// <summary>Classifies a stat's distance from its target given current and projected-potential values.</summary>
    internal static TargetState GetTargetState(int current, int potential, int target)
    {
        if (current >= target) return TargetState.Met;
        if (potential >= target) return TargetState.Reachable;
        return TargetState.Unmet;
    }

    /// <summary>
    /// Safe accessor for a priority/targetStats array element; returns
    /// <paramref name="fallback"/> when the array is null or the row is out of bounds.
    /// </summary>
    internal static int PriorityAt(int[]? priority, int row, int fallback)
    {
        if (priority == null || row < 0 || row >= priority.Length) return fallback;
        return priority[row];
    }

    // ---- increase editor ---------------------------------------------------

    /// <summary>
    /// Returns true when the given delta (±1) can be applied given the current count,
    /// per-stat cap, and remaining free budget.
    /// </summary>
    internal static bool CanApplyIncrease(int delta, int current, int cap, int freeBudget)
    {
        if (delta > 0) return current < cap && freeBudget > 0;
        if (delta < 0) return current > 0;
        return false;
    }

    // ---- expected value display --------------------------------------------

    /// <summary>
    /// Formats an <see cref="ExpectedStatsCalculator.Expected"/> result as a display string.
    /// Returns "—" only when there are remaining levels but none allocated to this stat.
    /// </summary>
    internal static string ExpectedDisplay(ExpectedStatsCalculator.Expected exp)
    {
        if (exp.RemainingLevels > 0 && exp.Count == 0) return "—";
        return exp.FinalExpected.ToString();
    }

    // ---- perk sorting ------------------------------------------------------

    /// <summary>
    /// Builds a comparer over perk hex-IDs for the given sort mode.
    /// </summary>
    /// <param name="mode">the sort mode to apply</param>
    /// <param name="tierOf">maps a perk hex-ID to its tier (use <see cref="TierOrMax"/> for the default)</param>
    /// <param name="countOf">maps a perk hex-ID to its commonality count (higher = more common)</param>
    /// <param name="nameOf">maps a perk hex-ID to its display name</param>
    internal static IComparer<string> PerkComparator(PerkSortMode mode,
                                                     Func<string, int> tierOf,
                                                     Func<string, long> countOf,
                                                     Func<string, string> nameOf)
    {
        var byTier = Comparer<string>.Create((a, b) => tierOf(a).CompareTo(tierOf(b)));
        // Java: comparingLong(id -> -countOf(id)) — higher commonality sorts first.
        var byCommon = Comparer<string>.Create((a, b) => (-countOf(a)).CompareTo(-countOf(b)));
        var byName = Comparer<string>.Create((a, b) =>
            string.Compare(nameOf(a), nameOf(b), StringComparison.OrdinalIgnoreCase));
        return mode switch
        {
            PerkSortMode.Tier => Chain(byTier, byName),
            PerkSortMode.Commonality => Chain(byCommon, byName),
            PerkSortMode.TierThenCommon => Chain(Chain(byTier, byCommon), byName),
            _ => Comparer<string>.Default,
        };
    }

    private static IComparer<string> Chain(IComparer<string> primary, IComparer<string> secondary) =>
        Comparer<string>.Create((x, y) =>
        {
            var c = primary.Compare(x, y);
            return c != 0 ? c : secondary.Compare(x, y);
        });

    /// <summary>Returns <paramref name="tier"/> when non-null, otherwise <see cref="int.MaxValue"/> (untiered-last).</summary>
    public static int TierOrMax(int? tier) => tier ?? int.MaxValue;

    /// <summary>Formats a perk name with its tier prefix ("[T1] name") when a tier is available.</summary>
    public static string DecoratePerkName(int? tier, string name) =>
        tier != null ? $"[T{tier}] {name}" : name;

    /// <summary>Combat weapon order for tier-4 "&lt;Weapon&gt; Mastery" perks, matching the in-game perk tree row.</summary>
    private static readonly string[] MasteryWeaponOrder =
    [
        "Mace", "Flail", "Hammer", "Axe", "Cleaver", "Sword",
        "Dagger", "Polearm", "Spear", "Crossbow", "Bow", "Throwing"
    ];

    /// <summary>
    /// Rank of a tier-4 weapon Mastery perk within <see cref="MasteryWeaponOrder"/>, matched by
    /// the "&lt;Weapon&gt; Mastery" name convention (case-insensitive). Returns <see cref="int.MaxValue"/>
    /// for names that don't match a known mastery, so they sort after recognized ones.
    /// </summary>
    public static int MasteryOrderRank(string? name)
    {
        if (name == null) return int.MaxValue;
        for (var i = 0; i < MasteryWeaponOrder.Length; i++)
        {
            if (name.Equals(MasteryWeaponOrder[i] + " Mastery", StringComparison.OrdinalIgnoreCase)) return i;
        }
        return int.MaxValue;
    }

    // ---- sort / classification helpers ------------------------------------

    /// <summary>
    /// Sort key for grouping brothers by role line.
    /// Returns 0 for frontline (or no-role) brothers, 1 for non-frontline.
    /// </summary>
    internal static int FrontlineKey(Role? role) => role != null && !role.Frontline ? 1 : 0;

    /// <summary>
    /// Determines the CSS tier style class for a weapon slot dot.
    /// Returns null when no tier class applies.
    /// </summary>
    internal static string? TierStyleClass(string? itemType, int? tier)
    {
        if (itemType == "namedWeapon") return "tier-named";
        if (tier is >= 1 and <= 3) return "tier-" + tier;
        return null;
    }

    /// <summary>
    /// Gathers up to 4 pouch slots from a brother's equipped slots and extra pouches,
    /// padding with empty slots when needed.
    /// </summary>
    internal static List<InventorySlot> GatherPouches(InventorySlot[]? equippedSlots,
                                                      List<InventorySlot>? extraPouches)
    {
        var list = new List<InventorySlot>();
        if (equippedSlots != null && equippedSlots.Length > 6) list.Add(equippedSlots[6]);
        if (extraPouches != null) list.AddRange(extraPouches);
        while (list.Count < 4) list.Add(InventorySlot.Empty(6));
        return list.GetRange(0, 4);
    }

    // ---- level-up modal ----------------------------------------------------

    /// <summary>Derives the status text for a level-up event card.</summary>
    internal static string LevelUpStatusText(bool post11, bool adjusted)
    {
        if (post11) return adjusted ? "Post-lv11 increases recorded ✓" : "Post-lv11 level-up detected";
        return adjusted ? "Planned increases adjusted ✓" : "No planned increases to adjust";
    }

    /// <summary>
    /// True when a level-up's plan under-covered it: the total planned increases consumed
    /// across all stats is below the expected 3 per assigned level. Returns false when no
    /// plan was consumed (empty/null map) — nothing to flag.
    /// </summary>
    internal static bool IsUnderConsumed(IReadOnlyDictionary<Stat, int>? consumedIncreases, int levelsAssigned)
    {
        if (consumedIncreases == null || consumedIncreases.Count == 0) return false;
        var total = 0;
        foreach (var v in consumedIncreases.Values) total += v;
        return total < 3 * levelsAssigned;
    }
}

// Transcribed from Java ui/PerkSortMode.java (same package as OverviewCalc; consumed by PerkComparator).
// Declaration order preserved. The label field and next() are UI-only (combobox cycling) — deferred to plan④.
internal enum PerkSortMode
{
    Off,
    Commonality,
    Tier,
    TierThenCommon,
}
