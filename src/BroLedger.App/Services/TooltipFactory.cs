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
        sb.Append("Base: ").Append(bd.Base);
        AppendContributions(sb, bd.Contributions);
        sb.Append("\n= ").Append(bd.FinalValue);
    }

    private static void AppendContributions(System.Text.StringBuilder sb,
        IReadOnlyList<StatModifierService.Contribution> contributions)
    {
        foreach (var c in contributions)
        {
            sb.Append('\n').Append(c.Name).Append(": ");
            if (c.Points is { } pts) sb.Append(pts > 0 ? "+" : "").Append(pts);
            if (c.Percentage is { } pct) sb.Append(pct > 0 ? "+" : "").Append(pct).Append('%');
        }
    }

    // ---- potential (Java forStatPotential) -----------------------------------

    public static string ForStatPotential(StatPotentialCalculator.Potential pot,
        StatModifierService.Breakdown bd)
    {
        var sb = new System.Text.StringBuilder("Potential at lv 11");
        AppendPotentialBlock(sb, pot, bd);
        return sb.ToString();
    }

    private static void AppendPotentialBlock(System.Text.StringBuilder sb,
        StatPotentialCalculator.Potential pot, StatModifierService.Breakdown bd)
    {
        if (pot.RemainingLevels == 0)
        {
            sb.Append("\n(already at max level)");
            return;
        }
        sb.Append("\nBase: ").Append(pot.BasePotential);
        AppendContributions(sb, bd.Contributions);
        sb.Append("\n= ").Append(pot.FinalPotential);
        sb.Append("\nRange: ").Append(pot.MinBasePotential)
            .Append('–').Append(pot.MaxBasePotential);
        sb.Append("\n(").Append(pot.RemainingLevels).Append(" rolls remaining)");
    }

    // ---- expected (Java forStatExpected + appendExpectedBlock) -----------------

    public static string ForStatExpected(
        ExpectedStatsCalculator.Expected naive,
        ExpectedStatsCalculator.Expected greedy,
        int usedBudget)
    {
        var sb = new System.Text.StringBuilder("Expected at lv 11");
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
                sb.Append("\n(lv11 baseline — no post-lv11 gains on this stat)");
            }
            else
            {
                sb.Append("\n−").Append(count).Append(" post-lv11 increase")
                    .Append(count == 1 ? "" : "s");
                sb.Append("\n= ").Append(naive.FinalExpected);
            }
            return;
        }
        if (count == 0)
        {
            sb.Append("\nNo increases allotted");
            sb.Append("\nAvailable: ").Append(totalBudget - usedBudget)
                .Append(" / ").Append(totalBudget);
        }
        else
        {
            sb.Append("\nAllotted: ").Append(count).Append(" / ").Append(remaining).Append(" max");
            AppendExpectedRow(sb, "Naive ", naive);
            if (usedBudget == totalBudget)
            {
                if (greedy.FinalExpected == naive.FinalExpected) sb.Append("\nGreedy: same");
                else AppendExpectedRow(sb, "Greedy", greedy);
            }
            else
            {
                sb.Append("\nGreedy: n/a (allocation incomplete)");
            }
            sb.Append("\nBudget: ").Append(usedBudget).Append(" / ").Append(totalBudget)
                .Append(" used");
        }
        sb.Append("\n(").Append(remaining).Append(" levels remaining)");
    }

    private static void AppendExpectedRow(System.Text.StringBuilder sb, string label,
        ExpectedStatsCalculator.Expected exp)
    {
        sb.Append('\n').Append(label).Append(": ");
        if (exp.BaseExpected != exp.FinalExpected)
            sb.Append("base ").Append(exp.BaseExpected).Append(" → ").Append(exp.FinalExpected);
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
                sb.Append("\nDamage: ").Append(s.DamageMin).Append(" – ").Append(s.DamageMax);
            if (s.DamageArmor > 0)
                sb.Append("\nArmor%: ").Append(Pct(s.DamageArmor));
            if (s.Penetration > 0)
                sb.Append("\nPenetration: ").Append(Pct(s.Penetration));
            if (s.FatigueUse > 0)
                sb.Append("\nFatigue: ").Append(s.FatigueUse);
            if (s.HitBonus != 0)
                sb.Append("\nHit: ").Append(s.HitBonus > 0 ? "+" : "").Append(s.HitBonus);
            if (s.HeadChance > 0)
                sb.Append("\nHead%: ").Append(s.HeadChance);
        }
        if (itemType == "namedWeapon")
        {
            sb.Append("\nNamed weapon");
        }
        else
        {
            var ws = WeaponStatsService.Instance.Get(hexId);
            if (ws?.WeaponClass != null)
            {
                sb.Append("\nClass: ").Append(ws.WeaponClass);
                if (ws.Tier != null) sb.Append("\nTier: ").Append(ws.Tier);
            }
        }
        return sb.ToString();
    }

    private static string ArmorText(string name, ItemStats? s, DictionaryService dict)
    {
        if (s == null) return name;
        var sb = new System.Text.StringBuilder(name);
        if (s.Durability > 0)
            sb.Append("\nDurability: ").Append((int)s.Durability);
        if (s.Fatigue != 0)
            sb.Append("\nFatigue: ").Append((int)s.Fatigue);
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
            sb.Append("\nDurability: ").Append((int)s.Durability);
        if (s.MDef != 0)
            sb.Append("\nMelee Def: +").Append(s.MDef);
        if (s.RDef != 0)
            sb.Append("\nRanged Def: +").Append(s.RDef);
        if (s.FatigueUse > 0)
            sb.Append("\nFatigue: ").Append(s.FatigueUse);
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
