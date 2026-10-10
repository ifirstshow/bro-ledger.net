using System.Text.Json.Serialization;

namespace BroLedger.Core.Models;

// Transcribed method-by-method from Java model/Role.java
public class Role
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; } = "";
    [JsonPropertyName("frontline")] public bool Frontline { get; set; } = true;

    // Per-stat threshold, 0 = not set. StatOrder indexing (same as Brother.stats).
    [JsonPropertyName("targetStats")] public int[]? TargetStats { get; set; } = new int[8];

    // Per-stat priority: 1=P1, 2=P2, 3=P3 (default). StatOrder indexing.
    [JsonPropertyName("priority")] public int[]? Priority { get; set; } = [3, 3, 3, 3, 3, 3, 3, 3];

    // Perk plan template: hex ID → "OPTIONAL" | "PLANNED". Null/absent = NOT. Null map = no template.
    [JsonPropertyName("perkPlanTemplate")] public Dictionary<string, string>? PerkPlanTemplate { get; set; }

    // Provenance of an imported role: the share code it was fetched with and the sender's
    // role id inside that pack. Enables update-in-place when the same pack is re-imported.
    // Null for locally created roles. Never published — RolePack.ForSharing strips it, so
    // codes of previously imported packs can't leak into new packs.
    [JsonPropertyName("sourcePackCode")] public string? SourcePackCode { get; set; }
    [JsonPropertyName("sourceRoleId")] public string? SourceRoleId { get; set; }

    public Role() { }

    /// <summary>
    /// Copies shareable content (everything except <see cref="Id"/> and provenance) from
    /// <paramref name="src"/> into <paramref name="dest"/>, deep-copying arrays and the
    /// perk-plan template.
    /// </summary>
    public static void CopyContentInto(Role src, Role dest)
    {
        dest.Name = src.Name;
        dest.Frontline = src.Frontline;
        dest.TargetStats = src.TargetStats != null ? (int[])src.TargetStats.Clone() : new int[8];
        dest.Priority = src.Priority != null ? (int[])src.Priority.Clone()
                                             : [3, 3, 3, 3, 3, 3, 3, 3];
        dest.PerkPlanTemplate = src.PerkPlanTemplate != null
            ? new Dictionary<string, string>(src.PerkPlanTemplate)
            : null;
    }

    public override string ToString() => Name ?? "";

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj)) return true;
        return obj is Role r && Id != null && Id.Equals(r.Id);
    }

    public override int GetHashCode() => Id?.GetHashCode() ?? 0;
}
