namespace BroLedger.Core.Models;

public class Brother
{
    // Stats 下标（读/显示序）：[0]HP [1]Res [2]Fat [3]MS [4]RS [5]MD [6]RD [7]Init
    // Stars 下标（天赋序）：[0]HP [1]Res [2]Fat [3]Init [4]MS [5]RS [6]MD [7]RD
    public Dictionary<string, VisualLayer> VisualLayerMap { get; } = new();

    public string? Header;

    public int[] Stats { get; } = new int[8];
    public int[] Stars { get; } = new int[8];
    public string[] TalentBytes { get; } = ["", "", "", "", "", "", "", ""];

    public int TalentPoints;
    public int ActionPoints;

    // 7 固定槽：[0]武器 [1]盾 [2]身甲 [3]盔 [4]饰品 [5]箭袋 [6]囊
    public InventorySlot[] EquippedSlots { get; } = new InventorySlot[7];
    public List<InventorySlot> ExtraPouches { get; } = [];

    public int PouchCount; // 2 或 4

    public string? BackgroundHexId; // 大写 8 位 hex

    public List<string> PerkIds { get; } = [];
    public List<TraitEntry> Traits { get; } = [];

    public string? Name;
    public string? Title;
    public string? Description;
    public string? DescriptionTemplate;

    public int Experience;
    public int LevelTotal;
    public int PerkPoints;
    public int PerkUsed;
    public int LevelPoints;

    public float Morale;
    public float LightWound;
    public float SalaryMultiplier;

    // 不透明数据块
    public string? GreedAndGluttony;    // 6 字节 raw hex
    public string? Unknown;             // 2 字节 raw hex
    public string? HumanBytes;          // 6 字节 raw hex
    public string? MoraleModifierHex;
    public string? Tattoo;              // 1 字节 raw hex，仅 wildman/barbarian

    public string? Fingerprint;

    public sealed record VisualLayer(string Value, string Separator);

    public Brother()
    {
        for (var i = 0; i < 7; i++) EquippedSlots[i] = InventorySlot.Empty(i);
    }
}
