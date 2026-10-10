using System.Text;
using System.Text.Json.Nodes;
using BroLedger.Core.Models;
using BroLedger.Core.Services;
using Xunit;

namespace BroLedger.Core.Tests.Services;

[Collection("DictionaryService")]
public class DictionaryServiceTests
{
    // 与 Java 一致：测试共享 getInstance()/Instance 单例，用已知条目 Load 填充后断言。
    private static DictionaryService NewService()
    {
        var s = DictionaryService.Instance;
        s.Load(LegacyJson());
        return s;
    }

    private static MemoryStream LegacyJson() => new(Encoding.UTF8.GetBytes("""
        [
          ["80DE01B7", {"name":"Additional Fur Padding","type":"attachment","slot":"attachment","fatigue":-2}],
          ["AB12CD34", {"name":"Test Sword","type":"genericWeapon","slot":"hand","durability":40,"damageMin":20,"damageMax":30,"damageArmor":15.5}]
        ]
        """));

    [Fact]
    public void Load_LegacyPairs_PopulatesUppercaseKeys()
    {
        var svc = NewService();
        Assert.True(svc.Has("80de01b7"));          // 查找键大写归一
        Assert.True(svc.Has("80DE01B7"));
        Assert.False(svc.Has("FFFFFFFF"));
    }

    [Fact]
    public void Get_ReturnsEntryWithFields()
    {
        var svc = NewService();
        var e = svc.Get("AB12CD34");
        Assert.NotNull(e);
        Assert.Equal("Test Sword", e!.Name);
        Assert.Equal("genericWeapon", e.Type);
        Assert.Equal(40, e.Durability);
        Assert.Equal(15.5, e.DamageArmor);
    }

    [Fact]
    public void GetType_ReturnsNullForUnknown()
    {
        var svc = NewService();
        Assert.Equal("attachment", svc.GetType("80DE01B7"));
        Assert.Null(svc.GetType("FFFFFFFF"));
    }

    [Fact]
    public void GetName_FallsBackToHexId() =>
        Assert.Equal("FFFFFFFF", NewService().GetName("FFFFFFFF"));

    [Fact]
    public void LoadFromEntries_FlattensBaseSubObject()
    {
        var svc = NewService();
        var entries = JsonNode.Parse("""
        {
          "80DE01B7": {"name":"Fur Padding","type":"attachment","slot":"attachment","base":{"durability":0,"fatigue":-2},"weapon":{},"modifier":{},"assets":[]},
          "ZZZZZZZZ": {"name":"Plain Armor","type":"genericArmor","slot":"body","fatigue":9}
        }
        """);
        svc.LoadFromEntries(entries);

        var flat = svc.Get("80DE01B7");
        Assert.NotNull(flat);
        Assert.Equal("Fur Padding", flat!.Name);
        Assert.Equal(-2, flat.Fatigue);          // 来自 base 子对象
        Assert.Equal(0, flat.Durability);

        var plain = svc.Get("zzzzzzzz");
        Assert.NotNull(plain);
        Assert.Equal(9, plain!.Fatigue);
    }

    [Fact]
    public void LoadFromEntries_NullOrEmpty_NoOp() =>
        NewService().LoadFromEntries(null); // 不抛即通过
}
