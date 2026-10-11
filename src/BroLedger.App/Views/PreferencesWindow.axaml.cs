using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BroLedger.App.ViewModels;

namespace BroLedger.App;

/// <summary>
/// Port of JAVA preferences.fxml + PreferencesController.java's window half — the state
/// and save-on-change logic lives in <see cref="PreferencesViewModel"/>. Opened by
/// MainWindow (Java openPreferences → Modals.showModal); the owner listens to the VM's
/// Changed event for the expected-stats repaint.
/// </summary>
public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
        DataContext = new PreferencesViewModel();

        // Java initialize: title-bar drag + scene ESC filter.
        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
    }

    /// <summary>Java PreferencesController's Initialize-from-AppConfig half is in the VM;
    /// callers reach it through this property to subscribe to Changed.</summary>
    internal PreferencesViewModel Prefs => (PreferencesViewModel)DataContext!;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
