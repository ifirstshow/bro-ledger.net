using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using BroLedger.App.Services;
using BroLedger.App.ViewModels;
using BroLedger.Core;
using BroLedger.Core.Services;

namespace BroLedger.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    // Java setupWindowControls double-click on the title bar toggles maximize.
    private DateTime _lastTitlePress;

    public MainWindow()
    {
        InitializeComponent(); // Avalonia 12 generated init already loads the XAML — a second
        // AvaloniaXamlLoader.Load(this) re-inflates the tree and orphans every code-behind
        // reference (toggles/pane built into a detached tree; window displayed the stale one).

        DataContext = _vm;
        _vm.CenterChanged += UpdateCenterContent;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.Overview.PropertyChanged += (_, _) => UpdateToggleVisuals();
        // Java showOverview row click → showCard.
        _vm.Overview.ShowCardRequested += b => _vm.ShowCard(b);
        // Java guidePopup: BrotherOverviewPane mounts its own LevelUpGuidePanel, which
        // consumes Overview.LevelGuideRequested (the MainWindow debug subscription is gone).

        // Java loadSave setOnFailed → showError: parse failure also opens an error modal.
        _vm.ParseFailed += (header, message) =>
            _ = ModalService.ErrorAsync(this, "错误", header, message);

        // Java loadSaveQuiet L483-489: open the level-up modal unless the mode is OFF;
        // startReloadCountdown closes it on the 15th tick when AUTO_CLOSE.
        _vm.LevelUpsDetected += OnLevelUpsDetected;
        _vm.ReloadCountdownFinished += CloseAutoCloseLevelUp;

        // Java buildToolbar L128-131: "Excerpt" is a developer/diagnostic export variant —
        // hidden in release builds.
        ExcerptBtn.IsVisible = AppInfo.IsDebug();

        BuildOverviewToggles();

        Icon = new WindowIcon(new Bitmap(AssetLoader.Open(
            new Uri("avares://BroLedger.App/Assets/BroLedger.ico"))));

        TitleBar.PointerPressed += OnTitleBarPointerPressed;
    }

    // ---- overview toggles (Java buildToolbar fancyToggle segment) -----------

    private readonly List<TextBlock> _toggleDescLabels = [];
    private readonly List<List<Avalonia.Controls.Border>> _toggleDots = [];

    private void BuildOverviewToggles()
    {
        for (var i = 0; i < OverviewViewModel.Toggles.Count; i++)
        {
            var spec = OverviewViewModel.Toggles[i];
            var toggleIndex = i;

            var ledRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            var dots = new List<Avalonia.Controls.Border>();
            foreach (var option in spec.Options)
            {
                var dot = new Avalonia.Controls.Border { Classes = { "btn-led", option.LedClass } };
                dots.Add(dot);
                ledRow.Children.Add(dot);
            }

            var desc = new TextBlock { Classes = { "btn-desc" } };
            _toggleDescLabels.Add(desc);
            _toggleDots.Add(dots);

            var button = new Button
            {
                Classes = { "fancy-btn" },
                Focusable = false,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children = { ledRow, new TextBlock { Classes = { "btn-header" }, Text = spec.Title }, desc },
                },
            };
            button.Click += (_, _) => _vm.Overview.CycleNext(toggleIndex);
            OverviewTogglesHost.Children.Add(button);
        }
        UpdateToggleVisuals();
    }

    /// <summary>Java fancyToggle's update runnable: the active option's led lights up and
    /// its description becomes the button's second line.</summary>
    private void UpdateToggleVisuals()
    {
        var vm = _vm.Overview;
        object[] current =
        {
            vm.WeaponTier, vm.Armor, vm.PerkSort, vm.Potential,
        };
        for (var t = 0; t < OverviewViewModel.Toggles.Count; t++)
        {
            var options = OverviewViewModel.Toggles[t].Options;
            for (var o = 0; o < options.Count; o++)
            {
                var active = Equals(options[o].Value, current[t]);
                var dot = _toggleDots[t][o];
                dot.Classes.Remove("led-lit");
                dot.Classes.Remove("led-muted");
                dot.Classes.Add(active ? "led-lit" : "led-muted");
                if (active) _toggleDescLabels[t].Text = options[o].Desc;
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Java setStatus toggles the status-label "warning" style class.
        if (e.PropertyName == nameof(MainViewModel.StatusIsWarning))
        {
            if (_vm.StatusIsWarning) StatusLabel.Classes.Add("warning");
            else StatusLabel.Classes.Remove("warning");
        }
    }

    /// <summary>
    /// Java App.start sequenced everything before/around stage.show(); here the startup work
    /// lives in OnOpened so that constructing the window stays side-effect-free (headless
    /// tests construct it). Loads config + singletons, restores geometry, auto-loads the
    /// last save, then prompts the Setup dialog on first run / outdated game-art.
    /// </summary>
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var config = AppConfig.Instance;
        config.Load();

        // Java App.start: dictionary + data services loaded from the classpath up front.
        DictionaryService.Instance.LoadFromEmbeddedResource();
        ImageMapService.Instance.LoadFromEmbeddedResource();
        StatModifierService.Instance.LoadFromEmbeddedResource();
        WeaponStatsService.Instance.LoadFromEmbeddedResource();

        // Geometry: config size, position only when still on-screen (Java start()).
        Width = config.WindowWidth;
        Height = config.WindowHeight;
        if (config.WindowX is int x && config.WindowY is int y &&
            Screens.All.Any(s => s.Bounds.Intersects(new PixelRect(x, y, config.WindowWidth, config.WindowHeight))))
        {
            Position = new PixelPoint(x, y);
        }

        var last = config.LastSaveFilePath;
        if (!string.IsNullOrWhiteSpace(last)) _vm.LoadSave(last);

        // Java App.promptForGameArtIfNeeded — deviation: Java blocks before the main stage
        // becomes visible; here the main window is already shown behind the modal dialog
        // (Avalonia's ShowDialog needs a shown owner — OnOpened fires a beat before the
        // platform window is up, so the dialog opens on the next dispatcher turn).
        if (!config.HasGameArtDirectory() || AssetExtractor.IsGameArtOutdated(config.GameArtRoot()))
        {
            var setup = new SetupWindow();
            await setup.ShowDialog(this);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Java stage.setOnCloseRequest — persist geometry so it survives restarts.
        var config = AppConfig.Instance;
        config.WindowWidth = (int)Width;
        config.WindowHeight = (int)Height;
        config.WindowX = Position.X;
        config.WindowY = Position.Y;
        config.Save();
        base.OnClosing(e);
    }

    // ---- hotkeys (Java HotkeyManager "global" scope: Ctrl+O / Ctrl+W / ESC) ----

    // Java HotkeyManager binds KeyCodeCombination.SHORTCUT_DOWN (Cmd on macOS, Ctrl
    // elsewhere) — PlatformAccelerator carries that mapping.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.O when e.KeyModifiers.HasFlag(PlatformAccelerator.Accelerator):
                _ = OpenSaveAsync();
                e.Handled = true;
                break;
            case Key.W when e.KeyModifiers.HasFlag(PlatformAccelerator.Accelerator):
                _vm.StopWatcher();
                e.Handled = true;
                break;
            case Key.Escape:
                _vm.NavigateBack();
                e.Handled = true;
                break;
            case Key.Enter when _vm.ShowingOverview && _vm.SelectedBrother != null:
                // Java hotkeys "overview" scope: ENTER opens the selected brother's card.
                _vm.ShowCard(_vm.SelectedBrother);
                e.Handled = true;
                break;
        }
        base.OnKeyDown(e);
    }

    // ---- window chrome (Java setupWindowControls / window button handlers) ----

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var now = DateTime.UtcNow;
        if ((now - _lastTitlePress).TotalMilliseconds < 500)
        {
            _lastTitlePress = default;
            ToggleMaximize();
            return;
        }
        _lastTitlePress = now;

        // Java drags only when not maximized; BeginMoveDrag delegates to the OS move loop.
        if (WindowState != WindowState.Maximized) BeginMoveDrag(e);
    }

    private void OnMinimizeWindow(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnToggleMaximize(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseWindow(object? sender, RoutedEventArgs e) => Close();

    // ---- toolbar ------------------------------------------------------------

    /// <summary>Java openSave: picker (starting in the last save's directory) → remember in
    /// config → loadSave.</summary>
    private async Task OpenSaveAsync()
    {
        var options = new FilePickerOpenOptions
        {
            Title = "打开 Battle Brothers 存档文件",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("存档文件 (*.sav)") { Patterns = ["*.sav"] }],
        };
        // Java openSave L345-349: setInitialDirectory to the last save's folder.
        if (Path.GetDirectoryName(AppConfig.Instance.LastSaveFilePath) is { } dir && Directory.Exists(dir))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(dir);

        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0) return;

        var path = files[0].Path.LocalPath;
        var config = AppConfig.Instance;
        config.LastSaveFilePath = path;
        config.Save();
        _vm.LoadSave(path);
    }

    private void OnOpenSave(object? sender, RoutedEventArgs e) => _ = OpenSaveAsync();

    // Java exportSave / exportExcerpt — button handlers; logic lives in MainViewModel.
    private void OnExportSave(object? sender, RoutedEventArgs e) => _vm.ExportSave();

    private void OnExportExcerpt(object? sender, RoutedEventArgs e) => _vm.ExportExcerpt();

    // Java openRoleManager: themed modal with usage counter + roles-changed repaint.
    private void OnOpenRoleManager(object? sender, RoutedEventArgs e)
    {
        var manager = new RoleManagerWindow();
        manager.Init(CountBrothersAssignedTo);
        manager.RolesChanged += OnRolesChanged;
        _ = manager.ShowDialog(this);
    }

    /// <summary>Java countBrothersAssignedTo — loaded-save brothers per role id.</summary>
    private long? CountBrothersAssignedTo(string roleId) => _vm.Brothers.Count(b =>
        b.Fingerprint != null && roleId == AnnotationService.Instance.Get(b.Fingerprint).RoleId);

    /// <summary>Java onRolesChanged: refresh the card's role widgets, re-show the overview.</summary>
    private void OnRolesChanged()
    {
        if (!_vm.ShowingOverview && _vm.SelectedBrother != null) _vm.ShowCard(_vm.SelectedBrother);
        if (_vm.ShowingOverview && _vm.Brothers.Count > 0) _vm.ShowOverview();
    }

    // Java openPreferences: themed modal; onPreferencesChanged repaints card + overview
    // when the expected-stats mode changed.
    private void OnOpenPreferences(object? sender, RoutedEventArgs e)
    {
        var prefs = new PreferencesWindow();
        prefs.Prefs.Changed += OnPreferencesChanged;
        _ = prefs.ShowDialog(this);
    }

    /// <summary>Java onPreferencesChanged: repopulate the card, re-show the overview.</summary>
    private void OnPreferencesChanged()
    {
        if (!_vm.ShowingOverview && _vm.SelectedBrother != null) _vm.ShowCard(_vm.SelectedBrother);
        if (_vm.ShowingOverview && _vm.Brothers.Count > 0) _vm.ShowOverview();
    }

    // ---- level-up modal (Java openLevelUpModal + startReloadCountdown tail) ----

    private Window? _levelUpWindow;
    private bool _levelUpAutoClose;

    private void OnLevelUpsDetected(IReadOnlyList<AnnotationService.LevelUpEvent> events,
                                    bool autoClose)
    {
        // Java blocks on showAndWait for MODAL, so at most one modal exists; a reload
        // during an open window replaces it rather than stacking.
        CloseAutoCloseLevelUp();
        _levelUpWindow?.Close();

        var levelUp = new LevelUpWindow();
        levelUp.OpenFor(this, events);
        _levelUpWindow = levelUp;
        _levelUpAutoClose = autoClose;
        levelUp.Closed += (_, _) =>
        {
            if (ReferenceEquals(_levelUpWindow, levelUp)) _levelUpWindow = null;
        };
    }

    /// <summary>Java startReloadCountdown's 15th tick: close the AUTO_CLOSE stage.</summary>
    private void CloseAutoCloseLevelUp()
    {
        if (_levelUpAutoClose && _levelUpWindow is { } w)
        {
            _levelUpAutoClose = false;
            w.Close();
        }
    }

    /// <summary>Java resetLayout: default size, top-left of the current screen, re-show overview.</summary>
    private void OnResetLayout(object? sender, RoutedEventArgs e)
    {
        var config = AppConfig.Instance;
        config.WindowWidth = 1800;
        config.WindowHeight = 1320;
        config.Save();
        WindowState = WindowState.Normal;
        Width = config.WindowWidth;
        Height = config.WindowHeight;
        var screen = Screens.All.FirstOrDefault(s => s.Bounds.Contains(Position)) ?? Screens.All.FirstOrDefault();
        if (screen != null) Position = new PixelPoint(screen.WorkingArea.X, screen.WorkingArea.Y);
        _vm.ShowOverview();
        // T3 mount: Java BrotherOverviewPane.resetColumnWidths().
    }

    // ---- center content -----------------------------------------------------

    private void UpdateCenterContent()
    {
        if (_vm.ShowingOverview)
        {
            // T3: BrotherOverviewPane manages its own scrolling — the outer ScrollViewer
            // must stretch it to the viewport instead of giving it unbounded height.
            CenterPane.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            CenterPane.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            CenterContent.Content = new Controls.BrotherOverviewPane(
                UiContext.Defaults(), _vm.Overview, _vm.Brothers);
        }
        else
        {
            // T4: the card scrolls inside the outer viewer (Java put it in a ScrollPane).
            CenterPane.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            CenterPane.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var card = new Controls.BrotherCard(
                UiContext.Defaults(), _vm.Overview, _vm.SelectedBrother!);
            // Java setOnBack → mainController.showOverview().
            card.BackRequested += () => _vm.ShowOverview();
            CenterContent.Content = card;
        }
    }
}
