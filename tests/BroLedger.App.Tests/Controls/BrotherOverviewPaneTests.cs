using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using BroLedger.App.Controls;
using BroLedger.App.ViewModels;
using BroLedger.Core.Services;

namespace BroLedger.App.Tests.Controls;

/// <summary>
/// Headless smoke for the T3 overview pane with the real TestSavefile.sav: construction
/// (header column count + one row per brother), the four-toggle cycle rebuilding rows, and
/// a valid column-order commit rebuilding the header. Drag interactions themselves need a
/// real pointer — covered by the manual checklist in task-3-report.md.
/// </summary>
public class BrotherOverviewPaneTests : IDisposable
{
    private readonly string _dataDir;
    private readonly List<string>? _savedOrder;
    private readonly string? _savedLastSave;

    public BrotherOverviewPaneTests()
    {
        // Same redirection trick as OverviewViewModelTests: keep AppConfig/AnnotationService
        // flushes out of the real ~/.bro-ledger.
        _dataDir = Path.Combine(Path.GetTempPath(), "broledger-pane-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", _dataDir);

        _savedOrder = AppConfig.Instance.OverviewColumnOrder;
        _savedLastSave = AppConfig.Instance.LastSaveFilePath;
        AppConfig.Instance.OverviewColumnOrder = null;
        DictionaryService.Instance.LoadFromEmbeddedResource();
        ImageMapService.Instance.LoadFromEmbeddedResource();
        StatModifierService.Instance.LoadFromEmbeddedResource();
        WeaponStatsService.Instance.LoadFromEmbeddedResource();
    }

    public void Dispose()
    {
        AppConfig.Instance.OverviewColumnOrder = _savedOrder;
        AppConfig.Instance.LastSaveFilePath = _savedLastSave;
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", null);
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    private static (MainViewModel Vm, BrotherOverviewPane Pane) BuildPane()
    {
        var vm = new MainViewModel();
        vm.LoadSave(Path.Combine(AppContext.BaseDirectory, "TestData", "TestSavefile.sav"));
        // Parse runs Task.Run off-thread; pump the UI dispatcher until ApplyResult landed so
        // the pane is built from the real roster (otherwise rows would be vacuously 0 == 0).
        for (var i = 0; i < 400 && vm.Brothers.Count == 0; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(25);
        }
        var pane = new BrotherOverviewPane(UiContext.Defaults(), vm.Overview, vm.Brothers);
        return (vm, pane);
    }

    [AvaloniaFact]
    public void RealSave_BuildsHeaderColumnsAndRosterRows()
    {
        var (vm, pane) = BuildPane();
        try
        {
            var header = pane.GetControl<StackPanel>("HeaderHost");
            var rows = pane.GetControl<StackPanel>("RowsHost");

            // Default column layout: 24 header cells (portrait..perks), one row per brother.
            Assert.Equal(24, header.Children.Count);
            Assert.Equal(vm.Brothers.Count, rows.Children.Count);
        }
        finally
        {
            vm.StopWatcher();
        }
    }

    [AvaloniaFact]
    public void FullToggleCycle_RebuildsRowsWithoutThrow()
    {
        var (vm, pane) = BuildPane();
        try
        {
            var rows = pane.GetControl<StackPanel>("RowsHost");

            // Every toggle clicked through its whole cycle: each mode change must refresh
            // the cells (stat/potential/armor/level visuals) without blowing up.
            for (var toggle = 0; toggle < 4; toggle++)
                for (var i = 0; i < OverviewViewModel.Toggles[toggle].Options.Count; i++)
                    vm.Overview.CycleNext(toggle);

            Assert.Equal(vm.Brothers.Count, rows.Children.Count);
        }
        finally
        {
            vm.StopWatcher();
        }
    }

    [AvaloniaFact]
    public void ValidColumnOrderCommit_RebuildsHeaderWithSameColumnCount()
    {
        var (vm, pane) = BuildPane();
        try
        {
            var header = pane.GetControl<StackPanel>("HeaderHost");

            // Swap two stat columns (within-group move passes validation) — the pane's
            // ColumnOrderChanged subscription must rebuild the header from the new order.
            var order = OverviewViewModel.DefaultColumnIds().ToList();
            (order[4], order[5]) = (order[5], order[4]);
            Assert.True(vm.Overview.TryCommitColumnOrder(order));

            Assert.Equal(24, header.Children.Count);
        }
        finally
        {
            vm.StopWatcher();
        }
    }
}
