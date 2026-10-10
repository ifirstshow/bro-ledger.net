using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.Core.Tests.Services;

// Transcribed assertion-by-assertion from JAVATEST/service/ExpectedStatsCalculatorTest.java.
// Record accessors → positional properties; IllegalArgumentException → ArgumentException.
public class ExpectedStatsCalculatorTests
{
    private static Brother Brother(int levelTotal) => Brother(levelTotal, 0);

    private static Brother Brother(int levelTotal, int levelPoints) =>
        new() { LevelTotal = levelTotal, LevelPoints = levelPoints, Fingerprint = "TEST" };
        // Leave stats/stars at 0; no perks → modifiers are identity

    // ---- naive formula: ceil(base + mean × count) ---------------------------

    [Fact]
    public void Naive_BasicCeil()
    {
        // HEALTH 0★: range (2,4) mean=3.0. level=6 → remaining=5.
        // count=3 → ceil(50 + 9) = 59
        var b = Brother(6);
        b.Stats[Stat.Health.StatIndex] = 50;
        var inc = new int[8];
        inc[Stat.Health.Ordinal] = 3;

        var e = ExpectedStatsCalculator.Compute(b, Stat.Health, inc, ExpectedStatsCalculator.Mode.Naive);

        Assert.Equal(59, e.BaseExpected);
        Assert.Equal(59, e.FinalExpected); // no modifiers
        Assert.Equal(3, e.Count);
        Assert.Equal(5, e.RemainingLevels);
    }

    [Fact]
    public void Naive_HalfRollRoundsUp()
    {
        // MELEE_SKILL 0★: range (1,3) mean=2.0. count=1 → ceil(20 + 2.0) = 22 (no fractional)
        var b = Brother(6);
        b.Stats[Stat.MeleeSkill.StatIndex] = 20;
        var inc = new int[8];
        inc[Stat.MeleeSkill.Ordinal] = 1;

        var e = ExpectedStatsCalculator.Compute(b, Stat.MeleeSkill, inc, ExpectedStatsCalculator.Mode.Naive);

        Assert.Equal(22, e.BaseExpected);
    }

    [Fact]
    public void Naive_StarAdjustedRange_OneStar()
    {
        // INITIATIVE 1★: range (4,5) mean=4.5. count=2 → ceil(80 + 9) = 89
        var b = Brother(6);
        b.Stats[Stat.Initiative.StatIndex] = 80;
        b.Stars[Stat.Initiative.StarIndex] = 1;
        var inc = new int[8];
        inc[Stat.Initiative.Ordinal] = 2;

        var e = ExpectedStatsCalculator.Compute(b, Stat.Initiative, inc, ExpectedStatsCalculator.Mode.Naive);

        Assert.Equal(89, e.BaseExpected);
    }

    [Fact]
    public void Naive_CountZero_ReturnsCurrentBase()
    {
        var b = Brother(6);
        b.Stats[Stat.Resolve.StatIndex] = 40;
        var inc = new int[8]; // all zero

        var e = ExpectedStatsCalculator.Compute(b, Stat.Resolve, inc, ExpectedStatsCalculator.Mode.Naive);

        Assert.Equal(40, e.BaseExpected);
        Assert.Equal(0, e.Count);
        Assert.Equal(5, e.RemainingLevels);
    }

    [Fact]
    public void Remaining0_ReturnsCurrentBase()
    {
        var b = Brother(11); // at level cap
        b.Stats[Stat.Health.StatIndex] = 75;
        var inc = new int[8];
        inc[Stat.Health.Ordinal] = 2;

        var e = ExpectedStatsCalculator.Compute(b, Stat.Health, inc, ExpectedStatsCalculator.Mode.Naive);

        Assert.Equal(75, e.BaseExpected);
        Assert.Equal(0, e.RemainingLevels);
    }

    [Fact]
    public void NullIncreasesEquivalentToAllZero()
    {
        var b = Brother(6);
        b.Stats[Stat.Health.StatIndex] = 50;

        var withNull = ExpectedStatsCalculator.Compute(b, Stat.Health, null, ExpectedStatsCalculator.Mode.Naive);
        var allZero = new int[8];
        var withZero = ExpectedStatsCalculator.Compute(b, Stat.Health, allZero, ExpectedStatsCalculator.Mode.Naive);

        Assert.Equal(withZero.BaseExpected, withNull.BaseExpected);
    }

    // ---- greedy fallback when allocation is incomplete ----------------------

    [Fact]
    public void GreedyFallsBackToNaive_WhenPartial()
    {
        // remaining=5, budget=15, only 3 assigned → partial → naive
        var b = Brother(6);
        b.Stats[Stat.Health.StatIndex] = 50;
        var inc = new int[8];
        inc[Stat.Health.Ordinal] = 3; // sum=3 != 15

        var naive = ExpectedStatsCalculator.Compute(b, Stat.Health, inc, ExpectedStatsCalculator.Mode.Naive);
        var greedy = ExpectedStatsCalculator.Compute(b, Stat.Health, inc, ExpectedStatsCalculator.Mode.Greedy);

        // Greedy must fall back to naive when allocation is incomplete
        Assert.Equal(naive.BaseExpected, greedy.BaseExpected);
    }

    [Fact]
    public void GreedyFallsBackToNaive_WhenAStatExceedsRemainingButSumMatches()
    {
        // Reproduces a stale allocation left over from a temporarily higher budget
        // (e.g. rolls assigned while a Gifted perk was planned, then un-planned):
        // sum(inc) == 3 × remaining, but one stat's count alone exceeds remaining.
        // IsFullyAllocated() only checks the sum, so without the extra guard this
        // used to reach ExpectedGains()'s Validate() and throw ArgumentException.
        var b = Brother(6); // remaining=5, budget=15
        b.Stats[Stat.Health.StatIndex] = 50;
        var inc = new int[8];
        inc[Stat.Health.Ordinal] = 10; // exceeds remaining=5
        inc[Stat.Resolve.Ordinal] = 5; // sum = 15 = 3×5
        // (rest stay 0)

        var naive = ExpectedStatsCalculator.Compute(b, Stat.Health, inc, ExpectedStatsCalculator.Mode.Naive);
        var greedy = ExpectedStatsCalculator.Compute(b, Stat.Health, inc, ExpectedStatsCalculator.Mode.Greedy);

        // Greedy must fall back to naive on a stale over-cap allocation instead of throwing
        Assert.Equal(naive.BaseExpected, greedy.BaseExpected);
    }

    // ---- autoAssignByRole ---------------------------------------------------

    private static Role RoleWith3P1()
    {
        var r = new Role();
        r.Priority = [3, 3, 3, 3, 3, 3, 3, 3];
        r.Priority[Stat.Health.Ordinal] = 1;
        r.Priority[Stat.Resolve.Ordinal] = 1;
        r.Priority[Stat.Fatigue.Ordinal] = 1;
        return r;
    }

    [Fact]
    public void AutoAssign_3P1Stats_AllMaxed()
    {
        // remaining=2, 3 P1 stats — each maxed to 2. Total = 6 = 3×2.
        var b = Brother(9);
        var role = RoleWith3P1();
        var result = ExpectedStatsCalculator.AutoAssignByRole(b, role);

        Assert.Equal(2, result[Stat.Health.Ordinal]);
        Assert.Equal(2, result[Stat.Resolve.Ordinal]);
        Assert.Equal(2, result[Stat.Fatigue.Ordinal]);
        // others must be 0
        Assert.Equal(0, result[Stat.MeleeSkill.Ordinal]);
        var sum = result.Sum();
        Assert.Equal(6, sum); // sum must equal 3 × remaining = 6
    }

    [Fact]
    public void AutoAssign_4P1Stats_SplitEquallyAlphabetically()
    {
        // remaining=3, budget=9, 4 P1 stats.
        // Alphabetical displayNames: "Fatigue", "Health", "Melee Def", "Resolve"
        // each=2, extra=1 → first alphabetically ("Fatigue") gets +1 → 3.
        var b = Brother(8);
        var role = new Role();
        role.Priority = [3, 3, 3, 3, 3, 3, 3, 3];
        role.Priority[Stat.Health.Ordinal] = 1;        // "Health"
        role.Priority[Stat.Resolve.Ordinal] = 1;       // "Resolve"
        role.Priority[Stat.Fatigue.Ordinal] = 1;       // "Fatigue"
        role.Priority[Stat.MeleeDefense.Ordinal] = 1;  // "Melee Def"

        var result = ExpectedStatsCalculator.AutoAssignByRole(b, role);

        Assert.Equal(3, result[Stat.Fatigue.Ordinal]); // Fatigue gets +1 (first alphabetically)
        Assert.Equal(2, result[Stat.Health.Ordinal]);
        Assert.Equal(2, result[Stat.MeleeDefense.Ordinal]);
        Assert.Equal(2, result[Stat.Resolve.Ordinal]);
        var sum = result.Sum();
        Assert.Equal(9, sum); // sum must equal 3 × remaining = 9
    }

    [Fact]
    public void AutoAssign_P1ThenP2Cascade()
    {
        // remaining=3, budget=9. 1 P1 → maxed (3). Remaining budget=6. 2 P2 → maxed (3 each). Done.
        var b = Brother(8);
        var role = new Role();
        role.Priority = [3, 3, 3, 3, 3, 3, 3, 3];
        role.Priority[Stat.MeleeSkill.Ordinal] = 1;
        role.Priority[Stat.Health.Ordinal] = 2;
        role.Priority[Stat.Initiative.Ordinal] = 2;

        var result = ExpectedStatsCalculator.AutoAssignByRole(b, role);

        Assert.Equal(3, result[Stat.MeleeSkill.Ordinal]); // P1 maxed
        Assert.Equal(3, result[Stat.Health.Ordinal]);     // P2 maxed
        Assert.Equal(3, result[Stat.Initiative.Ordinal]); // P2 maxed
        var sum = result.Sum();
        Assert.Equal(9, sum);
    }

    [Fact]
    public void AutoAssign_Remaining0_AllZero()
    {
        var b = Brother(11);
        var result = ExpectedStatsCalculator.AutoAssignByRole(b, RoleWith3P1());
        var sum = result.Sum();
        Assert.Equal(0, sum); // no increases possible at max level
    }

    [Fact]
    public void AutoAssign_NullRole_AllZero()
    {
        var b = Brother(6);
        var result = ExpectedStatsCalculator.AutoAssignByRole(b, null);
        var sum = result.Sum();
        Assert.Equal(0, sum);
    }

    [Fact]
    public void AutoAssign_SumAlwaysEquals3xRemaining()
    {
        // General invariant for various setups
        var b = Brother(5); // remaining=6, budget=18
        var role = new Role();
        role.Priority = [3, 3, 3, 3, 3, 3, 3, 3];
        role.Priority[Stat.MeleeSkill.Ordinal] = 1;
        role.Priority[Stat.Health.Ordinal] = 2;

        var result = ExpectedStatsCalculator.AutoAssignByRole(b, role);
        var sum = result.Sum();
        Assert.Equal(18, sum); // sum must equal 3 × 6 = 18
    }

    [Fact]
    public void AutoAssign_PerStatCapRespected()
    {
        // Every assigned stat must be <= remaining
        var b = Brother(8); // remaining=3
        var role = RoleWith3P1(); // all assigned to 3 P1 stats
        var result = ExpectedStatsCalculator.AutoAssignByRole(b, role);

        for (var i = 0; i < result.Length; i++)
        {
            Assert.True(result[i] <= 3,
                $"Stat {Stat.Values[i].DisplayName} exceeds per-stat cap: {result[i]}");
        }
    }

    // ---- remainingLevels: pending level-ups folded in -----------------------

    [Fact]
    public void RemainingLevels_NoPending_EqualsGapTo11()
    {
        Assert.Equal(6, ExpectedStatsCalculator.RemainingLevels(Brother(5, 0)));
        Assert.Equal(1, ExpectedStatsCalculator.RemainingLevels(Brother(10, 0)));
    }

    [Fact]
    public void RemainingLevels_WithPending_AddsPendingToGap()
    {
        Assert.Equal(7, ExpectedStatsCalculator.RemainingLevels(Brother(5, 1)));
        Assert.Equal(2, ExpectedStatsCalculator.RemainingLevels(Brother(10, 1)));
    }

    [Fact]
    public void RemainingLevels_AtOrBeyond11_AlwaysZero()
    {
        Assert.Equal(0, ExpectedStatsCalculator.RemainingLevels(Brother(11, 0)));
        Assert.Equal(0, ExpectedStatsCalculator.RemainingLevels(Brother(11, 1)));
        Assert.Equal(0, ExpectedStatsCalculator.RemainingLevels(Brother(15, 2)));
    }

    [Fact]
    public void Compute_PendingLevelReflectedInRemainingLevels()
    {
        // level=6, levelPoints=1 → remaining=6 (5 future + 1 pending)
        var b = Brother(6, 1);
        b.Stats[Stat.Health.StatIndex] = 50;
        var inc = new int[8];

        var e = ExpectedStatsCalculator.Compute(b, Stat.Health, inc, ExpectedStatsCalculator.Mode.Naive);
        Assert.Equal(6, e.RemainingLevels);
    }

    [Fact]
    public void AutoAssign_PendingLevel_EnlargesBudget()
    {
        // level=9, levelPoints=1 → remaining=3 (1 future + 1 pending), budget=9
        var b = Brother(9, 1);
        var role = RoleWith3P1();
        var result = ExpectedStatsCalculator.AutoAssignByRole(b, role);
        var sum = result.Sum();
        Assert.Equal(9, sum); // budget must be 3 × 3 = 9
    }

    // ---- Mode.Parse ---------------------------------------------------------

    [Fact]
    public void ModeParse_NaiveDefault()
    {
        Assert.Equal(ExpectedStatsCalculator.Mode.Naive, ExpectedStatsCalculator.Parse("NAIVE"));
        Assert.Equal(ExpectedStatsCalculator.Mode.Naive, ExpectedStatsCalculator.Parse("invalid"));
        Assert.Equal(ExpectedStatsCalculator.Mode.Naive, ExpectedStatsCalculator.Parse(null));
    }

    [Fact]
    public void ModeParse_Greedy()
    {
        Assert.Equal(ExpectedStatsCalculator.Mode.Greedy, ExpectedStatsCalculator.Parse("GREEDY"));
        Assert.Equal(ExpectedStatsCalculator.Mode.Greedy, ExpectedStatsCalculator.Parse("greedy"));
    }

    // ---- new pure helpers --------------------------------------------------

    [Fact]
    public void RemainingLevels_PureOverload_MatchesBrotherForm()
    {
        Assert.Equal(ExpectedStatsCalculator.RemainingLevels(Brother(7, 2)),
                     ExpectedStatsCalculator.RemainingLevels(7, 2));
        Assert.Equal(ExpectedStatsCalculator.RemainingLevels(Brother(11, 0)),
                     ExpectedStatsCalculator.RemainingLevels(11, 0));
    }

    [Fact]
    public void IsFixed_TrueWhenEqual()
    {
        Assert.True(ExpectedStatsCalculator.IsFixed(3, 3));
        Assert.False(ExpectedStatsCalculator.IsFixed(2, 3));
        Assert.False(ExpectedStatsCalculator.IsFixed(0, 3));
    }

    [Fact]
    public void IsFlexible_TrueWhenAllocatedButNotMax()
    {
        Assert.True(ExpectedStatsCalculator.IsFlexible(1, 3));
        Assert.True(ExpectedStatsCalculator.IsFlexible(2, 3));
        Assert.False(ExpectedStatsCalculator.IsFlexible(0, 3)); // not allocated
        Assert.False(ExpectedStatsCalculator.IsFlexible(3, 3)); // maxed (fixed)
    }

    [Fact]
    public void FlexibleSlots_ThreeMinusFixed()
    {
        Assert.Equal(3, ExpectedStatsCalculator.FlexibleSlots(0));
        Assert.Equal(1, ExpectedStatsCalculator.FlexibleSlots(2));
        Assert.Equal(0, ExpectedStatsCalculator.FlexibleSlots(3));
    }

    [Fact]
    public void MeanRoll_Midpoint()
    {
        var r = new StatPotentialCalculator.Range(2, 4);
        Assert.Equal(3.0, ExpectedStatsCalculator.MeanRoll(r), 9);
    }

    [Fact]
    public void RollDeviation_Positive()
    {
        var r = new StatPotentialCalculator.Range(1, 3);
        // mean = 2.0; roll=3 → deviation = 1.0
        Assert.Equal(1.0, ExpectedStatsCalculator.RollDeviation(3, r), 9);
    }

    [Fact]
    public void AllocateByRoleTiers_MatchesOriginalAutoAssignByRole()
    {
        var role = RoleWith3P1();
        var b = Brother(5, 0); // remaining = 6
        var expected = ExpectedStatsCalculator.AutoAssignByRole(b, role);
        var remaining = ExpectedStatsCalculator.RemainingLevels(b);
        var actual = ExpectedStatsCalculator.AllocateByRoleTiers(role.Priority, remaining);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void AllocateByRoleTiers_MatchesOriginalAutoAssignPost11ByRole()
    {
        var role = RoleWith3P1();
        var b = Brother(14, 0); // levelTotal=14 → postLevels=3
        var expected = ExpectedStatsCalculator.AutoAssignPost11ByRole(b, role);
        var postLevels = Math.Max(0, b.LevelTotal - 11);
        var actual = ExpectedStatsCalculator.AllocateByRoleTiers(role.Priority, postLevels);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void StatsAtTier_ReturnsCorrectStats()
    {
        var priority = new int[8]; // all P3 by default
        priority[Stat.Health.Ordinal] = 1;
        priority[Stat.Resolve.Ordinal] = 1;
        priority[Stat.Fatigue.Ordinal] = 2;
        var p1 = ExpectedStatsCalculator.StatsAtTier(priority, 1);
        Assert.Equal(2, p1.Count);
        Assert.Contains(Stat.Health, p1);
        Assert.Contains(Stat.Resolve, p1);
    }

    [Fact]
    public void StatsAtTier_DefaultsToP3WhenPriorityNull()
    {
        // null priority → all stats default to 3
        var p3 = ExpectedStatsCalculator.StatsAtTier(null, 3);
        Assert.Equal(8, p3.Count);
        Assert.Empty(ExpectedStatsCalculator.StatsAtTier(null, 1));
    }

    [Fact]
    public void IsFullyAllocated_TrueWhenSumEquals3xRemaining()
    {
        var inc = new int[8];
        inc[Stat.Health.Ordinal] = 3;
        inc[Stat.MeleeSkill.Ordinal] = 3;
        // sum=6, remaining=2 → 3×2=6 ✓
        Assert.True(ExpectedStatsCalculator.IsFullyAllocated(inc, 2));
        Assert.False(ExpectedStatsCalculator.IsFullyAllocated(inc, 3));
    }
}
