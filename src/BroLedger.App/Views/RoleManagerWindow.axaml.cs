using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BroLedger.App.Controls;
using BroLedger.App.Services;
using BroLedger.App.ViewModels;
using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App;

/// <summary>
/// Port of JAVA role-manager.fxml + ui/RoleManagerController.java's window half: title-bar
/// drag, the ESC filter, the code-built stat editor grid (Java buildStatEditorGrid), the
/// embedded perk-plan template pane and the delete confirmation. Decisions and persistence
/// live in <see cref="RoleManagerViewModel"/>.
/// </summary>
public partial class RoleManagerWindow : Window
{
    private readonly RoleManagerViewModel _vm;
    private readonly PerkPlanPane _perkPlanPane;
    private readonly Dictionary<int, TextBox> _targetBoxes = [];
    private readonly Dictionary<int, ToggleButton[]> _prioToggles = [];
    private bool _syncing;

    public RoleManagerWindow()
    {
        InitializeComponent();
        var ctx = UiContext.Defaults();
        _vm = new RoleManagerViewModel(ctx);
        DataContext = _vm;
        _vm.FormChanged += SyncForm;
        _vm.PlanChanged += () => UpdatePerkBadge(_vm.PlanStatus);

        // Java setUiContext: the shared perk pane edits the selected role's template.
        _perkPlanPane = new PerkPlanPane(ctx, _vm);
        PerkPlanScroll.Content = _perkPlanPane;

        RoleList.SelectionChanged += OnRoleListSelectionChanged;

        BuildStatEditorGrid();
        SyncForm();

        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
    }

    /// <summary>Java openRoleManager wiring — the loaded-save usage count for the
    /// delete confirmation.</summary>
    public void Init(Func<string, long?> roleUsageCounter) =>
        _vm.RoleUsageCounter = roleUsageCounter;

    /// <summary>Java setOnRolesChanged — fired after every catalogue mutation.</summary>
    public event Action? RolesChanged
    {
        add => _vm.RolesChanged += value;
        remove => _vm.RolesChanged -= value;
    }

    // ---- stat editor grid (Java buildStatEditorGrid) ----------------------------

    private void BuildStatEditorGrid()
    {
        AddGridCell(0, 0, new TextBlock { Classes = { "field-label" }, Text = "属性" });
        AddGridCell(0, 1, new TextBlock { Classes = { "field-label" }, Text = "目标" });
        AddGridCell(0, 2, new TextBlock { Classes = { "field-label" }, Text = "优先级" });

        for (var visual = 0; visual < _vm.StatRows.Count; visual++)
        {
            var row = _vm.StatRows[visual];
            var gridRow = visual + 1;
            StatEditorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var name = new TextBlock
            {
                Classes = { "stat-label" },
                Text = row.Stat.Label(),
                MinWidth = 100,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var target = new TextBox { Width = 60, PlaceholderText = "—" };
            _targetBoxes[row.Stat.Ordinal] = target;

            var prioBox = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            var toggles = new ToggleButton[3];
            for (var p = 1; p <= 3; p++)
            {
                var prio = p;
                var tb = new ToggleButton { Content = "P" + p, Width = 30, Classes = { "prio-toggle" } };
                tb.Click += (_, _) => SetPriority(row, prio, toggles);
                toggles[p - 1] = tb;
                prioBox.Children.Add(tb);
            }
            _prioToggles[row.Stat.Ordinal] = toggles;

            AddGridCell(gridRow, 0, name);
            AddGridCell(gridRow, 1, target);
            AddGridCell(gridRow, 2, prioBox);
        }
    }

    private void AddGridCell(int row, int column, Control child)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        StatEditorGrid.Children.Add(child);
    }

    private static void SetPriority(RoleManagerViewModel.StatRow row, int priority, ToggleButton[] toggles)
    {
        row.Priority = priority;
        for (var i = 0; i < toggles.Length; i++) toggles[i].IsChecked = i + 1 == priority;
    }

    // ---- form sync (window ↔ VM) -------------------------------------------------

    private void OnRoleListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        _vm.Select(RoleList.SelectedItem as Role);
    }

    /// <summary>Pushes the VM's list-selection + form state into the controls
    /// (Java populateForm + setFormDisabled + refreshList's selection half).</summary>
    private void SyncForm()
    {
        _syncing = true;
        try
        {
            RoleList.SelectedItem = _vm.Selected;
            NameField.Text = _vm.Name;
            FrontlineRadio.IsChecked = _vm.Frontline;
            BacklineRadio.IsChecked = !_vm.Frontline;
            foreach (var row in _vm.StatRows)
            {
                _targetBoxes[row.Stat.Ordinal].Text = row.Target;
                var toggles = _prioToggles[row.Stat.Ordinal];
                for (var i = 0; i < toggles.Length; i++)
                    toggles[i].IsChecked = i + 1 == row.Priority;
            }
            _perkPlanPane.RefreshPlan(); // Java populateForm: pane.setStatus(r.perkPlanTemplate)
            UpdatePerkBadge(_vm.PlanStatus);
            Editor.IsEnabled = _vm.FormEnabled; // Java setFormDisabled: pane disabled/cleared too
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Java updatePerkBadge — hidden when the plan has no planned/optional entries.</summary>
    private void UpdatePerkBadge(IReadOnlyDictionary<string, string>? plan)
    {
        var text = BrotherCardViewModel.PlanBadgeText(plan);
        PerkPlanBadge.Text = text ?? "";
        PerkPlanBadge.IsVisible = text != null;
    }

    /// <summary>Pushes the controls' current state into the VM (Java saveRole's read half).</summary>
    private void PullForm()
    {
        _vm.Name = NameField.Text ?? "";
        _vm.Frontline = FrontlineRadio.IsChecked == true;
        foreach (var row in _vm.StatRows)
        {
            row.Target = _targetBoxes[row.Stat.Ordinal].Text ?? "";
        }
    }

    // ---- actions (Java FXML actions) ------------------------------------------------

    private void OnAdd(object? sender, RoutedEventArgs e) => _vm.AddRole();

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        var selected = _vm.Selected;
        var body = _vm.DeleteConfirmation();
        if (selected == null || body == null) return;

        var confirmed = await ModalService.ConfirmAsync(this, "删除角色",
            "删除角色“" + selected.Name + "”？", body, "删除");
        if (confirmed) _vm.DeleteRole();
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e) => _vm.MoveRoleUp();

    private void OnMoveDown(object? sender, RoutedEventArgs e) => _vm.MoveRoleDown();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        PullForm();
        if (!_vm.SaveRole()) NameField.Focus(); // Java: blank name → requestFocus
    }

    /// <summary>Java openShareDialog.</summary>
    private void OnShare(object? sender, RoutedEventArgs e)
    {
        var share = new RoleShareWindow();
        share.OpenFor(this, _vm.Roles.ToList());
    }

    /// <summary>Java openImportDialog — refresh + notify after a successful import.</summary>
    private void OnImport(object? sender, RoutedEventArgs e)
    {
        var import = new RoleImportWindow();
        import.Imported += _vm.OnImportCompleted;
        _ = import.ShowDialog(this);
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
