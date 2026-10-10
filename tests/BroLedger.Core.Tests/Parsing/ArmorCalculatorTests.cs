using BroLedger.Core.Models;
using BroLedger.Core.Parsing;
using Xunit;

namespace BroLedger.Core.Tests.Parsing;

public class ArmorCalculatorTests
{
    private static Brother EmptyBrother() => new();

    private static void Equip(Brother b, int slot, float durability, float baseFatigue)
    {
        var s = b.EquippedSlots[slot];
        s.Empty = false;
        s.ItemId = "DEADBEEF";
        s.Stats = new ItemStats { Durability = durability, BaseFatigue = baseFatigue };
    }

    private static void EquipWeapon(Brother b, int dmgMin, int dmgMax, int fatigueUse)
    {
        var s = b.EquippedSlots[0];
        s.Empty = false;
        s.ItemId = "DEADBEEF";
        s.Stats = new ItemStats { DamageMin = dmgMin, DamageMax = dmgMax, FatigueUse = fatigueUse };
    }

    [Fact]
    public void EffectiveArmor_ZeroWhenNothingEquipped() =>
        Assert.Equal(0, ArmorCalculator.EffectiveArmor(EmptyBrother()));

    [Fact]
    public void EffectiveArmor_SumsBodyAndHelmet()
    {
        var b = EmptyBrother();
        Equip(b, 2, 150f, 0f);
        Equip(b, 3, 60f, 0f);
        Assert.Equal(210, ArmorCalculator.EffectiveArmor(b));
    }

    [Fact]
    public void EffectiveArmor_IgnoresWeaponSlot()
    {
        var b = EmptyBrother();
        EquipWeapon(b, 10, 20, 5);
        Equip(b, 2, 100f, 0f);
        Assert.Equal(100, ArmorCalculator.EffectiveArmor(b));
    }

    [Fact]
    public void FatiguePenalty_ZeroWhenEmpty() =>
        Assert.Equal(0, ArmorCalculator.TotalFatiguePenalty(EmptyBrother()));

    [Fact]
    public void FatiguePenalty_SumsArmorAndWeapon()
    {
        var b = EmptyBrother();
        Equip(b, 2, 150f, 12f);
        Equip(b, 3, 60f, 6f);
        EquipWeapon(b, 10, 20, 8);
        Assert.Equal(26, ArmorCalculator.TotalFatiguePenalty(b));
    }

    [Fact]
    public void DamageRange_DashWhenNoWeapon() =>
        Assert.Equal("—", ArmorCalculator.WeaponDamageRange(EmptyBrother()));

    [Fact]
    public void DamageRange_DashWhenZeroDamage()
    {
        var b = EmptyBrother();
        EquipWeapon(b, 0, 0, 5);
        Assert.Equal("—", ArmorCalculator.WeaponDamageRange(b));
    }

    [Fact]
    public void DamageRange_FormattedCorrectly()
    {
        var b = EmptyBrother();
        EquipWeapon(b, 30, 50, 8);
        Assert.Equal("30 – 50", ArmorCalculator.WeaponDamageRange(b));
    }

    [Fact]
    public void HasStats_NullSlot() => Assert.False(ArmorCalculator.HasStats(null));

    [Fact]
    public void HasStats_EmptySlot() =>
        Assert.False(ArmorCalculator.HasStats(InventorySlot.Empty(0)));

    [Fact]
    public void HasStats_SlotWithoutStats()
    {
        var s = InventorySlot.Empty(0);
        s.Empty = false;
        Assert.False(ArmorCalculator.HasStats(s));
    }

    [Fact]
    public void HasStats_SlotWithStats()
    {
        var s = InventorySlot.Empty(0);
        s.Empty = false;
        s.Stats = new ItemStats();
        Assert.True(ArmorCalculator.HasStats(s));
    }

    [Fact]
    public void IsUsableSlot_Null() => Assert.False(ArmorCalculator.IsUsableSlot(null));

    [Fact]
    public void IsUsableSlot_EmptySlot() =>
        Assert.False(ArmorCalculator.IsUsableSlot(InventorySlot.Empty(0)));

    [Fact]
    public void IsUsableSlot_NonEmpty()
    {
        var s = InventorySlot.Empty(0);
        s.Empty = false;
        Assert.True(ArmorCalculator.IsUsableSlot(s));
    }

    [Fact]
    public void HasNoDamage_TrueWhenBothZero()
    {
        var s = new ItemStats();
        Assert.True(ArmorCalculator.HasNoDamage(s));
    }

    [Fact]
    public void HasNoDamage_FalseWhenMinNonZero()
    {
        var s = new ItemStats { DamageMin = 5 };
        Assert.False(ArmorCalculator.HasNoDamage(s));
    }

    [Fact]
    public void FormatDamageRange_DashWhenBothZero() =>
        Assert.Equal("—", ArmorCalculator.FormatDamageRange(0, 0));

    [Fact]
    public void FormatDamageRange_RangeString() =>
        Assert.Equal("15 – 25", ArmorCalculator.FormatDamageRange(15, 25));
}
