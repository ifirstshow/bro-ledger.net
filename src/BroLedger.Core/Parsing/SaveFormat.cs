namespace BroLedger.Core.Parsing;

// Battle Brothers 存档二进制格式的字节常量，全部单位为字节
internal static class SaveFormat
{
    // ---- 视觉层 (BrotherParser.ParseVisualLayers) --------------------------
    public const int VisualLayerValueBytes = 12;
    public const int VisualLayerSepBytes = 53;

    // ---- 属性区 (BrotherParser.ParseActionPointsAndStats) ------------------
    public const int GreedGluttonyBytes = 6;

    // ---- 特性事件数据 (BrotherParser.ParseTraits) --------------------------
    public const int InjuryEventBytes = 14;
    public const int TrainingEventBytes = 37;
    public const int KnowledgeEventBytes = 12;
    public const int LearningEventBytes = 2;

    // ---- 名字后区域 (BrotherParser.ParseNameAndLevelData) ------------------
    public const int HumanBytes = 6;

    // ---- 士气修正 (BrotherParser.ParseMoraleModifiers) ----------------------
    public const int MoraleEntryTailBytes = 4;
    public const int MoraleBlockTailBytes = 8;

    // ---- 通用物品块 (ItemParser.Generic) -----------------------------------
    public const int QuantityHexBytes = 4;
    public const int GenericPaddingBytes = 2;

    // ---- 非身体部位挂件 (ItemParser.Attachment else 分支) -------------------
    public const int AttachmentDataBytes = 15;
    public const int AttachmentPaddingBytes = 2;

    // ---- 挂件块 (ItemParser.ReadAttachmentBlock) ---------------------------
    public const int AttStandalonePreIcon = 1;
    public const int AttStandalonePostIcon = 9;
    public const int AttStandalonePadding = 2;
    public const int AttInlinePreIcon = 2;
    public const int AttInlinePostIcon = 10;

    // ---- 命名武器 (ItemParser.NamedWeapon) ---------------------------------
    public const int NamedWeaponZeroFloatBytes = 4;
}
