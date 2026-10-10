using System.Text.Json.Serialization;

namespace BroLedger.Core.Models;

// Transcribed from Java model/WeaponStats.java
public class WeaponStats
{
    [JsonPropertyName("weaponClass")] public string? WeaponClass { get; set; }
    [JsonPropertyName("tier")] public int? Tier { get; set; }
    [JsonPropertyName("worth")] public int? Worth { get; set; }
    [JsonPropertyName("damageMin")] public int? DamageMin { get; set; }
    [JsonPropertyName("damageMax")] public int? DamageMax { get; set; }
    [JsonPropertyName("ignoresArmor")] public int? IgnoresArmor { get; set; }
    [JsonPropertyName("vsArmor")] public int? VsArmor { get; set; }
}
