using BroLedger.Core.Models;
using BroLedger.Core.Parsing;
using BroLedger.Core.Services;
using Xunit;

namespace BroLedger.Core.Tests.Parsing;

// 对真实 .sav 的集成测试。BRO_LEDGER_SAV 指定真实存档路径，缺省用 TestSavefile.sav。
// IClassFixture 保证存档只解析一次（Java @BeforeAll 等价物）。
public class SaveParserTests(SaveParserTests.Fixture fixture)
    : IClassFixture<SaveParserTests.Fixture>
{
    public class Fixture
    {
        public List<Brother> Brothers { get; }
        public DictionaryService Dict { get; } = DictionaryService.Instance;

        public Fixture()
        {
            Dict.LoadFromEmbeddedResource();
            var savPath = Environment.GetEnvironmentVariable("BRO_LEDGER_SAV")
                ?? Path.Combine(AppContext.BaseDirectory, "TestData", "TestSavefile.sav");
            // Java 版同名 if/else——两个解析器无公共接口，三元无法定型
            if (Environment.GetEnvironmentVariable("BRO_LEDGER_HEX_DUMP") is not null)
                Brothers = new Helpers.HexDumpSaveParser(Dict).Parse(savPath);
            else
                Brothers = new SaveParser(Dict).Parse(savPath);
            foreach (var b in Brothers) Console.WriteLine(b.Name + "found");
        }
    }

    private readonly Fixture _f = fixture;

    [Fact]
    public void BrotherCountIsPositive()
    {
        Assert.False(_f.Brothers.Count == 0, "Expected at least one brother in the save");
    }

    [Fact]
    public void FirstBrotherHasName()
    {
        var first = _f.Brothers[0];
        Assert.NotNull(first.Name);
        Assert.False(string.IsNullOrWhiteSpace(first.Name));
    }

    [Fact]
    public void FirstBrotherHasValidStats()
    {
        var first = _f.Brothers[0];
        Assert.True(first.Stats[Stat.Health.StatIndex] > 0,
            $"Health stat should be positive, got: {first.Stats[Stat.Health.StatIndex]}");
        for (var i = 0; i < 8; i++)
            Assert.True(first.Stats[i] is >= 0 and <= 300,
                $"Stat[{i}] out of range: {first.Stats[i]}");
    }

    [Fact]
    public void FirstBrotherHasBackground()
    {
        var first = _f.Brothers[0];
        Assert.NotNull(first.BackgroundHexId);
        Assert.Equal(8, first.BackgroundHexId!.Length);
    }

    [Fact]
    public void FirstBrotherStarsInRange()
    {
        var first = _f.Brothers[0];
        for (var i = 0; i < 8; i++)
            Assert.True(first.Stars[i] is >= 0 and <= 3,
                $"Star[{i}] out of range: {first.Stars[i]}");
    }

    [Fact]
    public void FirstBrotherHasFingerprint()
    {
        var first = _f.Brothers[0];
        Assert.NotNull(first.Fingerprint);
        Assert.Equal(32, first.Fingerprint!.Length);
    }

    [Fact]
    public void PerksAreKnownIds()
    {
        foreach (var b in _f.Brothers)
        foreach (var perkId in b.PerkIds)
            Assert.True(_f.Dict.Has(perkId),
                $"Perk id not in dictionary: {perkId} (brother: {b.Name})");
    }

    [Fact]
    public void PrintRoster()
    {
        // 非断言——人工核对用（对齐 Java printRoster/debugBrother）
        foreach (var b in _f.Brothers)
            Console.WriteLine(
                $"{b.Name,-25} {b.Title,-20}  HP={b.Stats[Stat.Health.StatIndex],-4} " +
                $"MS={b.Stats[Stat.MeleeSkill.StatIndex],-4}  perks=({string.Join(",", b.PerkIds)})  " +
                $"traits=({string.Join(",", b.Traits.Select(t => t.Id))})");
    }
}
