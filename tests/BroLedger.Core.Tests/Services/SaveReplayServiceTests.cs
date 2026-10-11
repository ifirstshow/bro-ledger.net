using BroLedger.Core.Services;
using Xunit;

namespace BroLedger.Core.Tests.Services;

// Transcribed assertion-by-assertion from JAVATEST/service/SaveReplayServiceTest.java.
// Seams: @TempDir → per-test GUID dir; package-private ctor → internal SaveReplayService(baseDir);
// reflection AppConfig injection → toggle AppConfig.Instance.ReplayCaptureEnabled (fresh instance
// is unnecessary here — only the flag is consumed).
// Collection: shares the static AppConfig singleton with AppConfigTests/RoleServiceTests.
[Collection("AppConfig")]
public class SaveReplayServiceTests : IDisposable
{
    private readonly string Tmp;
    private readonly string BaseDir;  // synthetic replay root
    private readonly string SaveDir;  // directory that holds the "save file"

    private readonly string SavePath;

    public SaveReplayServiceTests()
    {
        Tmp = Path.Combine(Path.GetTempPath(), "broledger-test-" + Guid.NewGuid().ToString("N"));
        BaseDir = Path.Combine(Tmp, "replay-root");
        SaveDir = Path.Combine(Tmp, "saves");
        Directory.CreateDirectory(SaveDir);

        SavePath = Path.Combine(SaveDir, "campaign.sav");
        File.WriteAllBytes(SavePath, [0x01, 0x02, 0x03]);

        // Java setUp: fresh AppConfig with replayCaptureEnabled = false.
        AppConfig.Instance.ReplayCaptureEnabled = false;
    }

    public void Dispose()
    {
        if (Directory.Exists(Tmp)) Directory.Delete(Tmp, recursive: true);
        AppConfig.Instance.ReplayCaptureEnabled = false;
    }

    // ---- replayDir ---------------------------------------------------------

    [Fact]
    public void ReplayDir_ReturnsDirUnderBaseDir()
    {
        var srs = new SaveReplayService(BaseDir);
        var dir = srs.ReplayDir(SavePath);
        Assert.True(dir.StartsWith(BaseDir + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "replay dir must be under baseDir");
        // Must not be the baseDir itself
        Assert.NotEqual(BaseDir, dir);
    }

    [Fact]
    public void ReplayDir_SameInputSameDir()
    {
        var srs = new SaveReplayService(BaseDir);
        var d1 = srs.ReplayDir(SavePath);
        var d2 = srs.ReplayDir(SavePath);
        Assert.Equal(d1, d2);
    }

    [Fact]
    public void ReplayDir_DifferentSavePathDifferentDir()
    {
        var other = Path.Combine(SaveDir, "other.sav");
        File.WriteAllBytes(other, [0x04]);
        var srs = new SaveReplayService(BaseDir);
        Assert.NotEqual(srs.ReplayDir(SavePath), srs.ReplayDir(other));
    }

    // ---- capture: disabled -------------------------------------------------

    [Fact]
    public void Capture_DoesNothingWhenDisabled()
    {
        AppConfig.Instance.ReplayCaptureEnabled = false;
        var srs = new SaveReplayService(BaseDir);
        srs.Capture(SavePath);

        // Give the daemon thread a moment (it shouldn't have started, but be safe)
        Thread.Sleep(200);

        Assert.Empty(srs.Snapshots(SavePath));
    }

    // ---- capture: enabled --------------------------------------------------

    [Fact]
    public void Capture_WritesSavSnapshotWhenEnabled()
    {
        AppConfig.Instance.ReplayCaptureEnabled = true;
        var srs = new SaveReplayService(BaseDir);
        srs.Capture(SavePath);

        AwaitSnapshot(srs, SavePath, 1, 2000);

        var snaps = srs.Snapshots(SavePath);
        Assert.Single(snaps);
        Assert.Equal(File.ReadAllBytes(SavePath), File.ReadAllBytes(snaps[0]));
    }

    [Fact]
    public void Capture_PairsAnnotationsWhenPresent()
    {
        AppConfig.Instance.ReplayCaptureEnabled = true;

        // Write a fake .broledger.json adjacent to the save
        var annotPath = Path.Combine(SaveDir, "campaign.sav.broledger.json");
        var annotJson = "{\"version\":1,\"annotations\":{}}";
        File.WriteAllText(annotPath, annotJson);

        var srs = new SaveReplayService(BaseDir);
        srs.Capture(SavePath);

        AwaitSnapshot(srs, SavePath, 1, 2000);

        var snap = srs.Snapshots(SavePath)[0];
        var paired = srs.AnnotationsFor(snap);
        Assert.NotNull(paired);
        Assert.Equal(annotJson, File.ReadAllText(paired!));
    }

    [Fact]
    public void Capture_WritesSourceTxt()
    {
        AppConfig.Instance.ReplayCaptureEnabled = true;
        var srs = new SaveReplayService(BaseDir);
        srs.Capture(SavePath);

        AwaitSnapshot(srs, SavePath, 1, 2000);

        var sourceTxt = Path.Combine(srs.ReplayDir(SavePath), "source.txt");
        Assert.True(File.Exists(sourceTxt), "source.txt must be written");
        Assert.Equal(Path.GetFullPath(SavePath), File.ReadAllText(sourceTxt).Trim());
    }

    [Fact]
    public void Capture_SkipsDuplicateSave()
    {
        AppConfig.Instance.ReplayCaptureEnabled = true;
        var srs = new SaveReplayService(BaseDir);

        srs.Capture(SavePath);
        AwaitSnapshot(srs, SavePath, 1, 2000);

        // Second capture of identical content — should be skipped
        // (small sleep to ensure different timestamp, so if it DOES capture we can count 2)
        Thread.Sleep(50);
        srs.Capture(SavePath);
        Thread.Sleep(500);

        Assert.Single(srs.Snapshots(SavePath));
    }

    [Fact]
    public void Capture_DoesNotSkipWhenContentChanges()
    {
        AppConfig.Instance.ReplayCaptureEnabled = true;
        var srs = new SaveReplayService(BaseDir);

        srs.Capture(SavePath);
        AwaitSnapshot(srs, SavePath, 1, 2000);

        // Change save content
        File.WriteAllBytes(SavePath, [0x0A, 0x0B, 0x0C, 0x0D]);
        Thread.Sleep(50);
        srs.Capture(SavePath);
        AwaitSnapshot(srs, SavePath, 2, 2000);

        Assert.Equal(2, srs.Snapshots(SavePath).Count);
    }

    // ---- prune -------------------------------------------------------------

    [Fact]
    public void Prune_KeepsLastNSnapshots()
    {
        AppConfig.Instance.ReplayCaptureEnabled = true;
        var srs = new SaveReplayService(BaseDir);
        var dir = srs.ReplayDir(SavePath);
        Directory.CreateDirectory(dir);

        // Create 7 fake snapshots manually (different names = different timestamps)
        for (var i = 1; i <= 7; i++)
        {
            File.WriteAllBytes(Path.Combine(dir, "2026010" + i + "T120000_000Z.sav"), [(byte)i]);
        }

        srs.Prune(dir, 5);

        var remaining = srs.Snapshots(SavePath);
        Assert.Equal(5, remaining.Count);
        // Oldest two (indices 1 and 2) should be gone; newest five kept
        Assert.All(remaining, p =>
            Assert.True(Path.GetFileName(p).CompareTo("20260103") > 0, // i=1 and i=2 pruned
                "oldest snapshots should be removed: " + p));
    }

    [Fact]
    public void Prune_RemovesPairedAnnotationWithSav()
    {
        var srs = new SaveReplayService(BaseDir);
        var dir = srs.ReplayDir(SavePath);
        Directory.CreateDirectory(dir);

        // Create 3 paired sets
        for (var i = 1; i <= 3; i++)
        {
            var stem = "2026010" + i + "T120000_000Z";
            File.WriteAllBytes(Path.Combine(dir, stem + ".sav"), [(byte)i]);
            File.WriteAllBytes(Path.Combine(dir, stem + ".broledger.json"), [(byte)'{']);
        }

        srs.Prune(dir, 2); // remove the oldest one (i=1)

        // i=1's .broledger.json should also be gone
        Assert.False(File.Exists(Path.Combine(dir, "20260101T120000_000Z.broledger.json")),
            "paired annotation must be pruned with its .sav");
        // i=2 and i=3 survive
        Assert.Equal(2, srs.Snapshots(SavePath).Count);
    }

    [Fact]
    public void Prune_NopWhenBelowCap()
    {
        var srs = new SaveReplayService(BaseDir);
        var dir = srs.ReplayDir(SavePath);
        Directory.CreateDirectory(dir);

        File.WriteAllBytes(Path.Combine(dir, "20260101T120000_000Z.sav"), [1]);
        File.WriteAllBytes(Path.Combine(dir, "20260102T120000_000Z.sav"), [2]);

        srs.Prune(dir, 5); // cap=5, only 2 exist → nothing deleted

        Assert.Equal(2, srs.Snapshots(SavePath).Count);
    }

    // ---- snapshots ---------------------------------------------------------

    [Fact]
    public void Snapshots_EmptyForUnknownSave()
    {
        var srs = new SaveReplayService(BaseDir);
        Assert.Empty(srs.Snapshots(SavePath));
    }

    [Fact]
    public void Snapshots_SortedChronologically()
    {
        var srs = new SaveReplayService(BaseDir);
        var dir = srs.ReplayDir(SavePath);
        Directory.CreateDirectory(dir);

        // Write in reverse order to ensure sorting is not filesystem-order dependent
        File.WriteAllBytes(Path.Combine(dir, "20260103T120000_000Z.sav"), [3]);
        File.WriteAllBytes(Path.Combine(dir, "20260101T120000_000Z.sav"), [1]);
        File.WriteAllBytes(Path.Combine(dir, "20260102T120000_000Z.sav"), [2]);

        var snaps = srs.Snapshots(SavePath);
        Assert.Equal(3, snaps.Count);
        Assert.Equal("20260101T120000_000Z.sav", Path.GetFileName(snaps[0]));
        Assert.Equal("20260102T120000_000Z.sav", Path.GetFileName(snaps[1]));
        Assert.Equal("20260103T120000_000Z.sav", Path.GetFileName(snaps[2]));
    }

    // ---- annotationsFor ----------------------------------------------------

    [Fact]
    public void AnnotationsFor_ReturnsNullWhenNoPair()
    {
        var srs = new SaveReplayService(BaseDir);
        var dir = srs.ReplayDir(SavePath);
        Directory.CreateDirectory(dir);
        var snap = Path.Combine(dir, "20260101T120000_000Z.sav");
        File.WriteAllBytes(snap, [1]);

        Assert.Null(srs.AnnotationsFor(snap));
    }

    [Fact]
    public void AnnotationsFor_ReturnsPairedJson()
    {
        var srs = new SaveReplayService(BaseDir);
        var dir = srs.ReplayDir(SavePath);
        Directory.CreateDirectory(dir);
        var snap = Path.Combine(dir, "20260101T120000_000Z.sav");
        var paired = Path.Combine(dir, "20260101T120000_000Z.broledger.json");
        File.WriteAllBytes(snap, [1]);
        File.WriteAllBytes(paired, [(byte)'{', (byte)'}']);

        Assert.Equal(paired, srs.AnnotationsFor(snap));
    }

    // ---- listAll -----------------------------------------------------------

    [Fact]
    public void ListAll_EmptyWhenBaseDirAbsent()
    {
        var missing = Path.Combine(BaseDir, "nonexistent");
        var srs = new SaveReplayService(missing);
        Assert.Empty(srs.ListAll());
    }

    [Fact]
    public void ListAll_ReturnsOneEntryPerSubDir()
    {
        var srs1 = new SaveReplayService(BaseDir);
        var srs2 = new SaveReplayService(BaseDir);

        var save1 = Path.Combine(SaveDir, "alpha.sav");
        var save2 = Path.Combine(SaveDir, "beta.sav");
        File.WriteAllBytes(save1, [1]);
        File.WriteAllBytes(save2, [2]);

        // Capture creates sub-dirs
        AppConfig.Instance.ReplayCaptureEnabled = true;
        srs1.Capture(save1); AwaitSnapshot(srs1, save1, 1, 2000);
        srs2.Capture(save2); AwaitSnapshot(srs2, save2, 1, 2000);

        var all = srs1.ListAll();
        Assert.Equal(2, all.Count);
        // Each entry should have snapshotCount=1
        Assert.All(all, e => Assert.Equal(1, e.SnapshotCount));
    }

    // ---- capture cap -------------------------------------------------------

    [Fact]
    public void Capture_PrunesOldestBeyondCap()
    {
        AppConfig.Instance.ReplayCaptureEnabled = true;
        var srs = new SaveReplayService(BaseDir);

        // Capture MAX_SNAPSHOTS + 1 distinct saves
        for (var i = 0; i <= SaveReplayService.MaxSnapshots; i++)
        {
            File.WriteAllBytes(SavePath, [(byte)i]);
            srs.Capture(SavePath);
            AwaitSnapshot(srs, SavePath, Math.Min(i + 1, SaveReplayService.MaxSnapshots), 2000);
            Thread.Sleep(20); // ensure distinct timestamps
        }

        Assert.True(srs.Snapshots(SavePath).Count <= SaveReplayService.MaxSnapshots,
            "snapshots must be capped at MAX_SNAPSHOTS=" + SaveReplayService.MaxSnapshots);
    }

    // ---- helpers -----------------------------------------------------------

    /// <summary>Polls until the expected snapshot count is reached or timeout expires.</summary>
    private static void AwaitSnapshot(SaveReplayService srs, string savePath,
        int expectedCount, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (srs.Snapshots(savePath).Count >= expectedCount) return;
            Thread.Sleep(50);
        }
        // Let the assertion in the caller fail with a clear message
    }
}
