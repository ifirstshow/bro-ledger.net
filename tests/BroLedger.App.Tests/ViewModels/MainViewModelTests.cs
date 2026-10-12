using BroLedger.App.ViewModels;
using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.Tests.ViewModels;

/// <summary>
/// Ported from JAVATEST/ui/MainControllerTest.java (142 lines).
///
/// Porting notes: the Java test exercises two static, UI-free members of MainController —
/// isValid(Brother) and danglingRoleMessage(List&lt;String&gt;) — both migrated here onto
/// MainViewModel. Everything else in MainController (toolbar build, hotkeys, window drag,
/// watch/reload pipeline) depends on JavaFX Scene/Stage and is covered by manual smoke
/// verification (see task-2-report), not by this class.
///
/// Additions beyond the Java source: ReloadCountdownText — the reload-countdown label logic
/// (Java startReloadCountdown's Timeline body) was extracted into a pure function so the
/// 1-second tick text is testable without a DispatcherTimer.
/// </summary>
public class MainViewModelTests
{
    // @BeforeAll loadDictionary. Porting note: Java's @BeforeAll/static-init loads at class
    // load time, but C# beforefieldinit initialization is lazy — xunit instantiates test
    // classes via GetUninitializedObject, so a static field initializer may not have run
    // when a test body starts (observed: DictionaryService still empty). Loading in the
    // instance ctor is deterministic; the load is idempotent (GameDataService guards on
    // _root), so the per-test ctor is cheap.
    public MainViewModelTests() => DictionaryService.Instance.LoadFromEmbeddedResource();

    // ---- helpers -----------------------------------------------------------

    /// <summary>A minimally valid brother.</summary>
    private static Brother Valid() => new()
    {
        Name = "Horik",
        LevelTotal = 3,
        BackgroundHexId = "F1D4D64F", // Companion (Spear) — type=background in dictionary
    };

    // ---- name validation ---------------------------------------------------

    [Fact]
    public void ValidBrotherPasses() => Assert.True(MainViewModel.IsValidBrother(Valid()));

    [Fact]
    public void NullNameFails()
    {
        var b = Valid();
        b.Name = null;
        Assert.False(MainViewModel.IsValidBrother(b));
    }

    [Fact]
    public void BlankNameFails()
    {
        var b = Valid();
        b.Name = "   ";
        Assert.False(MainViewModel.IsValidBrother(b));
    }

    [Fact]
    public void NameTooLongFails()
    {
        var b = Valid();
        b.Name = new string('A', 61);
        Assert.False(MainViewModel.IsValidBrother(b));
    }

    [Fact]
    public void NameExactly60CharsIsValid()
    {
        var b = Valid();
        b.Name = new string('A', 60);
        Assert.True(MainViewModel.IsValidBrother(b));
    }

    // ---- level validation --------------------------------------------------

    [Fact]
    public void LevelZeroFails()
    {
        var b = Valid();
        b.LevelTotal = 0;
        Assert.False(MainViewModel.IsValidBrother(b));
    }

    [Fact]
    public void NegativeLevelFails()
    {
        var b = Valid();
        b.LevelTotal = -1;
        Assert.False(MainViewModel.IsValidBrother(b));
    }

    [Fact]
    public void LevelOnePasses()
    {
        var b = Valid();
        b.LevelTotal = 1;
        Assert.True(MainViewModel.IsValidBrother(b));
    }

    // ---- background validation ---------------------------------------------

    [Fact]
    public void NullBackgroundFails()
    {
        var b = Valid();
        b.BackgroundHexId = null;
        Assert.False(MainViewModel.IsValidBrother(b));
    }

    [Fact]
    public void UnknownBackgroundHexFails()
    {
        var b = Valid();
        b.BackgroundHexId = "00000000"; // not a background in the dictionary
        Assert.False(MainViewModel.IsValidBrother(b));
    }

    [Fact]
    public void PerkHexIdAsBackgroundFails()
    {
        var b = Valid();
        b.BackgroundHexId = "3E7523FA"; // fast-adaptation perk, type != "background"
        Assert.False(MainViewModel.IsValidBrother(b));
    }

    [Fact]
    public void TraitHexIdAsBackgroundFails()
    {
        var b = Valid();
        b.BackgroundHexId = "6FF46EFE"; // athletic trait, type != "background"
        Assert.False(MainViewModel.IsValidBrother(b));
    }

    [Fact]
    public void AnotherKnownBackgroundPasses()
    {
        var b = Valid();
        b.BackgroundHexId = "B47205E5"; // Killer on the run
        Assert.True(MainViewModel.IsValidBrother(b));
    }

    // ---- danglingRoleMessage ------------------------------------------------

    [Fact]
    public void DanglingRoleMessage_ListsAllNamesWhenFiveOrFewer()
    {
        var msg = MainViewModel.DanglingRoleMessage(["Horik", "Sten"]);
        Assert.StartsWith("2 名兄弟引用了已删除的角色——分配已清除：", msg, StringComparison.Ordinal);
        Assert.Contains("Horik, Sten", msg, StringComparison.Ordinal);
        Assert.DoesNotContain("等共", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void DanglingRoleMessage_CapsAtFiveNames()
    {
        var msg = MainViewModel.DanglingRoleMessage(["A", "B", "C", "D", "E", "F", "G"]);
        Assert.StartsWith("7 名兄弟引用了已删除的角色——分配已清除：", msg, StringComparison.Ordinal);
        Assert.Contains("A, B, C, D, E", msg, StringComparison.Ordinal);
        Assert.Contains(" 等共 7 名", msg, StringComparison.Ordinal);
        Assert.DoesNotContain("F", msg, StringComparison.Ordinal);
    }

    // ---- reload countdown label (extraction of Java startReloadCountdown Timeline body) --

    [Fact]
    public void Countdown_AtZeroShowsReloadedZero()
    {
        Assert.Equal("0 秒前已重新载入", MainViewModel.ReloadCountdownText(0, null));
    }

    [Fact]
    public void Countdown_NoteAppendedWithBullet()
    {
        Assert.Equal("0 秒前已重新载入 • 检测到 3 次升级",
            MainViewModel.ReloadCountdownText(0, "检测到 3 次升级"));
    }

    [Fact]
    public void Countdown_Before15CountsSeconds()
    {
        Assert.Equal("14 秒前已重新载入", MainViewModel.ReloadCountdownText(14, null));
    }

    [Fact]
    public void Countdown_At15ShowsWatching()
    {
        Assert.Equal("监视中", MainViewModel.ReloadCountdownText(15, null));
    }
}
