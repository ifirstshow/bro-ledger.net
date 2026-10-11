using BroLedger.Core.Models;
using BroLedger.Core.Services;
using BroLedger.Tools;

namespace BroLedger.Core.Tests.Tools;

// Transcribed assertion-by-assertion from JAVATEST/tools/ReplayAnalyzerTest.java.
// ByteArrayOutputStream+PrintStream → StringWriter; Path.of → plain string;
// Brother.stats assignment → Array.Copy into get-only array.
public class ReplayAnalyzerTests
{
    // ---- displayTs ---------------------------------------------------------

    [Fact]
    public void DisplayTs_ParsesKnownStem()
    {
        var ts = ReplayAnalyzer.DisplayTs("20260613T120000_000Z");
        Assert.True(ts.Contains("2026"), "year must appear");
        Assert.True(ts.Contains("06"), "month must appear");
        Assert.True(ts.Contains("13"), "day must appear");
        Assert.True(ts.Contains("12"), "hour must appear");
        Assert.False(ts == "20260613T120000_000Z",
                "should have been reformatted, not returned as-is");
    }

    [Fact]
    public void DisplayTs_ReturnsInputOnBadFormat()
    {
        Assert.Equal("not-a-timestamp", ReplayAnalyzer.DisplayTs("not-a-timestamp"));
    }

    [Fact]
    public void DisplayTs_HandlesNullGracefully()
    {
        var ex = Record.Exception(() => ReplayAnalyzer.DisplayTs(null));
        Assert.Null(ex);
    }

    // ---- stemOf ------------------------------------------------------------

    [Fact]
    public void StemOf_StripsExtension()
    {
        Assert.Equal("20260613T120000_000Z",
                ReplayAnalyzer.StemOf("20260613T120000_000Z.sav"));
    }

    [Fact]
    public void StemOf_NoExtension()
    {
        Assert.Equal("somefile", ReplayAnalyzer.StemOf("somefile"));
    }

    // ---- isValid -----------------------------------------------------------

    [Fact]
    public void IsValid_RejectsBrotherWithNullName()
    {
        var b = new Brother
        {
            Name = null,
            LevelTotal = 5,
            BackgroundHexId = "AABBCCDD",
        };
        Assert.False(ReplayAnalyzer.IsValid(b));
    }

    [Fact]
    public void IsValid_RejectsBrotherWithLevel0()
    {
        var b = new Brother
        {
            Name = "Gorm",
            LevelTotal = 0,
            BackgroundHexId = "AABBCCDD",
        };
        Assert.False(ReplayAnalyzer.IsValid(b));
    }

    [Fact]
    public void IsValid_RejectsBrotherWithBlankName()
    {
        var b = new Brother
        {
            Name = "   ",
            LevelTotal = 5,
            BackgroundHexId = "AABBCCDD",
        };
        Assert.False(ReplayAnalyzer.IsValid(b));
    }

    [Fact]
    public void IsValid_RejectsBrotherWithTooLongName()
    {
        var b = new Brother
        {
            Name = new string('X', 61),
            LevelTotal = 5,
            BackgroundHexId = "AABBCCDD",
        };
        Assert.False(ReplayAnalyzer.IsValid(b));
    }

    [Fact]
    public void IsValid_RejectsBrotherWithNullBackgroundHexId()
    {
        var b = new Brother
        {
            Name = "Gorm",
            LevelTotal = 5,
            BackgroundHexId = null,
        };
        Assert.False(ReplayAnalyzer.IsValid(b));
    }

    // ---- detectAnomalies ---------------------------------------------------

    [Fact]
    public void DetectAnomalies_FlagsNegativeStatDelta()
    {
        var ob = BrotherOf("A", "Sigrid", new int[8]);
        var nb = BrotherOf("A", "Sigrid", new int[8]);
        ob.Stats[Stat.MeleeSkill.StatIndex] = 50;
        nb.Stats[Stat.MeleeSkill.StatIndex] = 45; // decreased

        var anomalies = new List<string>();
        ReplayAnalyzer.DetectAnomalies([ob], [nb], anomalies);

        Assert.False(anomalies.Count == 0, "negative stat delta must be flagged");
        Assert.True(anomalies[0].Contains("MS") || anomalies[0].Contains("decreased"));
    }

    [Fact]
    public void DetectAnomalies_NoAnomalyForPositiveDelta()
    {
        var ob = BrotherOf("B", "Gorm", new int[8]);
        var nb = BrotherOf("B", "Gorm", new int[8]);
        ob.Stats[Stat.Health.StatIndex] = 60;
        nb.Stats[Stat.Health.StatIndex] = 63;

        var anomalies = new List<string>();
        ReplayAnalyzer.DetectAnomalies([ob], [nb], anomalies);

        Assert.Empty(anomalies);
    }

    [Fact]
    public void DetectAnomalies_NoAnomalyForZeroDelta()
    {
        int[] stats = new int[8];
        var ob = BrotherOf("C", "Brunhilde", (int[])stats.Clone());
        var nb = BrotherOf("C", "Brunhilde", (int[])stats.Clone());

        var anomalies = new List<string>();
        ReplayAnalyzer.DetectAnomalies([ob], [nb], anomalies);

        Assert.Empty(anomalies);
    }

    // ---- reportRosterChanges -----------------------------------------------

    [Fact]
    public void ReportRosterChanges_FlagsBrotherCountMismatch()
    {
        var ob = BrotherOf("A", "Gorm", new int[8]);
        var nb1 = BrotherOf("A", "Gorm", new int[8]);
        var nb2 = BrotherOf("B", "Wulf", new int[8]);

        var anomalies = new List<string>();
        var buf = new StringWriter();
        ReplayAnalyzer.ReportRosterChanges([ob], [nb1, nb2], buf, anomalies);

        Assert.False(anomalies.Count == 0, "brother-count change must be flagged");
    }

    [Fact]
    public void ReportRosterChanges_NoFlagWhenCountSame()
    {
        var ob = BrotherOf("A", "Gorm", new int[8]);
        var nb = BrotherOf("A", "Gorm", new int[8]);

        var anomalies = new List<string>();
        var buf = new StringWriter();
        ReplayAnalyzer.ReportRosterChanges([ob], [nb], buf, anomalies);

        Assert.Empty(anomalies);
    }

    [Fact]
    public void ReportRosterChanges_PrintsAddedBrother()
    {
        var ob = BrotherOf("A", "Gorm", new int[8]);
        var nb1 = BrotherOf("A", "Gorm", new int[8]);
        var nb2 = BrotherOf("B", "Urist", new int[8]);

        var buf = new StringWriter();
        ReplayAnalyzer.ReportRosterChanges([ob], [nb1, nb2], buf, []);

        Assert.Contains("Urist", buf.ToString());
        Assert.Contains("+", buf.ToString());
    }

    [Fact]
    public void ReportRosterChanges_PrintsRemovedBrother()
    {
        var ob = BrotherOf("A", "Gorm", new int[8]);
        var ob2 = BrotherOf("B", "Tobi", new int[8]);
        var nb = BrotherOf("A", "Gorm", new int[8]); // Tobi removed

        var buf = new StringWriter();
        ReplayAnalyzer.ReportRosterChanges([ob, ob2], [nb], buf, []);

        Assert.Contains("Tobi", buf.ToString());
        Assert.Contains("-", buf.ToString());
    }

    // ---- crossCheck --------------------------------------------------------

    [Fact]
    public void CrossCheck_NoIssuesWhenMapsMatch()
    {
        var b = BrotherOf("A", "Gorm", new int[8]);

        var computed = new Dictionary<string, int[]>();
        var actual = new Dictionary<string, int[]>();
        int[] arr = [1, 0, 0, 0, 0, 0, 0, 0];
        computed["A"] = (int[])arr.Clone();
        actual["A"] = (int[])arr.Clone();

        var issues = ReplayAnalyzer.CrossCheck([b], computed, [], actual, []);

        Assert.Empty(issues);
    }

    [Fact]
    public void CrossCheck_FlagsMismatch()
    {
        var b = BrotherOf("A", "Gorm", new int[8]);

        var computed = new Dictionary<string, int[]>();
        var actual = new Dictionary<string, int[]>();
        computed["A"] = [2, 0, 0, 0, 0, 0, 0, 0];
        actual["A"] = [1, 0, 0, 0, 0, 0, 0, 0];

        var issues = ReplayAnalyzer.CrossCheck([b], computed, [], actual, []);

        Assert.False(issues.Count == 0, "mismatch must be reported");
        Assert.True(issues[0].Contains("Gorm"), "brother name must appear in issue");
        Assert.True(issues[0].Contains("planned"), "field label must appear");
    }

    [Fact]
    public void CrossCheck_TreatsNullAndAllZeroAsEquivalent()
    {
        var b = BrotherOf("A", "Gorm", new int[8]);

        var computed = new Dictionary<string, int[]>(); // no entry for "A"
        var actual = new Dictionary<string, int[]>();
        actual["A"] = new int[8]; // all-zero

        var issues = ReplayAnalyzer.CrossCheck([b], computed, [], actual, []);

        Assert.Empty(issues);
    }

    // ---- helpers -----------------------------------------------------------

    private static Brother BrotherOf(string? fp, string? name, int[] stats)
    {
        var b = new Brother
        {
            Fingerprint = fp,
            Name = name,
        };
        Array.Copy(stats, b.Stats, stats.Length);
        return b;
    }

    private static AnnotationService.LevelUpEvent FakeEvent(string? name, bool adjusted, int levels)
    {
        return new AnnotationService.LevelUpEvent(
                name, adjusted, levels,
                [], [], [], [], false);
    }
}
