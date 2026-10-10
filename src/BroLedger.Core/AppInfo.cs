namespace BroLedger.Core;

/// <summary>
/// Build-time app metadata, loaded once from the embedded
/// BroLedger.Core.Resources.version.properties resource.
/// debug.enabled defaults to true; only "false" (ignoring case) turns it off.
/// </summary>
public static class AppInfo
{
    private const string ResourceName = "BroLedger.Core.Resources.version.properties";

    private static readonly IReadOnlyDictionary<string, string> Props = Load();

    public static string Version() => Props.GetValueOrDefault("version", "");

    public static bool IsDebug() =>
        !string.Equals(Props.GetValueOrDefault("debug.enabled"), "false", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> Load()
    {
        var props = new Dictionary<string, string>();
        try
        {
            using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null) return props;
            using var reader = new StreamReader(stream);
            for (var line = reader.ReadLine(); line != null; line = reader.ReadLine())
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                var sep = trimmed.IndexOf('=');
                if (sep <= 0) continue;
                props[trimmed[..sep].Trim()] = trimmed[(sep + 1)..].Trim();
            }
        }
        catch
        {
            // ponytail: Java logs a warning here; embedded resource can't realistically fail — swallow.
        }
        return props;
    }
}
