namespace BroLedger.Core.Models;

/// <summary>
/// Port of JAVA/model/BrotherAnnotation.java. 公共字段语义照抄；
/// 无直接 JSON 契约——落盘经 AnnotationService 的 AnnotationEntry DTO。
/// </summary>
public class BrotherAnnotation
{
    public string? Fingerprint;

    public string? RoleId;       // UUID of assigned Role, or null

    public int? SortIndex;       // manual sort position; null = no manual order

    /// <summary>Per-stat planned level-up increases (lv 1–11), indexed by Stat.Ordinal. Null = all zero.</summary>
    public int[]? StatIncreases;

    /// <summary>Per-stat post-lv11 increases already taken (+1 each), indexed by Stat.Ordinal. Null = all zero.</summary>
    public int[]? Post11Increases;

    /// <summary>Per-brother perk plan: hex ID → "OPTIONAL" | "PLANNED". Null map = no plan set.</summary>
    public Dictionary<string, string>? PerkPlanStatus;

    public BrotherAnnotation() { }

    public BrotherAnnotation(string fingerprint)
    {
        Fingerprint = fingerprint;
    }
}
