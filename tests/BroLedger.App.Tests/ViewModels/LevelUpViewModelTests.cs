using BroLedger.App.ViewModels;
using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.Tests.ViewModels;

/// <summary>
/// Content pins for the level-up modal card assembly (Java LevelUpModalController.buildCard)
/// and the modal three-state branch decision (Java MainController.loadSaveQuiet L484-488 +
/// PreferencesController's mode normalization). Java has no tests for either — UI test
/// gap; these pin the port instead. Exact strings mirror the Java label texts
/// ("Name  +delta  (n planned consumed)" double spaces included).
/// </summary>
public class LevelUpViewModelTests
{
    public LevelUpViewModelTests() => DictionaryService.Instance.LoadFromEmbeddedResource();

    private static AnnotationService.LevelUpEvent Event(
        IReadOnlyList<KeyValuePair<Stat, int>>? deltas = null,
        IReadOnlyList<KeyValuePair<Stat, int>>? consumed = null,
        IReadOnlyList<string>? perks = null,
        int levelsAssigned = 1,
        bool adjusted = false,
        bool post11 = false) => new(
        "Horik", adjusted, levelsAssigned,
        deltas ?? [new(Stat.Health, 2)],
        [], consumed ?? [], perks ?? [], post11);

    // ---- stat delta lines (buildCard stat-label loop) ----------------------

    [Fact]
    public void StatLine_WithConsumed_ShowsPlannedNote()
    {
        var ev = Event(
            deltas: [new(Stat.Health, 2)],
            consumed: [new(Stat.Health, 1)],
            levelsAssigned: 2);
        var card = LevelUpViewModel.BuildCard(ev);
        var line = Assert.Single(card.Stats);
        Assert.Equal("Health  +2  (1 planned consumed)", line.Text);
    }

    [Fact]
    public void StatLine_WithoutConsumed_ShowsPlainDelta()
    {
        var ev = Event(deltas: [new(Stat.MeleeSkill, 3)]);
        var card = LevelUpViewModel.BuildCard(ev);
        Assert.Equal("Melee Skill  +3", Assert.Single(card.Stats).Text);
    }

    [Fact]
    public void StatLines_NonPositiveDeltas_Skipped()
    {
        var ev = Event(deltas:
        [
            new(Stat.Health, 0),
            new(Stat.Resolve, -1),
            new(Stat.Fatigue, 4),
        ]);
        var card = LevelUpViewModel.BuildCard(ev);
        var line = Assert.Single(card.Stats);
        Assert.Equal("Fatigue  +4", line.Text);
    }

    // ---- under-consumed flag (OverviewCalc.isUnderConsumed wiring) ---------

    [Fact]
    public void UnderConsumed_TotalBelow3xLevels_FlagsStatLines()
    {
        var ev = Event(
            deltas: [new(Stat.Health, 2)],
            consumed: [new(Stat.Health, 1)],
            levelsAssigned: 2); // consumed total 1 < 3*2
        var card = LevelUpViewModel.BuildCard(ev);
        Assert.True(Assert.Single(card.Stats).UnderConsumed);
    }

    [Fact]
    public void UnderConsumed_FullyCovered_NotFlagged()
    {
        var ev = Event(
            deltas: [new(Stat.Health, 2)],
            consumed: [new(Stat.Health, 1), new(Stat.Resolve, 2), new(Stat.Fatigue, 3)],
            levelsAssigned: 2); // consumed total 6 == 3*2
        var card = LevelUpViewModel.BuildCard(ev);
        Assert.False(Assert.Single(card.Stats).UnderConsumed);
    }

    [Fact]
    public void UnderConsumed_NothingConsumed_NotFlagged()
    {
        var card = LevelUpViewModel.BuildCard(Event(consumed: []));
        Assert.False(Assert.Single(card.Stats).UnderConsumed);
    }

    // ---- perk lines (buildCard "Perk: " + dict name) ------------------------

    [Fact]
    public void PerkLines_UseDictionaryNames()
    {
        var perkId = DictionaryService.Instance.GetAllByType("perk").First().Key;
        var card = LevelUpViewModel.BuildCard(Event(perks: [perkId]));
        Assert.Equal(["Perk: " + DictionaryService.Instance.GetName(perkId)], card.Perks);
    }

    [Fact]
    public void PerkLines_Empty_None()
    {
        var card = LevelUpViewModel.BuildCard(Event());
        Assert.Empty(card.Perks);
    }

    // ---- status line (OverviewCalc.levelUpStatusText wiring) ----------------

    [Fact]
    public void StatusText_Pre11NotAdjusted_SaysNoPlannedIncreases()
    {
        var card = LevelUpViewModel.BuildCard(Event());
        Assert.Equal("No planned increases to adjust", card.StatusText);
    }

    [Fact]
    public void StatusText_Pre11Adjusted_SaysAdjusted()
    {
        var card = LevelUpViewModel.BuildCard(Event(adjusted: true));
        Assert.Equal("Planned increases adjusted ✓", card.StatusText);
        Assert.True(card.Adjusted); // the green-colour flag rides Adjusted
    }

    [Fact]
    public void StatusText_Post11_BothVariants()
    {
        Assert.Equal("Post-lv11 level-up detected",
            LevelUpViewModel.BuildCard(Event(post11: true)).StatusText);
        Assert.Equal("Post-lv11 increases recorded ✓",
            LevelUpViewModel.BuildCard(Event(post11: true, adjusted: true)).StatusText);
    }

    // ---- name header + card list --------------------------------------------

    [Fact]
    public void Name_CarriesEventName()
    {
        Assert.Equal("Horik", LevelUpViewModel.BuildCard(Event()).Name);
    }

    [Fact]
    public void BuildCards_OneCardPerEvent()
    {
        var cards = LevelUpViewModel.BuildCards([Event(), Event(levelsAssigned: 3)]);
        Assert.Equal(2, cards.Count);
    }

    // ---- three-state branch (loadSaveQuiet + PreferencesController) ----------

    [Theory]
    [InlineData("MODAL", true, false)]
    [InlineData("AUTO_CLOSE", true, true)]
    [InlineData("OFF", false, false)]
    [InlineData("garbage", true, false)]
    [InlineData(null, true, false)]
    public void ThreeState_Branch(string? mode, bool shouldShow, bool autoClose)
    {
        Assert.Equal(shouldShow, LevelUpViewModel.ShouldShow(mode));
        Assert.Equal(autoClose, LevelUpViewModel.IsAutoClose(mode));
    }

    [Theory]
    [InlineData("MODAL", "MODAL")]
    [InlineData("AUTO_CLOSE", "AUTO_CLOSE")]
    [InlineData("OFF", "OFF")]
    [InlineData("garbage", "MODAL")]
    [InlineData(null, "MODAL")]
    public void NormalizeModalMode_PreferencesRadioInit(string? mode, string expected)
    {
        Assert.Equal(expected, LevelUpViewModel.NormalizeModalMode(mode));
    }
}
