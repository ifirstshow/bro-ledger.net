using System.Text.Json;
using System.Text.Json.Serialization;
using BroLedger.Core.Models;
using BroLedger.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BroLedger.Core.Services;

/// <summary>
/// Application configuration persisted to ~/.bro-ledger/config.json.
/// Port of JAVA/service/AppConfig.java — config.json 数据兼容（顶层 camelCase 字段，pretty）。
/// </summary>
public sealed class AppConfig
{
    private static AppConfig? _instance;

    /// <summary>Overrides the default config file during tests so Load()/Save() never touch ~/.bro-ledger/config.json.</summary>
    internal static string? ConfigFileForTest { get; set; }

    // ponytail: JUL → ILogger，默认 NullLogger；host DI 落地时替换
    private static readonly ILogger Log = NullLogger.Instance;

    // Public properties — read/write freely, then call Save(). camelCase 兼容 Java config.json。
    [JsonPropertyName("lastSaveFilePath")]
    public string? LastSaveFilePath { get; set; }

    [JsonPropertyName("gameArtDirectory")]
    public string? GameArtDirectory { get; set; }

    [JsonPropertyName("windowWidth")]
    public int WindowWidth { get; set; } = 1800;

    [JsonPropertyName("windowHeight")]
    public int WindowHeight { get; set; } = 1320;

    [JsonPropertyName("windowX")]
    public int? WindowX { get; set; }

    [JsonPropertyName("windowY")]
    public int? WindowY { get; set; }

    [JsonPropertyName("overviewColumnOrder")]
    public List<string>? OverviewColumnOrder { get; set; }

    /// <summary>Ordered list of role UUIDs defining user-chosen role display order.</summary>
    [JsonPropertyName("roleOrder")]
    public List<string>? RoleOrder { get; set; }

    /// <summary>Expected-stats calculation mode: "NAIVE" (default) or "GREEDY".</summary>
    [JsonPropertyName("expectedStatsMode")]
    public string ExpectedStatsMode { get; set; } = "NAIVE";

    /// <summary>Level-up modal behaviour: "MODAL" (default), "AUTO_CLOSE" (15 s), or "OFF".</summary>
    [JsonPropertyName("levelUpModalMode")]
    public string LevelUpModalMode { get; set; } = "MODAL";

    /// <summary>
    /// When true, the app captures a timestamped copy of the save file (plus the current
    /// planned-increase annotations) into ~/.bro-ledger/replay/&lt;hash&gt;/ before each
    /// overwrite is reconciled. Disabled by default.
    /// </summary>
    [JsonPropertyName("replayCaptureEnabled")]
    public bool ReplayCaptureEnabled { get; set; } = false;

    /// <summary>
    /// Most-recent-first history of role packs published from this machine, capped at
    /// <see cref="RecentSharesMax"/>. Entries that roll off the cap permanently lose the
    /// ability to update their code (the fallback is always publishing a new code).
    /// </summary>
    [JsonPropertyName("recentShares")]
    public List<ShareRecord>? RecentShares { get; set; }

    /// <summary>How many published packs stay updateable from this machine.</summary>
    public const int RecentSharesMax = 5;

    private AppConfig() { }

    public static AppConfig Instance => _instance ??= new();

    private static string DefaultConfigFile() => Path.Combine(AppPaths.DataDir(), "config.json");

    /// <summary>Load from the default config file; silently falls back to defaults if absent.</summary>
    public void Load() => LoadFrom(ConfigFileForTest ?? DefaultConfigFile());

    /// <summary>Load from an explicit file path (used by tests).</summary>
    public void LoadFrom(string file)
    {
        if (!File.Exists(file))
        {
            Log.LogTrace("AppConfig: no config file at {File} — using defaults", file);
            return;
        }
        Log.LogTrace("AppConfig: loading from {File}", file);
        try
        {
            var data = JsonSerializer.Deserialize(File.ReadAllText(file), DataJsonContext.Default.AppConfigData);
            if (data != null) CopyFrom(data);
            Log.LogTrace("AppConfig: loaded — lastSaveFilePath={LastSaveFilePath}, gameArtDirectory={GameArtDirectory}",
                LastSaveFilePath, GameArtDirectory);
        }
        catch (Exception e)
        {
            // File exists but is unreadable (corrupt, truncated, wrong format).
            // Preserve it under a .bad-<timestamp> name so the user can recover it,
            // and let the next Save() write a fresh file. Do NOT silently overwrite
            // with defaults — that would permanently destroy the user's settings.
            Log.LogError("AppConfig: could not read config — starting with defaults. Original file preserved. Error: {Message}",
                e.Message);
            try
            {
                var backup = file + ".bad-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                File.Move(file, backup);
                Log.LogWarning("AppConfig: corrupt config backed up to {Backup}", backup);
            }
            catch (Exception ex)
            {
                Log.LogWarning("AppConfig: could not back up corrupt config — {Message}", ex.Message);
            }
        }
    }

    // Jackson readerForUpdating 语义：DTO 中为 null 的字段（缺省或显式 null）保留单例现值。
    private void CopyFrom(AppConfigData d)
    {
        if (d.LastSaveFilePath != null) LastSaveFilePath = d.LastSaveFilePath;
        if (d.GameArtDirectory != null) GameArtDirectory = d.GameArtDirectory;
        if (d.WindowWidth is int ww) WindowWidth = ww;
        if (d.WindowHeight is int wh) WindowHeight = wh;
        if (d.WindowX is int x) WindowX = x;
        if (d.WindowY is int y) WindowY = y;
        if (d.OverviewColumnOrder != null) OverviewColumnOrder = d.OverviewColumnOrder;
        if (d.RoleOrder != null) RoleOrder = d.RoleOrder;
        if (d.ExpectedStatsMode != null) ExpectedStatsMode = d.ExpectedStatsMode;
        if (d.LevelUpModalMode != null) LevelUpModalMode = d.LevelUpModalMode;
        if (d.ReplayCaptureEnabled is bool rc) ReplayCaptureEnabled = rc;
        if (d.RecentShares != null) RecentShares = d.RecentShares;
    }

    /// <summary>Persist current values to the default config file.</summary>
    public void Save() => SaveTo(ConfigFileForTest ?? DefaultConfigFile());

    /// <summary>Persist current values to an explicit file path (used by tests).</summary>
    public void SaveTo(string file)
    {
        Log.LogTrace("AppConfig: saving to {File} — lastSaveFilePath={LastSaveFilePath}, gameArtDirectory={GameArtDirectory}",
            file, LastSaveFilePath, GameArtDirectory);
        var tmp = file + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            var dto = new AppConfigData(
                LastSaveFilePath, GameArtDirectory, WindowWidth, WindowHeight, WindowX, WindowY,
                OverviewColumnOrder, RoleOrder, ExpectedStatsMode, LevelUpModalMode,
                ReplayCaptureEnabled, RecentShares);
            File.WriteAllText(tmp, JsonSerializer.Serialize(dto, DataJsonContext.Default.AppConfigData));
            // ponytail: Java ATOMIC_MOVE + AtomicMoveNotSupportedException 回退分支在 .NET 无对应——
            // File.Move 即同卷移动且 overwrite:true 覆盖旧文件
            File.Move(tmp, file, overwrite: true);
            Log.LogTrace("AppConfig: saved OK");
        }
        catch (Exception e)
        {
            Log.LogWarning("AppConfig: could not save config — {Message}", e.Message);
            try { File.Delete(tmp); } catch { /* ignored, 同 Java deleteIfExists */ }
        }
    }

    /// <summary>
    /// Records a publish or update in <see cref="RecentShares"/>: an existing entry with the
    /// same code is replaced and moved to the top (an update refreshes title/timestamp
    /// without duplicating), and the list is trimmed to <see cref="RecentSharesMax"/>.
    /// The caller is responsible for <see cref="Save"/>.
    /// </summary>
    public void RecordShare(ShareRecord record)
    {
        RecentShares ??= [];
        RecentShares.RemoveAll(r => r == null || (record.Code != null && record.Code == r.Code));
        RecentShares.Insert(0, record);
        while (RecentShares.Count > RecentSharesMax)
        {
            RecentShares.RemoveAt(RecentShares.Count - 1);
        }
    }

    /// <summary>True if a game-art directory is configured and exists on disk.</summary>
    public bool HasGameArtDirectory() =>
        GameArtDirectory != null && Directory.Exists(GameArtDirectory);

    /// <summary>Resolved game-art root path, or null if not configured.</summary>
    public string? GameArtRoot() => HasGameArtDirectory() ? GameArtDirectory : null;

    /// <summary>
    /// Stats in the user's chosen column order (driven by <see cref="OverviewColumnOrder"/>).
    /// Always returns all 8 stats; any not present in the saved order are appended at the end.
    /// This is the single source of truth for stat display order across all views.
    /// </summary>
    public Stat[] OrderedStats()
    {
        if (OverviewColumnOrder is not { Count: > 0 })
        {
            return (Stat[])Stat.Values.Clone();
        }
        var ordered = new List<Stat>();
        var seen = new HashSet<Stat>();
        foreach (var colId in OverviewColumnOrder)
        {
            var abbrev = StatAbbrevFromColumnId(colId);
            if (abbrev == null) continue;
            foreach (var s in Stat.Values)
            {
                if (s.Abbrev == abbrev)
                {
                    ordered.Add(s);
                    seen.Add(s);
                    break;
                }
            }
        }
        foreach (var s in Stat.Values)
        {
            if (!seen.Contains(s)) ordered.Add(s);
        }
        return ordered.ToArray();
    }

    /// <summary>
    /// Extracts the stat abbreviation from an overview column-ID of the form "stat-&lt;abbrev&gt;".
    /// Returns null for non-stat column IDs.
    /// </summary>
    internal static string? StatAbbrevFromColumnId(string? colId)
    {
        if (colId != null && colId.StartsWith("stat-")) return colId.Substring(5);
        return null;
    }

    /// <summary>For tests only — resets the singleton and config-file override so the next Instance starts fresh.</summary>
    internal static void ResetForTest()
    {
        _instance = null;
        ConfigFileForTest = null;
    }
}

/// <summary>
/// Jackson readerForUpdating 的反序列化 DTO（JAVA AppConfig 12 个 JSON 字段）：
/// 读入后经 CopyFrom 逐字段拷贝到单例。unknown fields 由 STJ 默认跳过。
/// </summary>
internal sealed record AppConfigData(
    [property: JsonPropertyName("lastSaveFilePath")] string? LastSaveFilePath,
    [property: JsonPropertyName("gameArtDirectory")] string? GameArtDirectory,
    [property: JsonPropertyName("windowWidth")] int? WindowWidth,
    [property: JsonPropertyName("windowHeight")] int? WindowHeight,
    [property: JsonPropertyName("windowX")] int? WindowX,
    [property: JsonPropertyName("windowY")] int? WindowY,
    [property: JsonPropertyName("overviewColumnOrder")] List<string>? OverviewColumnOrder,
    [property: JsonPropertyName("roleOrder")] List<string>? RoleOrder,
    [property: JsonPropertyName("expectedStatsMode")] string? ExpectedStatsMode,
    [property: JsonPropertyName("levelUpModalMode")] string? LevelUpModalMode,
    [property: JsonPropertyName("replayCaptureEnabled")] bool? ReplayCaptureEnabled,
    [property: JsonPropertyName("recentShares")] List<ShareRecord>? RecentShares);
