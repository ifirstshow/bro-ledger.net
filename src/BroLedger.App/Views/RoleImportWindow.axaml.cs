using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using BroLedger.App.ViewModels;

namespace BroLedger.App;

/// <summary>
/// Port of JAVA role-import-dialog.fxml + ui/RoleImportController.java's window half:
/// title bar, status-label warning colouring and the close-after-import. Code validation,
/// the fetch flow and the checklist live in <see cref="RoleImportViewModel"/>.
/// </summary>
public partial class RoleImportWindow : Window
{
    private readonly RoleImportViewModel _vm;

    public RoleImportWindow()
    {
        InitializeComponent();
        _vm = new RoleImportViewModel(UiContext.Defaults());
        DataContext = _vm;

        // Java doImport's close() — and the manager's setOnImported callback.
        _vm.Imported += OnImported;
        // Java statusLabel.getStyleClass().setAll("dim-label"[, "warning"]).
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RoleImportViewModel.StatusWarning))
                SetStatusClasses();
        };

        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
    }

    /// <summary>Java setOnImported — fired after a successful import, before the close.</summary>
    public event Action? Imported;

    private void SetStatusClasses()
    {
        StatusLabel.Classes.Remove("warning");
        if (_vm.StatusWarning) StatusLabel.Classes.Add("warning");
    }

    private void OnImported()
    {
        Imported?.Invoke();
        Close();
    }

    // ---- actions (Java FXML actions) --------------------------------------------

    private void OnFetch(object? sender, RoutedEventArgs e) => _vm.Fetch();

    private void OnSelectAll(object? sender, RoutedEventArgs e) => _vm.SelectAllRoles();

    private void OnSelectNone(object? sender, RoutedEventArgs e) => _vm.SelectNoneRoles();

    private void OnImport(object? sender, RoutedEventArgs e) => _vm.DoImport();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

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
}
