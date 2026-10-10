using System.Text.Json;
using System.Text.Json.Serialization;
using BroLedger.Core.Models;
using BroLedger.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BroLedger.Core.Services;

/// <summary>
/// Persists per-brother role and target-stat annotations adjacent to the save file,
/// with a fallback location in ~/.bro-ledger/.
/// Port of JAVA/service/AnnotationService.java（核心半区；对账半区在下一任务）.
/// Storage format: {"version":1,"uiState":{...},"annotations":{"&lt;fingerprint&gt;":{...}}}
/// </summary>
public sealed class AnnotationService
{
    // ponytail: JUL → ILogger，默认 NullLogger；host DI 落地时替换
    private static readonly ILogger Log = NullLogger.Instance;

    private static AnnotationService? _instance;

    public static AnnotationService Instance => _instance ??= new();

    /// <summary>For tests only — Java 测试用反射重置单例，此处等价替换。</summary>
    internal static void ResetForTest() => _instance = null;

    // Current in-memory store: fingerprint → annotation
    // ponytail: Java LinkedHashMap 插入序语义；写盘 JSON 对象序不作契约，普通 Dictionary 即可
    private readonly Dictionary<string, BrotherAnnotation> _store = new();

    // Per-save UI toggle state
    private UiState _uiState = new();

    // Resolved paths for the current save
    private string? _primaryPath;   // &lt;save&gt;.broledger.json
    private string? _fallbackPath;  // ~/.bro-ledger/annotations-&lt;hash&gt;.json

    private AnnotationService() { }

    // ---- lifecycle ---------------------------------------------------------

    /// <summary>
    /// Call after a save file is selected/reloaded.
    /// Loads annotations from the primary path if it exists, else the fallback.
    /// </summary>
    public void LoadFor(string savePath)
    {
        // Java: savePath.resolveSibling(fileName + ".broledger.json")；
        // hash 输入是存档的**绝对路径字符串**
        var full = Path.GetFullPath(savePath);
        _primaryPath = Path.Combine(Path.GetDirectoryName(full)!,
            Path.GetFileName(savePath) + ".broledger.json");
        _fallbackPath = Path.Combine(AppPaths.DataDir(),
            "annotations-" + HexUtils.Sha256Hex(full) + ".json");

        _store.Clear();
        _uiState = new UiState();
        if (File.Exists(_primaryPath))
        {
            Read(_primaryPath);
        }
        else if (File.Exists(_fallbackPath))
        {
            Read(_fallbackPath);
        }
    }

    // ---- API ---------------------------------------------------------------

    public BrotherAnnotation Get(string fingerprint)
    {
        if (!_store.TryGetValue(fingerprint, out var a))
        {
            a = new BrotherAnnotation(fingerprint);
            _store[fingerprint] = a;
        }
        return a;
    }

    /// <summary>Store the role UUID for this brother (null = no role assigned).</summary>
    public void SetRole(string fingerprint, string? roleId)
    {
        Get(fingerprint).RoleId = roleId;
        Flush();
    }

    /// <summary>
    /// Clears every annotation's role reference to <paramref name="roleId"/> — called right
    /// after that role is deleted, so the loaded save never holds a dangling reference.
    /// Returns the affected fingerprints; flushes once when any were cleared.
    /// </summary>
    public List<string> ClearRoleRefs(string? roleId)
    {
        if (roleId == null) return [];
        return ClearRoleRefsMatching(id => id == roleId);
    }

    /// <summary>
    /// Clears role references that point outside <paramref name="validRoleIds"/> — repairs
    /// annotations whose role was deleted while this save was not loaded.
    /// Returns the affected fingerprints; flushes once when any were cleared.
    /// </summary>
    public List<string> ClearDanglingRoleRefs(HashSet<string> validRoleIds)
    {
        return ClearRoleRefsMatching(id => !validRoleIds.Contains(id));
    }

    private List<string> ClearRoleRefsMatching(Func<string, bool> shouldClear)
    {
        var affected = new List<string>();
        foreach (var (fp, a) in _store)
        {
            if (a.RoleId != null && shouldClear(a.RoleId))
            {
                a.RoleId = null;
                affected.Add(fp);
            }
        }
        if (affected.Count > 0) Flush();
        return affected;
    }

    /// <summary>Store the planned level-up increases (lv 1–11) for this brother (null = clear all).</summary>
    public void SetStatIncreases(string fingerprint, int[]? increases)
    {
        Get(fingerprint).StatIncreases = increases;
        Flush();
    }

    /// <summary>Store the post-lv11 increases already taken for this brother (null = clear all).</summary>
    public void SetPost11Increases(string fingerprint, int[]? increases)
    {
        Get(fingerprint).Post11Increases = increases;
        Flush();
    }

    /// <summary>Store the per-brother perk plan (hex ID → "OPTIONAL"|"PLANNED"). Null or empty = clear plan.</summary>
    public void SetPerkPlanStatus(string fingerprint, Dictionary<string, string>? status)
    {
        Get(fingerprint).PerkPlanStatus =
            (status == null || status.Count == 0) ? null : new Dictionary<string, string>(status);
        Flush();
    }

    public UiState GetUiState() => _uiState;

    public void SetUiState(UiState state)
    {
        _uiState = state;
        Flush();
    }

    /// <summary>Persist the display order of brothers; assigns sortIndex 0..n-1 and flushes once.</summary>
    public void SetOrder(List<string> fingerprintsInOrder)
    {
        for (var i = 0; i < fingerprintsInOrder.Count; i++)
        {
            Get(fingerprintsInOrder[i]).SortIndex = i;
        }
        Flush();
    }

    // ---- persistence -------------------------------------------------------

    private void Flush()
    {
        if (_primaryPath == null) return;
        var dest = ResolveDestination();
        if (dest == null) return;
        Write(dest);
    }

    private string? ResolveDestination()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_primaryPath)!);
            return _primaryPath;
        }
        catch (Exception)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_fallbackPath)!);
                return _fallbackPath;
            }
            catch (Exception ex)
            {
                Log.LogWarning("AnnotationService: cannot create directory — {Message}", ex.Message);
                return null;
            }
        }
    }

    private void Write(string dest)
    {
        try
        {
            var file = new AnnotationsFile { UiState = _uiState };
            foreach (var (fp, a) in _store)
            {
                file.Annotations[fp] = new AnnotationsFile.AnnotationEntry
                {
                    RoleId = a.RoleId,
                    SortIndex = a.SortIndex,
                    StatIncreases = a.StatIncreases,
                    Post11Increases = a.Post11Increases,
                    PerkPlanStatus = (a.PerkPlanStatus == null || a.PerkPlanStatus.Count == 0)
                        ? null : a.PerkPlanStatus,
                };
            }
            // 泛型 source-gen 重载——非泛型 object 重载会静默写出全 null
            File.WriteAllText(dest,
                JsonSerializer.Serialize(file, DataJsonContext.Default.AnnotationsFile));
        }
        catch (Exception e)
        {
            Log.LogWarning("AnnotationService: write failed — {Message}", e.Message);
        }
    }

    private void Read(string src)
    {
        try
        {
            var file = JsonSerializer.Deserialize(
                File.ReadAllText(src), DataJsonContext.Default.AnnotationsFile);
            if (file == null) return;
            if (file.UiState != null) _uiState = file.UiState;
            if (file.Annotations == null) return;
            foreach (var (fp, ae) in file.Annotations)
            {
                var a = new BrotherAnnotation(fp)
                {
                    RoleId = ae.RoleId,
                    SortIndex = ae.SortIndex,
                    StatIncreases = ae.StatIncreases,
                    Post11Increases = ae.Post11Increases,
                    PerkPlanStatus = (ae.PerkPlanStatus == null || ae.PerkPlanStatus.Count == 0)
                        ? null : ae.PerkPlanStatus,
                };
                _store[fp] = a;
            }
        }
        catch (Exception e)
        {
            Log.LogWarning("AnnotationService: read failed — {Message}", e.Message);
        }
    }

    // ---- JSON DTOs（形状契约见类注释） --------------------------------------

    public sealed class UiState
    {
        [JsonPropertyName("potentialMode")] public string? PotentialMode { get; set; }
        [JsonPropertyName("armorMode")] public string? ArmorMode { get; set; }
        [JsonPropertyName("weaponTierMode")] public string? WeaponTierMode { get; set; }
        [JsonPropertyName("perkSortMode")] public string? PerkSortMode { get; set; }
    }

    internal sealed class AnnotationsFile
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("uiState")] public UiState? UiState { get; set; }
        [JsonPropertyName("annotations")] public Dictionary<string, AnnotationEntry> Annotations { get; set; } = new();

        internal sealed class AnnotationEntry
        {
            [JsonPropertyName("roleId")] public string? RoleId { get; set; }
            [JsonPropertyName("sortIndex")] public int? SortIndex { get; set; }
            [JsonPropertyName("statIncreases")] public int[]? StatIncreases { get; set; }
            [JsonPropertyName("post11Increases")] public int[]? Post11Increases { get; set; }
            [JsonPropertyName("perkPlanStatus")] public Dictionary<string, string>? PerkPlanStatus { get; set; }
        }
    }
}
