using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.Core.Tests.Services;

// Transcribed assertion-by-assertion from JAVATEST/ui/OverviewCalcTest.java (458 lines).
// Seam: Java Comparator<String> → IComparer<string> (asserted via Enumerable.OrderBy);
// record accessors / enum ordinals → positional record properties / enum members.
public class OverviewCalcTests
{
    // ---- SumOrZero ---------------------------------------------------------

    [Fact]
    public void SumOrZero_NullArray()
    {
        Assert.Equal(0, OverviewCalc.SumOrZero(null));
    }

    [Fact]
    public void SumOrZero_EmptyArray()
    {
        Assert.Equal(0, OverviewCalc.SumOrZero([]));
    }

    [Fact]
    public void SumOrZero_Summed()
    {
        Assert.Equal(6, OverviewCalc.SumOrZero([1, 2, 3]));
    }

    // ---- AllWithinCap --------------------------------------------------------

    [Fact]
    public void AllWithinCap_NullArray()
    {
        Assert.True(OverviewCalc.AllWithinCap(null, 5));
    }

    [Fact]
    public void AllWithinCap_TrueWhenAllFit()
    {
        Assert.True(OverviewCalc.AllWithinCap([1, 5, 0, 5], 5));
    }

    [Fact]
    public void AllWithinCap_FalseWhenOneExceeds()
    {
        // Sum can match the total budget while one entry alone exceeds the per-stat cap —
        // e.g. a stale allocation left over from a temporarily higher budget.
        Assert.False(OverviewCalc.AllWithinCap([10, 5, 0, 0], 5));
    }

    // ---- LevelBudget -------------------------------------------------------

    [Fact]
    public void LevelBudget_Pre11()
    {
        var lb = OverviewCalc.GetLevelBudget(5, 6, [3, 3, 0, 0, 0, 0, 0, 0], null);
        Assert.False(lb.Post11);
        Assert.Equal(6, lb.Cap);
        Assert.Equal(18, lb.TotalBudget);
        Assert.Equal(6, lb.Used);
        Assert.Equal(12, lb.Free);
    }

    [Fact]
    public void LevelBudget_Post11()
    {
        var post11 = new int[8];
        post11[0] = 2;
        var lb = OverviewCalc.GetLevelBudget(14, 0, null, post11);
        Assert.True(lb.Post11);
        Assert.Equal(3, lb.Cap);        // 14-11=3
        Assert.Equal(9, lb.TotalBudget);
        Assert.Equal(2, lb.Used);
        Assert.Equal(7, lb.Free);
    }

    [Fact]
    public void LevelBudget_NullArrays_UsedZero()
    {
        var lb = OverviewCalc.GetLevelBudget(5, 6, null, null);
        Assert.Equal(0, lb.Used);
        Assert.Equal(18, lb.Free);
    }

    // ---- BudgetState -------------------------------------------------------

    [Fact]
    public void BudgetState_Under()
    {
        Assert.Equal(OverviewCalc.BudgetState.Under, OverviewCalc.GetBudgetState(5, 10));
    }

    [Fact]
    public void BudgetState_Exact()
    {
        Assert.Equal(OverviewCalc.BudgetState.Exact, OverviewCalc.GetBudgetState(10, 10));
    }

    [Fact]
    public void BudgetState_Over()
    {
        Assert.Equal(OverviewCalc.BudgetState.Over, OverviewCalc.GetBudgetState(11, 10));
    }

    // ---- FormatBudgetLabel -------------------------------------------------

    [Fact]
    public void FormatBudgetLabel_Pre11()
    {
        Assert.Equal("5/10", OverviewCalc.FormatBudgetLabel(false, 5, 10));
    }

    [Fact]
    public void FormatBudgetLabel_Post11()
    {
        Assert.Equal("-5/-10", OverviewCalc.FormatBudgetLabel(true, 5, 10));
    }

    // ---- TargetState -------------------------------------------------------

    [Fact]
    public void TargetState_Met()
    {
        Assert.Equal(OverviewCalc.TargetState.Met, OverviewCalc.GetTargetState(60, 65, 55));
    }

    [Fact]
    public void TargetState_Reachable()
    {
        Assert.Equal(OverviewCalc.TargetState.Reachable, OverviewCalc.GetTargetState(50, 65, 60));
    }

    [Fact]
    public void TargetState_Unmet()
    {
        Assert.Equal(OverviewCalc.TargetState.Unmet, OverviewCalc.GetTargetState(50, 58, 60));
    }

    // ---- PriorityAt --------------------------------------------------------

    [Fact]
    public void PriorityAt_NormalAccess()
    {
        Assert.Equal(2, OverviewCalc.PriorityAt([1, 2, 3], 1, 0));
    }

    [Fact]
    public void PriorityAt_NullArray_Fallback()
    {
        Assert.Equal(3, OverviewCalc.PriorityAt(null, 0, 3));
    }

    [Fact]
    public void PriorityAt_OutOfBounds_Fallback()
    {
        Assert.Equal(3, OverviewCalc.PriorityAt([1, 2], 5, 3));
    }

    [Fact]
    public void PriorityAt_NegativeIndex_Fallback()
    {
        Assert.Equal(3, OverviewCalc.PriorityAt([1, 2], -1, 3));
    }

    // ---- CanApplyIncrease --------------------------------------------------

    [Fact]
    public void CanApplyIncrease_PlusAllowed()
    {
        Assert.True(OverviewCalc.CanApplyIncrease(+1, 2, 5, 3));
    }

    [Fact]
    public void CanApplyIncrease_PlusBlockedAtCap()
    {
        Assert.False(OverviewCalc.CanApplyIncrease(+1, 5, 5, 3));
    }

    [Fact]
    public void CanApplyIncrease_PlusBlockedByZeroBudget()
    {
        Assert.False(OverviewCalc.CanApplyIncrease(+1, 2, 5, 0));
    }

    [Fact]
    public void CanApplyIncrease_PlusBlockedByZeroCap()
    {
        Assert.False(OverviewCalc.CanApplyIncrease(+1, 0, 0, 5));
    }

    [Fact]
    public void CanApplyIncrease_MinusAllowed()
    {
        Assert.True(OverviewCalc.CanApplyIncrease(-1, 1, 5, 0));
    }

    [Fact]
    public void CanApplyIncrease_MinusBlockedAtZero()
    {
        Assert.False(OverviewCalc.CanApplyIncrease(-1, 0, 5, 3));
    }

    [Fact]
    public void CanApplyIncrease_ZeroDeltaAlwaysFalse()
    {
        Assert.False(OverviewCalc.CanApplyIncrease(0, 2, 5, 3));
    }

    // ---- ExpectedDisplay ---------------------------------------------------

    [Fact]
    public void ExpectedDisplay_DashWhenRemainingAndCountZero()
    {
        // remaining > 0, count == 0 → "—"
        var exp = new ExpectedStatsCalculator.Expected(50, 55, 0, 5);
        Assert.Equal("—", OverviewCalc.ExpectedDisplay(exp));
    }

    [Fact]
    public void ExpectedDisplay_ValueWhenCountNonZero()
    {
        var exp = new ExpectedStatsCalculator.Expected(50, 55, 2, 5);
        Assert.Equal("55", OverviewCalc.ExpectedDisplay(exp));
    }

    [Fact]
    public void ExpectedDisplay_ValueWhenRemainingZero()
    {
        var exp = new ExpectedStatsCalculator.Expected(50, 55, 0, 0);
        Assert.Equal("55", OverviewCalc.ExpectedDisplay(exp));
    }

    // ---- TierOrMax / DecoratePerkName --------------------------------------

    [Fact]
    public void TierOrMax_NonNull()
    {
        Assert.Equal(2, OverviewCalc.TierOrMax(2));
    }

    [Fact]
    public void TierOrMax_Null()
    {
        Assert.Equal(int.MaxValue, OverviewCalc.TierOrMax(null));
    }

    [Fact]
    public void DecoratePerkName_WithTier()
    {
        Assert.Equal("[T2] Iron Lungs", OverviewCalc.DecoratePerkName(2, "Iron Lungs"));
    }

    [Fact]
    public void DecoratePerkName_NoTier()
    {
        Assert.Equal("Iron Lungs", OverviewCalc.DecoratePerkName(null, "Iron Lungs"));
    }

    // ---- FrontlineKey ------------------------------------------------------

    [Fact]
    public void FrontlineKey_Null_IsFrontline()
    {
        Assert.Equal(0, OverviewCalc.FrontlineKey(null));
    }

    [Fact]
    public void FrontlineKey_FrontlineRole()
    {
        var r = new Role { Frontline = true };
        Assert.Equal(0, OverviewCalc.FrontlineKey(r));
    }

    [Fact]
    public void FrontlineKey_NonFrontlineRole()
    {
        var r = new Role { Frontline = false };
        Assert.Equal(1, OverviewCalc.FrontlineKey(r));
    }

    // ---- TierStyleClass ----------------------------------------------------

    [Fact]
    public void TierStyleClass_NamedWeapon()
    {
        Assert.Equal("tier-named", OverviewCalc.TierStyleClass("namedWeapon", null));
        Assert.Equal("tier-named", OverviewCalc.TierStyleClass("namedWeapon", 2));
    }

    [Fact]
    public void TierStyleClass_Tier1to3()
    {
        Assert.Equal("tier-1", OverviewCalc.TierStyleClass("weapon", 1));
        Assert.Equal("tier-2", OverviewCalc.TierStyleClass("weapon", 2));
        Assert.Equal("tier-3", OverviewCalc.TierStyleClass("weapon", 3));
    }

    [Fact]
    public void TierStyleClass_NullTier()
    {
        Assert.Null(OverviewCalc.TierStyleClass("weapon", null));
    }

    [Fact]
    public void TierStyleClass_OutOfRangeTier()
    {
        Assert.Null(OverviewCalc.TierStyleClass("weapon", 0));
        Assert.Null(OverviewCalc.TierStyleClass("weapon", 4));
    }

    // ---- GatherPouches -----------------------------------------------------

    [Fact]
    public void GatherPouches_AlwaysReturns4()
    {
        var pouches = OverviewCalc.GatherPouches(null, null);
        Assert.Equal(4, pouches.Count);
    }

    [Fact]
    public void GatherPouches_IncludesSlot6FromEquipped()
    {
        var equipped = new InventorySlot[7];
        for (var i = 0; i < 7; i++) equipped[i] = InventorySlot.Empty(i);
        equipped[6] = InventorySlot.Empty(6);
        equipped[6].ItemId = "AAAA0001"; // distinguish from empty
        var pouches = OverviewCalc.GatherPouches(equipped, null);
        Assert.Equal(4, pouches.Count);
        Assert.Equal("AAAA0001", pouches[0].ItemId);
    }

    [Fact]
    public void GatherPouches_IncludesExtraPouches()
    {
        var extra = InventorySlot.Empty(6);
        extra.ItemId = "EXTRA001";
        var pouches = OverviewCalc.GatherPouches(null, [extra]);
        Assert.Equal(4, pouches.Count);
        Assert.Equal("EXTRA001", pouches[0].ItemId);
    }

    // ---- LevelUpStatusText / IsUnderConsumed --------------------------------

    [Fact]
    public void LevelUpStatusText_Post11Adjusted()
    {
        Assert.Equal("Post-lv11 increases recorded ✓", OverviewCalc.LevelUpStatusText(true, true));
    }

    [Fact]
    public void LevelUpStatusText_Post11NotAdjusted()
    {
        Assert.Equal("Post-lv11 level-up detected", OverviewCalc.LevelUpStatusText(true, false));
    }

    [Fact]
    public void LevelUpStatusText_Pre11Adjusted()
    {
        Assert.Equal("Planned increases adjusted ✓", OverviewCalc.LevelUpStatusText(false, true));
    }

    [Fact]
    public void LevelUpStatusText_Pre11NotAdjusted()
    {
        Assert.Equal("No planned increases to adjust", OverviewCalc.LevelUpStatusText(false, false));
    }

    [Fact]
    public void IsUnderConsumed_TrueWhenTotalBelowExpected()
    {
        Dictionary<Stat, int> consumed = new() { [Stat.MeleeSkill] = 2 };
        Assert.True(OverviewCalc.IsUnderConsumed(consumed, 1)); // 2 < 3*1
    }

    [Fact]
    public void IsUnderConsumed_FalseWhenTotalMeetsExpected()
    {
        Dictionary<Stat, int> consumed = new() { [Stat.MeleeSkill] = 4, [Stat.Resolve] = 2 };
        Assert.False(OverviewCalc.IsUnderConsumed(consumed, 2)); // 6 == 3*2
    }

    [Fact]
    public void IsUnderConsumed_TrueWhenMultiStatStillShort()
    {
        Dictionary<Stat, int> consumed = new() { [Stat.MeleeSkill] = 3, [Stat.Resolve] = 2 };
        Assert.True(OverviewCalc.IsUnderConsumed(consumed, 2)); // 5 < 3*2
    }

    [Fact]
    public void IsUnderConsumed_FalseWhenEmpty()
    {
        Assert.False(OverviewCalc.IsUnderConsumed(new Dictionary<Stat, int>(), 2));
    }

    [Fact]
    public void IsUnderConsumed_FalseWhenNull()
    {
        Assert.False(OverviewCalc.IsUnderConsumed(null, 2));
    }

    // ---- PerkComparator ----------------------------------------------------

    [Fact]
    public void PerkComparator_TIER_SortsByTier()
    {
        var ids = new List<string> { "C", "A", "B" };
        var comp = OverviewCalc.PerkComparator(
            PerkSortMode.Tier,
            id => id switch { "A" => 1, "B" => 2, _ => 3 },
            _ => 0L,
            id => id);
        var sorted = ids.OrderBy(id => id, comp).ToList();
        Assert.Equal("A", sorted[0]);
        Assert.Equal("B", sorted[1]);
        Assert.Equal("C", sorted[2]);
    }

    [Fact]
    public void PerkComparator_TIER_NameBreaksTie()
    {
        // both tier 1; should sort alphabetically by name
        var comp = OverviewCalc.PerkComparator(
            PerkSortMode.Tier,
            _ => 1,
            _ => 0L,
            id => id switch { "A" => "Zebra", _ => "Apple" });
        var sorted = new List<string> { "A", "B" }.OrderBy(id => id, comp).ToList();
        Assert.Equal("B", sorted[0]); // "Apple" < "Zebra"
    }

    [Fact]
    public void PerkComparator_COMMONALITY_SortsByCountDesc()
    {
        var comp = OverviewCalc.PerkComparator(
            PerkSortMode.Commonality,
            _ => 1,
            id => id switch { "A" => 10L, "B" => 5L, _ => 1L },
            id => id);
        var sorted = new List<string> { "C", "B", "A" }.OrderBy(id => id, comp).ToList();
        Assert.Equal("A", sorted[0]); // highest count first
    }

    [Fact]
    public void PerkComparator_OFF_NaturalOrder()
    {
        // OFF mode returns natural order → sorts alphabetically
        var ids = new List<string> { "C", "A", "B" };
        var comp = OverviewCalc.PerkComparator(PerkSortMode.Off, _ => 1, _ => 0L, id => id);
        var sorted = ids.OrderBy(id => id, comp).ToList();
        Assert.Equal(new List<string> { "A", "B", "C" }, sorted);
    }

    // ---- MasteryOrderRank ---------------------------------------------------

    [Fact]
    public void MasteryOrderRank_FirstAndLast()
    {
        Assert.Equal(0, OverviewCalc.MasteryOrderRank("Mace Mastery"));
        Assert.Equal(11, OverviewCalc.MasteryOrderRank("Throwing Mastery"));
    }

    [Fact]
    public void MasteryOrderRank_CrossbowBeforeBow()
    {
        // Guards against a naive prefix match confusing "Bow" and "Crossbow".
        var crossbow = OverviewCalc.MasteryOrderRank("Crossbow Mastery");
        var bow = OverviewCalc.MasteryOrderRank("Bow Mastery");
        Assert.True(crossbow < bow);
    }

    [Fact]
    public void MasteryOrderRank_CaseInsensitive()
    {
        Assert.Equal(0, OverviewCalc.MasteryOrderRank("mace mastery"));
    }

    [Fact]
    public void MasteryOrderRank_UnknownName_MaxValue()
    {
        Assert.Equal(int.MaxValue, OverviewCalc.MasteryOrderRank("Iron Lungs"));
    }

    [Fact]
    public void MasteryOrderRank_NullName_MaxValue()
    {
        Assert.Equal(int.MaxValue, OverviewCalc.MasteryOrderRank(null));
    }

    [Fact]
    public void MasteryOrderRank_FullSortMatchesRequestedOrder()
    {
        var shuffled = new List<string>
        {
            "Throwing Mastery", "Bow Mastery", "Crossbow Mastery", "Spear Mastery",
            "Polearm Mastery", "Dagger Mastery", "Sword Mastery", "Cleaver Mastery",
            "Axe Mastery", "Hammer Mastery", "Flail Mastery", "Mace Mastery"
        };
        var sorted = shuffled.OrderBy(OverviewCalc.MasteryOrderRank).ToList();
        Assert.Equal(new List<string>
        {
            "Mace Mastery", "Flail Mastery", "Hammer Mastery", "Axe Mastery",
            "Cleaver Mastery", "Sword Mastery", "Dagger Mastery", "Polearm Mastery",
            "Spear Mastery", "Crossbow Mastery", "Bow Mastery", "Throwing Mastery"
        }, sorted);
    }
}
