namespace BroLedger.Core.Utils;

public static class AppPaths
{
    public static string DataDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable("BROLEDGER_DATADIR");
        if (!string.IsNullOrWhiteSpace(overrideDir)) return overrideDir;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bro-ledger");
    }
}
