using Avalonia.Threading;
using BroLedger.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BroLedger.App.ViewModels;

/// <summary>
/// Port of the logic of JAVA/ui/SetupController.java: data_001.dat + output folder state,
/// extract-enable guard, background AssetExtractor run with progress, "use existing folder".
/// Pickers, window dragging and the timed auto-close stay in SetupWindow code-behind.
/// </summary>
internal partial class SetupViewModel : ObservableObject
{
    private readonly AssetExtractor _extractor = new();
    private readonly AppConfig _config = AppConfig.Instance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExtract))]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    private string _datPath = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExtract))]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    private string _outDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "game-art");

    // Java toggles progressBar/progressLabel visible + managed when extraction starts.
    [ObservableProperty] private bool _progressVisible;
    [ObservableProperty] private double _progress; // 0..1, Java updateProgress(done, total)
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private bool _outdated;

    /// <summary>Java setOutdated: header switches when the existing game-art is stale.</summary>
    public string HeaderText => Outdated
        ? "Game-art is outdated and needs to be re-extracted. Choose an option:"
        : "No game-art directory is configured. Choose an option:";

    /// <summary>Java initialize's extractBtn disable binding (both fields must be filled).</summary>
    public bool CanExtract =>
        !ExtractRunning &&
        !string.IsNullOrWhiteSpace(DatPath) &&
        !string.IsNullOrWhiteSpace(OutDir);

    /// <summary>While the extractor task runs the button stays disabled (Java startExtract).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExtract))]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    private bool _extractRunning;

    /// <summary>Java onConfigured — fired after game-art is configured (extract or existing).</summary>
    public event Action? Configured;

    /// <summary>Java task.setOnSucceeded — the window auto-closes after 2 s.</summary>
    public event Action? ExtractionSucceeded;

    /// <summary>Java task.setOnFailed — the window auto-closes after 3 s.</summary>
    public event Action? ExtractionFailed;

    /// <summary>Java browseOutDir's confirm: offer a game-art subfolder for other folder names.</summary>
    public static bool NeedsGameArtSubfolder(string folderName) => folderName != "game-art";

    /// <summary>Java startExtract: run AssetExtractor.ExtractWithFallback off the UI thread,
    /// marshaling progress onto the UI thread, persisting the configured directory on success.</summary>
    [RelayCommand(CanExecute = nameof(CanExtract))]
    private void Extract()
    {
        ProgressVisible = true;
        ExtractRunning = true;

        var datFile = DatPath;
        var outDir = OutDir;

        Task.Run<AssetExtractor.ExtractionResult>(() =>
            _extractor.ExtractWithFallback(datFile, outDir, (done, total, file) =>
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Progress = total == 0 ? 0 : (double)done / total;
                    ProgressText = file;
                })))
        .ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                ProgressText = "Extraction failed: " + t.Exception?.GetBaseException();
                ExtractRunning = false;
                ExtractionFailed?.Invoke();
                return;
            }
            var r = t.Result;
            Progress = 1.0;
            ProgressText = $"Done — extracted {r.Extracted}/{r.Total} files ({r.Fallback} fallback)";
            _config.GameArtDirectory = outDir;
            _config.Save();
            Configured?.Invoke();
            ExtractionSucceeded?.Invoke();
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Java selectExisting: adopt an existing game-art folder as-is.</summary>
    public void UseExisting(string dir)
    {
        _config.GameArtDirectory = dir;
        _config.Save();
        Configured?.Invoke();
    }
}
