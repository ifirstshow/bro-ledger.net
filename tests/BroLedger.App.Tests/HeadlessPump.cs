using Avalonia.Threading;

namespace BroLedger.App.Tests;

/// <summary>
/// Shared headless pump: services the UI dispatcher while waiting for an async result,
/// with a 10s ceiling (400 ticks × 25ms). On timeout the assert carries the waited-for
/// condition, the ticks actually pumped and a caller-supplied state snapshot, so CI
/// slow-machine failures say why they stalled instead of dying on a bare assert.
/// </summary>
internal static class HeadlessPump
{
    private const int MaxTicks = 400; // 400 × 25ms = 10s ceiling, unchanged from the old inline loops

    public static void Until(Func<bool> done, string what, Func<string>? lastState = null)
    {
        var ticks = 0;
        while (!done() && ticks < MaxTicks)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(25);
            ticks++;
        }
        Assert.True(done(),
            $"Timed out after {ticks}/{MaxTicks} pumped ticks (10s ceiling) waiting for '{what}'."
            + (lastState is null ? "" : $" Last state: {lastState()}"));
    }
}
