using Avalonia.Controls;
using BroLedger.Core.Models;
using BroLedger.Core.Parsing;
using BroLedger.Core.Services;

namespace BroLedger.App.Services;

/// <summary>
/// Builds the item-tooltip texts for all displayable types — port of Java ui/TooltipFactory.java.
/// Java wraps each text in a styled <c>Tooltip</c>; here <see cref="Install"/> creates the
/// ToolTip instance with the <c>item-tooltip</c> class (the keeper.css look), because a raw
/// <c>ToolTip.SetTip(control, string)</c> creates an anonymous tooltip that styles can't target.
/// The graphical roll-priority guide builders (Java forLevelPriorityGuide / guideContent /
/// forLevelAllocationIncomplete) mount with T5's guide popup.
/// </summary>
internal static class TooltipFactory
{
    // Java STAT_ICONS — reserved for T5's graphical guide rows; kept as the Java table's twin.
    public static readonly IReadOnlyDictionary<Stat, string> StatIcons = new Dictionary<Stat, string>
    {
        [Stat.Health] = "icon-health.png",
        [Stat.Resolve] = "icon-resolve.png",
        [Stat.Fatigue] = "icon-fatigue.png",
        [Stat.Initiative] = "icon-initiative.png",
        [Stat.MeleeSkill] = "icon-mSkill.png",
        [Stat.RangedSkill] = "icon-rSkill.png",
        [Stat.MeleeDefense] = "icon-mDefense.png",
        [Stat.RangedDefense] = "icon-rDefense.png",
    };

    /// <summary>
    /// Installs a styled item-tooltip. Null/empty text clears any stale tip — Java callers do
    /// <c>control.setTooltip(TooltipFactory.forSlot(...))</c>, where a null tooltip clears.
    /// The class lives on the ToolTip instance, so clearing the tip drops it with it.
    /// </summary>
    public static void Install(Control control, string? text)
    {
        ToolTip.SetTip(control, string.IsNullOrEmpty(text)
            ? null
            : new ToolTip { Content = text, Classes = { "item-tooltip" } });
    }

    // ---- equipped item slot (Java forSlot) ---------------------------------

    /// <summary>Tooltip text for an equipped item slot. Null for empty slots.</summary>
    public static string? ForSlot(InventorySlot? slot)
    {
        if (slot == null || slot.Empty || slot.ItemId == null) return null;
        var dict = DictionaryService.Instance;
        var name = dict.GetName(slot.ItemId);
        var s = slot.Stats;

        if (s?.Name is { Length: > 0 } named) name = named;

        var type = slot.ItemType;
        if (IsWeaponType(type)) return WeaponText(name, s, slot.ItemId, type);
        if (IsArmorType(type)) return ArmorText(name, s, dict);
        if (IsShieldType(type)) return ShieldText(name, s);
        return name;
    }

    // ---- perk / trait / background (Java forHex) -----------------------------

    public static string? ForHex(string? hexId)
    {
        if (hexId == null) return null;
        var name = DictionaryService.Instance.GetName(hexId);
        var mod = StatModifierService.Instance.ByHexId(hexId);
        if (mod?.Description is { Length: > 0 } desc) return name + "\n" + desc;
        return name;
    }

    // ---- stat breakdown (Java forStatBreakdown + appendBreakdown) -------------

    public static string ForStatBreakdown(StatModifierService.Breakdown bd)
    {
        var sb = new System.Text.StringBuilder();
        AppendBreakdown(sb, bd);
        return sb.ToString();
    }

    private static void AppendBreakdown(System.Text.StringBuilder sb, StatModifierService.Breakdown bd)
    {
        sb.Append("基础：").Append(bd.Base);
        AppendContributions(sb, bd.Contributions);
        sb.Append("\n= ").Append(bd.FinalValue);
    }

    private static void AppendContributions(System.Text.StringBuilder sb,
        IReadOnlyList<StatModifierService.Contribution> contributions)
    {
        foreach (var c in contributions)
        {
            sb.Append('\n').Append(c.Name).Append("：");
            if (c.Points is { } pts) sb.Append(pts > 0 ? "+" : "").Append(pts);
            if (c.Percentage is { } pct) sb.Append(pct > 0 ? "+" : "").Append(pct).Append('%');
        }
    }

    // ---- potential (Java forStatPotential) -----------------------------------

    public static string ForStatPotential(StatPotentialCalculator.Potential pot,
        StatModifierService.Breakdown bd)
    {
        var sb = new System.Text.StringBuilder("11 级潜力");
        AppendPotentialBlock(sb, pot, bd);
        return sb.ToString();
    }

    private static void AppendPotentialBlock(System.Text.StringBuilder sb,
        StatPotentialCalculator.Potential pot, StatModifierService.Breakdown bd)
    {
        if (pot.RemainingLevels == 0)
        {
            sb.Append("\n（已达最高等级）");
            return;
        }
        sb.Append("\n基础：").Append(pot.BasePotential);
        AppendContributions(sb, bd.Contributions);
        sb.Append("\n= ").Append(pot.FinalPotential);
        sb.Append("\n范围：").Append(pot.MinBasePotential)
            .Append('–').Append(pot.MaxBasePotential);
        sb.Append("\n（剩余 ").Append(pot.RemainingLevels).Append(" 次投骰）");
    }

    // ---- expected (Java forStatExpected + appendExpectedBlock) -----------------

    public static string ForStatExpected(
        ExpectedStatsCalculator.Expected naive,
        ExpectedStatsCalculator.Expected greedy,
        int usedBudget)
    {
        var sb = new System.Text.StringBuilder("11 级期望");
        AppendExpectedBlock(sb, naive, greedy, usedBudget);
        return sb.ToString();
    }

    private static void AppendExpectedBlock(System.Text.StringBuilder sb,
        ExpectedStatsCalculator.Expected naive, ExpectedStatsCalculator.Expected greedy,
        int usedBudget)
    {
        var remaining = naive.RemainingLevels;
        var totalBudget = 3 * remaining;
        var count = naive.Count;

        if (remaining == 0)
        {
            if (count == 0)
            {
                sb.Append("\n（11 级基线——该属性无 11 级后增量）");
            }
            else
            {
                sb.Append("\n−").Append(count).Append(" 次 11 级后增量");
                sb.Append("\n= ").Append(naive.FinalExpected);
            }
            return;
        }
        if (count == 0)
        {
            sb.Append("\n未分配增量");
            sb.Append("\n可用：").Append(totalBudget - usedBudget)
                .Append(" / ").Append(totalBudget);
        }
        else
        {
            sb.Append("\n已分配：").Append(count).Append(" / 上限 ").Append(remaining);
            AppendExpectedRow(sb, "朴素", naive);
            if (usedBudget == totalBudget)
            {
                if (greedy.FinalExpected == naive.FinalExpected) sb.Append("\n贪心：相同");
                else AppendExpectedRow(sb, "贪心", greedy);
            }
            else
            {
                sb.Append("\n贪心：不适用（分配未完成）");
            }
            sb.Append("\n预算：已用 ").Append(usedBudget).Append(" / ").Append(totalBudget);
        }
        sb.Append("\n（剩余 ").Append(remaining).Append(" 级）");
    }

    private static void AppendExpectedRow(System.Text.StringBuilder sb, string label,
        ExpectedStatsCalculator.Expected exp)
    {
        sb.Append('\n').Append(label).Append("：");
        if (exp.BaseExpected != exp.FinalExpected)
            sb.Append("基础 ").Append(exp.BaseExpected).Append(" → ").Append(exp.FinalExpected);
        else
            sb.Append(exp.FinalExpected);
    }

    // ---- slot text builders (Java weaponText / armorText / shieldText) ---------

    private static string WeaponText(string name, ItemStats? s, string hexId, string? itemType)
    {
        var sb = new System.Text.StringBuilder(name);
        if (s != null)
        {
            if (s.DamageMin > 0 || s.DamageMax > 0)
                sb.Append("\n伤害：").Append(s.DamageMin).Append(" – ").Append(s.DamageMax);
            if (s.DamageArmor > 0)
                sb.Append("\n护甲伤害：").Append(Pct(s.DamageArmor));
            if (s.Penetration > 0)
                sb.Append("\n破甲：").Append(Pct(s.Penetration));
            if (s.FatigueUse > 0)
                sb.Append("\n疲劳消耗：").Append(s.FatigueUse);
            if (s.HitBonus != 0)
                sb.Append("\n命中：").Append(s.HitBonus > 0 ? "+" : "").Append(s.HitBonus);
            if (s.HeadChance > 0)
                sb.Append("\n爆头率：").Append(s.HeadChance);
        }
        if (itemType == "namedWeapon")
        {
            sb.Append("\n名品武器");
        }
        else
        {
            var ws = WeaponStatsService.Instance.Get(hexId);
            if (ws?.WeaponClass != null)
            {
                sb.Append("\n类别：").Append(ws.WeaponClass);
                if (ws.Tier != null) sb.Append("\n品级：").Append(ws.Tier);
            }
        }
        return sb.ToString();
    }

    private static string ArmorText(string name, ItemStats? s, DictionaryService dict)
    {
        if (s == null) return name;
        var sb = new System.Text.StringBuilder(name);
        if (s.Durability > 0)
            sb.Append("\n耐久：").Append((int)s.Durability);
        if (s.Fatigue != 0)
            sb.Append("\n疲劳消耗：").Append((int)s.Fatigue);
        if (s.HasAttachment())
        {
            var att = dict.Get(s.Attachment!);
            if (att?.Name != null) sb.Append("\n+ ").Append(att.Name);
        }
        return sb.ToString();
    }

    private static string ShieldText(string name, ItemStats? s)
    {
        if (s == null) return name;
        var sb = new System.Text.StringBuilder(name);
        if (s.Durability > 0)
            sb.Append("\n耐久：").Append((int)s.Durability);
        if (s.MDef != 0)
            sb.Append("\n近防：+").Append(s.MDef);
        if (s.RDef != 0)
            sb.Append("\n远防：+").Append(s.RDef);
        if (s.FatigueUse > 0)
            sb.Append("\n疲劳消耗：").Append(s.FatigueUse);
        return sb.ToString();
    }

    private static string Pct(double v) => (v * 100).ToString("F0") + "%";

    // Java: t.toLowerCase().contains("...") — OrdinalIgnoreCase keeps that semantics for the
    // camelCase dictionary types (genericWeapon/namedArmor/genericHelmet/genericShield).
    private static bool IsWeaponType(string? t) =>
        t != null && t.Contains("weapon", StringComparison.OrdinalIgnoreCase);
    private static bool IsArmorType(string? t) =>
        t != null && (t.Contains("armor", StringComparison.OrdinalIgnoreCase)
            || t.Contains("helmet", StringComparison.OrdinalIgnoreCase));
    private static bool IsShieldType(string? t) =>
        t != null && t.Contains("shield", StringComparison.OrdinalIgnoreCase);
}
