using Avalonia;

namespace BroLedger.App;

internal static class Program
{
    // Dev and production share this single entry point (Java App.launch equivalent).
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Needed for IDE previewer infrastructure.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
