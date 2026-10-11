using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using BroLedger.App.ViewModels;
using BroLedger.Core.Models;

namespace BroLedger.App;

/// <summary>
/// Port of JAVA role-share-dialog.fxml + ui/RoleShareController.java's window half: title
/// bar, status-label warning colouring and the clipboard. State, history and the publish
/// flow live in <see cref="RoleShareViewModel"/>.
/// </summary>
public partial class RoleShareWindow : Window
{
    private readonly RoleShareViewModel _vm;

    public RoleShareWindow()
    {
        InitializeComponent();
        _vm = new RoleShareViewModel(UiContext.Defaults());
        DataContext = _vm;

        // Java task.setOnSucceeded's copyToClipboard — marshalled here (UI thread).
        _vm.CodeReady += code => _ = CopyToClipboardAsync(code);
        // Java statusLabel.getStyleClass().setAll("dim-label"[, "warning"]).
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RoleShareViewModel.StatusWarning))
                SetStatusClasses();
        };

        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
    }

    /// <summary>Java openShareDialog's setRoles half.</summary>
    public void OpenFor(Window owner, IReadOnlyList<Role> roles)
    {
        _vm.SetRoles(roles);
        _ = ShowDialog(owner);
    }

    private void SetStatusClasses()
    {
        StatusLabel.Classes.Remove("warning");
        if (_vm.StatusWarning) StatusLabel.Classes.Add("warning");
    }

    // ---- actions (Java FXML actions) --------------------------------------------

    private void OnSelectAll(object? sender, RoutedEventArgs e) => _vm.SelectAllRoles();

    private void OnSelectNone(object? sender, RoutedEventArgs e) => _vm.SelectNoneRoles();

    private void OnNewCode(object? sender, RoutedEventArgs e) => _vm.NewCode();

    private void OnShare(object? sender, RoutedEventArgs e) => _vm.Share();

    /// <summary>Java copyCode.</summary>
    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (_vm.Code.Length > 0) _ = CopyToClipboardAsync(_vm.Code);
    }

    private async Task CopyToClipboardAsync(string text)
    {
        // Java Clipboard.setContent — Avalonia 12 replaced SetTextAsync with the
        // DataTransfer model (SetDataAsync; Avalonia owns the transfer).
        if (Clipboard == null) return;
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(text));
        await Clipboard.SetDataAsync(data);
    }

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
