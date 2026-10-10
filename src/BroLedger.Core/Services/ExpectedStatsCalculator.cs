using BroLedger.Core.Models;

namespace BroLedger.Core.Services;

// Transcribed method-by-method from Java service/ExpectedStatsCalculator.java.
//
// Computes expected stat values based on user-assigned level-up increase allocations.
// The "expected" stat for a given allocation count is ceil(currentBase + mean_per_level
// increase(stars) × count), then trait/perk modifiers applied — mirroring how
// StatPotentialCalculator works.
//
// Increase allocations are indexed by Stat.Ordinal, matching Role.Priority indexing.
public static class ExpectedStatsCalculator
{
    private static readonly Stat[] Stats = Stat.Values;
    private static readonly int N = Stats.Length;

    /// <summary>
    /// Returns <c>[greedy, naive]</c> expected total stat gains to level 11, both
    /// indexed by <see cref="Stat.Ordinal"/>.
    /// <c>result[0]</c> — greedy: deviation-based flexible-slot algorithm.<br/>
    /// <c>result[1]</c> — naive: <c>rolls[i] * E[roll[i]]</c> with no selection benefit.
    /// </summary>
    /// <exception cref="TooManyFlexibleStatsException">if more than 6 stats are flexible</exception>
    public static double[][] ExpectedGains(int levelsRemaining, int[] stars, int[] rolls, int[] priority)
    {
        Validate(levelsRemaining, stars, rolls, priority);

        var naive = new double[N];
        for (var i = 0; i < N; i++)
        {
            var r = StatPotentialCalculator.RangeForStars(Stats[i].StatIndex, stars[i])!;
            naive[i] = rolls[i] * MeanRoll(r);
        }

        if (levelsRemaining == 0) return [new double[N], naive];

        var fixedCount = 0;
        for (var i = 0; i < N; i++)
            if (IsFixed(rolls[i], levelsRemaining)) fixedCount++;
        // K counts only genuinely-flexible stats: allocated but not maxed (rolls[i] > 0 && < levelsRemaining).
        // Zero-allocation stats are excluded — they can never be selected and must not inflate K.
        var k = 0;
        for (var i = 0; i < N; i++)
            if (IsFlexible(rolls[i], levelsRemaining)) k++;
        if (k > 6) throw new TooManyFlexibleStatsException(k);
        var flexSlots = FlexibleSlots(fixedCount);

        var greedy = new double[N];
        for (var i = 0; i < N; i++)
            if (IsFixed(rolls[i], levelsRemaining))
            {
                var r = StatPotentialCalculator.RangeForStars(Stats[i].StatIndex, stars[i])!;
                greedy[i] = rolls[i] * MeanRoll(r);
            }

        if (flexSlots == 0) return [greedy, naive];

        var flexIdx = new int[k];
        var flexRolls = new int[k];
        var lo = new int[k];
        var hi = new int[k];
        var eRoll = new double[k];
        var flexPriority = new int[k];
        var j = 0;
        for (var i = 0; i < N; i++)
        {
            if (IsFlexible(rolls[i], levelsRemaining))
            {
                var r = StatPotentialCalculator.RangeForStars(Stats[i].StatIndex, stars[i])!;
                flexIdx[j] = i;
                flexRolls[j] = rolls[i];
                lo[j] = r.Min;
                hi[j] = r.Max;
                eRoll[j] = MeanRoll(r);
                flexPriority[j] = priority[i];
                j++;
            }
        }

        var memo = new Dictionary<string, double[]>();
        var flexGains = GreedyExpected(flexRolls, lo, hi, eRoll, flexPriority, flexSlots, memo);
        for (j = 0; j < k; j++)
            greedy[flexIdx[j]] = flexGains[j];

        return [greedy, naive];
    }

    /// <summary>Structured entry produced by <see cref="RollPriorityGuideEntries"/>.</summary>
    public sealed record PriorityEntry(Stat Stat, bool Fixed, int RollValue, double Deviation);

    /// <summary>
    /// Structured form of the roll-priority guide.
    /// Fixed stats (always pick) come first; flexible stats follow sorted by descending deviation.
    /// </summary>
    /// <exception cref="TooManyFlexibleStatsException">if more than 6 stats are flexible</exception>
    public static List<PriorityEntry> RollPriorityGuideEntries(int levelsRemaining, int[] stars,
                                                               int[] rolls, int[] priority)
    {
        Validate(levelsRemaining, stars, rolls, priority);
        var result = new List<PriorityEntry>();
        if (levelsRemaining == 0) return result;
        var flexEntries = BuildSortedFlexEntries(levelsRemaining, stars, rolls, priority);
        for (var i = 0; i < N; i++)
            if (IsFixed(rolls[i], levelsRemaining))
                result.Add(new PriorityEntry(Stats[i], true, 0, 0.0));
        foreach (var e in flexEntries)
        {
            var r = StatPotentialCalculator.RangeForStars(Stats[e[0]].StatIndex, stars[e[0]])!;
            result.Add(new PriorityEntry(Stats[e[0]], false, e[1], RollDeviation(e[1], r)));
        }
        return result;
    }

    /// <summary>
    /// Returns a human-readable pick-priority guide for the flexible slot(s) at each level-up.
    /// Fixed stats appear first; flexible stats are listed as one entry per (stat, roll) outcome.
    /// </summary>
    /// <exception cref="TooManyFlexibleStatsException">if more than 6 stats are flexible</exception>
    public static List<string> RollPriorityGuide(int levelsRemaining, int[] stars, int[] rolls, int[] priority)
    {
        Validate(levelsRemaining, stars, rolls, priority);
        var guide = new List<string>();
        if (levelsRemaining == 0) return guide;
        var entries = BuildSortedFlexEntries(levelsRemaining, stars, rolls, priority);
        for (var i = 0; i < N; i++)
            if (IsFixed(rolls[i], levelsRemaining))
                guide.Add(string.Format("Always pick {0,-18}  -", Stats[i].DisplayName));
        foreach (var e in entries)
        {
            var r = StatPotentialCalculator.RangeForStars(Stats[e[0]].StatIndex, stars[e[0]])!;
            // {2:+0.0;-0.0;+0.0} mirrors Java "%+.1f" (explicit sign; deviations are exact
            // half-integers, so no rounding-mode divergence).
            guide.Add(string.Format("{0,-18}  {1}   {2:+0.0;-0.0;+0.0}",
                Stats[e[0]].DisplayName, e[1], RollDeviation(e[1], r)));
        }
        return guide;
    }

    private static List<int[]> BuildSortedFlexEntries(int levelsRemaining, int[] stars,
                                                      int[] rolls, int[] priority)
    {
        // No K > 6 guard here — sorting by deviation is O(K × roll_range), not exponential.
        // The K ≤ 6 limit lives only in ExpectedGains where the full enumeration runs.
        var entries = new List<int[]>();
        for (var i = 0; i < N; i++)
        {
            if (IsFixed(rolls[i], levelsRemaining) || rolls[i] == 0) continue;
            var r = StatPotentialCalculator.RangeForStars(Stats[i].StatIndex, stars[i])!;
            for (var rv = r.Max; rv >= r.Min; rv--)
                entries.Add([i, rv]);
        }
        entries.Sort((a, b) =>
        {
            var rA = StatPotentialCalculator.RangeForStars(Stats[a[0]].StatIndex, stars[a[0]])!;
            var rB = StatPotentialCalculator.RangeForStars(Stats[b[0]].StatIndex, stars[b[0]])!;
            var devA = RollDeviation(a[1], rA);
            var devB = RollDeviation(b[1], rB);
            if (devA != devB) return devB.CompareTo(devA);
            if (rolls[a[0]] != rolls[b[0]]) return rolls[b[0]].CompareTo(rolls[a[0]]);
            if (priority[a[0]] != priority[b[0]]) return priority[a[0]].CompareTo(priority[b[0]]);
            // Java compareTo() on ASCII = ordinal comparison, not culture-aware.
            return string.CompareOrdinal(Stats[a[0]].DisplayName, Stats[b[0]].DisplayName);
        });
        return entries;
    }

    private static double[] GreedyExpected(int[] remaining, int[] lo, int[] hi, double[] eRoll,
                                           int[] priority, int flexSlots,
                                           Dictionary<string, double[]> memo)
    {
        // Java memo key was Arrays.toString(remaining); format only affects hits, not semantics.
        var key = string.Join(",", remaining);
        if (memo.TryGetValue(key, out var cached)) return cached;

        var k = remaining.Length;
        var result = new double[k];

        var ac = 0;
        var active = new int[k];
        for (var j = 0; j < k; j++) if (remaining[j] > 0) active[ac++] = j;
        if (ac == 0) { memo[key] = result; return result; }

        var slotsToFill = Math.Min(flexSlots, ac);
        long totalCombos = 1;
        var aLo = new int[ac];
        var aHi = new int[ac];
        for (var ai = 0; ai < ac; ai++)
        {
            aLo[ai] = lo[active[ai]];
            aHi[ai] = hi[active[ai]];
            totalCombos *= aHi[ai] - aLo[ai] + 1;
        }

        var accumulated = new double[k];
        var rollBuf = new int[ac];
        Enumerate(0, ac, aLo, aHi, rollBuf,
                  active, remaining, lo, hi, eRoll, priority, flexSlots, slotsToFill,
                  accumulated, memo);

        for (var j = 0; j < k; j++) result[j] = accumulated[j] / totalCombos;
        memo[key] = result;
        return result;
    }

    private static void Enumerate(int depth, int ac, int[] aLo, int[] aHi, int[] rollBuf,
                                  int[] active, int[] remaining,
                                  int[] lo, int[] hi, double[] eRoll, int[] priority,
                                  int flexSlots, int slotsToFill,
                                  double[] accumulated, Dictionary<string, double[]> memo)
    {
        if (depth == ac)
        {
            var picked = new bool[ac];
            for (var slot = 0; slot < slotsToFill; slot++)
            {
                var best = -1;
                for (var ai = 0; ai < ac; ai++)
                {
                    if (picked[ai]) continue;
                    if (best == -1) { best = ai; continue; }
                    var dBest = rollBuf[best] - eRoll[active[best]];
                    var dAi = rollBuf[ai] - eRoll[active[ai]];
                    if (dAi > dBest) { best = ai; continue; }
                    if (dAi < dBest) continue;
                    var rBest = remaining[active[best]];
                    var rAi = remaining[active[ai]];
                    if (rAi > rBest) { best = ai; continue; }
                    if (rAi < rBest) continue;
                    if (priority[active[ai]] < priority[active[best]]) best = ai;
                }
                picked[best] = true;
            }

            var newRem = (int[])remaining.Clone();
            for (var ai = 0; ai < ac; ai++)
                if (picked[ai]) newRem[active[ai]]--;

            var sub = GreedyExpected(newRem, lo, hi, eRoll, priority, flexSlots, memo);

            for (var j = 0; j < remaining.Length; j++) accumulated[j] += sub[j];
            for (var ai = 0; ai < ac; ai++)
                if (picked[ai]) accumulated[active[ai]] += rollBuf[ai];
            return;
        }

        for (var r = aLo[depth]; r <= aHi[depth]; r++)
        {
            rollBuf[depth] = r;
            Enumerate(depth + 1, ac, aLo, aHi, rollBuf,
                      active, remaining, lo, hi, eRoll, priority, flexSlots, slotsToFill,
                      accumulated, memo);
        }
    }

    private static void Validate(int levelsRemaining, int[] stars, int[] rolls, int[] priority)
    {
        if (levelsRemaining < 0 || levelsRemaining > 11)
            throw new ArgumentException($"levelsRemaining must be 0–11, got {levelsRemaining}");
        if (stars.Length != N)
            throw new ArgumentException($"stars[] must have length {N}");
        if (rolls.Length != N)
            throw new ArgumentException($"rolls[] must have length {N}");
        if (priority.Length != N)
            throw new ArgumentException($"priority[] must have length {N}");
        var total = 0;
        for (var i = 0; i < N; i++)
        {
            if (stars[i] < 0 || stars[i] > 3)
                throw new ArgumentException($"{Stats[i].DisplayName}: stars must be 0–3, got {stars[i]}");
            if (rolls[i] < 0)
                throw new ArgumentException($"{Stats[i].DisplayName}: rolls must be >= 0, got {rolls[i]}");
            if (rolls[i] > levelsRemaining)
                throw new ArgumentException(
                    $"{Stats[i].DisplayName}: rolls ({rolls[i]}) exceeds levelsRemaining ({levelsRemaining})");
            if (priority[i] < 1 || priority[i] > 3)
                throw new ArgumentException($"{Stats[i].DisplayName}: priority must be 1–3, got {priority[i]}");
            total += rolls[i];
        }
        if (total != 3 * levelsRemaining)
            throw new ArgumentException(
                $"Sum of rolls ({total}) must equal 3 × levelsRemaining = {3 * levelsRemaining}");
    }

    public enum Mode
    {
        /// <summary>Simple per-stat formula: ceil(currentBase + mean × count). Always live-updating.</summary>
        Naive,
        /// <summary>
        /// Greedy optimal (see <c>BattleBrothersLevelPredictor</c>): accounts for choosing
        /// high-roll levels. Only valid when all increases are assigned (sum == 3×remaining);
        /// falls back to Naive for partial allocations.
        /// </summary>
        Greedy
    }

    /// <summary>"GREEDY" (case-insensitive) → <see cref="Mode.Greedy"/>; anything else (incl. null) → Naive.</summary>
    public static Mode Parse(string? s) =>
        string.Equals("GREEDY", s, StringComparison.OrdinalIgnoreCase) ? Mode.Greedy : Mode.Naive;

    /// <summary>
    /// <paramref name="BaseExpected"/>: projected base stat at level 11 (before trait/perk mods)<br/>
    /// <paramref name="FinalExpected"/>: BaseExpected after trait/perk modifiers<br/>
    /// <paramref name="Count"/>: increases allocated to this stat<br/>
    /// <paramref name="RemainingLevels"/>: level-ups remaining before 11 (0 = already ≥ 11)
    /// </summary>
    public sealed record Expected(int BaseExpected, int FinalExpected, int Count, int RemainingLevels);

    /// <summary>
    /// Level-ups still to allocate before lv 11, including earned-but-unassigned ones
    /// (<see cref="Brother.LevelPoints"/>). Returns 0 for brothers at lv 11 or beyond.
    /// </summary>
    public static int RemainingLevels(Brother b) => RemainingLevels(b.LevelTotal, b.LevelPoints);

    /// <summary>Pure overload — computes remaining pre-lv11 level-ups from raw fields.</summary>
    public static int RemainingLevels(int levelTotal, int levelPoints)
    {
        if (levelTotal >= 11) return 0;
        return Math.Max(0, 11 - levelTotal) + Math.Max(0, levelPoints);
    }

    /// <summary>
    /// Computes the expected stat value for one stat given the current allocation.
    /// Delegates to the 5-arg overload with <c>null</c> post11Increases.
    /// </summary>
    /// <param name="b">the brother</param>
    /// <param name="stat">which stat</param>
    /// <param name="increases">pre-lv11 increase counts indexed by <see cref="Stat.Ordinal"/>; may be null (all zero)</param>
    /// <param name="mode">calculation mode</param>
    public static Expected Compute(Brother b, Stat stat, int[]? increases, Mode mode) =>
        Compute(b, stat, increases, null, mode);

    /// <summary>
    /// Computes the expected stat value normalised to lv 11.
    /// For brothers at level ≤ 11: projects forward using <paramref name="increases"/>
    /// (random-roll phase). For brothers beyond level 11: strips the post-lv11 deterministic
    /// gains recorded in <paramref name="post11Increases"/> to produce the lv-11-comparable baseline.
    /// </summary>
    public static Expected Compute(Brother b, Stat stat, int[]? increases, int[]? post11Increases, Mode mode)
    {
        var remaining = RemainingLevels(b);
        var currentBase = b.Stats[stat.StatIndex];

        if (remaining == 0)
        {
            var p11count = post11Increases != null && stat.Ordinal < post11Increases.Length
                ? post11Increases[stat.Ordinal] : 0;
            var lv11Base = currentBase - p11count;
            var bd = StatModifierService.Instance.ComputeWithBase(b, stat, lv11Base);
            return new Expected(lv11Base, bd.FinalValue, p11count, 0);
        }

        var count = increases != null && stat.Ordinal < increases.Length
            ? increases[stat.Ordinal] : 0;

        if (count == 0)
        {
            var bd = StatModifierService.Instance.ComputeWithBase(b, stat, currentBase);
            return new Expected(currentBase, bd.FinalValue, 0, remaining);
        }

        int baseExpected;
        if (mode == Mode.Greedy && IsFullyAllocated(increases, remaining) && WithinRemaining(increases, remaining))
        {
            baseExpected = GreedyBase(b, stat, increases!, currentBase, remaining);
        }
        else
        {
            baseExpected = NaiveBase(b, stat, currentBase, count);
        }

        var bdFinal = StatModifierService.Instance.ComputeWithBase(b, stat, baseExpected);
        return new Expected(baseExpected, bdFinal.FinalValue, count, remaining);
    }

    /// <summary>
    /// Auto-assigns increases for a brother based on the role's stat priorities.
    /// P1 stats are filled first (split equally with alphabetic tiebreaker when > 3),
    /// then the remaining budget cascades to P2 and P3.
    /// </summary>
    /// <returns>int[8] increase allocations indexed by <see cref="Stat.Ordinal"/>, summing to 3×remaining</returns>
    public static int[] AutoAssignByRole(Brother b, Role? role) =>
        AutoAssignByRole(role, RemainingLevels(b));

    public static int[] AutoAssignByRole(Role? role, int effectiveRemaining)
    {
        if (effectiveRemaining == 0 || role == null) return new int[N];
        return AllocateByRoleTiers(role.Priority, effectiveRemaining);
    }

    /// <summary>
    /// Auto-assigns post-lv11 increases for a brother based on the role's stat priorities.
    /// Same algorithm as <see cref="AutoAssignByRole"/> but uses the post-lv11 budget:
    /// <c>3 × (levelTotal − 11)</c> total, per-stat cap = <c>levelTotal − 11</c>.
    /// </summary>
    public static int[] AutoAssignPost11ByRole(Brother b, Role? role)
    {
        var postLevels = Math.Max(0, b.LevelTotal - 11);
        if (postLevels == 0 || role == null) return new int[N];
        return AllocateByRoleTiers(role.Priority, postLevels);
    }

    /// <summary>
    /// Core role-priority tier-allocation algorithm.
    /// P1 stats are filled first (up to perStatCap each), then P2, then P3.
    /// When the budget can't fully cover a tier, it is split equally with alphabetical tiebreaker.
    /// </summary>
    /// <param name="rolePriority">per-stat priority (1–3), indexed by <see cref="Stat.Ordinal"/>; may be null</param>
    /// <param name="perStatCap">maximum increases per stat (= remaining pre-11 levels or post-11 levels)</param>
    /// <returns>int[8] allocations indexed by <see cref="Stat.Ordinal"/>, summing to 3 × perStatCap</returns>
    internal static int[] AllocateByRoleTiers(int[]? rolePriority, int perStatCap)
    {
        var result = new int[N];
        var budget = 3 * perStatCap;
        for (int tier = 1; tier <= 3 && budget > 0; tier++)
        {
            var tierStats = StatsAtTier(rolePriority, tier);
            if (tierStats.Count == 0) continue;
            var count = tierStats.Count;
            if ((long)count * perStatCap <= budget)
            {
                // Can max out all stats in this tier.
                foreach (var s in tierStats) result[s.Ordinal] = perStatCap;
                budget -= count * perStatCap;
            }
            else
            {
                // Split budget equally; alphabetical tiebreaker for the remainder.
                tierStats.Sort((x, y) => string.CompareOrdinal(x.DisplayName, y.DisplayName));
                var each = budget / count;
                var extra = budget % count;
                for (var i = 0; i < count; i++)
                {
                    result[tierStats[i].Ordinal] = each + (i < extra ? 1 : 0);
                }
                budget = 0;
            }
        }
        return result;
    }

    /// <summary>
    /// Returns all stats whose effective priority equals the given tier.
    /// Stats absent from (or out-of-bounds in) <paramref name="rolePriority"/> default to priority 3.
    /// </summary>
    internal static List<Stat> StatsAtTier(int[]? rolePriority, int tier)
    {
        var tierStats = new List<Stat>();
        foreach (var s in Stats)
        {
            var ord = s.Ordinal;
            var prio = rolePriority != null && ord < rolePriority.Length ? rolePriority[ord] : 3;
            if (prio == tier) tierStats.Add(s);
        }
        return tierStats;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Expected value of one roll for the given range: (min + max) / 2.0.</summary>
    internal static double MeanRoll(StatPotentialCalculator.Range r) => (r.Min + r.Max) / 2.0;

    /// <summary>Deviation of an actual roll value from the stat's expected mean: rollValue − E[roll].</summary>
    internal static double RollDeviation(int rollValue, StatPotentialCalculator.Range r) =>
        rollValue - MeanRoll(r);

    /// <summary>A stat is "fixed" (always chosen at every level-up) when all its rolls are allocated.</summary>
    internal static bool IsFixed(int rolls, int levelsRemaining) => rolls == levelsRemaining;

    /// <summary>A stat is "flexible" when some but not all levels are allocated to it.</summary>
    internal static bool IsFlexible(int rolls, int levelsRemaining) =>
        rolls > 0 && rolls < levelsRemaining;

    /// <summary>Number of flexible pick slots per level-up (3 picks minus the fixed-stat count).</summary>
    internal static int FlexibleSlots(int fixedCount) => 3 - fixedCount;

    private static int NaiveBase(Brother b, Stat stat, int currentBase, int count)
    {
        var r = StatPotentialCalculator.RangeForStars(stat.StatIndex, b.Stars[stat.StarIndex])!;
        return (int)Math.Ceiling(currentBase + count * MeanRoll(r));
    }

    private static int GreedyBase(Brother b, Stat stat, int[] increases, int currentBase, int remaining)
    {
        var stars = new int[N];
        for (var i = 0; i < N; i++) stars[i] = b.Stars[Stats[i].StarIndex];
        var priority = new int[N];
        Array.Fill(priority, 3);
        try
        {
            var gains = ExpectedGains(remaining, stars, increases, priority);
            return (int)Math.Ceiling(currentBase + gains[0][stat.Ordinal]);
        }
        catch (TooManyFlexibleStatsException)
        {
            return NaiveBase(b, stat, currentBase, increases[stat.Ordinal]);
        }
    }

    internal static bool IsFullyAllocated(int[]? increases, int remaining)
    {
        if (increases == null || increases.Length < N) return false;
        var sum = 0;
        foreach (var v in increases) sum += v;
        return sum == 3 * remaining;
    }

    /// <summary>
    /// True when every per-stat count fits within <paramref name="remaining"/>. A stale allocation can
    /// exceed it — e.g. rolls assigned while a pending Gifted perk temporarily raised the
    /// budget, then the perk plan was reverted — which would otherwise crash the greedy
    /// algorithm's internal validation.
    /// </summary>
    private static bool WithinRemaining(int[]? increases, int remaining)
    {
        if (increases == null) return true;
        foreach (var v in increases) if (v > remaining) return false;
        return true;
    }

    public sealed class TooManyFlexibleStatsException(int k)
        : Exception($"Flexible stat count {k} exceeds limit of 6 — computation would be too expensive.");
}
