using System.Text.Json.Serialization;

namespace BroLedger.Core.Models;

// Transcribed from Java model/StatModifier.java
public class StatModifier
{
    [JsonPropertyName("hexId")] public string? HexId { get; set; }
    [JsonPropertyName("tier")] public int? Tier { get; set; }           // nullable; 1–7 for perks, null for traits
    [JsonPropertyName("effects")] public List<Effect> Effects { get; set; } = [];
    [JsonPropertyName("description")] public string? Description { get; set; } // nullable; trait/perk icon tooltip

    public class Effect
    {
        [JsonPropertyName("stat")] public string? Stat { get; set; }        // snake_case key (health, resolve, …)
        [JsonPropertyName("percentage")] public int? Percentage { get; set; } // nullable; +25 means +25%
        [JsonPropertyName("points")] public int? Points { get; set; }         // nullable; +5 means +5 pts
    }
}
