using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using BroLedger.App.ViewModels;
using BroLedger.Core.Services;

namespace BroLedger.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    // Java setupWindowControls double-click on the title bar toggles maximize.
    private DateTime _lastTitlePress;

    public MainWindow()
    {
        InitializeComponent();
        AvaloniaXamlLoader.Load(this);

        DataContext = _vm;
        _vm.CenterChanged += UpdateCenterContent;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        Icon = new WindowIcon(new Bitmap(AssetLoader.Open(
            new Uri("avares://BroLedger.App/Assets/BroLedger.ico"))));

        TitleBar.PointerPressed += OnTitleBarPointerPressed;
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.O when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _ = OpenSaveAsync();
                e.Handled = true;
                break;
            case Key.W when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _vm.StopWatcher();
                e.Handled = true;
                break;
            case Key.Escape:
                _vm.NavigateBack();
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

    /// <summary>Java openSave: picker → remember in config → loadSave.</summary>
    private async Task OpenSaveAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Battle Brothers Save File",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Save files (*.sav)") { Patterns = ["*.sav"] }],
        });
        if (files.Count == 0) return;

        var path = files[0].Path.LocalPath;
        var config = AppConfig.Instance;
        config.LastSaveFilePath = path;
        config.Save();
        _vm.LoadSave(path);
    }

    private void OnOpenSave(object? sender, RoutedEventArgs e) => _ = OpenSaveAsync();

    // T6 mount: Java openRoleManager — RoleManager dialog lands with T6.
    private void OnOpenRoleManager(object? sender, RoutedEventArgs e) { }

    // T5 mount: Java openPreferences — Preferences dialog lands with T5.
    private void OnOpenPreferences(object? sender, RoutedEventArgs e) { }

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
            // T3 mount: BrotherOverviewPane.build(...) replaces this placeholder.
            CenterContent.Content = new TextBlock
            {
                Text = _vm.Brothers.Count + " brother(s) loaded — overview lands in Task 3",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Opacity = 0.6,
            };
        }
        else
        {
            // T4 mount: BrotherCard replaces this placeholder.
            CenterContent.Content = new TextBlock
            {
                Text = _vm.SelectedBrother?.Name is { } name ? name + " — card lands in Task 4" : "",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Opacity = 0.6,
            };
        }
    }
}
