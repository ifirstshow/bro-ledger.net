using BroLedger.App.Controls;
using BroLedger.App.ViewModels;
using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.Tests.Controls;

/// <summary>
/// Pins for the level-cell guide popup content (Java TooltipFactory.guideContent texts +
/// BrotherOverviewPane.buildPlannedPerkEntries filter/sort and the guideReady predicate).
/// Java has no tests for these — UI test gap; these pin the port instead.
/// </summary>
public class LevelUpGuidePanelTests
{
    public LevelUpGuidePanelTests()
    {
        DictionaryService.Instance.LoadFromEmbeddedResource();
        StatModifierService.Instance.LoadFromEmbeddedResource();
    }

    // ---- verbatim texts (Java guideContent label literals) ------------------

    [Fact]
    public void HeaderText_TwoSpacesBeforeCount()
    {
        Assert.Equal("Roll Priority Guide  (7 levels remaining)",
            LevelUpGuidePanel.HeaderText(7));
    }

    [Fact]
    public void SectionHeaders_Verbatim()
    {
        Assert.Equal("Always pick:", LevelUpGuidePanel.AlwaysPickHeader);
        Assert.Equal("Pick by roll outcome (higher = better):", LevelUpGuidePanel.RollOutcomeHeader);
        Assert.Equal("Planned perks (next pick):", LevelUpGuidePanel.PlannedPerksHeader);
    }

    [Fact]
    public void FormatDeviation_JavaPlusPointOneF()
    {
        Assert.Equal("+1.5", LevelUpGuidePanel.FormatDeviation(1.5));
        Assert.Equal("-0.5", LevelUpGuidePanel.FormatDeviation(-0.5));
        Assert.Equal("+0.0", LevelUpGuidePanel.FormatDeviation(0.0));
    }

    // ---- guideReady predicate (extracted from T3's BuildLevelCell) ----------

    private static OverviewCalc.LevelBudget Budget(int used, int total, bool post11 = false, int cap = 8) =>
        new(post11, cap, total, used, total - used);

    [Fact]
    public void IsGuideReady_BudgetFullyUsed_WithinCap()
    {
        int[] increases = [3, 3, 3, 3, 3, 3, 3, 3];
        Assert.True(LevelUpGuidePanel.IsGuideReady(Budget(24, 24), increases));
    }

    [Fact]
    public void IsGuideReady_PartialBudget_False()
    {
        int[] increases = [3, 3, 3, 3, 3, 3, 3, 3];
        Assert.False(LevelUpGuidePanel.IsGuideReady(Budget(12, 24), increases));
    }

    [Fact]
    public void IsGuideReady_NoIncreases_OrPost11_False()
    {
        int[] increases = [3, 3, 3, 3, 3, 3, 3, 3];
        Assert.False(LevelUpGuidePanel.IsGuideReady(Budget(24, 24), null));
        Assert.False(LevelUpGuidePanel.IsGuideReady(Budget(24, 24, post11: true), increases));
    }

    [Fact]
    public void IsGuideReady_RollBeyondCap_False()
    {
        int[] increases = [9, 3, 3, 3, 3, 3, 3, 0];
        Assert.False(LevelUpGuidePanel.IsGuideReady(Budget(27, 27, cap: 8), increases));
    }

    // ---- planned perk entries (buildPlannedPerkEntries filter + sort) -------

    private static Brother BrotherWithPlan(Dictionary<string, string> plan)
    {
        var b = new Brother { Name = "Horik", LevelTotal = 3, PerkUsed = 98,
            Fingerprint = "perk-plan-test-fp" };
        b.PerkIds.Add("C0FFEE01"); // owned — filtered out even if planned
        AnnotationService.Instance.SetPerkPlanStatus(b.Fingerprint, plan);
        return b;
    }

    /// <summary>Perk hex ids that carry a tier in the real game data (all perks do;
    /// the filter just keeps the test honest if data ever gains a tier-less entry).</summary>
    private static List<string> TieredPerks(int count) =>
        DictionaryService.Instance.GetAllByType("perk").Select(e => e.Key)
            .Where(h => StatModifierService.Instance.GetTier(h) != null)
            .Take(count).ToList();

    [Fact]
    public void PlannedPerks_PlannedBeforeOptional_AlphabeticalWithin()
    {
        var perks = TieredPerks(3);
        var b = BrotherWithPlan(new Dictionary<string, string>
        {
            [perks[0]] = "OPTIONAL",
            [perks[1]] = "PLANNED",
            [perks[2]] = "PLANNED",
        });

        var panel = new LevelUpGuidePanel(UiContext.Defaults(), new OverviewViewModel(), _ => null);
        var result = panel.BuildPlannedPerks(b);

        Assert.Equal(3, result.Count);
        // Within a status block: alphabetical by dictionary name (Java's lowercase
        // compareTo tiebreaker); PLANNED block strictly before OPTIONAL.
        Assert.Equal(new[] { perks[1], perks[2] }.OrderBy(h => DictionaryService.Instance.GetName(h),
            StringComparer.Ordinal), result.Take(2).Select(p => p.HexId));
        Assert.Equal(new[] { perks[0] }, result.Skip(2).Select(p => p.HexId));
    }

    [Fact]
    public void PlannedPerks_Owned_Filtered()
    {
        var perk = DictionaryService.Instance.GetAllByType("perk").First().Key;
        var b = BrotherWithPlan(new Dictionary<string, string> { [perk] = "PLANNED" });
        b.PerkIds.Add(perk); // owned

        var panel = new LevelUpGuidePanel(UiContext.Defaults(), new OverviewViewModel(), _ => null);
        Assert.Empty(panel.BuildPlannedPerks(b));
    }

    [Fact]
    public void PlannedPerks_BeyondNextPickTierCap_Filtered()
    {
        var statModifier = StatModifierService.Instance;
        var perk = DictionaryService.Instance.GetAllByType("perk")
            .Select(e => e.Key)
            .FirstOrDefault(h => (statModifier.GetTier(h) ?? 99) > 1);
        Assert.NotNull(perk); // the game has higher-tier perks

        var b = BrotherWithPlan(new Dictionary<string, string> { [perk!] = "PLANNED" });
        b.PerkUsed = 0; // tierCap = 1 — a tier>1 perk is out of reach

        var panel = new LevelUpGuidePanel(UiContext.Defaults(), new OverviewViewModel(), _ => null);
        Assert.Empty(panel.BuildPlannedPerks(b));
    }

    [Fact]
    public void PlannedPerks_NonPickableStatuses_Excluded()
    {
        var perk = DictionaryService.Instance.GetAllByType("perk").First().Key;
        var b = BrotherWithPlan(new Dictionary<string, string> { [perk] = "NOT" });

        var panel = new LevelUpGuidePanel(UiContext.Defaults(), new OverviewViewModel(), _ => null);
        Assert.Empty(panel.BuildPlannedPerks(b));
    }

    [Fact]
    public void PlannedPerks_EmptyPlan_Empty()
    {
        var panel = new LevelUpGuidePanel(UiContext.Defaults(), new OverviewViewModel(), _ => null);
        Assert.Empty(panel.BuildPlannedPerks(BrotherWithPlan(new Dictionary<string, string>())));
    }

    // ---- guide data assembly (stars/role → RollPriorityGuideEntries) --------

    [Fact]
    public void BuildData_FullyAllocated_ProducesFixedEntriesInStatOrder()
    {
        // levelTotal 10 → one level-up left (cap 1, budget 3); the three stats that
        // consumed it have roll == levelsRemaining → fixed, in Stat.Values order.
        var b = new Brother { Name = "Horik", LevelTotal = 10, Fingerprint = "guide-test-fp" };
        for (var i = 0; i < 8; i++) b.Stars[i] = 2;
        var increases = new[] { 1, 1, 1, 0, 0, 0, 0, 0 };
        AnnotationService.Instance.SetStatIncreases(b.Fingerprint, increases);

        var panel = new LevelUpGuidePanel(UiContext.Defaults(), new OverviewViewModel(), _ => null);
        var (entries, remaining, perks) = panel.BuildData(b);

        Assert.NotNull(entries);
        Assert.Equal(1, remaining);
        Assert.All(entries!, e => Assert.True(e.Fixed));
        Assert.Equal(Stat.Values.Take(3).Select(s => s.DisplayName),
            entries!.Select(e => e.Stat.DisplayName));
    }
}
