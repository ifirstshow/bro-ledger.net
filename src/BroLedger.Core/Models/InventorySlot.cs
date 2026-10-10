namespace BroLedger.Core.Models;

public class InventorySlot(int slotIndex)
{
    public readonly int SlotIndex = slotIndex;
    public bool Empty = true;
    public string? ItemId;    // 大写 8 位 hex
    public string? ItemType;  // 字典类型：genericWeapon、namedArmor 等
    public ItemStats? Stats;
}

// ponytail: Java 允许字段 empty 与静态工厂 empty(int) 同名，C# 同一声明空间不行（CS0102）——
// 工厂改用 C#14 静态扩展成员，调用形状 InventorySlot.Empty(i) 与测试保持不变。
public static class InventorySlotFactory
{
    extension(InventorySlot)
    {
        public static InventorySlot Empty(int slotIndex) => new(slotIndex);
    }
}
