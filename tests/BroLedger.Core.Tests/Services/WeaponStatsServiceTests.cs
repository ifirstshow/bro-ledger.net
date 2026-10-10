using System.Text;
using System.Text.Json.Nodes;
using BroLedger.Core.Services;
using Xunit;

namespace BroLedger.Core.Tests.Services;

// Java has no WeaponStatsServiceTest — 3 minimal pin tests per task-8 brief.
// Single shared singleton: xUnit runs tests in one class sequentially, so the
// Load calls below cannot interleave (no [Collection] needed).
public class WeaponStatsServiceTests
{
    [Fact]
    public void Load_NormalizesKeysToUpper()
    {
        var svc = WeaponStatsService.Instance;
        svc.Load(new MemoryStream(Encoding.UTF8.GetBytes(
            """{"ab12cd34": {"weaponClass":"1h","tier":2,"damageMin":20}}""")));
        Assert.NotNull(svc.Get("AB12CD34"));
        Assert.Equal("1h", svc.GetWeaponClass("AB12CD34"));
        Assert.Equal(2, svc.GetTier("AB12CD34"));
    }

    [Fact]
    public void Get_UnknownOrNull_ReturnsNull()
    {
        var svc = WeaponStatsService.Instance;
        Assert.Null(svc.Get("FFFFFFFF"));
        Assert.Null(svc.Get(null));
        Assert.Null(svc.GetTier(null));
    }

    [Fact]
    public void Load_StoresAllNullEntry_JavaFaithful()
    {
        var svc = WeaponStatsService.Instance;
        // Java load(InputStream) deserializes into a Map<String, WeaponStats>
        // and stores every entry unconditionally — Jackson drops the unknown
        // "name" property, yielding an all-null WeaponStats that IS stored.
        // Filtering entries without a "weapon" sub-object is loadFromEntries'
        // behavior (see next test), not load's.
        svc.Load(new MemoryStream(Encoding.UTF8.GetBytes(
            """{"deadbeef": {"name":"not a weapon"}}""")));
        var ws = svc.Get("DEADBEEF");
        Assert.NotNull(ws);
        Assert.Null(ws.WeaponClass);
        Assert.Null(ws.Tier);
        Assert.Null(ws.Worth);
        Assert.Null(ws.DamageMin);
        Assert.Null(ws.DamageMax);
        Assert.Null(ws.IgnoresArmor);
        Assert.Null(ws.VsArmor);
    }

    [Fact]
    public void LoadFromEntries_SkipsEntriesWithoutWeaponSubObject()
    {
        var svc = WeaponStatsService.Instance;
        // Fresh keys: the singleton already holds DEADBEEF from Load_Stores
        // above, so filtering must be checked on keys no other test touches.
        var entries = JsonNode.Parse(
            """{"cafebabe": {"name":"not a weapon"}, "ff01": {"weapon": {"weaponClass":"1h","tier":2}}}""");
        svc.LoadFromEntries(entries);
        Assert.Null(svc.Get("CAFEBABE"));
        Assert.Equal("1h", svc.GetWeaponClass("FF01"));
        Assert.Equal(2, svc.GetTier("FF01"));
    }
}
