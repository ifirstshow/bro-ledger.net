using BroLedger.Core.Utils;
using Xunit;

namespace BroLedger.Core.Tests.Utils;

[Collection("EnvVar-BroLedgerDataDir")]
public class AppPathsTests : IDisposable
{
    private readonly string? _saved = Environment.GetEnvironmentVariable("BROLEDGER_DATADIR");

    [Fact]
    public void DataDir_OverrideEnvVar_Wins()
    {
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", @"D:\custom-dir");
        Assert.Equal(@"D:\custom-dir", AppPaths.DataDir());
    }

    [Fact]
    public void DataDir_EmptyOverride_FallsBackToHome()
    {
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", "");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.Combine(home, ".bro-ledger"), AppPaths.DataDir());
    }

    [Fact]
    public void AppInfo_IsDebug_DefaultsTrue() => Assert.True(AppInfo.IsDebug());

    public void Dispose() =>
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", _saved);
}
