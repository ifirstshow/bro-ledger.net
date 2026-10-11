using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.ViewModels;

/// <summary>
/// UI-free half of the level-up modal (Java LevelUpModalController.buildCard + the
/// three-state branch of MainController.loadSaveQuiet L484-488 and
/// PreferencesController's mode normalization). Window concerns live in LevelUpWindow.
/// </summary>
internal static class LevelUpViewModel
{
    /// <summary>One stat row of a modal card. <see cref="UnderConsumed"/> paints the line
    /// amber (#c87820) when the plan under-covered the level-up.</summary>
    public sealed record StatLine(string Text, bool UnderConsumed);

    /// <summary>One brother's section: name header, stat deltas, perk lines, status text.
    /// <see cref="Adjusted"/> paints the status line green (#78c87a).</summary>
    public sealed record LevelUpCard(
        string? Name,
        IReadOnlyList<StatLine> Stats,
        IReadOnlyList<string> Perks,
        string StatusText,
        bool Adjusted);

    /// <summary>Java buildCard — one card section per level-up event.</summary>
    public static LevelUpCard BuildCard(AnnotationService.LevelUpEvent ev)
    {
        var underConsumed = IsUnderConsumed(ev.ConsumedIncreases, ev.LevelsAssigned);

        var stats = new List<StatLine>();
        foreach (var s in Stat.Values) // Java EnumMap iterates in declaration order
        {
            var delta = ev.StatDeltas.FirstOrDefault(kv => kv.Key == s).Value;
            if (delta <= 0) continue; // Java: null or <= 0 skipped
            var consumed = ev.ConsumedIncreases.FirstOrDefault(kv => kv.Key == s).Value;
            var text = s.DisplayName + "  +" + delta
                + (consumed != 0 ? "  (" + consumed + " planned consumed)" : "");
            stats.Add(new StatLine(text, underConsumed));
        }

        var dict = DictionaryService.Instance;
        var perks = ev.AddedPerkIds.Select(id => "Perk: " + dict.GetName(id)).ToList();

        return new LevelUpCard(ev.Name, stats, perks,
            OverviewCalc.LevelUpStatusText(ev.Post11, ev.Adjusted), ev.Adjusted);
    }

    public static IReadOnlyList<LevelUpCard> BuildCards(
        IReadOnlyList<AnnotationService.LevelUpEvent> events) =>
        events.Select(BuildCard).ToList();

    /// <summary>Java OverviewCalc.isUnderConsumed: consumed total below 3 per assigned
    /// level; nothing consumed → nothing to flag.</summary>
    private static bool IsUnderConsumed(
        IReadOnlyList<KeyValuePair<Stat, int>> consumedIncreases, int levelsAssigned)
    {
        if (consumedIncreases.Count == 0) return false;
        return consumedIncreases.Sum(kv => kv.Value) < 3 * levelsAssigned;
    }

    // ---- modal three-state (AppConfig.LevelUpModalMode) ----------------------

    /// <summary>Java loadSaveQuiet: `!"OFF".equals(lvlMode)` gates opening the modal.
    /// Null/garbage behave like MODAL (Java's else-branch).</summary>
    public static bool ShouldShow(string? mode) => mode != "OFF";

    /// <summary>Java loadSaveQuiet: `"AUTO_CLOSE".equals(lvlMode)` picks the auto-closing
    /// stage variant.</summary>
    public static bool IsAutoClose(string? mode) => mode == "AUTO_CLOSE";

    /// <summary>Java PreferencesController's radio-init normalization: AUTO_CLOSE and OFF
    /// keep their radio; anything else (incl. null) selects Modal.</summary>
    public static string NormalizeModalMode(string? mode) => mode switch
    {
        "AUTO_CLOSE" => "AUTO_CLOSE",
        "OFF" => "OFF",
        _ => "MODAL",
    };
}
