using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.Core.Tests.Services;

// Transcribed assertion-by-assertion from JAVATEST/service/AnnotationServiceHelpersTest.java.
// Everything targets internal static seams (Java package-private → internal). EnumMap/Map
// params → List<KeyValuePair<Stat,int>> in Stat.Values order (declaration-order parity).
public class AnnotationServiceHelpersTests
{
    // ---- A1: statDeltas ----------------------------------------------------

    [Fact]
    public void StatDeltas_ReturnsPositiveDeltasOnly()
    {
        var ob = MakeStats(50, 40, 0, 0, 0, 0, 0, 0);
        var nb = MakeStats(53, 38, 0, 0, 0, 0, 0, 0); // health +3, resolve -2
        var d = AnnotationService.StatDeltas(ob, nb);
        Assert.Equal(3, Delta(d, Stat.Health));
        Assert.DoesNotContain(d, kv => kv.Key == Stat.Resolve); // negative delta excluded
    }

    [Fact]
    public void StatDeltas_EmptyWhenNoPositiveDeltas()
    {
        var ob = MakeStats(60, 50, 0, 0, 0, 0, 0, 0);
        var nb = MakeStats(60, 50, 0, 0, 0, 0, 0, 0);
        Assert.Empty(AnnotationService.StatDeltas(ob, nb));
    }

    [Fact]
    public void StatDeltas_AllStatsDetected()
    {
        int[] old = { 50, 40, 80, 60, 40, 20, 15, 70 };
        int[] nw  = { 51, 41, 81, 61, 41, 21, 16, 71 };
        var ob = MakeFromArray(old);
        var nb = MakeFromArray(nw);
        var d = AnnotationService.StatDeltas(ob, nb);
        foreach (var s in Stat.Values)
        {
            Assert.Equal(1, Delta(d, s));
        }
    }

    // ---- A2: raisesLowerBound ----------------------------------------------

    [Fact]
    public void RaisesLowerBound_MeleeSkill0Stars_Delta2_Is1()
    {
        // MELEE_SKILL (statIndex=3), 0★: max=3 → ceil(2/3)=1
        Assert.Equal(1, AnnotationService.RaisesLowerBound(2, Stat.MeleeSkill.StatIndex, 0));
    }

    [Fact]
    public void RaisesLowerBound_Health0Stars_Delta5_Is2()
    {
        // HEALTH (statIndex=0), 0★: max=4 → ceil(5/4)=2
        Assert.Equal(2, AnnotationService.RaisesLowerBound(5, Stat.Health.StatIndex, 0));
    }

    [Fact]
    public void RaisesLowerBound_ExactMax_Is1()
    {
        // MELEE_SKILL, 0★: max=3 → ceil(3/3)=1
        Assert.Equal(1, AnnotationService.RaisesLowerBound(3, Stat.MeleeSkill.StatIndex, 0));
    }

    [Fact]
    public void RaisesLowerBound_MaxPlusOne_Is2()
    {
        // MELEE_SKILL, 0★: max=3 → ceil(4/3)=2
        Assert.Equal(2, AnnotationService.RaisesLowerBound(4, Stat.MeleeSkill.StatIndex, 0));
    }

    // ---- A3: addedPerks ----------------------------------------------------

    [Fact]
    public void AddedPerks_ReturnsNewPerksOnly()
    {
        var ob = MakeWithPerks("AAAA", "BBBB");
        var nb = MakeWithPerks("AAAA", "BBBB", "CCCC");
        Assert.Equal(new List<string> { "CCCC" }, AnnotationService.AddedPerks(ob, nb));
    }

    [Fact]
    public void AddedPerks_EmptyWhenNoneAdded()
    {
        var ob = MakeWithPerks("AAAA");
        var nb = MakeWithPerks("AAAA");
        Assert.Empty(AnnotationService.AddedPerks(ob, nb));
    }

    [Fact]
    public void AddedPerks_AllNewWhenObHasNone()
    {
        var ob = MakeWithPerks();
        var nb = MakeWithPerks("XXXX", "YYYY");
        Assert.Equal(2, AnnotationService.AddedPerks(ob, nb).Count);
    }

    // ---- A4: isPost11Reconcile --------------------------------------------

    [Fact]
    public void IsPost11Reconcile_TrueWhenBothPast11()
    {
        Assert.True(AnnotationService.IsPost11Reconcile(MakeWithLevel(12), MakeWithLevel(13)));
    }

    [Fact]
    public void IsPost11Reconcile_TrueWhenObExactly11()
    {
        Assert.True(AnnotationService.IsPost11Reconcile(MakeWithLevel(11), MakeWithLevel(12)));
    }

    [Fact]
    public void IsPost11Reconcile_FalseWhenObBelow11()
    {
        // mixed 10→12 crossing: treated as pre-11 by design
        Assert.False(AnnotationService.IsPost11Reconcile(MakeWithLevel(10), MakeWithLevel(12)));
    }

    [Fact]
    public void IsPost11Reconcile_FalseWhenNbExactly11()
    {
        // ob=11, nb=11: nb must be > 11
        Assert.False(AnnotationService.IsPost11Reconcile(MakeWithLevel(11), MakeWithLevel(11)));
    }

    // ---- A5: findMatch -----------------------------------------------------

    [Fact]
    public void FindMatch_ReturnsMatchingBrother()
    {
        var a = MakeWithFp("FP_A");
        var b = MakeWithFp("FP_B");
        Assert.Same(a, AnnotationService.FindMatch(new List<Brother> { a, b }, "FP_A"));
    }

    [Fact]
    public void FindMatch_ReturnsNullWhenNotFound()
    {
        Assert.Null(AnnotationService.FindMatch(new List<Brother> { MakeWithFp("FP_A") }, "FP_MISSING"));
    }

    [Fact]
    public void FindMatch_ReturnsNullForEmptyList()
    {
        Assert.Null(AnnotationService.FindMatch([], "FP_ANY"));
    }

    // ---- A6: levelsAssigned -----------------------------------------------

    [Fact]
    public void LevelsAssigned_OneLevelDecreased()
    {
        Assert.Equal(1, AnnotationService.LevelsAssigned(MakeWithPoints(2), MakeWithPoints(1), false));
    }

    [Fact]
    public void LevelsAssigned_NormalPlusGifted()
    {
        Assert.Equal(2, AnnotationService.LevelsAssigned(MakeWithPoints(1), MakeWithPoints(0), true));
    }

    [Fact]
    public void LevelsAssigned_GiftedOnlyNoLevelDecrease()
    {
        Assert.Equal(1, AnnotationService.LevelsAssigned(MakeWithPoints(0), MakeWithPoints(0), true));
    }

    [Fact]
    public void LevelsAssigned_NeverNegativeWhenPointsIncrease()
    {
        // Defensive: levelPoints went up (shouldn't happen, but Math.Max guards it)
        Assert.Equal(0, AnnotationService.LevelsAssigned(MakeWithPoints(1), MakeWithPoints(2), false));
    }

    // ---- B1: reconcilePost11 ----------------------------------------------

    [Fact]
    public void ReconcilePost11_EmptyDeltasReturnsFalse()
    {
        var r = AnnotationService.ReconcilePost11(new BrotherAnnotation("FP"), []);
        Assert.False(r.Changed);
        Assert.Null(r.UpdatedPost11Increases);
    }

    [Fact]
    public void ReconcilePost11_RecordsDeltas()
    {
        var a = new BrotherAnnotation("FP");
        var deltas = new List<KeyValuePair<Stat, int>> { new(Stat.Health, 1), new(Stat.MeleeSkill, 1) };
        var r = AnnotationService.ReconcilePost11(a, deltas);
        Assert.True(r.Changed);
        Assert.Equal(1, r.UpdatedPost11Increases![Stat.Health.Ordinal]);
        Assert.Equal(1, r.UpdatedPost11Increases[Stat.MeleeSkill.Ordinal]);
    }

    [Fact]
    public void ReconcilePost11_AccumulatesOnExistingPost11()
    {
        var a = new BrotherAnnotation("FP");
        a.Post11Increases = new int[Stat.Values.Length];
        a.Post11Increases[Stat.Health.Ordinal] = 2;
        var r = AnnotationService.ReconcilePost11(a, [new(Stat.Health, 1)]);
        Assert.Equal(3, r.UpdatedPost11Increases![Stat.Health.Ordinal]);
    }

    [Fact]
    public void ReconcilePost11_DoesNotMutateAnnotationArray()
    {
        var a = new BrotherAnnotation("FP");
        a.Post11Increases = new int[Stat.Values.Length];
        a.Post11Increases[Stat.Health.Ordinal] = 1;
        AnnotationService.ReconcilePost11(a, [new(Stat.Resolve, 1)]);
        Assert.Equal(1, a.Post11Increases[Stat.Health.Ordinal]);  // original unchanged
        Assert.Equal(0, a.Post11Increases[Stat.Resolve.Ordinal]); // original unchanged
    }

    // ---- B2: consumePlannedIncreases --------------------------------------

    [Fact]
    public void ConsumePlanned_NullStatIncreasesReturnsFalse()
    {
        var r = AnnotationService.ConsumePlannedIncreases(
            new BrotherAnnotation("FP"), MakeFromArray(new int[8]), [new(Stat.Health, 3)]);
        Assert.False(r.Changed);
        Assert.Null(r.UpdatedStatIncreases);
    }

    [Fact]
    public void ConsumePlanned_ConsumesLowerBoundProof()
    {
        // HEALTH 0★: delta=5, max=4 → ceil(5/4)=2, planned=3 → consume 2, leave 1
        var a = AnnotationWithPlan(Stat.Health, 3);
        var r = AnnotationService.ConsumePlannedIncreases(
            a, MakeFromArray(new int[8]), [new(Stat.Health, 5)]);
        Assert.True(r.Changed);
        Assert.Equal(2, Delta(r.ConsumedIncreases, Stat.Health));
        Assert.Equal(1, r.UpdatedStatIncreases![Stat.Health.Ordinal]);
        Assert.Contains(Stat.Health, r.AdjustedStats);
    }

    [Fact]
    public void ConsumePlanned_CapsAtPlanned()
    {
        // HEALTH 0★: delta=12, ceil(12/4)=3, planned=2 → consume 2, leave 0
        var a = AnnotationWithPlan(Stat.Health, 2);
        var r = AnnotationService.ConsumePlannedIncreases(
            a, MakeFromArray(new int[8]), [new(Stat.Health, 12)]);
        Assert.Equal(2, Delta(r.ConsumedIncreases, Stat.Health));
        Assert.Equal(0, r.UpdatedStatIncreases![Stat.Health.Ordinal]);
    }

    [Fact]
    public void ConsumePlanned_SkipsDeltaWithZeroPlanned()
    {
        var a = new BrotherAnnotation("FP");
        a.StatIncreases = new int[Stat.Values.Length]; // all zeros
        var r = AnnotationService.ConsumePlannedIncreases(
            a, MakeFromArray(new int[8]), [new(Stat.Health, 3)]);
        Assert.False(r.Changed);
    }

    [Fact]
    public void ConsumePlanned_DoesNotMutateAnnotationArray()
    {
        var a = AnnotationWithPlan(Stat.Health, 5);
        var snapshot = (int[])a.StatIncreases!.Clone();
        AnnotationService.ConsumePlannedIncreases(a, MakeFromArray(new int[8]), [new(Stat.Health, 4)]);
        Assert.Equal(snapshot, a.StatIncreases);
    }

    [Fact]
    public void ConsumePlanned_MultipleStats()
    {
        // HEALTH 0★: delta=4 → ceil(4/4)=1, planned=2 → consume 1, leave 1
        // MELEE_SKILL 0★: delta=3 → ceil(3/3)=1, planned=1 → consume 1, leave 0
        var a = new BrotherAnnotation("FP");
        a.StatIncreases = new int[Stat.Values.Length];
        a.StatIncreases[Stat.Health.Ordinal] = 2;
        a.StatIncreases[Stat.MeleeSkill.Ordinal] = 1;
        var deltas = new List<KeyValuePair<Stat, int>> { new(Stat.Health, 4), new(Stat.MeleeSkill, 3) };
        var r = AnnotationService.ConsumePlannedIncreases(a, MakeFromArray(new int[8]), deltas);
        Assert.True(r.Changed);
        Assert.Equal(2, r.AdjustedStats.Count);
        Assert.Equal(1, r.UpdatedStatIncreases![Stat.Health.Ordinal]);
        Assert.Equal(0, r.UpdatedStatIncreases[Stat.MeleeSkill.Ordinal]);
    }

    // ---- helpers -----------------------------------------------------------

    /// <summary>stats[] in save-file order: [0]=HP [1]=Res [2]=Fat [3]=MS [4]=RS [5]=MD [6]=RD [7]=Init</summary>
    private static Brother MakeStats(int hp, int res, int fat, int ms,
                                     int rs, int md, int rd, int init) =>
        MakeFromArray([hp, res, fat, ms, rs, md, rd, init]);

    private static Brother MakeFromArray(int[] stats)
    {
        var b = new Brother();
        Array.Copy(stats, b.Stats, stats.Length);
        return b;
    }

    private static Brother MakeWithPerks(params string[] perkIds)
    {
        var b = new Brother();
        foreach (var id in perkIds) b.PerkIds.Add(id);
        return b;
    }

    private static Brother MakeWithLevel(int levelTotal)
    {
        return new Brother { LevelTotal = levelTotal };
    }

    private static Brother MakeWithFp(string fp)
    {
        return new Brother { Fingerprint = fp };
    }

    private static Brother MakeWithPoints(int levelPoints)
    {
        return new Brother { LevelPoints = levelPoints };
    }

    private static BrotherAnnotation AnnotationWithPlan(Stat s, int count)
    {
        var a = new BrotherAnnotation("FP");
        a.StatIncreases = new int[Stat.Values.Length];
        a.StatIncreases[s.Ordinal] = count;
        return a;
    }

    private static int Delta(IReadOnlyList<KeyValuePair<Stat, int>> m, Stat s)
    {
        foreach (var kv in m) if (kv.Key == s) return kv.Value;
        throw new InvalidOperationException($"no delta recorded for {s.DisplayName}");
    }
}
