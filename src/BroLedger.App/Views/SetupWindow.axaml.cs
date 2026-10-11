using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BroLedger.App.Services;
using BroLedger.App.ViewModels;
using BroLedger.Core.Services;

namespace BroLedger.App;

public partial class SetupWindow : Window
{
    private readonly SetupViewModel _vm = new();

    public SetupWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        TitleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };

        _vm.ExtractionSucceeded += () => CloseAfter(TimeSpan.FromSeconds(2)); // Java success pause
        _vm.ExtractionFailed += () => CloseAfter(TimeSpan.FromSeconds(3));    // Java failure pause
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Java setOutdated is called before showing when game-art exists but is stale.
        _vm.Outdated = AssetExtractor.IsGameArtOutdated(AppConfig.Instance.GameArtDirectory);
    }

    // ---- pickers ------------------------------------------------------------

    /// <summary>Java browseDat: *.dat file picker.</summary>
    private async void OnBrowseDat(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select data_001.dat",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Game Data (*.dat)") { Patterns = ["*.dat"] }],
        });
        if (files.Count > 0) _vm.DatPath = files[0].Path.LocalPath;
    }

    /// <summary>Java browseOutDir: folder picker + game-art subfolder confirmation.</summary>
    private async void OnBrowseOutDir(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select output folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;

        var dir = folders[0].Path.LocalPath;
        var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!SetupViewModel.NeedsGameArtSubfolder(name))
        {
            _vm.OutDir = dir;
            return;
        }

        // choice == -1 (dismissed via ✕): leave OutDir untouched, matching the old Alert.
        var choice = await ModalService.ChoiceAsync(this, "Confirm output folder", null,
            "The selected folder is '" + name + "'.\n\n" +
            "Create a 'game-art' subfolder inside it to keep extracted files organized?",
            "Create 'game-art' subfolder", "Use selected folder");
        if (choice == 0) _vm.OutDir = Path.Combine(dir, "game-art");
        else if (choice == 1) _vm.OutDir = dir;
    }

    /// <summary>Java selectExisting: adopt an existing folder as the game-art directory.</summary>
    private async void OnSelectExisting(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select game-art directory",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;

        _vm.UseExisting(folders[0].Path.LocalPath);
        Close();
    }

    private void OnSkip(object? sender, RoutedEventArgs e) => Close();

    private void CloseAfter(TimeSpan delay)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
        };
        timer.Start();
    }
}
