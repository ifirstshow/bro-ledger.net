namespace BroLedger.Core.Models;

// 双下标约定：StatIndex = Brother.Stats[] 读/显示序；StarIndex = Brother.Stars[] 存档/天赋序（Initiative 在 3）
// Ordinal = Java 枚举声明序（Stat.values() 下标），注解/角色数组的索引契约
public sealed record Stat(int Ordinal, int StatIndex, int StarIndex, string JsonKey, string Abbrev, string DisplayName)
{
    public static readonly Stat Health = new(0, 0, 0, "health", "HP", "Health");
    public static readonly Stat Resolve = new(1, 1, 1, "resolve", "Res", "Resolve");
    public static readonly Stat Fatigue = new(2, 2, 2, "fatigue", "Fat", "Fatigue");
    public static readonly Stat Initiative = new(3, 7, 3, "initiative", "Init", "Initiative");
    public static readonly Stat MeleeSkill = new(4, 3, 4, "melee_skill", "MS", "Melee Skill");
    public static readonly Stat RangedSkill = new(5, 4, 5, "ranged_skill", "RS", "Ranged Skill");
    public static readonly Stat MeleeDefense = new(6, 5, 6, "melee_defense", "MD", "Melee Def");
    public static readonly Stat RangedDefense = new(7, 6, 7, "ranged_defense", "RD", "Ranged Def");

    public static readonly Stat[] Values =
    [
        Health, Resolve, Fatigue, Initiative, MeleeSkill, RangedSkill, MeleeDefense, RangedDefense
    ];
}
