using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using BroLedger.App.Services;
using BroLedger.Core.Models;
using BroLedger.Core.Services;
using Xunit;

namespace BroLedger.App.Tests.Services;

/// <summary>
/// Content pins for the ForSlot tooltip port against the real embedded game-data.json —
/// the dictionary types are camelCase (genericWeapon / namedArmor), so the type dispatch
/// must be case-insensitive like Java's toLowerCase().contains(...) or every equipment
/// tooltip degrades to the bare item name. Exact strings pin the Java weaponText/armorText
/// output format (TooltipFactory.java L322/L350).
/// </summary>
public class TooltipFactoryTests
{
    public TooltipFactoryTests()
    {
        DictionaryService.Instance.LoadFromEmbeddedResource();
        WeaponStatsService.Instance.LoadFromEmbeddedResource();
    }

    private static InventorySlot Slot(string id, string type, ItemStats stats) =>
        new(0) { Empty = false, ItemId = id, ItemType = type, Stats = stats };

    [Fact]
    public void ForSlot_GenericWeapon_BuildsWeaponText()
    {
        // Real dictionary entry B13235B0 "Ancient Bladed Pike" (weaponClass Polearms, tier 2).
        var text = TooltipFactory.ForSlot(Slot("B13235B0", "genericWeapon",
            new ItemStats { DamageMin = 55, DamageMax = 80 }));
        Assert.Equal("Ancient Bladed Pike\nDamage: 55 – 80\nClass: Polearms\nTier: 2", text);
    }

    [Fact]
    public void ForSlot_NamedArmor_BuildsArmorText()
    {
        // Real dictionary entry 50CC8BAE "Alloy Plate Armor" — the capital A in namedArmor
        // is the whole point: case-sensitive Contains() missed it and returned the bare name.
        var text = TooltipFactory.ForSlot(Slot("50CC8BAE", "namedArmor",
            new ItemStats { Durability = 280, Fatigue = -35 }));
        Assert.Equal("Alloy Plate Armor\nDurability: 280\nFatigue: -35", text);
    }

    [AvaloniaFact]
    public void Install_EmptyText_ClearsStaleTip()
    {
        var target = new Border();
        TooltipFactory.Install(target, "old text");
        var tip = Assert.IsType<ToolTip>(ToolTip.GetTip(target));
        Assert.Equal("old text", tip.Content);
        Assert.Contains("item-tooltip", tip.Classes);

        // Java callers do control.setTooltip(factory.forSlot(...)) — a null tooltip clears.
        TooltipFactory.Install(target, null);
        Assert.Null(ToolTip.GetTip(target));
    }
}
