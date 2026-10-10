using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using BroLedger.Core.Models;

namespace BroLedger.Core.Services;

// Transcribed method-by-method from Java service/StatModifierService.java
public sealed class StatModifierService
{
    public sealed record Breakdown(int Base, int Flat, int SumPct, int FinalValue,
                                   IReadOnlyList<Contribution> Contributions);

    public sealed record Contribution(string HexId, string? Name,
                                      int? Percentage, int? Points);

    public sealed class Wrapper
    {
        [JsonPropertyName("modifiers")] public List<StatModifier> Modifiers { get; set; } = [];
    }

    private static StatModifierService? _instance;
    public static StatModifierService Instance => _instance ??= new();

    private readonly Dictionary<string, StatModifier> _byHexId = new();

    private StatModifierService() { }

    public void LoadFromEmbeddedResource()
    {
        var gds = GameDataService.Instance;
        gds.LoadFromEmbeddedResource();
        LoadFromEntries(gds.GetEntries());
    }

    /// <summary>
    /// Populates from the <c>entries</c> node of game-data.json.
    /// Only entries that carry a <c>modifier</c> sub-object are loaded.
    /// </summary>
    internal void LoadFromEntries(JsonNode? entriesNode)
    {
        _byHexId.Clear();
        if (entriesNode == null) return;
        foreach (var (key, node) in entriesNode.AsObject())
        {
            var modNode = node?["modifier"];
            if (modNode == null) continue;
            try
            {
                var mod = modNode.Deserialize(DataJsonContext.Default.StatModifier)
                          ?? throw new JsonException("null modifier");
                mod.HexId = key.ToUpperInvariant();
                _byHexId[mod.HexId] = mod;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse modifier entry {key}", ex);
            }
        }
    }

    /// <summary>
    /// Loads from a <see cref="Stream"/> in the legacy
    /// <c>{modifiers:[{hexId,tier,effects,description}]}</c> format.
    /// Testing seam for unit tests that inject inline JSON.
    /// </summary>
    internal void LoadFromStream(Stream stream)
    {
        var w = JsonSerializer.Deserialize(stream, DataJsonContext.Default.Wrapper)
                ?? throw new JsonException("null wrapper");
        _byHexId.Clear();
        if (w.Modifiers != null)
        {
            foreach (var m in w.Modifiers)
                if (m.HexId != null) _byHexId[m.HexId.ToUpperInvariant()] = m;
        }
    }

    /// <summary>Returns the modifier for the given hex ID, or null if none.</summary>
    public StatModifier? ByHexId(string? hexId) =>
        hexId != null ? _byHexId.GetValueOrDefault(hexId.ToUpperInvariant()) : null;

    /// <summary>Returns the perk tier (1–7) for the given hex ID, or null for traits / unknowns.</summary>
    public int? GetTier(string? hexId) => ByHexId(hexId)?.Tier;

    /// <summary>
    /// Computes the modified stat value for a brother using the brother's own base stat.
    /// Order: flat additions first, then percentage applied to (base + flat).
    /// Multiple percentages are additive. Rounding: (int) cast truncates toward 0.
    /// </summary>
    public Breakdown Compute(Brother b, Stat stat) =>
        ComputeWithBase(b, stat, b.Stats[stat.StatIndex]);

    /// <summary>
    /// Computes a stat breakdown using an explicit base value instead of b.stats[statIdx].
    /// Used to apply the trait/perk pipeline to a projected base (e.g. potential at level 11).
    /// </summary>
    public Breakdown ComputeWithBase(Brother? b, Stat? stat, int baseOverride)
    {
        if (stat == null || b == null)
            return new Breakdown(baseOverride, 0, 0, baseOverride, []);
        var key = stat.JsonKey;

        var contributions = new List<Contribution>();
        foreach (var hexId in b.PerkIds) Accumulate(hexId, key, contributions);
        foreach (var t in b.Traits) Accumulate(t.Id, key, contributions);

        var sumFlat = 0;
        var sumPct = 0;
        foreach (var c in contributions)
        {
            if (c.Points != null) sumFlat += c.Points.Value;
            if (c.Percentage != null) sumPct += c.Percentage.Value;
        }

        var finalValue = ApplyModifiers(baseOverride, sumFlat, sumPct);
        return new Breakdown(baseOverride, sumFlat, sumPct, finalValue, contributions);
    }

    /// <summary>
    /// Core stat modifier formula: flat bonuses applied first, then additive percentages, truncated to int.
    /// <c>(base + flatBonus) × (1 + sumPercent / 100)</c>
    /// </summary>
    internal static int ApplyModifiers(int baseValue, int flatBonus, int sumPercent) =>
        (int)((baseValue + flatBonus) * (1 + sumPercent / 100.0));

    private void Accumulate(string hexId, string statKey, List<Contribution> acc)
    {
        var mod = ByHexId(hexId);
        if (mod == null || mod.Effects == null) return;
        foreach (var e in mod.Effects)
        {
            if (statKey != e.Stat) continue; // Java equals(): null → false
            var name = DictionaryService.Instance.GetName(hexId);
            acc.Add(new Contribution(hexId, name, e.Percentage, e.Points));
        }
    }
}
