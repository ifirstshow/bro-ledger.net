using System.Text;
using BroLedger.Core.Models;
using BroLedger.Core.Services;
using Xunit;

namespace BroLedger.Core.Tests.Services;

// Transcribed assertion-by-assertion from JAVATEST/service/StatModifierServiceTest.java.
// Seam: loadFromStream(new ByteArrayInputStream(...)) → LoadFromStream(new MemoryStream(...)).
// Collection: shares the static StatModifierService.Instance with StatPotentialCalculatorTests.
[Collection("StatModifierService")]
public class StatModifierServiceTests
{
    private readonly StatModifierService _svc;
    private readonly Brother _brother;

    public StatModifierServiceTests()
    {
        _svc = StatModifierService.Instance;
        _brother = new Brother();
        int[] stats = [60, 40, 80, 55, 30, 20, 15, 70];
        for (var i = 0; i < stats.Length; i++) _brother.Stats[i] = stats[i];
    }

    private void Load(string json) =>
        _svc.LoadFromStream(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void NoModifiers_ReturnsBase()
    {
        Load("""{"modifiers":[]}""");
        var bd = _svc.Compute(_brother, Stat.Health);
        Assert.Equal(60, bd.FinalValue);
        Assert.Empty(bd.Contributions);
    }

    [Fact]
    public void FlatOnly_AddsPoints()
    {
        _brother.PerkIds.Add("AAAAAAAA");
        Load("""
            {"modifiers":[{"hexId":"AAAAAAAA","effects":[{"stat":"health","points":10}]}]}
            """);
        var bd = _svc.Compute(_brother, Stat.Health);
        Assert.Equal(70, bd.FinalValue);
        Assert.Equal(10, bd.Flat);
        Assert.Equal(0, bd.SumPct);
        Assert.Single(bd.Contributions);
    }

    [Fact]
    public void FlatNegative_SubtractsPoints()
    {
        _brother.PerkIds.Add("AAAAAAAA");
        Load("""
            {"modifiers":[{"hexId":"AAAAAAAA","effects":[{"stat":"health","points":-10}]}]}
            """);
        var bd = _svc.Compute(_brother, Stat.Health);
        Assert.Equal(50, bd.FinalValue);
    }

    [Fact]
    public void PercentageOnly_Positive()
    {
        _brother.PerkIds.Add("AAAAAAAA");
        // base 60, +25% → (int)(60 * 1.25) = 75
        Load("""
            {"modifiers":[{"hexId":"AAAAAAAA","effects":[{"stat":"health","percentage":25}]}]}
            """);
        var bd = _svc.Compute(_brother, Stat.Health);
        Assert.Equal(75, bd.FinalValue);
    }

    [Fact]
    public void PercentageOnly_Negative_TruncatesTowardZero()
    {
        _brother.PerkIds.Add("AAAAAAAA");
        // base 10, -25% → (int)(10 * 0.75) = 7 (truncate, not floor)
        _brother.Stats[Stat.Health.StatIndex] = 10;
        Load("""
            {"modifiers":[{"hexId":"AAAAAAAA","effects":[{"stat":"health","percentage":-25}]}]}
            """);
        var bd = _svc.Compute(_brother, Stat.Health);
        Assert.Equal(7, bd.FinalValue);
    }

    [Fact]
    public void FlatThenPercentage_FlatFirst()
    {
        _brother.PerkIds.Add("AAAAAAAA");
        // base 60, +5 flat → 65, then +25% → (int)(65 * 1.25) = 81
        Load("""
            {"modifiers":[{"hexId":"AAAAAAAA","effects":[{"stat":"health","points":5},{"stat":"health","percentage":25}]}]}
            """);
        var bd = _svc.Compute(_brother, Stat.Health);
        Assert.Equal(81, bd.FinalValue);
        Assert.Equal(5, bd.Flat);
        Assert.Equal(25, bd.SumPct);
    }

    [Fact]
    public void MultipleModifiers_PercentagesAdditive()
    {
        _brother.PerkIds.Add("AAAAAAAA");
        _brother.PerkIds.Add("BBBBBBBB");
        // base 60, +25% + +10% = +35% → (int)(60 * 1.35) = 81
        Load("""
            {"modifiers":[
              {"hexId":"AAAAAAAA","effects":[{"stat":"health","percentage":25}]},
              {"hexId":"BBBBBBBB","effects":[{"stat":"health","percentage":10}]}
            ]}
            """);
        var bd = _svc.Compute(_brother, Stat.Health);
        Assert.Equal(81, bd.FinalValue);
        Assert.Equal(35, bd.SumPct);
        Assert.Equal(2, bd.Contributions.Count);
    }

    [Fact]
    public void TraitContributes()
    {
        _brother.Traits.Add(new TraitEntry("CCCCCCCC", null));
        Load("""
            {"modifiers":[{"hexId":"CCCCCCCC","effects":[{"stat":"resolve","points":5}]}]}
            """);
        var bd = _svc.Compute(_brother, Stat.Resolve);
        Assert.Equal(45, bd.FinalValue);
    }

    [Fact]
    public void UnrelatedStat_Ignored()
    {
        _brother.PerkIds.Add("AAAAAAAA");
        // modifier only affects resolve, not health
        Load("""
            {"modifiers":[{"hexId":"AAAAAAAA","effects":[{"stat":"resolve","points":5}]}]}
            """);
        var bd = _svc.Compute(_brother, Stat.Health);
        Assert.Equal(60, bd.FinalValue);
        Assert.Empty(bd.Contributions);
    }

    [Fact]
    public void TruncationBoundary_Positive()
    {
        _brother.PerkIds.Add("AAAAAAAA");
        // base 7, +33% → 7 * 1.33 = 9.31 → (int) = 9
        _brother.Stats[Stat.Health.StatIndex] = 7;
        Load("""
            {"modifiers":[{"hexId":"AAAAAAAA","effects":[{"stat":"health","percentage":33}]}]}
            """);
        Assert.Equal(9, _svc.Compute(_brother, Stat.Health).FinalValue);
    }

    [Fact]
    public void HexIdCaseInsensitive()
    {
        _brother.PerkIds.Add("aaaaaaaa");
        Load("""
            {"modifiers":[{"hexId":"AAAAAAAA","effects":[{"stat":"health","points":3}]}]}
            """);
        Assert.Equal(63, _svc.Compute(_brother, Stat.Health).FinalValue);
    }

    [Fact]
    public void LoadFromEmbeddedResource_DoesNotThrow() =>
        _svc.LoadFromEmbeddedResource(); // 抛出即失败

    // ---- ApplyModifiers (pure helper) ----------------------------------------

    [Fact]
    public void ApplyModifiers_NoModifiers_ReturnsBase() =>
        Assert.Equal(60, StatModifierService.ApplyModifiers(60, 0, 0));

    [Fact]
    public void ApplyModifiers_FlatBonus() =>
        Assert.Equal(65, StatModifierService.ApplyModifiers(60, 5, 0));

    [Fact]
    public void ApplyModifiers_Percentage()
    {
        // 60 * 1.1 = 66.0 → (int) 66
        Assert.Equal(66, StatModifierService.ApplyModifiers(60, 0, 10));
    }

    [Fact]
    public void ApplyModifiers_FlatThenPercent()
    {
        // (60 + 5) * 1.1 = 71.5 → (int) 71
        Assert.Equal(71, StatModifierService.ApplyModifiers(60, 5, 10));
    }

    [Fact]
    public void ApplyModifiers_TruncatesDown()
    {
        // 7 * 1.33 = 9.31 → (int) 9
        Assert.Equal(9, StatModifierService.ApplyModifiers(7, 0, 33));
    }

    [Fact]
    public void ApplyModifiers_NegativePercent()
    {
        // 100 * 0.9 = 90
        Assert.Equal(90, StatModifierService.ApplyModifiers(100, 0, -10));
    }
}
