using Avalonia.Input;

namespace BroLedger.App.Tests;

/// <summary>
/// Pins the SHORTCUT_DOWN mapping's non-mac branch: Windows/Linux keep Ctrl (existing
/// behavior regression pin). The macOS Meta branch can't run on this host — real-Mac
/// behavior is left to CI/user on 计划⑤'s cross-platform acceptance pass.
/// </summary>
public class PlatformAcceleratorTests
{
    [Fact]
    public void Accelerator_IsControl_OnNonMacOs()
    {
        if (OperatingSystem.IsMacOS()) return; // Meta branch needs a real Mac host

        Assert.Equal(KeyModifiers.Control, PlatformAccelerator.Accelerator);
        Assert.False(OperatingSystem.IsMacOS());
    }
}
