using System.Globalization;
using BroLedger.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BroLedger.Core.Services;

/// <summary>
/// Captures timestamped snapshots of the active save file (and its current planned-increase
/// annotations) into <c>~/.bro-ledger/replay/&lt;hash&gt;/</c> whenever an overwrite is detected,
/// so that the sequence can be replayed offline.
///
/// <para>Capture is off by default and is gated on <see cref="AppConfig.ReplayCaptureEnabled"/>,
/// and is additionally disabled outright in non-debug (tagged-release) builds —
/// see <see cref="AppInfo.IsDebug"/>.</para>
///
/// <para>At most <see cref="MaxSnapshots"/> save snapshots are kept per save file; older ones
/// (plus their paired <c>.broledger.json</c> annotation files) are pruned automatically.</para>
/// </summary>
// Transcribed method-by-method from Java service/SaveReplayService.java.
public sealed class SaveReplayService
{
    /// <summary>Maximum number of .sav snapshots retained per save file.</summary>
    public const int MaxSnapshots = 5;

    // ponytail: JUL → ILogger，默认 NullLogger；host DI 落地时替换
    private static readonly ILogger Log = NullLogger.Instance;

    // Timestamp format: lexicographically sortable, unambiguous UTC.
    // Java "SSS" = millisecond-of-day fraction → .NET "fff".
    private static readonly string TsFmt = "yyyyMMdd'T'HHmmss'_'fff'Z'";

    private static SaveReplayService? _instance;

    /// <summary>Root directory that holds one sub-directory per captured save path.</summary>
    private readonly string _baseDir;

    /// <summary>Default constructor — stores snapshots under ~/.bro-ledger/replay/.</summary>
    private SaveReplayService()
    {
        _baseDir = Path.Combine(AppPaths.DataDir(), "replay");
    }

    /// <summary>Test constructor — uses the supplied base directory.</summary>
    internal SaveReplayService(string baseDir)
    {
        _baseDir = baseDir;
    }

    public static SaveReplayService Instance => _instance ??= new();

    /// <summary>For tests only — resets the singleton so the next Instance starts fresh.</summary>
    internal static void ResetInstance() => _instance = null;

    // ---- public API --------------------------------------------------------

    /// <summary>Replay directory for a given save path, keyed by SHA-256 of its absolute path.</summary>
    public string ReplayDir(string savePath) =>
        Path.Combine(_baseDir, HexUtils.Sha256Hex(Path.GetFullPath(savePath)));

    /// <summary>
    /// Captures the current save file into the replay directory if
    /// <see cref="AppConfig.ReplayCaptureEnabled"/> is true and this is a debug build.
    ///
    /// <para>The annotation bytes are read <em>synchronously</em> on the calling thread so they
    /// reflect the pre-reconciliation state. The actual file copy is done on a background
    /// thread to keep the UI thread responsive.</para>
    ///
    /// <para>Safe to call even when capture is disabled — returns immediately.</para>
    /// </summary>
    public void Capture(string savePath)
    {
        if (!AppInfo.IsDebug() || !AppConfig.Instance.ReplayCaptureEnabled) return;

        // Read annotation bytes NOW (pre-reconciliation state, caller thread) before spawning
        // the background thread that may race with reconcileOnReload writing to disk.
        var annotBytes = ReadCurrentAnnotationBytes(savePath);

        Task.Run(() => DoCapture(savePath, annotBytes));
    }

    /// <summary>Sorted list of .sav snapshot paths for a given save (chronological, ascending).</summary>
    public List<string> Snapshots(string savePath) => ListSavSnapshots(ReplayDir(savePath));

    /// <summary>
    /// Returns the <c>.broledger.json</c> paired with a snapshot file (same timestamp stem),
    /// or null if none was captured.
    /// </summary>
    public string? AnnotationsFor(string snapshot)
    {
        var name = Path.GetFileName(snapshot);
        var stem = name.EndsWith(".sav", StringComparison.Ordinal) ? name[..^4] : name;
        var paired = Path.Combine(Path.GetDirectoryName(snapshot)!, stem + ".broledger.json");
        return File.Exists(paired) ? paired : null;
    }

    /// <summary>Describes one captured save entry (one replay sub-directory).</summary>
    public sealed record ReplayEntry(string Dir, string SourceSavePath, int SnapshotCount);

    /// <summary>
    /// Lists all replay entries across all captured save files, sorted by source save path.
    /// Each entry corresponds to one sub-directory under the base directory.
    /// </summary>
    public List<ReplayEntry> ListAll()
    {
        if (!Directory.Exists(_baseDir)) return [];
        try
        {
            return Directory.EnumerateDirectories(_baseDir)
                .Select(EntryFor)
                .OrderBy(e => e.SourceSavePath, StringComparer.Ordinal)
                .ToList();
        }
        catch (IOException e)
        {
            Log.LogWarning("SaveReplayService: listAll failed — {Message}", e.Message);
            return [];
        }
    }

    /// <summary>
    /// Prunes <paramref name="dir"/> so that at most <paramref name="keep"/> .sav snapshots
    /// remain, deleting the oldest first along with their paired .broledger.json files.
    /// </summary>
    public void Prune(string dir, int keep)
    {
        var savs = ListSavSnapshots(dir);
        var toDelete = savs.Count - keep;
        for (var i = 0; i < toDelete; i++)
        {
            var sav = savs[i];
            try
            {
                File.Delete(sav); // no-op if missing — Java deleteIfExists
                var paired = AnnotationsFor(sav);
                if (paired != null) File.Delete(paired);
            }
            catch (IOException e)
            {
                Log.LogDebug("SaveReplayService: prune delete failed — {Message}", e.Message);
            }
        }
    }

    // ---- internal ----------------------------------------------------------

    private void DoCapture(string savePath, byte[]? annotBytes)
    {
        try
        {
            var dir = ReplayDir(savePath);
            Directory.CreateDirectory(dir);

            // Skip exact duplicates (same bytes as the most recent snapshot).
            var existing = ListSavSnapshots(dir);
            if (existing.Count > 0)
            {
                var last = existing[^1];
                var saveBytes = File.ReadAllBytes(savePath);
                if (new FileInfo(last).Length == saveBytes.Length &&
                    File.ReadAllBytes(last).AsSpan().SequenceEqual(saveBytes))
                {
                    return;
                }
            }

            var ts = DateTime.UtcNow.ToString(TsFmt, CultureInfo.InvariantCulture);
            var destSav = Path.Combine(dir, ts + ".sav");
            // Java Files.copy without REPLACE_EXISTING — a same-millisecond collision throws
            // and aborts the capture (logged below), exactly as in Java.
            File.Copy(savePath, destSav);

            if (annotBytes != null)
            {
                File.WriteAllBytes(Path.Combine(dir, ts + ".broledger.json"), annotBytes);
            }

            // Write/refresh source.txt so the interactive analyzer can show a human-readable name.
            File.WriteAllText(Path.Combine(dir, "source.txt"), Path.GetFullPath(savePath));

            Prune(dir, MaxSnapshots);
        }
        catch (Exception e)
        {
            Log.LogWarning("SaveReplayService: capture failed — {Message}", e.Message);
        }
    }

    /// <summary>
    /// Reads the current (pre-reconciliation) annotation bytes from disk.
    /// Prefers the primary path adjacent to the save; falls back to the ~/.bro-ledger fallback.
    /// Returns null if neither exists.
    /// </summary>
    private static byte[]? ReadCurrentAnnotationBytes(string savePath)
    {
        var hash = HexUtils.Sha256Hex(Path.GetFullPath(savePath));
        var primary = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(savePath))!,
            Path.GetFileName(savePath) + ".broledger.json");
        var fallback = Path.Combine(AppPaths.DataDir(), "annotations-" + hash + ".json");
        var source = File.Exists(primary) ? primary
                   : File.Exists(fallback) ? fallback
                   : null;
        if (source == null) return null;
        try
        {
            return File.ReadAllBytes(source);
        }
        catch (IOException e)
        {
            Log.LogDebug("SaveReplayService: could not read annotation bytes — {Message}", e.Message);
            return null;
        }
    }

    private ReplayEntry EntryFor(string dir)
    {
        var sourceTxt = Path.Combine(dir, "source.txt");
        var sourcePath = "<unknown>";
        if (File.Exists(sourceTxt))
        {
            try { sourcePath = File.ReadAllText(sourceTxt).Trim(); } catch (IOException) { }
        }
        return new ReplayEntry(dir, sourcePath, ListSavSnapshots(dir).Count);
    }

    private static List<string> ListSavSnapshots(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        try
        {
            // Java Files.list() + fileName.endsWith(".sav") + Path.sort → ordinal path-string order.
            return Directory.EnumerateFileSystemEntries(dir)
                .Where(p => Path.GetFileName(p).EndsWith(".sav", StringComparison.Ordinal))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
    }
}
