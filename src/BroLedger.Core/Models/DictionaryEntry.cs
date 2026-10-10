using System.Text.Json.Serialization;

namespace BroLedger.Core.Models;

public class DictionaryEntry
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("subType")] public string? SubType { get; set; }
    [JsonPropertyName("slot")] public string? Slot { get; set; }
    [JsonPropertyName("icon")] public int? Icon { get; set; }
    [JsonPropertyName("iconSet")] public string? IconSet { get; set; }

    [JsonPropertyName("durability")] public int? Durability { get; set; }
    [JsonPropertyName("fatigue")] public int? Fatigue { get; set; }
    [JsonPropertyName("fatigueUse")] public int? FatigueUse { get; set; }
    [JsonPropertyName("damageMin")] public int? DamageMin { get; set; }
    [JsonPropertyName("damageMax")] public int? DamageMax { get; set; }
    [JsonPropertyName("damageArmor")] public double? DamageArmor { get; set; }
    [JsonPropertyName("headChance")] public int? HeadChance { get; set; }
    [JsonPropertyName("damageShield")] public int? DamageShield { get; set; }
    [JsonPropertyName("hitBonus")] public int? HitBonus { get; set; }
    [JsonPropertyName("penetration")] public double? Penetration { get; set; }
    [JsonPropertyName("ammoMax")] public int? AmmoMax { get; set; }
}
