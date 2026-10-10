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
/// Port of JAVA/service/AnnotationService.java（核心半区 + 对账半区）.
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

    // ---- level-up reconciliation -------------------------------------------

    internal const string GiftedPerkId = "9899E380";

    /// <summary>
    /// One detected level-up (or Gifted-perk selection) after a save reload.
    /// StatDeltas/ConsumedIncreases: Java EnumMap iterates in enum declaration order —
    /// carried here as List&lt;KeyValuePair&lt;Stat,int&gt;&gt; built in Stat.Values order (Dictionary 禁用).
    /// </summary>
    public sealed record LevelUpEvent(
        string? Name,
        bool Adjusted,
        int LevelsAssigned,
        IReadOnlyList<KeyValuePair<Stat, int>> StatDeltas,
        IReadOnlyList<Stat> AdjustedStats,
        IReadOnlyList<KeyValuePair<Stat, int>> ConsumedIncreases,
        IReadOnlyList<string> AddedPerkIds,
        bool Post11);

    internal sealed record Reconciliation(
        IReadOnlyList<Stat> AdjustedStats,
        IReadOnlyList<KeyValuePair<Stat, int>> ConsumedIncreases,
        int[]? UpdatedStatIncreases,
        int[]? UpdatedPost11Increases,
        bool Changed);

    /// <summary>
    /// Compares old vs new brother lists after a save reload and reduces
    /// statIncreases[] for any brother whose levelPoints count decreased
    /// (i.e. they assigned a level-up in-game).
    /// </summary>
    public List<LevelUpEvent> ReconcileOnReload(List<Brother> oldList, List<Brother> newList)
    {
        var events = new List<LevelUpEvent>();
        Log.LogTrace("reconcileOnReload: checking {New} brothers against {Old} old", newList.Count, oldList.Count);
        foreach (var nb in newList)
        {
            var fp = nb.Fingerprint;
            if (fp == null)
            {
                Log.LogTrace("  skip [{Name}]: no fingerprint", nb.Name);
                continue;
            }
            var ob = FindMatch(oldList, fp);
            if (ob == null)
            {
                Log.LogTrace("  skip [{Name}]: no match in old list (new brother?)", nb.Name);
                continue;
            }
            Log.LogTrace("  [{Name}] old.levelPoints={Old}  new.levelPoints={New}",
                nb.Name, ob.LevelPoints, nb.LevelPoints);
            var e = ReconcileBrother(ob, nb);
            if (e != null) events.Add(e);
        }
        Log.LogTrace("reconcileOnReload: done — {Count} level-up event(s)", events.Count);
        return events;
    }

    private LevelUpEvent? ReconcileBrother(Brother ob, Brother nb)
    {
        var fp = nb.Fingerprint!; // caller checked non-null
        var addedPerkIds = AddedPerks(ob, nb);
        if (addedPerkIds.Count > 0)
        {
            Log.LogTrace("  [{Name}]: {Count} perk(s) added: {Perks}", nb.Name, addedPerkIds.Count, addedPerkIds);
        }

        var levelPointsDecreased = nb.LevelPoints < ob.LevelPoints;
        var giftedSelected = addedPerkIds.Contains(GiftedPerkId);

        if (!levelPointsDecreased && addedPerkIds.Count == 0)
        {
            Log.LogTrace("  [{Name}]: no level assigned and no perks added — skipping", nb.Name);
            return null;
        }

        if (levelPointsDecreased)
        {
            Log.LogTrace("  [{Name}]: {Levels} level(s) assigned in-game", nb.Name, ob.LevelPoints - nb.LevelPoints);
        }
        if (giftedSelected)
        {
            Log.LogTrace("  [{Name}]: Gifted bonus round — treating as extra level for reconciliation", nb.Name);
        }

        var levels = LevelsAssigned(ob, nb, giftedSelected);
        var a = Get(fp);
        var deltas = StatDeltas(ob, nb);
        var post11 = levelPointsDecreased && IsPost11Reconcile(ob, nb);
        Reconciliation r;

        if (post11)
        {
            r = ReconcilePost11(a, deltas);
            if (r.Changed)
            {
                Log.LogTrace("  [{Name}]: post-lv11 increases recorded: {Deltas}", nb.Name, deltas);
                SetPost11Increases(fp, r.UpdatedPost11Increases);
            }
            else
            {
                Log.LogTrace("  [{Name}]: post-lv11 level-up but no stat deltas detected", nb.Name);
            }
        }
        else if (levelPointsDecreased || giftedSelected)
        {
            r = ConsumePlannedIncreases(a, nb, deltas);
            if (a.StatIncreases == null)
            {
                Log.LogTrace("  [{Name}]: no planned statIncreases — recording level-up without adjustment", nb.Name);
            }
            else if (r.Changed)
            {
                Log.LogTrace("  [{Name}]: saving updated statIncreases", nb.Name);
                SetStatIncreases(fp, r.UpdatedStatIncreases);
            }
            else
            {
                Log.LogTrace("  [{Name}]: leveled up but no planned increases were affected", nb.Name);
            }
        }
        else
        {
            r = new Reconciliation([], [], null, null, false);
        }

        return new LevelUpEvent(
            nb.Name,
            r.Changed,
            levels,
            deltas,
            r.AdjustedStats,
            r.ConsumedIncreases,
            addedPerkIds,
            post11
        );
    }

    // ---- static reconciliation helpers -------------------------------------

    internal static List<KeyValuePair<Stat, int>> StatDeltas(Brother ob, Brother nb)
    {
        // Java EnumMap 迭代序 = 枚举声明序；按 Stat.Values 序构建即天然有序
        var deltas = new List<KeyValuePair<Stat, int>>();
        foreach (var s in Stat.Values)
        {
            var si = s.StatIndex;
            if (si >= nb.Stats.Length || si >= ob.Stats.Length) continue;
            var d = nb.Stats[si] - ob.Stats[si];
            if (d > 0) deltas.Add(new(s, d));
        }
        return deltas;
    }

    internal static int RaisesLowerBound(int statDelta, int statIndex, int stars)
    {
        var range = StatPotentialCalculator.RangeForStars(statIndex, stars);
        var maxPerRoll = range?.Max ?? statDelta;
        return (int)Math.Ceiling((double)statDelta / maxPerRoll);
    }

    internal static List<string> AddedPerks(Brother ob, Brother nb) =>
        nb.PerkIds.Where(id => !ob.PerkIds.Contains(id)).ToList();

    internal static bool IsPost11Reconcile(Brother ob, Brother nb) =>
        ob.LevelTotal >= 11 && nb.LevelTotal > 11;

    internal static Brother? FindMatch(List<Brother> oldList, string fingerprint) =>
        oldList.FirstOrDefault(b => fingerprint == b.Fingerprint);

    internal static int LevelsAssigned(Brother ob, Brother nb, bool gifted)
    {
        var normal = Math.Max(0, ob.LevelPoints - nb.LevelPoints);
        return normal + (gifted ? 1 : 0);
    }

    internal static Reconciliation ReconcilePost11(BrotherAnnotation a, List<KeyValuePair<Stat, int>> deltas)
    {
        if (deltas.Count == 0) return new Reconciliation([], [], null, null, false);
        // Java Arrays.copyOf(x, 8)：短则补零、长则截断
        var post11 = new int[Stat.Values.Length];
        if (a.Post11Increases != null)
        {
            Array.Copy(a.Post11Increases, post11, Math.Min(a.Post11Increases.Length, post11.Length));
        }
        foreach (var (s, v) in deltas)
        {
            post11[s.Ordinal] += v;
        }
        return new Reconciliation([], [], null, post11, true);
    }

    internal static Reconciliation ConsumePlannedIncreases(
        BrotherAnnotation a, Brother nb, List<KeyValuePair<Stat, int>> deltas)
    {
        if (a.StatIncreases == null) return new Reconciliation([], [], null, null, false);
        var statIncreases = (int[])a.StatIncreases.Clone();
        var adjustedStats = new List<Stat>();
        var consumedIncreases = new List<KeyValuePair<Stat, int>>();
        foreach (var (s, delta) in deltas)
        {
            var ord = s.Ordinal;
            var si = s.StatIndex;
            var planned = ord < statIncreases.Length ? statIncreases[ord] : 0;
            if (planned <= 0) continue;
            var stars = si < nb.Stars.Length ? nb.Stars[s.StarIndex] : 0;
            var toConsume = Math.Min(RaisesLowerBound(delta, si, stars), planned);
            statIncreases[ord] = planned - toConsume;
            consumedIncreases.Add(new(s, toConsume));
            adjustedStats.Add(s);
        }
        if (adjustedStats.Count == 0) return new Reconciliation([], [], null, null, false);
        return new Reconciliation(adjustedStats, consumedIncreases, statIncreases, null, true);
    }

    // ---- non-destructive preview reconciliation (for ReplayAnalyzer) -------

    /// <summary>
    /// Non-persisting reconciliation for the replay tool.
    /// Identical in logic to <see cref="ReconcileOnReload"/> but reads planned/post-11
    /// increases from the supplied maps and writes updated values back into them —
    /// no store access, no disk I/O.
    /// </summary>
    /// <param name="plannedByFp">mutable map fingerprint → statIncreases (updated in place)</param>
    /// <param name="post11ByFp">mutable map fingerprint → post11Increases (updated in place)</param>
    public List<LevelUpEvent> PreviewReconcile(
        List<Brother> oldList, List<Brother> newList,
        Dictionary<string, int[]> plannedByFp,
        Dictionary<string, int[]> post11ByFp)
    {
        var events = new List<LevelUpEvent>();
        foreach (var nb in newList)
        {
            var fp = nb.Fingerprint;
            if (fp == null) continue;
            var ob = FindMatch(oldList, fp);
            if (ob == null) continue;
            var e = PreviewReconcileBrother(ob, nb, plannedByFp, post11ByFp);
            if (e != null) events.Add(e);
        }
        return events;
    }

    private static LevelUpEvent? PreviewReconcileBrother(
        Brother ob, Brother nb,
        Dictionary<string, int[]> plannedByFp,
        Dictionary<string, int[]> post11ByFp)
    {
        var fp = nb.Fingerprint!;
        var addedPerkIds = AddedPerks(ob, nb);
        var levelPointsDecreased = nb.LevelPoints < ob.LevelPoints;
        var giftedSelected = addedPerkIds.Contains(GiftedPerkId);

        if (!levelPointsDecreased && addedPerkIds.Count == 0) return null;

        var levels = LevelsAssigned(ob, nb, giftedSelected);
        var deltas = StatDeltas(ob, nb);
        var post11 = levelPointsDecreased && IsPost11Reconcile(ob, nb);
        Reconciliation r;

        if (post11)
        {
            var a = new BrotherAnnotation(fp) { Post11Increases = post11ByFp.GetValueOrDefault(fp) };
            r = ReconcilePost11(a, deltas);
            if (r.Changed) post11ByFp[fp] = r.UpdatedPost11Increases!;
        }
        else if (levelPointsDecreased || giftedSelected)
        {
            var a = new BrotherAnnotation(fp) { StatIncreases = plannedByFp.GetValueOrDefault(fp) };
            r = ConsumePlannedIncreases(a, nb, deltas);
            if (r.Changed) plannedByFp[fp] = r.UpdatedStatIncreases!;
        }
        else
        {
            r = new Reconciliation([], [], null, null, false);
        }

        return new LevelUpEvent(
            nb.Name,
            r.Changed,
            levels,
            deltas,
            r.AdjustedStats,
            r.ConsumedIncreases,
            addedPerkIds,
            post11
        );
    }

    /// <summary>
    /// Reads planned and post-11 stat increases from a <c>.broledger.json</c> file into the
    /// supplied maps. Silently does nothing if the path is null, absent, or unreadable.
    /// Intended for use by the replay analyzer.
    /// </summary>
    public static void ReadAnnotationMaps(
        string? annotPath,
        Dictionary<string, int[]> plannedByFp,
        Dictionary<string, int[]> post11ByFp)
    {
        if (annotPath == null || !File.Exists(annotPath)) return;
        try
        {
            var file = JsonSerializer.Deserialize(
                File.ReadAllText(annotPath), DataJsonContext.Default.AnnotationsFile);
            if (file?.Annotations == null) return;
            foreach (var (fp, ae) in file.Annotations)
            {
                if (ae.StatIncreases != null) plannedByFp[fp] = (int[])ae.StatIncreases.Clone();
                if (ae.Post11Increases != null) post11ByFp[fp] = (int[])ae.Post11Increases.Clone();
            }
        }
        catch (Exception)
        {
            // Unreadable snapshot annotations → leave maps empty (no planned increases known)
        }
    }
}
