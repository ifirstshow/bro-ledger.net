using System.Text.Json;
using System.Text.Json.Nodes;
using BroLedger.Core.Models;

namespace BroLedger.Core.Services;

public sealed class DictionaryService
{
    private static DictionaryService? _instance;
    public static DictionaryService Instance => _instance ??= new();

    private readonly Dictionary<string, DictionaryEntry> _entries = new(1200);

    private DictionaryService() { }

    // legacy [[hexId, {fields}], ...] 格式（测试缝）
    public void Load(Stream json)
    {
        var root = JsonNode.Parse(json) ?? throw new IOException("empty json");
        foreach (var pair in root.AsArray())
        {
            var id = pair![0]!.GetValue<string>().ToUpperInvariant();
            var entry = pair[1]!.Deserialize(DataJsonContext.Default.DictionaryEntry)
                ?? throw new IOException($"Failed to parse entry {id}");
            _entries[id] = entry;
        }
    }

    public void LoadFromEmbeddedResource()
    {
        GameDataService.Instance.LoadFromEmbeddedResource();
        LoadFromEntries(GameDataService.Instance.GetEntries());
    }

    // 顶层字段（去掉 base/weapon/modifier/assets）+ base 子对象合并 → DictionaryEntry
    public void LoadFromEntries(JsonNode? entriesNode)
    {
        if (entriesNode == null) return;
        foreach (var (key, node) in entriesNode.AsObject())
        {
            var hexId = key.ToUpperInvariant();
            var flat = new JsonObject();
            if (node is JsonObject obj)
                foreach (var (k, v) in obj)
                    if (k is not ("base" or "weapon" or "modifier" or "assets"))
                        flat[k] = v?.DeepClone();
            if (node?["base"] is JsonObject baseObj)
                foreach (var (k, v) in baseObj)
                    flat[k] = v?.DeepClone();
            try
            {
                var entry = flat.Deserialize(DataJsonContext.Default.DictionaryEntry)
                    ?? throw new JsonException("null entry");
                _entries[hexId] = entry;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse entry {hexId}", ex);
            }
        }
    }

    public bool Has(string hexId) => _entries.ContainsKey(hexId.ToUpperInvariant());

    public DictionaryEntry? Get(string hexId) => _entries.GetValueOrDefault(hexId.ToUpperInvariant());

    public string? GetType(string hexId) => Get(hexId)?.Type;

    public string GetName(string hexId) => Get(hexId)?.Name ?? hexId;

    // 按类型过滤、按名称排序（null 排最后）
    public List<KeyValuePair<string, DictionaryEntry>> GetAllByType(string type) =>
        _entries
            .Where(e => type == e.Value.Type)
            .OrderBy(e => e.Value.Name is null ? 1 : 0)
            .ThenBy(e => e.Value.Name, StringComparer.Ordinal)
            .ToList();

    public IReadOnlyDictionary<string, DictionaryEntry> GetAll() => _entries;
}
