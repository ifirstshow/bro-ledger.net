using System.Security.Cryptography;
using System.Text;
using BroLedger.Core.Models;
using BroLedger.Core.Services;
using BroLedger.Core.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BroLedger.Core.Parsing;

// 移植 classes.js 的 battleBrother 构造器
public class BrotherParser
{
    // Wildman / Barbarian 背景 ID——多一个 tattoo 字节
    private const string Wildman = "6DF381C6";
    private const string Barbarian = "CB90AA90";

    private readonly DictionaryService _dict;
    private readonly ItemParser _itemParser;
    private readonly ILogger _logger;

    public BrotherParser(DictionaryService dict, List<string> warnings, ILogger? logger = null)
    {
        _dict = dict;
        _itemParser = new ItemParser(dict, warnings, logger);
        _logger = logger ?? NullLogger.Instance;
    }

    public Brother Parse(HexReader reader, string fullHexData)
    {
        var b = new Brother();
        ParseVisualLayers(reader, b);
        b.Header = ParseHeader(reader, fullHexData);
        ParseActionPointsAndStats(reader, b);
        if (!ParseInventory(reader, b)) return b; // 未知物品 → 部分解析
        var circleCount = ParseCircles(reader, b);
        ParseDescriptions(reader, b);
        ParseTraits(reader, b, circleCount);
        ParseNameAndLevelData(reader, b);
        ParseMoraleModifiers(reader, b);
        ParseStarsAndTalents(reader, b);
        b.Fingerprint = ComputeFingerprint(b);
        return b;
    }

    private void ParseVisualLayers(HexReader reader, Brother b)
    {
        var layerCount = reader.ReadUInt8();
        for (var i = 0; i < layerCount; i++)
        {
            var key = reader.ReadString();
            var value = reader.ReadHexBytes(SaveFormat.VisualLayerValueBytes);
            var sep = reader.ReadHexBytes(SaveFormat.VisualLayerSepBytes);
            b.VisualLayerMap[key] = new Brother.VisualLayer(value, sep);
        }
    }

    private string ParseHeader(HexReader reader, string fullHexData)
    {
        var start = reader.Cursor;
        var memberCount = reader.ReadInt16LE();
        for (var i = 0; i < memberCount; i++)
        {
            var nameLen = reader.ReadInt16LE();
            reader.Skip(nameLen);
            var memberType = reader.ReadHexBytes(1);
            if (memberType == "02")
            {
                reader.Skip(1);
            }
            else if (memberType == "03")
            {
                reader.Skip(reader.ReadUInt16LE());
            }
            else
            {
                reader.Skip(4);
            }
        }
        return fullHexData.Substring(start, reader.Cursor - start).ToUpperInvariant();
    }

    private void ParseActionPointsAndStats(HexReader reader, Brother b)
    {
        b.ActionPoints = reader.ReadUInt8();
        for (var i = 0; i < 8; i++)
        {
            b.Stats[i] = reader.ReadInt16LE();
        }
        b.GreedAndGluttony = reader.ReadHexBytes(SaveFormat.GreedGluttonyBytes);
        b.PouchCount = reader.ReadUInt8();
    }

    /** 未知物品 ID 时返回 false（标记部分解析）。 */
    private bool ParseInventory(HexReader reader, Brother b)
    {
        var itemCount = reader.ReadUInt8();
        for (var i = 0; i < itemCount; i++)
        {
            var slotIndex = reader.ReadUInt8();
            var itemId = reader.ReadHexBytes(4);

            var slot = new InventorySlot(slotIndex);
            slot.ItemId = itemId;

            if (!_dict.Has(itemId))
            {
                _logger.LogWarning("BrotherParser: unknown item id {ItemId} in slot {Slot} — skipping brother remainder", itemId, slotIndex);
                return false;
            }

            slot.ItemType = _dict.GetType(itemId);
            _itemParser.Parse(reader, slot);

            if (slotIndex == 6)
            {
                b.ExtraPouches.Add(slot);
            }
            else if (slotIndex < 7)
            {
                b.EquippedSlots[slotIndex] = slot;
            }
        }
        return true;
    }

    /** 读取 perk 与背景；返回原始 circleCount 供后续 trait 遍历使用。 */
    private int ParseCircles(HexReader reader, Brother b)
    {
        var circleCount = reader.ReadUInt16LE();
        for (var i = 0; i < circleCount; i++)
        {
            var hexId = reader.ReadHexBytes(4);
            reader.Skip(1); // isNew 字节

            if (!_dict.Has(hexId))
            {
                _logger.LogWarning("BrotherParser: unknown circle id {HexId}", hexId);
                continue;
            }

            var type = _dict.GetType(hexId);
            if (type == "background")
            {
                b.BackgroundHexId = hexId;
                break;
            }
            b.PerkIds.Add(hexId);
        }
        return circleCount;
    }

    private void ParseDescriptions(HexReader reader, Brother b)
    {
        b.Description = reader.ReadString();
        b.DescriptionTemplate = reader.ReadString();
        b.Unknown = reader.ReadHexBytes(2);
        b.SalaryMultiplier = reader.ReadFloatLE();

        // Wildman / Barbarian 背景在此处多带一个 tattoo 字节
        if (HasTattooByte(b.BackgroundHexId))
        {
            b.Tattoo = reader.ReadHexBytes(1);
        }
    }

    private void ParseTraits(HexReader reader, Brother b, int circleCount)
    {
        // perk + 背景之后的剩余 circle 条目
        for (var i = b.PerkIds.Count + 1; i < circleCount; i++)
        {
            var traitId = reader.ReadHexBytes(4);

            if (!_dict.Has(traitId))
            {
                _logger.LogWarning("BrotherParser: unknown trait id {TraitId}", traitId);
                b.Traits.Add(new TraitEntry(traitId, null));
                continue;
            }

            var traitType = _dict.GetType(traitId);

            if (traitType != "injury")
            {
                reader.Skip(1); // isNew 字节（injury 没有 isNew）
            }

            var eventData = traitType switch
            {
                "injury"    => reader.ReadHexBytes(SaveFormat.InjuryEventBytes),
                "training"  => reader.ReadHexBytes(SaveFormat.TrainingEventBytes),
                "knowledge" => reader.ReadHexBytes(SaveFormat.KnowledgeEventBytes),
                "learning"  => reader.ReadHexBytes(SaveFormat.LearningEventBytes),
                _           => null,
            };

            b.Traits.Add(new TraitEntry(traitId, eventData));
        }

        // 游戏把本应是 perk 的条目误标为 trait——提升之
        b.Traits.RemoveAll(trait =>
        {
            if (_dict.GetType(trait.Id) == "perk")
            {
                b.PerkIds.Add(trait.Id);
                return true;
            }
            return false;
        });
    }

    private void ParseNameAndLevelData(HexReader reader, Brother b)
    {
        b.Name = reader.ReadString();
        b.Title = reader.ReadString();
        b.LightWound = reader.ReadFloatLE();
        b.Experience = (int)reader.ReadUInt32LE();
        b.HumanBytes = reader.ReadHexBytes(SaveFormat.HumanBytes);
        b.LevelTotal = reader.ReadUInt8();
        b.PerkPoints = reader.ReadUInt8();
        b.PerkUsed = reader.ReadUInt8();
        b.LevelPoints = reader.ReadUInt8();
        b.Morale = reader.ReadFloatLE();
    }

    private void ParseMoraleModifiers(HexReader reader, Brother b)
    {
        var modCountHex = reader.ReadHexBytes(1);
        var modCount = Convert.ToInt32(modCountHex, 16);
        var sb = new StringBuilder(modCountHex);
        for (var i = 0; i < modCount; i++)
        {
            sb.Append(reader.ReadHexBytes(1));
            var textLength = reader.ReadUInt16LE();
            // 重新编码 textLength 为 2 字节 LE hex 供 blob 存储
            sb.Append(HexUtils.UInt16ToHexLE(textLength));
            sb.Append(reader.ReadHexBytes(textLength + SaveFormat.MoraleEntryTailBytes));
        }
        sb.Append(reader.ReadHexBytes(SaveFormat.MoraleBlockTailBytes));
        b.MoraleModifierHex = sb.ToString();
    }

    private void ParseStarsAndTalents(HexReader reader, Brother b)
    {
        for (var i = 0; i < 8; i++)
        {
            b.Stars[i] = reader.ReadUInt8();
        }
        // 每个天赋组：1 字节计数 + 该数量的原始天赋字节
        for (var i = 0; i < 8; i++)
        {
            b.TalentPoints = reader.ReadUInt8(); // 最后的值留在模型上
            b.TalentBytes[i] = reader.ReadHexBytes(b.TalentPoints);
        }
    }

    private static string ComputeFingerprint(Brother b)
    {
        var input = FingerprintInput(b.Name, b.BackgroundHexId, b.Stars);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash, 0, 16); // 前 16 字节 → 32 大写 hex 字符
    }

    /** 背景在存档格式中携带额外 tattoo 字节时返回 true。 */
    internal static bool HasTattooByte(string? backgroundHexId) =>
        backgroundHexId is Wildman or Barbarian;

    /** 由兄弟的身份字段构建 fingerprint 的规范输入串。 */
    internal static string FingerprintInput(string? name, string? backgroundHexId, int[] stars)
    {
        // backgroundHexId 为 null 时追加字面量 "null"（对齐 Java StringBuilder.append(null) 语义）
        var sb = new StringBuilder(name).Append('|').Append(backgroundHexId ?? "null").Append('|');
        foreach (var s in stars) sb.Append(s);
        return sb.ToString();
    }
}
