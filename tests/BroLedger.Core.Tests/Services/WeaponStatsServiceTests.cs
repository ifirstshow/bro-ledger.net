using System.Text;
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
    public void Load_IgnoresEntriesWithoutWeaponSubObject()
    {
        var svc = WeaponStatsService.Instance;
        svc.Load(new MemoryStream(Encoding.UTF8.GetBytes(
            """{"deadbeef": {"name":"not a weapon"}}""")));
        Assert.Null(svc.Get("DEADBEEF"));
    }
}
