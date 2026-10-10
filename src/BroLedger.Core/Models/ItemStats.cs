namespace BroLedger.Core.Models;

public class ItemStats
{
    // Common
    public float Durability;
    public float MaxDurability;
    public int Icon;
    public string? IconSet;
    public string? Repair;      // 原始 1 字节 hex
    public int MagicNumber;

    // Armor / helmet
    public float Fatigue;
    public float BaseFatigue;
    public float BaseDurability;
    public string? Attachment;      // 4 字节 hex，"00000000" = 无
    public int AttachmentIcon;
    public string? House;           // 1 字节 hex（noble 护甲/盾）

    // Named item
    public string? Name;

    // Weapon
    public int DamageMin;
    public int DamageMax;
    public float DamageArmor;
    public int HeadChance;
    public int DamageShield;
    public int HitBonus;
    public float Penetration;
    public int FatigueUse;
    public int Ammo;
    public int AmmoMax;

    // Shield
    public int MDef;
    public int RDef;

    // Auxiliary
    public float Amount;    // provisions
    public float Expiry;    // provisions
    public int Bought;      // provisions / commodity

    // Special
    public string? Description; // davkul armor

    public bool HasAttachment() => Attachment is not null && Attachment != "00000000";
}
