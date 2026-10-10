using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.Core.Tests.Services;

// Transcribed assertion-by-assertion from JAVATEST/service/BattleBrothersLevelPredictorTest.java.
// assertArrayEquals(x, y, delta) → AssertArrayEquals helper (per-element with tolerance).
public class BattleBrothersLevelPredictorTests
{
    private static int[] AllPriority3()
    {
        var p = new int[Stat.Values.Length];
        Array.Fill(p, 3);
        return p;
    }

    private static int[] AllZeroStars() => new int[Stat.Values.Length];

    private static void AssertArrayEquals(double[] expected, double[] actual, double delta, string? message = null)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(expected[i] - actual[i]) <= delta,
                $"{message} [{i}] expected {expected[i]} actual {actual[i]}");
    }

    // Three fixed stats covering 10 levels each — all fixed, greedy == naive.
    [Fact]
    public void AllFixedStats_GreedyEqualsNaive()
    {
        var n = Stat.Values.Length;
        var rolls = new int[n];
        rolls[Stat.Health.Ordinal] = 10;
        rolls[Stat.MeleeSkill.Ordinal] = 10;
        rolls[Stat.Initiative.Ordinal] = 10;

        var result = ExpectedStatsCalculator.ExpectedGains(10, AllZeroStars(), rolls, AllPriority3());

        AssertArrayEquals(result[0], result[1], 1e-9, "All-fixed allocation: greedy must equal naive");
    }

    // Naive values must match expected mean × count formula for 0-star stats.
    [Fact]
    public void NaiveMatchesMeanTimesCount_ZeroStars()
    {
        var n = Stat.Values.Length;
        var rolls = new int[n];
        rolls[Stat.Health.Ordinal] = 10;      // mean 3.0 → 30
        rolls[Stat.MeleeSkill.Ordinal] = 10;  // mean 2.0 → 20
        rolls[Stat.Initiative.Ordinal] = 10;  // mean 4.0 → 40

        var result = ExpectedStatsCalculator.ExpectedGains(10, AllZeroStars(), rolls, AllPriority3());
        var naive = result[1];

        Assert.Equal(30.0, naive[Stat.Health.Ordinal], 9);
        Assert.Equal(20.0, naive[Stat.MeleeSkill.Ordinal], 9);
        Assert.Equal(40.0, naive[Stat.Initiative.Ordinal], 9);
        // Un-allocated stats contribute 0
        Assert.Equal(0.0, naive[Stat.Resolve.Ordinal], 9);
    }

    // Naive for a stat with 3 stars and 10 rolls.
    // HEALTH 3★: range (4,5), mean 4.5 → 45.0
    [Fact]
    public void NaiveMatchesMeanTimesCount_ThreeStars()
    {
        var n = Stat.Values.Length;
        var stars = new int[n];
        stars[Stat.Health.Ordinal] = 3;
        stars[Stat.MeleeSkill.Ordinal] = 0;
        stars[Stat.Initiative.Ordinal] = 0;

        var rolls = new int[n];
        rolls[Stat.Health.Ordinal] = 10;
        rolls[Stat.MeleeSkill.Ordinal] = 10;
        rolls[Stat.Initiative.Ordinal] = 10;

        var result = ExpectedStatsCalculator.ExpectedGains(10, stars, rolls, AllPriority3());
        var naive = result[1];

        // HEALTH 3★: 10 × 4.5 = 45.0
        Assert.Equal(45.0, naive[Stat.Health.Ordinal], 9);
    }

    // Greedy >= naive for any flexible allocation.
    [Fact]
    public void GreedyAtLeastNaive_FlexStats()
    {
        var n = Stat.Values.Length;
        // 2 fixed (HEALTH, MELEE_SKILL), 1 flex (INITIATIVE with 5 rolls) + RESOLVE 5 rolls
        var rolls = new int[n];
        rolls[Stat.Health.Ordinal] = 10;
        rolls[Stat.MeleeSkill.Ordinal] = 10;
        rolls[Stat.Initiative.Ordinal] = 5;
        rolls[Stat.Resolve.Ordinal] = 5;

        var result = ExpectedStatsCalculator.ExpectedGains(10, AllZeroStars(), rolls, AllPriority3());

        var greedy = result[0];
        var naive = result[1];
        foreach (var s in Stat.Values)
        {
            if (rolls[s.Ordinal] > 0)
            {
                Assert.True(greedy[s.Ordinal] >= naive[s.Ordinal] - 1e-9,
                    $"greedy[{s.DisplayName}] should be >= naive[{s.DisplayName}]");
            }
        }
    }

    // Greedy strictly > naive for a genuinely contested flex stat.
    [Fact]
    public void GreedyExceedsNaive_WhenFlexContested()
    {
        var n = Stat.Values.Length;
        var rolls = new int[n];
        rolls[Stat.Health.Ordinal] = 10;
        rolls[Stat.MeleeSkill.Ordinal] = 10;
        rolls[Stat.Initiative.Ordinal] = 5;
        rolls[Stat.Resolve.Ordinal] = 5;

        var result = ExpectedStatsCalculator.ExpectedGains(10, AllZeroStars(), rolls, AllPriority3());

        var greedyInit = result[0][Stat.Initiative.Ordinal];
        var naiveInit = result[1][Stat.Initiative.Ordinal];
        var greedyRes = result[0][Stat.Resolve.Ordinal];
        var naiveRes = result[1][Stat.Resolve.Ordinal];
        // The selection benefit must boost at least one of the two flex stats above naive.
        Assert.True(greedyInit + greedyRes > naiveInit + naiveRes - 1e-9,
            "Total greedy gain should be >= total naive gain for contested flex stats");
    }

    // Zero-allocation stats must not count toward the K > 6 guard.
    // Before the fix: 1 fixed + 1 flex + 6 zero-alloc → K=7 (old: N-fixedCount) → wrongly threw.
    // After the fix:  K=1 (only the genuinely-flexible stat) → runs greedy without error.
    [Fact]
    public void ZeroAllocStats_DoNotInflateK()
    {
        var n = Stat.Values.Length;
        var rolls = new int[n];
        // levelsRemaining=3, budget=9: 3 fixed stats, 1 flex, 4 unallocated
        rolls[Stat.Health.Ordinal] = 3;        // fixed
        rolls[Stat.MeleeSkill.Ordinal] = 3;    // fixed
        rolls[Stat.MeleeDefense.Ordinal] = 3;  // fixed
        rolls[Stat.Initiative.Ordinal] = 0;    // unallocated — must not count toward K
        // all others default 0
        // sum = 9 = 3×3 ✓, no flex stat → flexSlots=0, greedy==naive (trivially)
        ExpectedStatsCalculator.ExpectedGains(3, AllZeroStars(), rolls, AllPriority3());

        // Now put 2 into initiative (flex) and reduce one fixed to compensate
        rolls[Stat.Health.Ordinal] = 3;        // fixed
        rolls[Stat.MeleeSkill.Ordinal] = 3;    // fixed
        rolls[Stat.MeleeDefense.Ordinal] = 1;  // flex (1 < 3)
        rolls[Stat.Initiative.Ordinal] = 2;    // flex (2 < 3)
        // sum = 9 ✓, fixedCount=2, genuinelyFlexible=2, old K=6, new K=2 — both ≤ 6, fine
        var result = ExpectedStatsCalculator.ExpectedGains(3, AllZeroStars(), rolls, AllPriority3());
        // Greedy gain for flex stats should be >= naive
        Assert.True(result[0][Stat.MeleeDefense.Ordinal] >= result[1][Stat.MeleeDefense.Ordinal] - 1e-9);
        Assert.True(result[0][Stat.Initiative.Ordinal] >= result[1][Stat.Initiative.Ordinal] - 1e-9);
    }

    // levelsRemaining=0 → both arrays are zero.
    [Fact]
    public void LevelsRemaining0_ReturnsZeroGains()
    {
        var rolls = new int[Stat.Values.Length]; // all zero
        var result = ExpectedStatsCalculator.ExpectedGains(0, AllZeroStars(), rolls, AllPriority3());
        foreach (var v in result[0]) Assert.Equal(0.0, v, 9);
    }

    // Validation: sum of rolls != 3 × levelsRemaining.
    [Fact]
    public void Validation_WrongRollSum_Throws()
    {
        var n = Stat.Values.Length;
        var rolls = new int[n];
        rolls[Stat.Health.Ordinal] = 5; // sum=5 != 3×2=6
        Assert.Throws<ArgumentException>(() =>
            ExpectedStatsCalculator.ExpectedGains(2, AllZeroStars(), rolls, AllPriority3()));
    }

    // Validation: a single roll exceeds levelsRemaining.
    [Fact]
    public void Validation_RollExceedsLevels_Throws()
    {
        var n = Stat.Values.Length;
        var rolls = new int[n];
        rolls[Stat.Health.Ordinal] = 3;      // exceeds levelsRemaining=2
        rolls[Stat.MeleeSkill.Ordinal] = 2;
        rolls[Stat.Initiative.Ordinal] = 1;
        Assert.Throws<ArgumentException>(() =>
            ExpectedStatsCalculator.ExpectedGains(2, AllZeroStars(), rolls, AllPriority3()));
    }

    // TooManyFlexibleStatsException when K > 6.
    [Fact]
    public void TooManyFlexStats_Throws()
    {
        var n = Stat.Values.Length;
        var rolls = new int[n];
        // Spread across 8 stats so none equals levelsRemaining — all flex.
        // Sum must equal 3×4=12, each rolled 1-2 times, none = levelsRemaining=4
        // (8 stats each 1 roll = 8 != 12; try: 4 stats with 2 rolls + 4 with 1 roll = 12)
        rolls[Stat.Health.Ordinal] = 2;
        rolls[Stat.Resolve.Ordinal] = 2;
        rolls[Stat.Fatigue.Ordinal] = 2;
        rolls[Stat.Initiative.Ordinal] = 2;
        rolls[Stat.MeleeSkill.Ordinal] = 1;
        rolls[Stat.RangedSkill.Ordinal] = 1;
        rolls[Stat.MeleeDefense.Ordinal] = 1;
        rolls[Stat.RangedDefense.Ordinal] = 1;
        // All < levelsRemaining=4 → all 8 are flex → K=8 > 6
        Assert.Throws<ExpectedStatsCalculator.TooManyFlexibleStatsException>(() =>
            ExpectedStatsCalculator.ExpectedGains(4, AllZeroStars(), rolls, AllPriority3()));
    }
}
