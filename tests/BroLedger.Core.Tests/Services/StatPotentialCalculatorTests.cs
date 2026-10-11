using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.Core.Tests.Services;

// Transcribed assertion-by-assertion from JAVATEST/service/StatPotentialCalculatorTest.java.
// Seam: JUnit @CsvSource table → xUnit [InlineData] rows; record accessors → positional properties.
// Collection: shares the static StatModifierService.Instance with StatModifierServiceTests.
[Collection("StatModifierService")]
public class StatPotentialCalculatorTests
{
    // ---- RangeForStars: wiki table coverage --------------------------------

    [Theory]
    [InlineData(0, 0, 2, 4)] // HP
    [InlineData(0, 1, 3, 4)]
    [InlineData(0, 2, 4, 4)]
    [InlineData(0, 3, 4, 5)]
    [InlineData(1, 0, 2, 4)] // Resolve
    [InlineData(1, 1, 3, 4)]
    [InlineData(1, 2, 4, 4)]
    [InlineData(1, 3, 4, 5)]
    [InlineData(2, 0, 2, 4)] // Fatigue
    [InlineData(2, 1, 3, 4)]
    [InlineData(2, 2, 4, 4)]
    [InlineData(2, 3, 4, 5)]
    [InlineData(3, 0, 1, 3)] // Melee Skill
    [InlineData(3, 1, 2, 3)]
    [InlineData(3, 2, 3, 3)]
    [InlineData(3, 3, 3, 4)]
    [InlineData(4, 0, 2, 4)] // Ranged Skill
    [InlineData(4, 1, 3, 4)]
    [InlineData(4, 2, 4, 4)]
    [InlineData(4, 3, 4, 5)]
    [InlineData(5, 0, 1, 3)] // Melee Defense
    [InlineData(5, 1, 2, 3)]
    [InlineData(5, 2, 3, 3)]
    [InlineData(5, 3, 3, 4)]
    [InlineData(6, 0, 2, 4)] // Ranged Defense
    [InlineData(6, 1, 3, 4)]
    [InlineData(6, 2, 4, 4)]
    [InlineData(6, 3, 4, 5)]
    [InlineData(7, 0, 3, 5)] // Initiative
    [InlineData(7, 1, 4, 5)]
    [InlineData(7, 2, 5, 5)]
    [InlineData(7, 3, 5, 6)]
    public void RangeForStars_MatchesWikiTable(int statIdx, int stars, int expMin, int expMax)
    {
        var r = StatPotentialCalculator.RangeForStars(statIdx, stars);
        Assert.NotNull(r);
        Assert.Equal(expMin, r.Min); // min mismatch for stat= statIdx stars= stars
        Assert.Equal(expMax, r.Max); // max mismatch for stat= statIdx stars= stars
    }

    // ---- ExpectedBasePotential --------------------------------------------

    [Fact]
    public void ExpectedBasePotential_Hp_0Star_Level1()
    {
        // HP, 0★, at level 1: 10 rolls × avg 3.0 = +30
        var result = StatPotentialCalculator.ExpectedBasePotential(50, Stat.Health, 0, 1);
        Assert.Equal(80, result);
    }

    [Fact]
    public void ExpectedBasePotential_Hp_3Star_Level1()
    {
        // HP, 3★, at level 1: range 4-5, avg 4.5 → 10 × 4.5 = 45, round(50+45)=95
        var result = StatPotentialCalculator.ExpectedBasePotential(50, Stat.Health, 3, 1);
        Assert.Equal(95, result);
    }

    [Fact]
    public void ExpectedBasePotential_MSkill_1Star_Level6()
    {
        // Melee Skill, 1★, level 6: 5 rolls × avg 2.5 = +12.5 → round(20+12.5)=33
        var result = StatPotentialCalculator.ExpectedBasePotential(20, Stat.MeleeSkill, 1, 6);
        Assert.Equal(33, result);
    }

    [Fact]
    public void ExpectedBasePotential_ClampsAtLevel11()
    {
        var result = StatPotentialCalculator.ExpectedBasePotential(70, Stat.MeleeSkill, 3, 11);
        Assert.Equal(70, result);
    }

    [Fact]
    public void ExpectedBasePotential_ClampsAboveLevel11()
    {
        var result = StatPotentialCalculator.ExpectedBasePotential(70, Stat.MeleeSkill, 3, 15);
        Assert.Equal(70, result);
    }

    // ---- Initiative uses STAT_INITIATIVE=7 (statOrder) but
    //      STAR_INITIATIVE=3 (talentOrder) — Compute() must accept both ------

    [Fact]
    public void Compute_InitiativeIndexing_UsesCorrectStarOrder()
    {
        var b = new Brother { LevelTotal = 1 };
        b.Stats[Stat.Initiative.StatIndex] = 80;
        b.Stars[Stat.Initiative.StarIndex] = 2; // 2★ Initiative

        var p = StatPotentialCalculator.Compute(b, Stat.Initiative);

        // 2★ Initiative range: 5-5 (avg 5.0); 10 rolls → +50
        Assert.Equal(130, p.BasePotential); // expected base 80+50=130
        Assert.Equal(10, p.RemainingLevels);
    }

    // ---- Compute() applies trait/perk modifiers --------------------------

    [Fact]
    public void Compute_AppliesModifiers()
    {
        var svc = StatModifierService.Instance;
        svc.LoadFromStream(new MemoryStream(
            """
            {"modifiers":[{"hexId":"AABBCCDD","effects":[{"stat":"health","percentage":25}]}]}
            """u8.ToArray()));

        var b = new Brother { LevelTotal = 1 };
        b.Stats[Stat.Health.StatIndex] = 50;
        b.Stars[Stat.Health.StarIndex] = 3; // 3★: range 4-5, avg 4.5 → expectedBase = 95
        b.PerkIds.Add("AABBCCDD");

        var p = StatPotentialCalculator.Compute(b, Stat.Health);

        // finalPotential = (int)(95 * 1.25) = 118
        Assert.Equal(95, p.BasePotential);
        Assert.Equal(118, p.FinalPotential);
        Assert.Equal(10, p.RemainingLevels);
    }

    // ---- RemainingLevels = 0 at level 11 ----------------------------------

    [Fact]
    public void Compute_AtLevel11_RemainingZero()
    {
        var b = new Brother { LevelTotal = 11 };
        b.Stats[Stat.MeleeSkill.StatIndex] = 65;

        var p = StatPotentialCalculator.Compute(b, Stat.MeleeSkill);

        Assert.Equal(0, p.RemainingLevels);
        Assert.Equal(65, p.BasePotential);
        Assert.Equal(65, p.MinBasePotential);
        Assert.Equal(65, p.MaxBasePotential);
    }

    // ---- new pure helpers --------------------------------------------------

    [Fact]
    public void ApplyStars_ZeroStars_Identity()
    {
        var r = StatPotentialCalculator.ApplyStars(new StatPotentialCalculator.Range(2, 4), 0);
        Assert.Equal(2, r.Min);
        Assert.Equal(4, r.Max);
    }

    [Fact]
    public void ApplyStars_OneStar_RaisesMin()
    {
        var r = StatPotentialCalculator.ApplyStars(new StatPotentialCalculator.Range(2, 4), 1);
        Assert.Equal(3, r.Min);
        Assert.Equal(4, r.Max);
    }

    [Fact]
    public void ApplyStars_ThreeStar_RaisesMinAndMax()
    {
        var r = StatPotentialCalculator.ApplyStars(new StatPotentialCalculator.Range(2, 4), 3);
        Assert.Equal(4, r.Min); // +2
        Assert.Equal(5, r.Max); // +1
    }

    [Fact]
    public void ApplyStars_ClampsBelowZero()
    {
        var r = StatPotentialCalculator.ApplyStars(new StatPotentialCalculator.Range(2, 4), -5);
        Assert.Equal(2, r.Min);
        Assert.Equal(4, r.Max);
    }

    [Fact]
    public void ApplyStars_ClampsAboveThree()
    {
        var baseRange = new StatPotentialCalculator.Range(2, 4);
        var r3 = StatPotentialCalculator.ApplyStars(baseRange, 3);
        var r9 = StatPotentialCalculator.ApplyStars(baseRange, 9); // clamped to 3
        Assert.Equal(r3.Min, r9.Min);
        Assert.Equal(r3.Max, r9.Max);
    }

    [Fact]
    public void ProjectBase_AddsRemainingTimesRoll()
    {
        Assert.Equal(60, StatPotentialCalculator.ProjectBase(50, 5, 2)); // 50+5*2=60
        Assert.Equal(50, StatPotentialCalculator.ProjectBase(50, 0, 3)); // no levels left
    }

    [Fact]
    public void ProjectExpectedBase_RoundsCorrectly()
    {
        var r = new StatPotentialCalculator.Range(2, 4); // mean=3.0
        // 50 + 5 * 3.0 = 65.0 → round → 65
        Assert.Equal(65, StatPotentialCalculator.ProjectExpectedBase(50, 5, r));
    }

    [Fact]
    public void ProjectExpectedBase_MatchesRangeForStars0Stars()
    {
        // HEALTH 0★: range(2,4), mean=3. base=40, remaining=4 → round(40+12) = 52
        var r = StatPotentialCalculator.RangeForStars(Stat.Health.StatIndex, 0);
        Assert.NotNull(r);
        Assert.Equal(52, StatPotentialCalculator.ProjectExpectedBase(40, 4, r));
    }
}
