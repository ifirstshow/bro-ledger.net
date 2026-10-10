using BroLedger.Core.Models;
using BroLedger.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BroLedger.Core.Parsing;

/**
 * Ports the JS Forge object. Each method reads item-specific bytes from the reader
 * and populates the slot's Stats. The reader cursor must be positioned immediately
 * after the item's 4-byte ID when these methods are called.
 */
public class ItemParser(DictionaryService dict, List<string> warnings, ILogger? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public void Parse(HexReader reader, InventorySlot slot)
    {
        _logger.LogTrace("Parsing item: {ItemId}", slot.ItemId);
        slot.Stats = new ItemStats();
        switch (ItemTypeKeys.From(slot.ItemType))
        {
            case ItemType.GenericWeapon: GenericWeapon(reader, slot); break;
            case ItemType.NamedWeapon:   NamedWeapon(reader, slot);   break;
            case ItemType.GenericArmor:  GenericArmor(reader, slot);  break;
            case ItemType.NamedArmor:    NamedArmor(reader, slot);    break;
            case ItemType.GenericHelmet: GenericHelmet(reader, slot); break;
            case ItemType.NamedHelmet:   NamedHelmet(reader, slot);   break;
            case ItemType.GenericShield: GenericShield(reader, slot); break;
            case ItemType.NamedShield:   NamedShield(reader, slot);   break;
            case ItemType.Auxiliary:     Auxiliary(reader, slot);     break;
            case ItemType.Attachment:    Attachment(reader, slot);    break;
            case ItemType.Unknown:
                warnings.Add($"Unknown item type '{slot.ItemType}' (id {slot.ItemId})");
                break;
        }
        slot.Empty = false;
    }

    // -----------------------------------------------------------------------
    // Forge.generic
    // -----------------------------------------------------------------------
    private void Generic(HexReader reader, InventorySlot slot)
    {
        var s = slot.Stats!;
        s.Repair     = reader.ReadHexBytes(1);
        s.Icon       = reader.ReadUInt16LE();
        s.Durability = reader.ReadFloatLE();
        reader.Skip(SaveFormat.QuantityHexBytes); // one quantityHex ("0000803F")
        s.MagicNumber = reader.ReadUInt8();
        reader.Skip(SaveFormat.GenericPaddingBytes); // padding added in patch 1.5.1
    }

    // -----------------------------------------------------------------------
    // Forge.genericWeapon
    // -----------------------------------------------------------------------
    private void GenericWeapon(HexReader reader, InventorySlot slot)
    {
        Generic(reader, slot);
        slot.Stats!.Ammo = reader.ReadUInt16LE();
        var entry = dict.Get(slot.ItemId!);
        if (entry?.SubType == "masterworkBow")
        {
            slot.Stats.Name = reader.ReadString();
        }
    }

    // -----------------------------------------------------------------------
    // Forge.genericShield
    // -----------------------------------------------------------------------
    private void GenericShield(HexReader reader, InventorySlot slot)
    {
        Generic(reader, slot);
        var entry = dict.Get(slot.ItemId!);
        if (entry?.SubType == "nobleShield")
        {
            slot.Stats!.House = reader.ReadHexBytes(1);
        }
    }

    // -----------------------------------------------------------------------
    // Forge.attachment (handles both body-armor attachment block and standalone attachments)
    // -----------------------------------------------------------------------
    private void Attachment(HexReader reader, InventorySlot slot)
    {
        var s = slot.Stats!;
        var entry = dict.Get(slot.ItemId!);

        if (entry?.Slot == "body")
        {
            ReadAttachmentBlock(reader, s, AttachmentMode.Standalone);
        }
        else
        {
            reader.Skip(1);  // repair
            s.Icon = reader.ReadUInt16LE();
            reader.Skip(SaveFormat.AttachmentDataBytes);    // quantityHexes, durability, fatigue, magic
            reader.Skip(SaveFormat.AttachmentPaddingBytes); // padding
        }
    }

    // -----------------------------------------------------------------------
    // Forge.bodyArmorStats
    // -----------------------------------------------------------------------
    private void BodyArmorStats(HexReader reader, InventorySlot slot)
    {
        _logger.LogTrace("Parsing bodyArmorStats");
        var s = slot.Stats!;
        s.Fatigue = reader.ReadFloatLE();

        if (s.BaseFatigue == 0 && s.BaseDurability == 0)
        {
            var entry = dict.Get(slot.ItemId!);
            if (slot.ItemType == "genericArmor" && entry != null)
            {
                s.BaseFatigue    = entry.Fatigue    ?? 0;
                s.BaseDurability = entry.Durability ?? 0;
            }
            else
            {
                s.BaseFatigue    = s.Fatigue;
                s.BaseDurability = s.MaxDurability;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Forge.genericArmor
    // -----------------------------------------------------------------------
    private void GenericArmor(HexReader reader, InventorySlot slot)
    {
        _logger.LogTrace("Parsing generic armor");
        var entry = dict.Get(slot.ItemId!);
        var s = slot.Stats!;

        if (entry?.SubType == "nobleArmor")
        {
            _logger.LogTrace("Is Noble armor");
            s.House = reader.ReadHexBytes(1);
        }

        var isBody = entry?.Slot == "body";
        if (isBody)
        {
            ReadAttachmentBlock(reader, s, AttachmentMode.BodyArmorInline);
        }

        Generic(reader, slot);
        _logger.LogTrace("Parsing durability");
        s.Durability = reader.ReadFloatLE(); // second durability read (overwrite)

        if (isBody)
        {
            BodyArmorStats(reader, slot);
        }

        if (entry?.SubType == "davkul")
        {
            s.Description = reader.ReadString();
        }
    }

    // -----------------------------------------------------------------------
    // Forge.genericHelmet — identical to genericArmor, but fatigue is not
    // stored in the save file for helmets; use the dictionary base value.
    // -----------------------------------------------------------------------
    private void GenericHelmet(HexReader reader, InventorySlot slot)
    {
        GenericArmor(reader, slot);
        var entry = dict.Get(slot.ItemId!);
        if (entry?.Fatigue is not null)
        {
            slot.Stats!.Fatigue = entry.Fatigue.Value;
        }
    }

    // -----------------------------------------------------------------------
    // Forge.namedWeapon
    // -----------------------------------------------------------------------
    private void NamedWeapon(HexReader reader, InventorySlot slot)
    {
        var s = slot.Stats!;
        s.Name          = reader.ReadString();
        s.MaxDurability = reader.ReadFloatLE();
        s.Fatigue       = reader.ReadInt8();
        s.DamageMin     = reader.ReadUInt16LE();
        s.DamageMax     = reader.ReadUInt16LE();
        s.DamageArmor   = reader.ReadFloatLE();
        s.HeadChance    = reader.ReadUInt8();
        s.DamageShield  = reader.ReadUInt16LE();
        s.HitBonus      = reader.ReadUInt16LE();
        s.Penetration   = reader.ReadFloatLE();
        s.FatigueUse    = reader.ReadInt16LE();
        s.AmmoMax       = reader.ReadInt16LE();
        reader.Skip(SaveFormat.NamedWeaponZeroFloatBytes); // zero float
        GenericWeapon(reader, slot);
    }

    // -----------------------------------------------------------------------
    // Forge.namedArmor
    // -----------------------------------------------------------------------
    private void NamedArmor(HexReader reader, InventorySlot slot)
    {
        var s = slot.Stats!;
        s.Name          = reader.ReadString();
        s.MaxDurability = reader.ReadFloatLE();
        s.Fatigue       = reader.ReadInt8();
        GenericArmor(reader, slot);
    }

    // -----------------------------------------------------------------------
    // Forge.namedHelmet — identical to namedArmor
    // -----------------------------------------------------------------------
    private void NamedHelmet(HexReader reader, InventorySlot slot)
    {
        NamedArmor(reader, slot);
    }

    // -----------------------------------------------------------------------
    // Forge.namedShield
    // -----------------------------------------------------------------------
    private void NamedShield(HexReader reader, InventorySlot slot)
    {
        var s = slot.Stats!;
        s.MaxDurability = reader.ReadFloatLE();
        GenericShield(reader, slot);
        s.Name       = reader.ReadString();
        s.Fatigue    = reader.ReadInt8();
        s.MDef       = reader.ReadUInt16LE();
        s.RDef       = reader.ReadUInt16LE();
        s.FatigueUse = reader.ReadInt16LE();
    }

    // -----------------------------------------------------------------------
    // Forge.auxiliary
    // -----------------------------------------------------------------------
    private void Auxiliary(HexReader reader, InventorySlot slot)
    {
        Generic(reader, slot);
        var entry = dict.Get(slot.ItemId!);
        var subType = entry?.SubType;

        switch (subType)
        {
            case "ammo":       slot.Stats!.Ammo = reader.ReadUInt16LE(); break;
            case "canine":     slot.Stats!.Name = reader.ReadString();   break;
            case "provisions":
                slot.Stats!.Amount = reader.ReadFloatLE();
                slot.Stats.Expiry  = reader.ReadFloatLE();
                slot.Stats.Bought  = reader.ReadInt16LE();
                break;
            case "commodity":  slot.Stats!.Bought = reader.ReadInt16LE(); break;
        }
    }

    // -----------------------------------------------------------------------
    // Shared attachment-block reader
    // -----------------------------------------------------------------------

    private enum AttachmentMode { Standalone, BodyArmorInline }

    /**
     * Reads the 4-byte attachment ID and, if present, the icon + base stats.
     *
     * Both call sites consume exactly 12 skipped bytes around the icon read, but
     * the icon sits at a different offset in each mode:
     *   STANDALONE        (Attachment() → body-slot item):  skip(1) + icon + skip(9) + skip(2)
     *   BODY_ARMOR_INLINE (GenericArmor() → isBody block):  skip(2) + icon + skip(10)
     * The divergence mirrors the JS source and has not been verified against a real
     * save with both item kinds present — do not collapse the branches until confirmed.
     */
    private void ReadAttachmentBlock(HexReader reader, ItemStats s, AttachmentMode mode)
    {
        s.Attachment = reader.ReadHexBytes(4);
        if (s.Attachment == "00000000") return;
        if (mode == AttachmentMode.Standalone)
        {
            reader.Skip(SaveFormat.AttStandalonePreIcon);
            s.AttachmentIcon = reader.ReadUInt16LE();
            reader.Skip(SaveFormat.AttStandalonePostIcon);
            reader.Skip(SaveFormat.AttStandalonePadding);
        }
        else
        {
            reader.Skip(SaveFormat.AttInlinePreIcon);
            s.AttachmentIcon = reader.ReadUInt16LE();
            reader.Skip(SaveFormat.AttInlinePostIcon);
        }
        s.BaseDurability = reader.ReadFloatLE();
        s.BaseFatigue    = reader.ReadInt16LE(); // Java: (float) readInt16LE()
    }
}
