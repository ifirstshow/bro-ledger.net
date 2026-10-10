using BroLedger.Core.Models;
using BroLedger.Core.Services;
using Xunit;

namespace BroLedger.Core.Tests.Services;

// Transcribed assertion-by-assertion from JAVATEST/service/AppConfigTest.java.
// Seams: @TempDir → per-test GUID dir; fresh() reflection bypass → ResetForTest() + Instance.
public class AppConfigTests : IDisposable
{
    private readonly string Tmp;

    public AppConfigTests()
    {
        Tmp = Path.Combine(Path.GetTempPath(), "broledger-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Tmp);
    }

    public void Dispose()
    {
        if (Directory.Exists(Tmp)) Directory.Delete(Tmp, recursive: true);
        AppConfig.ResetForTest();
    }

    // Java fresh(): reflection-instantiates a clean AppConfig; here the singleton is reset instead.
    private static AppConfig Fresh()
    {
        AppConfig.ResetForTest();
        return AppConfig.Instance;
    }

    [Fact]
    public void DefaultValues()
    {
        var cfg = Fresh();
        Assert.Null(cfg.LastSaveFilePath);
        Assert.Null(cfg.GameArtDirectory);
        Assert.Equal(1800, cfg.WindowWidth);
        Assert.Equal(1320, cfg.WindowHeight);
        Assert.False(cfg.ReplayCaptureEnabled, "replayCaptureEnabled must default to false");
    }

    [Fact]
    public void RoundTrip()
    {
        var file = Path.Combine(Tmp, "config.json");

        var written = Fresh();
        written.LastSaveFilePath = "/saves/my.sav";
        written.GameArtDirectory = "/art";
        written.WindowWidth = 1600;
        written.WindowHeight = 1000;
        written.ReplayCaptureEnabled = true;
        written.SaveTo(file);

        var read = Fresh();
        read.LoadFrom(file);

        Assert.Equal("/saves/my.sav", read.LastSaveFilePath);
        Assert.Equal("/art", read.GameArtDirectory);
        Assert.Equal(1600, read.WindowWidth);
        Assert.Equal(1000, read.WindowHeight);
        Assert.True(read.ReplayCaptureEnabled, "replayCaptureEnabled must round-trip");
    }

    [Fact]
    public void LoadMissingFileKeepsDefaults()
    {
        var cfg = Fresh();
        cfg.LoadFrom(Path.Combine(Tmp, "nonexistent.json"));
        Assert.Equal(1800, cfg.WindowWidth);
        Assert.Null(cfg.LastSaveFilePath);
    }

    [Fact]
    public void PartialJsonPreservesDefaults()
    {
        // File contains only windowWidth — other fields should stay at defaults.
        var file = Path.Combine(Tmp, "partial.json");
        File.WriteAllText(file, "{\"windowWidth\":800}");

        var cfg = Fresh();
        cfg.LoadFrom(file);

        Assert.Equal(800, cfg.WindowWidth);
        Assert.Equal(1320, cfg.WindowHeight);  // default preserved
        Assert.Null(cfg.LastSaveFilePath);
    }

    [Fact]
    public void HasGameArtDirectory_FalseWhenNull()
    {
        var cfg = Fresh();
        Assert.False(cfg.HasGameArtDirectory());
    }

    [Fact]
    public void HasGameArtDirectory_FalseWhenPathDoesNotExist()
    {
        var cfg = Fresh();
        cfg.GameArtDirectory = "/does/not/exist/ever";
        Assert.False(cfg.HasGameArtDirectory());
    }

    // Java: hasGameArtDirectory_trueForExistingDirectory(@TempDir Path dir)
    [Fact]
    public void HasGameArtDirectory_TrueForExistingDirectory()
    {
        var cfg = Fresh();
        cfg.GameArtDirectory = Tmp;
        Assert.True(cfg.HasGameArtDirectory());
        Assert.Equal(Tmp, cfg.GameArtRoot());
    }

    [Fact]
    public void OrderedStats_NullConfigReturnsCanonicalOrder()
    {
        var cfg = Fresh();
        var result = cfg.OrderedStats();
        Assert.Equal(Stat.Values, result);
    }

    [Fact]
    public void OrderedStats_EmptyConfigReturnsCanonicalOrder()
    {
        var cfg = Fresh();
        cfg.OverviewColumnOrder = [];
        Assert.Equal(Stat.Values, cfg.OrderedStats());
    }

    [Fact]
    public void OrderedStats_FullReorderRespected()
    {
        var cfg = Fresh();
        // Reverse the default order
        cfg.OverviewColumnOrder =
        [
            "stat-RD", "stat-MD", "stat-RS", "stat-MS",
            "stat-Init", "stat-Fat", "stat-Res", "stat-HP"
        ];
        var result = cfg.OrderedStats();
        Assert.Equal(Stat.RangedDefense, result[0]);
        Assert.Equal(Stat.MeleeDefense, result[1]);
        Assert.Equal(Stat.RangedSkill, result[2]);
        Assert.Equal(Stat.MeleeSkill, result[3]);
        Assert.Equal(Stat.Initiative, result[4]);
        Assert.Equal(Stat.Fatigue, result[5]);
        Assert.Equal(Stat.Resolve, result[6]);
        Assert.Equal(Stat.Health, result[7]);
        Assert.Equal(8, result.Length);
    }

    [Fact]
    public void OrderedStats_PartialConfigAppendsMissingStats()
    {
        var cfg = Fresh();
        cfg.OverviewColumnOrder = ["stat-MS", "stat-HP", "other-col"];
        var result = cfg.OrderedStats();
        Assert.Equal(8, result.Length);
        Assert.Equal(Stat.MeleeSkill, result[0]);
        Assert.Equal(Stat.Health, result[1]);
        // Remaining 6 stats appended in canonical order (no duplicates)
        Assert.Equal(8, result.Distinct().Count());
    }

    [Fact]
    public void OrderedStats_UnknownEntriesIgnored()
    {
        var cfg = Fresh();
        cfg.OverviewColumnOrder = ["stat-UNKNOWN", "stat-HP", "stat-Res"];
        var result = cfg.OrderedStats();
        Assert.Equal(8, result.Length);
        Assert.Equal(Stat.Health, result[0]);
        Assert.Equal(Stat.Resolve, result[1]);
    }

    // ---- RecentShares ------------------------------------------------------

    private static ShareRecord Share(string code, string title) =>
        new(code, "token-" + code, title, "2026-07-05T12:00:00Z", 3);

    [Fact]
    public void RecordShare_CapsAtMaxDroppingOldest()
    {
        var cfg = Fresh();
        for (int i = 1; i <= AppConfig.RecentSharesMax + 1; i++)
        {
            cfg.RecordShare(Share("CODE000" + i, "Pack " + i));
        }
        Assert.Equal(AppConfig.RecentSharesMax, cfg.RecentShares!.Count);
        // Most recent first; the very first share rolled off.
        Assert.Equal("CODE0006", cfg.RecentShares[0].Code);
        Assert.True(cfg.RecentShares.All(r => r.Code != "CODE0001"));
    }

    [Fact]
    public void RecordShare_SameCodeMovesToTopWithoutDuplicate()
    {
        var cfg = Fresh();
        cfg.RecordShare(Share("CODEAAAA", "First"));
        cfg.RecordShare(Share("CODEBBBB", "Second"));
        cfg.RecordShare(Share("CODEAAAA", "First v2"));

        Assert.Equal(2, cfg.RecentShares!.Count);
        Assert.Equal("CODEAAAA", cfg.RecentShares[0].Code);
        Assert.Equal("First v2", cfg.RecentShares[0].Title);
        Assert.Equal("CODEBBBB", cfg.RecentShares[1].Code);
    }

    [Fact]
    public void RecentShares_RoundTrip()
    {
        var file = Path.Combine(Tmp, "config.json");

        var written = Fresh();
        written.RecordShare(new ShareRecord("AB3D9F2K", "cafe0123", "My Pack",
                "2026-07-05T12:00:00Z", 4));
        written.SaveTo(file);

        var read = Fresh();
        read.LoadFrom(file);

        Assert.NotNull(read.RecentShares);
        Assert.Single(read.RecentShares!);  // Java: assertEquals(1, recentShares.size())
        var r = read.RecentShares[0];
        Assert.Equal("AB3D9F2K", r.Code);
        Assert.Equal("cafe0123", r.OwnerToken);
        Assert.Equal("My Pack", r.Title);
        Assert.Equal("2026-07-05T12:00:00Z", r.UploadedAt);
        Assert.Equal(4, r.RoleCount);
        Assert.True(r.Updatable());
    }

    [Fact]
    public void RecordShare_TokenlessEntryIsNotUpdatable()
    {
        var cfg = Fresh();
        cfg.RecordShare(new ShareRecord("AB3D9F2K", null, "Old", "2026-07-05T12:00:00Z", 1));
        Assert.False(cfg.RecentShares![0].Updatable());
    }

    // ---- StatAbbrevFromColumnId ----------------------------------------------

    [Fact]
    public void StatAbbrevFromColumnId_ExtractsAbbrev()
    {
        Assert.Equal("HP", AppConfig.StatAbbrevFromColumnId("stat-HP"));
        Assert.Equal("MS", AppConfig.StatAbbrevFromColumnId("stat-MS"));
        Assert.Equal("RD", AppConfig.StatAbbrevFromColumnId("stat-RD"));
    }

    [Fact]
    public void StatAbbrevFromColumnId_NullForNonStatColumn()
    {
        Assert.Null(AppConfig.StatAbbrevFromColumnId("level"));
        Assert.Null(AppConfig.StatAbbrevFromColumnId("name"));
        Assert.Null(AppConfig.StatAbbrevFromColumnId(null));
    }

    [Fact]
    public void StatAbbrevFromColumnId_NullForEmptyAbbrev()
    {
        // "stat-" with nothing after it returns empty string (not null)
        Assert.Equal("", AppConfig.StatAbbrevFromColumnId("stat-"));
    }

    // ---- Persistence hardening -------------------------------------------------

    [Fact]
    public void UnknownFieldsInJsonAreIgnored()
    {
        // A config.json written by an older (or newer) build may contain fields
        // that the current class doesn't recognise. They must be silently skipped,
        // not cause an exception that wipes all settings.
        var file = Path.Combine(Tmp, "config.json");
        File.WriteAllText(file,
                "{\"lastSaveFilePath\":\"/my/save.sav\"," +
                "\"windowWidth\":1600," +
                "\"obsoleteField\":\"some-old-value\"," +
                "\"anotherRemovedField\":42}");

        var cfg = Fresh();
        cfg.LoadFrom(file);   // must not throw

        Assert.Equal("/my/save.sav", cfg.LastSaveFilePath);
        Assert.Equal(1600, cfg.WindowWidth);
        // Unknown fields didn't corrupt anything — other defaults intact
        Assert.Equal(1320, cfg.WindowHeight);
    }

    [Fact]
    public void CorruptFileIsPreservedAndDefaultsUsed()
    {
        // When config.json is corrupt (truncated write, encoding error, etc.),
        // loadFrom() must NOT silently keep defaults and then let the next save()
        // overwrite the file — that would permanently destroy the user's settings.
        // Instead: back the corrupt file up as config.json.bad-<ts> and use defaults.
        var file = Path.Combine(Tmp, "config.json");
        File.WriteAllText(file, "{ not valid json at all");

        var cfg = Fresh();
        cfg.LoadFrom(file);   // must not throw

        // Singleton stays at defaults
        Assert.Null(cfg.LastSaveFilePath);
        Assert.Equal(1800, cfg.WindowWidth);

        // Corrupt original was renamed away (no longer exists at original path)
        Assert.False(File.Exists(file), "Corrupt config.json should have been renamed away");

        // A .bad-* backup must exist in the same directory
        Assert.True(Directory.EnumerateFiles(Tmp, "config.json.bad-*").Any(),
                "A config.json.bad-<ts> backup must have been created");

        // A subsequent save() writes a fresh, valid file at the original path
        cfg.SaveTo(file);
        Assert.True(File.Exists(file), "saveTo() should create a fresh config.json");
        var written = File.ReadAllText(file);
        Assert.True(written.Contains("windowWidth"), "New config.json should be valid JSON");
    }

    [Fact]
    public void AtomicWriteLeavesNoTmpFile()
    {
        var file = Path.Combine(Tmp, "config.json");

        var cfg = Fresh();
        cfg.LastSaveFilePath = "/saves/test.sav";
        cfg.SaveTo(file);

        // No leftover .tmp sibling
        Assert.False(File.Exists(Path.Combine(Tmp, "config.json.tmp")),
                "saveTo() must not leave a .tmp file behind");

        // The real file is valid and round-trips
        var read = Fresh();
        read.LoadFrom(file);
        Assert.Equal("/saves/test.sav", read.LastSaveFilePath);
    }
}
