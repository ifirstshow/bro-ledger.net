using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using BroLedger.App.ViewModels;
using BroLedger.Core;
using BroLedger.Core.Services;

namespace BroLedger.App.Tests.Views;

/// <summary>
/// Smoke anchors for the T2 window shells: main.fxml/setup.fxml structure is compiled into
/// these windows, so instantiating them under the headless platform proves the AXAML wires
/// up (bindings, classes, named controls). Window chrome behavior (drag/resize/buttons) and
/// the file-flow pipeline need a real platform — covered by the manual checklist in
/// task-2-report.md.
/// </summary>
public class MainWindowShellTests
{
    [AvaloniaFact]
    public void MainWindow_HasShellStructure()
    {
        var window = new MainWindow();

        // main.fxml 1:1: title-bar (3 window buttons) / toolbar / center ScrollViewer / status bar.
        Assert.NotNull(window.GetControl<Avalonia.Controls.Border>("TitleBar"));
        Assert.NotNull(window.GetControl<Button>("MinimizeBtn"));
        Assert.NotNull(window.GetControl<Button>("MaximizeBtn"));
        Assert.NotNull(window.GetControl<Button>("CloseBtn"));
        Assert.NotNull(window.GetControl<ScrollViewer>("CenterPane"));
        Assert.NotNull(window.GetControl<TextBlock>("StatusLabel"));

        // MVVM wiring: the shell binds a MainViewModel.
        Assert.IsType<MainViewModel>(window.DataContext);
    }

    [AvaloniaFact]
    public void SetupWindow_HasShellStructureAndGuards()
    {
        var window = new SetupWindow();

        Assert.NotNull(window.GetControl<TextBox>("DatPathField"));
        Assert.NotNull(window.GetControl<TextBox>("OutDirField"));
        Assert.NotNull(window.GetControl<Button>("ExtractBtn"));
        Assert.NotNull(window.GetControl<Button>("SelectExistingBtn"));

        var vm = Assert.IsType<SetupViewModel>(window.DataContext);
        // Guards mirror SetupController: Extract disabled until both paths are filled;
        // out-dir defaults to ~/game-art.
        Assert.False(vm.CanExtract);
        Assert.EndsWith("game-art", vm.OutDir, StringComparison.Ordinal);
    }

    // ---- toolbar Export/Excerpt wiring (Java exportSave / exportExcerpt) ----

    [AvaloniaFact]
    public void ExportAndExcerptButtons_RouteToViewModelExportGuard()
    {
        var window = new MainWindow();
        var vm = Assert.IsType<MainViewModel>(window.DataContext);

        // Excerpt is debug-only (Java buildToolbar L128-131); dev builds are always debug.
        Assert.Equal(AppInfo.IsDebug(), window.GetControl<Button>("ExcerptBtn").IsVisible);

        var saved = AppConfig.Instance.LastSaveFilePath;
        try
        {
            AppConfig.Instance.LastSaveFilePath = null;
            window.GetControl<Button>("ExportBtn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("尚未载入存档——请先打开存档", vm.StatusText);
            Assert.True(vm.StatusIsWarning);

            window.GetControl<Button>("ExcerptBtn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("尚未载入存档——请先打开存档", vm.StatusText);
        }
        finally
        {
            AppConfig.Instance.LastSaveFilePath = saved;
        }
    }

    [AvaloniaFact]
    public void ExportSave_WritesCsvNextToSaveAndReportsStatus()
    {
        DictionaryService.Instance.LoadFromEmbeddedResource();
        StatModifierService.Instance.LoadFromEmbeddedResource();

        var save = Path.Combine(AppContext.BaseDirectory, "TestData", "TestSavefile.sav");
        var csv = Path.Combine(AppContext.BaseDirectory, "TestData", "TestSavefile.csv");
        File.Delete(csv);

        var window = new MainWindow();
        var vm = Assert.IsType<MainViewModel>(window.DataContext);
        var saved = AppConfig.Instance.LastSaveFilePath;
        try
        {
            AppConfig.Instance.LastSaveFilePath = save;
            window.GetControl<Button>("ExportBtn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(File.Exists(csv), "CSV should be written next to the save");
            Assert.Equal("已导出到 TestSavefile.csv", vm.StatusText);
            Assert.False(vm.StatusIsWarning);
        }
        finally
        {
            AppConfig.Instance.LastSaveFilePath = saved;
            File.Delete(csv);
        }
    }
}
