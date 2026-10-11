using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;

[assembly: AvaloniaTestApplication(typeof(BroLedger.App.Tests.Resources.TestAppBuilder))]

namespace BroLedger.App.Tests.Resources;

/// <summary>
/// Bootstraps the headless Avalonia runtime for this test assembly — AssetLoader (avares://)
/// reads go through the runtime's service locator, which this initializes.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// Ported from Java Phase3ResourcesTest + Phase4ResourcesTest — verifies resource files
/// are present and non-empty on the avares asset store, without requiring a visible window.
///
/// Porting deviation (documented in plan④): the Java tests asserted FXML well-formedness
/// with an XML parser. On the C# side AXAML well-formedness is proven by the compile-time
/// XAML compiler — if App.axaml / MainWindow.axaml didn't parse, the build would fail.
/// These tests therefore keep only existence + non-empty + key style anchors.
/// </summary>
public class AppResourceTests
{

    // ---- keeper style base (Phase3: keeperCssExistsOnClasspath / keeperCssIsNonEmpty) ----
    // Compiled axaml sources are not embedded verbatim, so "exists and is non-empty" is
    // asserted against the running app: the styles loaded into Application.Styles.

    [AvaloniaFact]
    public void KeeperStyleBaseLoadedIntoApp()
    {
        var selectors = SelectorStrings().ToList();
        foreach (var anchor in new[] { "app-frame", "modal-frame", "title-bar", "window-btn", "status-label" })
            Assert.Contains(selectors, s => s.Contains(anchor));
    }

    // ---- token resources (keeper.css variable segment → Resources dictionary) ----

    [AvaloniaFact]
    public void TokenResourcesRegistered()
    {
        var resources = Application.Current!.Resources;
        Assert.True(resources.TryGetResource("GoldBrush", null, out _));
        Assert.True(resources.TryGetResource("BgAppBrush", null, out _));
        Assert.True(resources.TryGetResource("Cinzel", null, out _));
    }

    // ---- main AXAML (Phase3: mainFxmlExistsOnClasspath; well-formedness is build-proven;
    //      loading the window proves the compiled XAML wires up with tokens and fonts) ----

    [AvaloniaFact]
    public void MainWindowLoadsContent()
    {
        var window = new MainWindow();
        Assert.NotNull(window.Content);
    }

    // ---- stat icon assets (Phase4, icon list per Java test source) ----

    [AvaloniaTheory]
    [InlineData("icon-health.png")]
    [InlineData("icon-resolve.png")]
    [InlineData("icon-fatigue.png")]
    [InlineData("icon-initiative.png")]
    [InlineData("icon-mSkill.png")]
    [InlineData("icon-rSkill.png")]
    [InlineData("icon-mDefense.png")]
    [InlineData("icon-rDefense.png")]
    [InlineData("unknown-trait.png")]
    public void StatImageExistsOnAvaress(string filename)
    {
        var bytes = ReadAsset("avares://BroLedger.App/Assets/" + filename);
        Assert.NotEmpty(bytes);
    }

    // ---- fonts (Phase4 asserts Cinzel-Regular; Bold added — used by title-bar styles) ----

    [AvaloniaTheory]
    [InlineData("Cinzel-Regular.otf")]
    [InlineData("Cinzel-Bold.otf")]
    public void CinzelFontExistsOnAvaress(string filename)
    {
        Assert.NotEmpty(ReadAsset("avares://BroLedger.App/Assets/" + filename));
    }

    // ---- app icon ----

    [AvaloniaFact]
    public void AppIconExistsOnAvaress()
    {
        Assert.NotEmpty(ReadAsset("avares://BroLedger.App/Assets/BroLedger.ico"));
    }

    // ---- helpers -----------------------------------------------------------

    private static IEnumerable<string> SelectorStrings() => SelectorStrings(Application.Current!.Styles);

    private static IEnumerable<string> SelectorStrings(Avalonia.Styling.Styles styles)
    {
        foreach (var style in styles)
        {
            if (style is Avalonia.Styling.Style s && s.Selector is not null)
                yield return s.Selector.ToString()!;
            else if (style is Avalonia.Styling.Styles nested)
                foreach (var inner in SelectorStrings(nested))
                    yield return inner;
        }
    }

    private static byte[] ReadAsset(string uri)
    {
        using var stream = AssetLoader.Open(new Uri(uri));
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
