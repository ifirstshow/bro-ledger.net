using System.Text.RegularExpressions;
using BroLedger.Core;
using Xunit;

namespace BroLedger.Core.Tests.Utils;

/// <summary>
/// version.properties is stamped per-build by the msbuild Target in BroLedger.Core.csproj:
/// version = UTC yyyyMMddHHmm (pom maven.build.timestamp semantics, minute precision).
/// These pins would fail against a static placeholder copy (plan 2 T1 shipped "2026-10-11")
/// or a missing resource (AppInfo fallback "").
/// </summary>
public class AppInfoTests
{
    [Fact]
    public void Version_IsInjected_NotPlaceholder() =>
        Assert.True(AppInfo.Version().Length > 0 && !AppInfo.Version().Contains("${"));

    [Fact]
    public void Version_IsUtcMinuteTimestamp_12Digits() =>
        Assert.Matches(@"^\d{12}$", AppInfo.Version());
}
