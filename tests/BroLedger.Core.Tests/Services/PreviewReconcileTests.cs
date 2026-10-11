using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.Core.Tests.Services;

// Transcribed assertion-by-assertion from JAVATEST/service/PreviewReconcileTest.java.
// @TempDir → per-test GUID dir; reflection singleton reset → ResetForTest().
// LevelUpEvent.StatDeltas/ConsumedIncreases are List<KeyValuePair<Stat,int>> in
// Stat.Values order — Java Map equality becomes ordered sequence equality here.
[Collection("EnvVar-BroLedgerDataDir")]
public class PreviewReconcileTests : IDisposable
{
    private readonly string Tmp;
    private AnnotationService Service;
    private string SavePath;

    public PreviewReconcileTests()
    {
        Tmp = Path.Combine(Path.GetTempPath(), "broledger-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Tmp);
        SavePath = Path.Combine(Tmp, "test.sav");
        File.WriteAllText(SavePath, "dummy");
        AnnotationService.ResetForTest();
        Service = AnnotationService.Instance;
        Service.LoadFor(SavePath);
    }

    public void Dispose()
    {
        AnnotationService.ResetForTest();
        if (Directory.Exists(Tmp)) Directory.Delete(Tmp, recursive: true);
    }

    // Java: savePath.resolveSibling(savePath.getFileName() + ".broledger.json")
    private string PrimaryFile =>
        Path.Combine(Path.GetDirectoryName(SavePath)!, Path.GetFileName(SavePath) + ".broledger.json");

    // ---- helpers -----------------------------------------------------------

    private static Brother MakeBrother(string? fp, string? name, int levelPoints,
                                       int[] stats, int[] stars)
    {
        var b = new Brother
        {
            Fingerprint = fp,
            Name = name,
            LevelPoints = levelPoints,
        };
        Array.Copy(stats, b.Stats, stats.Length);
        Array.Copy(stars, b.Stars, stars.Length);
        return b;
    }

    private static int[] StatsWithHP(int hp)
    {
        var s = new int[8];
        s[Stat.Health.StatIndex] = hp;
        return s;
    }

    private static int[] StatsWithMS(int ms)
    {
        var s = new int[8];
        s[Stat.MeleeSkill.StatIndex] = ms;
        return s;
    }

    private static int Get(IReadOnlyList<KeyValuePair<Stat, int>> m, Stat s, int def = 0)
    {
        foreach (var kv in m) if (kv.Key == s) return kv.Value;
        return def;
    }

    // ---- previewReconcile matches reconcileOnReload ------------------------

    [Fact]
    public void PreviewReconcile_ProducesNoEvents_WhenNoChange()
    {
        var stats = StatsWithHP(60);
        var ob = MakeBrother("A", "Gorm", 1, stats, new int[8]);
        var nb = MakeBrother("A", "Gorm", 1, (int[])stats.Clone(), new int[8]);

        var planned = new Dictionary<string, int[]>();
        var post11 = new Dictionary<string, int[]>();
        var events = Service.PreviewReconcile(
            new List<Brother> { ob }, new List<Brother> { nb }, planned, post11);

        Assert.Empty(events);
    }

    [Fact]
    public void PreviewReconcile_ProducesLevelUpEvent()
    {
        int[] statsOld = StatsWithHP(60);
        int[] statsNew = StatsWithHP(63);

        var ob = MakeBrother("B", "Torben", 1, statsOld, new int[8]);
        var nb = MakeBrother("B", "Torben", 0, statsNew, new int[8]);

        var planned = new Dictionary<string, int[]>();
        var post11 = new Dictionary<string, int[]>();

        var events = Service.PreviewReconcile(
            new List<Brother> { ob }, new List<Brother> { nb }, planned, post11);

        Assert.Single(events);
        var e = events[0];
        Assert.Equal("Torben", e.Name);
        Assert.Equal(1, e.LevelsAssigned);
        Assert.Equal(3, Get(e.StatDeltas, Stat.Health));
        Assert.False(e.Adjusted); // no planned increases supplied
    }

    [Fact]
    public void PreviewReconcile_ConsumesPlannedIncreases()
    {
        int[] statsOld = StatsWithHP(60);
        int[] statsNew = StatsWithHP(63);

        var ob = MakeBrother("C", "Wulf", 1, statsOld, new int[8]);
        var nb = MakeBrother("C", "Wulf", 0, statsNew, new int[8]);

        var planned = new Dictionary<string, int[]>();
        var inc = new int[Stat.Values.Length];
        inc[Stat.Health.Ordinal] = 2;
        planned["C"] = (int[])inc.Clone();

        var post11 = new Dictionary<string, int[]>();

        var events = Service.PreviewReconcile(
            new List<Brother> { ob }, new List<Brother> { nb }, planned, post11);

        Assert.Single(events);
        Assert.True(events[0].Adjusted);
        // HP delta=3 at 0★: ceil(3/4)=1 raise proved → consume 1
        Assert.Equal(1, Get(events[0].ConsumedIncreases, Stat.Health));
        // planned map updated: 2-1=1 remaining
        var updated = planned["C"];
        Assert.NotNull(updated);
        Assert.Equal(1, updated[Stat.Health.Ordinal]);
    }

    [Fact]
    public void PreviewReconcile_MatchesReconcileOnReload()
    {
        int[] statsOld = StatsWithMS(50);
        int[] statsNew = StatsWithMS(52);

        var ob = MakeBrother("D", "Sigrid", 1, statsOld, new int[8]);
        var nb = MakeBrother("D", "Sigrid", 0, statsNew, new int[8]);

        // Set planned increases in the live service
        var inc = new int[Stat.Values.Length];
        inc[Stat.MeleeSkill.Ordinal] = 2;
        Service.SetStatIncreases("D", (int[])inc.Clone());

        // Run the live reconcile (mutates the store + disk)
        var liveEvents = Service.ReconcileOnReload(new List<Brother> { ob }, new List<Brother> { nb });

        // Reset singleton and run previewReconcile with same inputs
        AnnotationService.ResetForTest();
        Service = AnnotationService.Instance;
        Service.LoadFor(SavePath);

        var planned = new Dictionary<string, int[]> { ["D"] = (int[])inc.Clone() };
        var post11 = new Dictionary<string, int[]>();

        var previewEvents = Service.PreviewReconcile(
            new List<Brother> { ob }, new List<Brother> { nb }, planned, post11);

        Assert.Equal(liveEvents.Count, previewEvents.Count);
        var live = liveEvents[0];
        var preview = previewEvents[0];

        Assert.Equal(live.Name, preview.Name);
        Assert.Equal(live.Adjusted, preview.Adjusted);
        Assert.Equal(live.LevelsAssigned, preview.LevelsAssigned);
        Assert.Equal(live.StatDeltas, preview.StatDeltas);
        Assert.Equal(live.AdjustedStats, preview.AdjustedStats);
        Assert.Equal(live.ConsumedIncreases, preview.ConsumedIncreases);
        Assert.Equal(live.Post11, preview.Post11);
    }

    [Fact]
    public void PreviewReconcile_DoesNotWriteToDisk()
    {
        int[] statsOld = StatsWithHP(60);
        int[] statsNew = StatsWithHP(64);

        var ob = MakeBrother("E", "Otto", 1, statsOld, new int[8]);
        var nb = MakeBrother("E", "Otto", 0, statsNew, new int[8]);

        Assert.False(File.Exists(PrimaryFile), "no annotation file should exist yet");

        var planned = new Dictionary<string, int[]>();
        Service.PreviewReconcile(
            new List<Brother> { ob }, new List<Brother> { nb }, planned, new Dictionary<string, int[]>());

        // No disk write should have occurred
        Assert.False(File.Exists(PrimaryFile), "previewReconcile must not write to disk");
    }

    // ---- carry-forward across transitions ---------------------------------

    [Fact]
    public void PreviewReconcile_CarryForwardAcrossTwoTransitions()
    {
        // Transition 1: HP +3 → consumes 1 planned HP raise
        // Transition 2: HP +2 → consumes 1 more planned HP raise
        // (Java test's dead intermediate brothers dropped — net effect: two 1→0 transitions)

        var s0 = StatsWithHP(60);
        var s1 = StatsWithHP(63);
        var s2 = StatsWithHP(65);

        var b0 = MakeBrother("F", "Brunhilde", 1, s0, new int[8]);
        var b1 = MakeBrother("F", "Brunhilde", 0, s1, new int[8]);
        var b1real = MakeBrother("F", "Brunhilde", 1, s1, new int[8]);
        var b2real = MakeBrother("F", "Brunhilde", 0, s2, new int[8]);

        var planned = new Dictionary<string, int[]>();
        var inc = new int[Stat.Values.Length];
        inc[Stat.Health.Ordinal] = 3;
        planned["F"] = (int[])inc.Clone();
        var post11 = new Dictionary<string, int[]>();

        // T1: b0 → b1
        Service.PreviewReconcile(new List<Brother> { b0 }, new List<Brother> { b1 }, planned, post11);
        var afterT1 = planned.GetValueOrDefault("F", new int[Stat.Values.Length])[Stat.Health.Ordinal];

        // T2: b1real → b2real (levelPoints: 1→0)
        Service.PreviewReconcile(new List<Brother> { b1real }, new List<Brother> { b2real }, planned, post11);
        var afterT2 = planned.GetValueOrDefault("F", new int[Stat.Values.Length])[Stat.Health.Ordinal];

        // After T1: ceil(3/4)=1 consumed → 3-1=2
        Assert.Equal(2, afterT1);
        // After T2: ceil(2/4)=1 consumed → 2-1=1
        Assert.Equal(1, afterT2);
    }

    // ---- readAnnotationMaps ------------------------------------------------

    [Fact]
    public void ReadAnnotationMaps_EmptyForNullPath()
    {
        var p = new Dictionary<string, int[]>();
        var q = new Dictionary<string, int[]>();
        AnnotationService.ReadAnnotationMaps(null, p, q);
        Assert.Empty(p);
        Assert.Empty(q);
    }

    [Fact]
    public void ReadAnnotationMaps_EmptyForAbsentFile()
    {
        var absent = Path.Combine(Tmp, "absent.json");
        var p = new Dictionary<string, int[]>();
        var q = new Dictionary<string, int[]>();
        AnnotationService.ReadAnnotationMaps(absent, p, q);
        Assert.Empty(p);
        Assert.Empty(q);
    }

    [Fact]
    public void ReadAnnotationMaps_LoadsPlannedIncreases()
    {
        // Write a realistic annotations file
        var annotPath = Path.Combine(Tmp, "snap.broledger.json");
        // Ordinals: HEALTH=0, RESOLVE=1, FATIGUE=2, INITIATIVE=3, MELEE_SKILL=4, …
        // statIncreases[0]=3 (HEALTH), statIncreases[4]=2 (MELEE_SKILL)
        File.WriteAllText(annotPath,
            "{\"version\":1,\"annotations\":" +
            "{\"FP001\":{\"statIncreases\":[3,0,0,0,2,0,0,0]}," +
            "\"FP002\":{\"post11Increases\":[1,0,0,0,0,0,0,0]}" +
            "}}");

        var planned = new Dictionary<string, int[]>();
        var post11 = new Dictionary<string, int[]>();
        AnnotationService.ReadAnnotationMaps(annotPath, planned, post11);

        Assert.NotNull(planned.GetValueOrDefault("FP001"));
        Assert.Equal(3, planned["FP001"][Stat.Health.Ordinal]);
        Assert.Equal(2, planned["FP001"][Stat.MeleeSkill.Ordinal]);

        Assert.NotNull(post11.GetValueOrDefault("FP002"));
        Assert.Equal(1, post11["FP002"][Stat.Health.Ordinal]);

        // FP002 has no statIncreases → not in planned
        Assert.False(planned.ContainsKey("FP002"));
    }

    [Fact]
    public void ReadAnnotationMaps_CorruptFileIsIgnored()
    {
        var corrupt = Path.Combine(Tmp, "corrupt.json");
        File.WriteAllText(corrupt, "{ not json");

        var p = new Dictionary<string, int[]>();
        var q = new Dictionary<string, int[]>();
        AnnotationService.ReadAnnotationMaps(corrupt, p, q);  // must not throw
        Assert.Empty(p);
        Assert.Empty(q);
    }

    [Fact]
    public void ReadAnnotationMaps_DoesNotShareArrayWithOriginal()
    {
        var annotPath = Path.Combine(Tmp, "snap2.broledger.json");
        File.WriteAllText(annotPath,
            "{\"version\":1,\"annotations\":" +
            "{\"FP003\":{\"statIncreases\":[1,0,0,0,0,0,0,0]}" +
            "}}");

        var planned = new Dictionary<string, int[]>();
        AnnotationService.ReadAnnotationMaps(annotPath, planned, new Dictionary<string, int[]>());

        var arr = planned["FP003"];
        Assert.NotNull(arr);
        var original = arr[Stat.Health.Ordinal];
        arr[Stat.Health.Ordinal] = 99; // mutate the returned copy

        // Re-read and confirm the file wasn't affected (and the map returns a fresh copy)
        var planned2 = new Dictionary<string, int[]>();
        AnnotationService.ReadAnnotationMaps(annotPath, planned2, new Dictionary<string, int[]>());
        Assert.Equal(original, planned2["FP003"][Stat.Health.Ordinal]);
    }
}
