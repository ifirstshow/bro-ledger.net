using BroLedger.Core;
using BroLedger.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BroLedger.App.ViewModels;

/// <summary>
/// Port of Java ui/PreferencesController.java (the Initialize-from-AppConfig + save-on-change
/// half). Radio exclusivity is enforced by the RadioButton GroupNames in the XAML — the VM
/// mirrors each pick in its own bool and acts on the true transitions. Save timing mirrors
/// Java: each change persists immediately via AppConfig.Save(); only the expected-stats mode
/// additionally fires <see cref="Changed"/> (Java setOnChanged → onPreferencesChanged
/// repaints card + overview).
/// </summary>
internal partial class PreferencesViewModel : ObservableObject
{
    private bool _initializing = true;

    [ObservableProperty] private bool _naiveMode = true;
    [ObservableProperty] private bool _greedyMode;

    [ObservableProperty] private bool _modalOnLevelUp = true;
    [ObservableProperty] private bool _modalAutoClose;
    [ObservableProperty] private bool _modalOff;

    [ObservableProperty] private bool _replayCapture;

    /// <summary>Java initialize: the replay section is hidden and force-disabled outside
    /// debug builds (AppInfo.isDebug()).</summary>
    [ObservableProperty] private bool _replayVisible = AppInfo.IsDebug();

    /// <summary>Java setOnChanged — fired only for expected-stats-mode changes.</summary>
    public event Action? Changed;

    public PreferencesViewModel()
    {
        var config = AppConfig.Instance;

        if (ExpectedStatsCalculator.Parse(config.ExpectedStatsMode)
            == ExpectedStatsCalculator.Mode.Greedy)
        {
            GreedyMode = true;
            NaiveMode = false;
        }

        switch (LevelUpViewModel.NormalizeModalMode(config.LevelUpModalMode))
        {
            case "AUTO_CLOSE": ModalAutoClose = true; ModalOnLevelUp = false; break;
            case "OFF": ModalOff = true; ModalOnLevelUp = false; break;
        }

        if (ReplayVisible)
        {
            ReplayCapture = config.ReplayCaptureEnabled;
        }
        else if (config.ReplayCaptureEnabled)
        {
            // Java: hidden + force-disabled outside debug builds.
            config.ReplayCaptureEnabled = false;
            config.Save();
        }

        _initializing = false;
    }

    partial void OnNaiveModeChanged(bool value)
    {
        if (_initializing || !value) return;
        AppConfig.Instance.ExpectedStatsMode = "NAIVE";
        AppConfig.Instance.Save();
        Changed?.Invoke();
    }

    partial void OnGreedyModeChanged(bool value)
    {
        if (_initializing || !value) return;
        AppConfig.Instance.ExpectedStatsMode = "GREEDY";
        AppConfig.Instance.Save();
        Changed?.Invoke();
    }

    partial void OnModalOnLevelUpChanged(bool value)
    {
        if (_initializing || !value) return;
        SetModalMode("MODAL");
    }

    partial void OnModalAutoCloseChanged(bool value)
    {
        if (_initializing || !value) return;
        SetModalMode("AUTO_CLOSE");
    }

    partial void OnModalOffChanged(bool value)
    {
        if (_initializing || !value) return;
        SetModalMode("OFF");
    }

    private static void SetModalMode(string mode)
    {
        AppConfig.Instance.LevelUpModalMode = mode;
        AppConfig.Instance.Save();
    }

    partial void OnReplayCaptureChanged(bool value)
    {
        if (_initializing) return;
        AppConfig.Instance.ReplayCaptureEnabled = value;
        AppConfig.Instance.Save();
    }
}
