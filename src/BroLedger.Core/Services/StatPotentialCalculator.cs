using BroLedger.Core.Models;

namespace BroLedger.Core.Services;

// Transcribed method-by-method from Java service/StatPotentialCalculator.java.
// Roll ranges sourced from the Battle Brothers wiki (Talents.html in project root).
// Rule: 1★ adds +1 to min, 2★ adds +2 to min, 3★ adds +2 to min and +1 to max.
public static class StatPotentialCalculator
{
    public sealed record Range(int Min, int Max);

    /// <summary>
    /// <paramref name="BasePotential"/>: expected base stat at level 11 (before trait/perk mods).<br/>
    /// <paramref name="FinalPotential"/>: base potential after trait/perk modifiers.<br/>
    /// <paramref name="MinBasePotential"/>/<paramref name="MaxBasePotential"/>: worst/best-case base stat at level 11.<br/>
    /// <paramref name="RemainingLevels"/>: level-ups remaining before level 11 (0 means already ≥ 11).
    /// </summary>
    public sealed record Potential(int BasePotential, int FinalPotential,
                                   int MinBasePotential, int MaxBasePotential,
                                   int RemainingLevels);

    // Base (0★) per-level roll min/max, indexed by Stat.StatIndex.
    private static readonly Range?[] BaseRanges =
    [
        new(2, 4), // [0] HP
        new(2, 4), // [1] Res
        new(2, 4), // [2] Fat
        new(1, 3), // [3] MS
        new(2, 4), // [4] RS
        new(1, 3), // [5] MD
        new(2, 4), // [6] RD
        new(3, 5), // [7] Init
    ];

    /// <summary>0★ per-level roll range for the given stat index, or null when out of bounds.</summary>
    internal static Range? BaseRange(int statIdx) =>
        statIdx >= 0 && statIdx < BaseRanges.Length ? BaseRanges[statIdx] : null;

    /// <summary>Per-level roll range adjusted for the given star count (0–3), or null when out of bounds.</summary>
    internal static Range? RangeForStars(int statIdx, int stars)
    {
        var baseRange = BaseRange(statIdx);
        return baseRange == null ? null : ApplyStars(baseRange, stars);
    }

    /// <summary>
    /// Applies talent-star adjustments to a base roll range.
    /// Rule: 1★/2★ raise the min by 1/2 respectively; 3★ also raises the max by 1.
    /// </summary>
    internal static Range ApplyStars(Range baseRange, int stars)
    {
        var s = Math.Clamp(stars, 0, 3);
        var min = baseRange.Min + Math.Min(s, 2);
        var max = baseRange.Max + (s >= 3 ? 1 : 0);
        return new Range(min, max);
    }

    /// <summary>Projects a base stat forward by <paramref name="remaining"/> level-ups using a fixed per-level roll.</summary>
    internal static int ProjectBase(int baseValue, int remaining, int perLevelRoll) =>
        baseValue + remaining * perLevelRoll;

    /// <summary>
    /// Projects the expected (rounded) base stat forward using the mean of the roll range.
    /// Java Math.round(double) = floor(x + 0.5); C# Math.Round defaults to banker's rounding (ToEven),
    /// so halves-up must be spelled out explicitly.
    /// </summary>
    internal static int ProjectExpectedBase(int baseValue, int remaining, Range range) =>
        (int)Math.Floor(baseValue + remaining * (range.Min + range.Max) / 2.0 + 0.5);

    /// <summary>
    /// Expected base potential (no trait/perk mods) rounded to nearest integer.
    /// Returns <paramref name="currentBase"/> unchanged when currentLevel ≥ 11.
    /// </summary>
    public static int ExpectedBasePotential(int currentBase, Stat stat,
                                            int stars, int currentLevel)
    {
        var remaining = Math.Max(0, 11 - currentLevel);
        if (remaining == 0) return currentBase;
        var range = RangeForStars(stat.StatIndex, stars);
        if (range == null) return currentBase;
        return ProjectExpectedBase(currentBase, remaining, range);
    }

    /// <summary>Full potential calculation including trait/perk modifiers.</summary>
    /// <param name="b">the brother</param>
    /// <param name="stat">the stat (carries both StatIndex and StarIndex)</param>
    public static Potential Compute(Brother b, Stat stat)
    {
        var statIdx = stat.StatIndex;
        var remaining = ExpectedStatsCalculator.RemainingLevels(b);
        if (remaining == 0)
        {
            var bd = StatModifierService.Instance.Compute(b, stat);
            return new Potential(b.Stats[statIdx], bd.FinalValue,
                                 b.Stats[statIdx], b.Stats[statIdx], 0);
        }

        var stars = b.Stars[stat.StarIndex];
        var range = RangeForStars(statIdx, stars) ?? new Range(0, 0);

        var baseValue = b.Stats[statIdx];
        var minBase = ProjectBase(baseValue, remaining, range.Min);
        var maxBase = ProjectBase(baseValue, remaining, range.Max);
        var expectedBase = ProjectExpectedBase(baseValue, remaining, range);

        var bdExpected = StatModifierService.Instance.ComputeWithBase(b, stat, expectedBase);
        return new Potential(expectedBase, bdExpected.FinalValue, minBase, maxBase, remaining);
    }
}
