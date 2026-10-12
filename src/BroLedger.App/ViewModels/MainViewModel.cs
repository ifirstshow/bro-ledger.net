using Avalonia.Threading;
using BroLedger.Core;
using BroLedger.Core.Models;
using BroLedger.Core.Parsing;
using BroLedger.Core.Services;
using BroLedger.Tools;
using BroLedger.Tools.Parser;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.RegularExpressions;

namespace BroLedger.App.ViewModels;

/// <summary>
/// Port of the UI-free logic of JAVA/ui/MainController.java (loadSave / openSave flow /
/// stopWatcher / navigateBack / status bar / guards / reload countdown). Window concerns
/// (chrome, hotkeys, file pickers, reset layout) stay in MainWindow code-behind.
/// </summary>
internal partial class MainViewModel : ObservableObject
{
    private readonly UiContext _ctx = UiContext.Defaults();
    private readonly FileWatcherService _watcher = new();

    private DispatcherTimer? _reloadCountdown;
    private int _reloadSecondsElapsed;
    private string? _reloadNote;

    // Java buildToolbar: currentFileName = "No file open" — the Open Save button's desc line.
    [ObservableProperty] private string _currentFileName = "未打开存档";
    [ObservableProperty] private string _watchStatusText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _statusIsWarning;

    /// <summary>T3 mount: the four overview toggle buttons become visible in overview.</summary>
    [ObservableProperty] private bool _overviewTogglesVisible;

    public List<Brother> Brothers { get; private set; } = [];
    public Brother? SelectedBrother { get; private set; }
    public bool ShowingOverview { get; private set; }

    /// <summary>Overview toggle modes, column order and pane events (T3).</summary>
    public OverviewViewModel Overview { get; } = new();

    /// <summary>Raised when the center content must be swapped (overview placeholder now;
    /// T3 swaps in BrotherOverviewPane, T4 the BrotherCard).</summary>
    public event Action? CenterChanged;

    /// <summary>Raised on the UI thread when a save parse fails — MainWindow shows the
    /// Java setOnFailed→showError modal (the VM stays window-free). loadSaveQuiet stays
    /// status-only, exactly like the Java original.</summary>
    public event Action<string, string>? ParseFailed;

    /// <summary>Java loadSaveQuiet L483-489: level-ups detected on reload — MainWindow
    /// opens the LevelUpWindow unless the mode is OFF; the bool is the AUTO_CLOSE variant
    /// (LevelUpViewModel.ShouldShow / IsAutoClose decide).</summary>
    public event Action<IReadOnlyList<AnnotationService.LevelUpEvent>, bool>? LevelUpsDetected;

    /// <summary>Raised at the 15th countdown tick — Java's startReloadCountdown closes the
    /// AUTO_CLOSE level-up stage there.</summary>
    public event Action? ReloadCountdownFinished;

    // Java readVersion(): blank version renders as "" (label simply absent).
    public string VersionLabelText { get; } = FormatVersion();

    private static string FormatVersion()
    {
        var v = AppInfo.Version();
        return v.Length == 0 ? "" : "v" + v;
    }

    // ---- pure / testable logic (MainControllerTest surface) ----------------

    /// <summary>Java MainController.isValid — the save-parse row filter.</summary>
    public static bool IsValidBrother(Brother b)
    {
        if (b.Name == null || string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 60) return false;
        if (b.LevelTotal < 1) return false;
        if (b.BackgroundHexId == null) return false;
        var bg = DictionaryService.Instance.Get(b.BackgroundHexId);
        return bg != null && bg.Type == "background";
    }

    /// <summary>Status-bar text listing the brothers whose deleted-role assignment was cleared.</summary>
    public static string DanglingRoleMessage(IReadOnlyList<string> names)
    {
        var shown = string.Join(", ", names.Take(5));
        var more = names.Count > 5 ? " 等共 " + names.Count + " 名" : "";
        return names.Count + " 名兄弟引用了已删除的角色——分配已清除：" + shown + more;
    }

    /// <summary>
    /// Java startReloadCountdown's Timeline body as a pure function: counts seconds since the
    /// auto-reload, then settles on "Watching" at the 15th tick.
    /// </summary>
    public static string ReloadCountdownText(int secondsElapsed, string? note)
    {
        if (secondsElapsed >= 15) return "监视中";
        var suffix = note != null ? " • " + note : "";
        return $"{secondsElapsed} 秒前已重新载入{suffix}";
    }

    // ---- file flow ---------------------------------------------------------

    /// <summary>Java loadSave: reset UI state, load annotations, parse off-thread, then apply,
    /// show overview, capture a replay baseline and start watching.</summary>
    public void LoadSave(string savePath)
    {
        StopReloadCountdown();
        CurrentFileName = Path.GetFileName(savePath);
        WatchStatusText = "";
        SetStatus("", false);
        Brothers = [];
        SelectedBrother = null;

        _ctx.Annotation.LoadFor(savePath);
        // Java restoreUiState(): the four overview toggles come back from the annotation file.
        Overview.RestoreUiState(_ctx.Annotation);

        var parser = new SaveParser(_ctx.Dict);
        ParseAsync(parser, savePath).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                var message = t.Exception?.GetBaseException().Message ?? "未知错误";
                SetStatus("存档解析失败：" + message, true);
                ParseFailed?.Invoke("存档解析失败", message);
                return;
            }
            ApplyResult(parser, t.Result);
            ShowOverview();
            // Capture baseline snapshot so ReplayAnalyzer has a predecessor for the first overwrite.
            SaveReplayService.Instance.Capture(savePath);
            StartWatching(savePath);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Java stopWatcher.</summary>
    public void StopWatcher()
    {
        _watcher.Stop();
        StopReloadCountdown();
        WatchStatusText = "";
    }

    /// <summary>Java navigateBack — ESC from the detail view returns to the overview.</summary>
    public void NavigateBack()
    {
        if (!ShowingOverview && Brothers.Count > 0) ShowOverview();
    }

    /// <summary>Java showOverview — T3 replaces the placeholder content.</summary>
    public void ShowOverview()
    {
        if (Brothers.Count == 0) return;
        ShowingOverview = true;
        OverviewTogglesVisible = true;
        CenterChanged?.Invoke();
    }

    /// <summary>Java showCard — T4 mount point (BrotherCard content).</summary>
    public void ShowCard(Brother brother)
    {
        SelectedBrother = brother;
        ShowingOverview = false;
        OverviewTogglesVisible = false;
        CenterChanged?.Invoke();
    }

    // ---- export (Java exportSave / exportExcerpt) --------------------------

    /// <summary>Java exportSave: last save's brothers → sibling .csv (semicolon-separated).</summary>
    public void ExportSave()
    {
        var target = ExportTarget(".csv");
        if (target == null) return;
        try
        {
            new BrotherCsvExporter(_ctx.Dict, _ctx.StatModifier, _ctx.Annotation, _ctx.Roles)
                .Export(AppConfig.Instance, target.Value.Save, target.Value.Out);
            SetStatus("已导出到 " + Path.GetFileName(target.Value.Out), false);
        }
        catch (IOException e)
        {
            SetStatus("导出失败：" + e.Message, true);
        }
    }

    /// <summary>Java exportExcerpt: raw parse trace → sibling .tsv (debug-only button).</summary>
    public void ExportExcerpt()
    {
        var target = ExportTarget(".tsv");
        if (target == null) return;
        try
        {
            new BrotherSavefileExporter(_ctx.Dict).Write(target.Value.Save, target.Value.Out);
            SetStatus("节选已写入 " + Path.GetFileName(target.Value.Out), false);
        }
        catch (IOException e)
        {
            SetStatus("节选导出失败：" + e.Message, true);
        }
    }

    /// <summary>Shared guard + sibling-path rule of Java exportSave/exportExcerpt: no last save
    /// → warning status and null; else the save path and its "&lt;base&gt;&lt;ext&gt;" neighbour.</summary>
    private (string Save, string Out)? ExportTarget(string extension)
    {
        var savePath = AppConfig.Instance.LastSaveFilePath;
        if (string.IsNullOrWhiteSpace(savePath))
        {
            SetStatus("尚未载入存档——请先打开存档", true);
            return null;
        }
        var baseName = Regex.Replace(Path.GetFileName(savePath), @"\.[^.]+$", "");
        return (savePath, Path.Combine(Path.GetDirectoryName(savePath) ?? "", baseName + extension));
    }

    // ---- internals ---------------------------------------------------------

    private Task<List<Brother>> ParseAsync(SaveParser parser, string savePath) =>
        Task.Run(() => parser.Parse(savePath).Where(IsValidBrother).ToList());

    // Java applyResult sorts via BrotherOverviewPane.sortComparator — T3 mount; order stays parse order.
    private void ApplyResult(SaveParser parser, List<Brother> parsed)
    {
        Brothers = parsed;
        ShowWarnings(parser.Warnings);
        RepairDanglingRoleRefs();
    }

    private void ShowWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
            SetStatus("", false);
        else
            SetStatus(warnings.Distinct().Count() + " 个未知物品类型——相应槽位无法显示属性", true);
    }

    private void SetStatus(string message, bool warning)
    {
        StatusText = message;
        StatusIsWarning = warning;
    }

    /// <summary>Java repairDanglingRoleRefs: clear annotation role refs whose role was deleted
    /// while this save was not loaded; persist and tell the user which brothers were affected.</summary>
    private void RepairDanglingRoleRefs()
    {
        var validIds = _ctx.Roles.GetAll().Select(r => r.Id)
            .Where(id => id != null).Select(id => id!).ToHashSet();
        var cleared = new HashSet<string>(_ctx.Annotation.ClearDanglingRoleRefs(validIds));
        if (cleared.Count == 0) return;
        var names = Brothers
            .Where(b => b.Fingerprint != null && cleared.Contains(b.Fingerprint))
            .Select(b => b.Name!)
            .ToList();
        // Entries for brothers no longer in the save are repaired silently.
        if (names.Count > 0) SetStatus(DanglingRoleMessage(names), true);
    }

    /// <summary>Java startWatching: watcher callbacks arrive marshaled onto the UI thread
    /// (计划②裁决 — marshal = Dispatcher.UIThread.InvokeAsync).</summary>
    private void StartWatching(string savePath)
    {
        WatchStatusText = "监视中";
        _watcher.Watch(savePath, () =>
        {
            _ctx.Annotation.LoadFor(savePath);
            // Capture the save + pre-reconciliation annotations BEFORE reconcileOnReload mutates them.
            SaveReplayService.Instance.Capture(savePath);
            LoadSaveQuiet(savePath);
        }, action => Dispatcher.UIThread.InvokeAsync(action));
    }

    /// <summary>Java loadSaveQuiet: watcher-triggered reload — reconcile, keep the selected
    /// brother by fingerprint, then run the reload countdown.</summary>
    private void LoadSaveQuiet(string savePath)
    {
        var selected = SelectedBrother;
        var parser = new SaveParser(_ctx.Dict);
        ParseAsync(parser, savePath).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                SetStatus("重新载入失败：" + t.Exception?.GetBaseException().Message, true);
                return;
            }
            var oldBrothers = Brothers;
            var newBrothers = t.Result;
            var events = _ctx.Annotation.ReconcileOnReload(oldBrothers, newBrothers);
            ApplyResult(parser, newBrothers);
            if (selected != null)
            {
                var match = Brothers.FirstOrDefault(b =>
                    b.Fingerprint != null && b.Fingerprint == selected.Fingerprint);
                if (match != null) SelectedBrother = match; // T4 mount: cardController.populate(b)
            }
            if (ShowingOverview) ShowOverview();
            if (events.Count > 0)
            {
                // Java loadSaveQuiet: OFF never opens; AUTO_CLOSE opens the auto-closing variant.
                var mode = AppConfig.Instance.LevelUpModalMode;
                if (LevelUpViewModel.ShouldShow(mode))
                    LevelUpsDetected?.Invoke(events, LevelUpViewModel.IsAutoClose(mode));
                StartReloadCountdown("检测到 " + events.Count + " 次升级");
            }
            else
            {
                StartReloadCountdown(null);
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void StartReloadCountdown(string? note)
    {
        _reloadCountdown?.Stop();
        _reloadSecondsElapsed = 0;
        _reloadNote = note;
        WatchStatusText = ReloadCountdownText(0, note);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            _reloadSecondsElapsed++;
            if (_reloadSecondsElapsed < 15)
            {
                WatchStatusText = ReloadCountdownText(_reloadSecondsElapsed, _reloadNote);
            }
            else
            {
                WatchStatusText = "监视中";
                timer.Stop();
                ReloadCountdownFinished?.Invoke(); // Java closes the AUTO_CLOSE stage here
            }
        };
        timer.Start();
        _reloadCountdown = timer;
    }

    private void StopReloadCountdown()
    {
        _reloadCountdown?.Stop();
        _reloadCountdown = null;
    }
}
