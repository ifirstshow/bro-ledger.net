using BroLedger.Core.Models;

namespace BroLedger.Core.Parsing;

public static class ArmorCalculator
{
    private const int SlotWeapon = 0;
    private const int SlotShield = 1;
    private const int SlotBody = 2;
    private const int SlotHelmet = 3;

    public static int EffectiveArmor(Brother b) =>
        (int)Durability(b, SlotBody) + (int)Durability(b, SlotHelmet);

    public static int TotalFatiguePenalty(Brother b) =>
        (int)BaseFatigue(b, SlotBody)
        + (int)BaseFatigue(b, SlotHelmet)
        + WeaponFatigue(b);

    public static string WeaponDamageRange(Brother b)
    {
        var slot = Slot(b, SlotWeapon);
        if (!HasStats(slot)) return "—";
        var s = slot!.Stats!; // ponytail: HasStats 已判空，! 仅为满足 NRT 流分析
        return FormatDamageRange(s.DamageMin, s.DamageMax);
    }

    internal static bool HasStats(InventorySlot? s) => IsUsableSlot(s) && s!.Stats != null;

    internal static bool IsUsableSlot(InventorySlot? s) => s is not null && !s.Empty;

    internal static bool HasNoDamage(ItemStats s) => s.DamageMin == 0 && s.DamageMax == 0;

    internal static string FormatDamageRange(int min, int max) =>
        min == 0 && max == 0 ? "—" : $"{min} – {max}";

    private static float Durability(Brother b, int idx)
    {
        var s = Slot(b, idx);
        return HasStats(s) ? s!.Stats!.Durability : 0f;
    }

    private static float BaseFatigue(Brother b, int idx)
    {
        var s = Slot(b, idx);
        return HasStats(s) ? s!.Stats!.BaseFatigue : 0f;
    }

    private static int WeaponFatigue(Brother b)
    {
        var s = Slot(b, SlotWeapon);
        return HasStats(s) ? s!.Stats!.FatigueUse : 0;
    }

    private static InventorySlot? Slot(Brother b, int idx)
    {
        if (b.EquippedSlots == null || idx >= b.EquippedSlots.Length) return null;
        var s = b.EquippedSlots[idx];
        return IsUsableSlot(s) ? s : null;
    }
}
