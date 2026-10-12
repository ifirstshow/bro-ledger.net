using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using BroLedger.App.ViewModels;
using Xunit;
using BroLedger.App.Tests;

namespace BroLedger.App.Tests.Views;

public class SetupCanExtractProbe
{
    [AvaloniaFact]
    public void Vm_CanExtract_True_WhenBothPathsSet()
    {
        var vm = new SetupViewModel();
        Assert.False(vm.CanExtract);
        vm.DatPath = @"C:\x\data_001.dat";
        Assert.True(vm.CanExtract, "CanExtract should be true with DatPath set and default OutDir");
    }

    // Pins the live-app bug surfaced by the win-x64 AOT rehearsal: ExtractBtn rendered
    // disabled although the IsEnabled binding tracked CanExtract correctly. Avalonia's
    // ButtonBase folds Command.CanExecute into IsEffectivelyEnabled (IsEnabledCore), and
    // CommunityToolkit RelayCommand only raises CanExecuteChanged on explicit notification
    // — hence [NotifyCanExecuteChangedFor] on every CanExtract input. Assert the
    // user-visible state (IsEffectivelyEnabled), not the raw IsEnabled.
    [AvaloniaFact]
    public void ExtractBtn_EffectivelyEnabled_WhenBothPathsSet()
    {
        var window = new SetupWindow();
        var vm = Assert.IsType<SetupViewModel>(window.DataContext);
        var btn = window.GetControl<Button>("ExtractBtn");
        Assert.False(btn.IsEffectivelyEnabled);
        vm.DatPath = @"C:\x\data_001.dat";
        Assert.True(btn.IsEffectivelyEnabled,
            "ExtractBtn must be effectively enabled after DatPath is set (OutDir has default)");
    }

    [AvaloniaFact]
    public void ExtractBtn_Disabled_WhileExtractRunning()
    {
        var window = new SetupWindow();
        var vm = Assert.IsType<SetupViewModel>(window.DataContext);
        var btn = window.GetControl<Button>("ExtractBtn");
        vm.DatPath = @"C:\x\data_001.dat";
        Assert.True(btn.IsEffectivelyEnabled);

        vm.ExtractRunning = true;
        Assert.False(btn.IsEffectivelyEnabled, "button must disable while extraction runs");
        vm.ExtractRunning = false;
        Assert.True(btn.IsEffectivelyEnabled, "button must re-enable when extraction finishes");
    }
}
