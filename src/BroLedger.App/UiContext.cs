using BroLedger.App.Services;
using BroLedger.Core.Services;

namespace BroLedger.App;

/// <summary>
/// Mirrors Java ui/UiContext.java — the bag of singleton services handed to views.
/// Component names follow the Java record's component order/naming (PascalCased).
/// </summary>
internal sealed record UiContext(
    AppConfig AppConfig,
    AnnotationService Annotation,
    RoleService Roles,
    DictionaryService Dict,
    ImageMapService ImageMap,
    ImageCache ImageCache,
    StatModifierService StatModifier,
    WeaponStatsService WeaponStats)
{
    public static UiContext Defaults() => new(
        AppConfig.Instance,
        AnnotationService.Instance,
        RoleService.Instance,
        DictionaryService.Instance,
        ImageMapService.Instance,
        ImageCache.Instance,
        StatModifierService.Instance,
        WeaponStatsService.Instance);
}
