using Avalonia.Input;

namespace BroLedger.App;

/// <summary>
/// Java KeyCodeCombination.SHORTCUT_DOWN semantics: Cmd on macOS, Ctrl elsewhere.
/// Spec §7 writes "Ctrl+O" literally; SHORTCUT_DOWN is the semantic source of truth.
/// </summary>
internal static class PlatformAccelerator
{
    public static KeyModifiers Accelerator =>
        OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
}
