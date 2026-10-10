using System.Text.Json;
using System.Text.Json.Nodes;
using BroLedger.Core.Models;

namespace BroLedger.Core.Services;

// Transcribed method-by-method from Java service/WeaponStatsService.java.
// Java's StreamReadFeature.INCLUDE_SOURCE_IN_LOCATION only enriches parse-error
// location info — no C# equivalent needed, omitted.

public sealed class WeaponStatsService
{
    private static WeaponStatsService? _instance;
    public static WeaponStatsService Instance => _instance ??= new();

    private readonly Dictionary<string, WeaponStats> _entries = new(200);

    private WeaponStatsService() { }

    // Legacy {hexId: {weaponClass,…}} 格式（测试缝）。
    // Java load(InputStream) 反序列化成 Map 后无条件存入每个 entry
    // （未知字段被忽略 → 可能产出全 null 的 WeaponStats，仍入库）；
    // 过滤无 weapon 子对象的 entry 是 loadFromEntries 的行为。
    public void Load(Stream json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new IOException("expected json object");
        foreach (var (key, node) in root)
        {
            var ws = (node ?? throw new IOException($"null entry {key}"))
                .Deserialize(DataJsonContext.Default.WeaponStats)
                ?? throw new IOException($"Failed to parse entry {key}");
            _entries[key.ToUpperInvariant()] = ws;
        }
    }

    public void LoadFromEmbeddedResource()
    {
        GameDataService.Instance.LoadFromEmbeddedResource();
        LoadFromEntries(GameDataService.Instance.GetEntries());
    }

    // 只收带 weapon 子对象的 entry（去掉包装后整包反序列化）
    internal void LoadFromEntries(JsonNode? entriesNode)
    {
        if (entriesNode == null) return;
        foreach (var (key, node) in entriesNode.AsObject())
        {
            if (node?["weapon"] is not JsonObject weapon) continue;
            try
            {
                var ws = weapon.Deserialize(DataJsonContext.Default.WeaponStats)
                         ?? throw new JsonException("null weapon stats");
                _entries[key.ToUpperInvariant()] = ws;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse weapon entry {key}", ex);
            }
        }
    }

    public WeaponStats? Get(string? hexId) =>
        hexId != null ? _entries.GetValueOrDefault(hexId.ToUpperInvariant()) : null;

    public int? GetTier(string? hexId) => Get(hexId)?.Tier;

    public string? GetWeaponClass(string? hexId) => Get(hexId)?.WeaponClass;

    public IReadOnlyDictionary<string, WeaponStats> GetAll() => _entries;
}
