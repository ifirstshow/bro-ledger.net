using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using BroLedger.App.ViewModels;
using BroLedger.Core.Services;

namespace BroLedger.App.Tests.Views;

/// <summary>
/// Diagnostic smoke: MainWindow + real save → is the toolbar toggle host populated and is
/// the overview pane mounted as CenterContent after LoadSave's async continuation?
/// </summary>
[Collection("EnvVar-BroLedgerDataDir")]
public class MainWindowOverviewSmokeTests : IDisposable
{
    private readonly string _dataDir;

    public MainWindowOverviewSmokeTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "broledger-win-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", _dataDir);
        DictionaryService.Instance.LoadFromEmbeddedResource();
        ImageMapService.Instance.LoadFromEmbeddedResource();
        StatModifierService.Instance.LoadFromEmbeddedResource();
        WeaponStatsService.Instance.LoadFromEmbeddedResource();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", null);
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    [AvaloniaFact]
    public void MainWindow_AfterLoadSave_ShowsTogglesAndPane()
    {
        var window = new MainWindow();
        var vm = Assert.IsType<MainViewModel>(window.DataContext);
        vm.LoadSave(Path.Combine(AppContext.BaseDirectory, "TestData", "TestSavefile.sav"));

        // Pump the dispatcher until LoadSave's continuation (parse → apply → ShowOverview)
        // ran: the parse is Task.Run off-thread (~1s for the 7MB fixture), its continuation
        // posts back to the UI-thread sync context.
        HeadlessPump.Until(() => vm.ShowingOverview,
            "vm.ShowingOverview (LoadSave continuation ran ShowOverview)",
            () => $"ShowingOverview={vm.ShowingOverview} OverviewTogglesVisible={vm.OverviewTogglesVisible}");
        Assert.True(vm.OverviewTogglesVisible);

        var host = window.GetControl<StackPanel>("OverviewTogglesHost");
        Assert.True(host.IsVisible);
        Assert.Equal(4, host.Children.Count);

        var center = window.GetControl<ContentControl>("CenterContent");
        Assert.IsType<BroLedger.App.Controls.BrotherOverviewPane>(center.Content);

        vm.StopWatcher();
    }
}
