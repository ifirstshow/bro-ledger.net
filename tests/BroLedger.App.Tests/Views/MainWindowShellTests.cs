using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using BroLedger.App.ViewModels;

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
}
