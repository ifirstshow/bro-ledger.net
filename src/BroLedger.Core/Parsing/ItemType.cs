namespace BroLedger.Core.Parsing;

public enum ItemType
{
    GenericWeapon,
    NamedWeapon,
    GenericArmor,
    NamedArmor,
    GenericHelmet,
    NamedHelmet,
    GenericShield,
    NamedShield,
    Auxiliary,
    Attachment,
    Unknown,
}

// Java ItemType.fromString 的等价静态工厂；null/未知 → Unknown
public static class ItemTypeKeys
{
    public static ItemType From(string? key) => key switch
    {
        "genericWeapon" => ItemType.GenericWeapon,
        "namedWeapon" => ItemType.NamedWeapon,
        "genericArmor" => ItemType.GenericArmor,
        "namedArmor" => ItemType.NamedArmor,
        "genericHelmet" => ItemType.GenericHelmet,
        "namedHelmet" => ItemType.NamedHelmet,
        "genericShield" => ItemType.GenericShield,
        "namedShield" => ItemType.NamedShield,
        "auxiliary" => ItemType.Auxiliary,
        "attachment" => ItemType.Attachment,
        _ => ItemType.Unknown,
    };
}
