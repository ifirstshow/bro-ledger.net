using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BroLedger.App.Controls;
using BroLedger.App.ViewModels;
using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.Tests.Controls;

/// <summary>
/// Headless smoke for the T4 brother card with the real TestSavefile.sav: construction and
/// repopulation don't throw, the stats grid has the Java geometry (header row + one row per
/// stat, 8 fixed columns), equipment renders 7 slots, the footer is filled, the perk grid has
/// slots, plan edits surface the badge, back navigation raises BackRequested, and a valid
/// overview column reorder rebuilds the grid in the new order.
/// </summary>
[Collection("EnvVar-BroLedgerDataDir")]
public class BrotherCardTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _savedLastSave;
    private readonly List<string>? _savedOrder;

    public BrotherCardTests()
    {
        // Same redirection trick as BrotherOverviewPaneTests: keep AnnotationService flushes
        // out of the real ~/.bro-ledger.
        _dataDir = Path.Combine(Path.GetTempPath(), "broledger-card-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", _dataDir);

        _savedLastSave = AppConfig.Instance.LastSaveFilePath;
        _savedOrder = AppConfig.Instance.OverviewColumnOrder;
        AppConfig.Instance.OverviewColumnOrder = null;
        _saveCopy = Path.Combine(_dataDir, "TestSavefile.sav");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "TestSavefile.sav"), _saveCopy, true);
        DictionaryService.Instance.LoadFromEmbeddedResource();
        ImageMapService.Instance.LoadFromEmbeddedResource();
        StatModifierService.Instance.LoadFromEmbeddedResource();
        WeaponStatsService.Instance.LoadFromEmbeddedResource();
    }

    public void Dispose()
    {
        AppConfig.Instance.LastSaveFilePath = _savedLastSave;
        AppConfig.Instance.OverviewColumnOrder = _savedOrder;
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", null);
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    private static string _saveCopy = null!;

    private static (MainViewModel Vm, BrotherCard Card) BuildCard()
    {
        var vm = new MainViewModel();
        // Load from the temp copy: annotation flushes land as <save>.broledger.json next to
        // the save file, and must not pollute the shared TestData output.
        vm.LoadSave(_saveCopy);
        // Parse runs Task.Run off-thread; pump until the roster landed.
        HeadlessPump.Until(() => vm.Brothers.Count > 0,
            "vm.Brothers.Count > 0 (save parse applied)",
            () => $"Brothers.Count={vm.Brothers.Count}");
        var card = new BrotherCard(UiContext.Defaults(), vm.Overview, vm.Brothers[0]);
        return (vm, card);
    }

    [AvaloniaFact]
    public void RealSave_BuildsCardStructure()
    {
        var (vm, card) = BuildCard();
        try
        {
            var grid = card.GetControl<Grid>("StatsGrid");
            var equipment = card.GetControl<StackPanel>("EquipmentHost");
            var perkRows = card.GetControl<StackPanel>("PerkPlanHost");

            Assert.Equal(card.GetControl<TextBlock>("NameLabel").Text, vm.Brothers[0].Name);
            // Header row + one row per stat; 8 fixed columns until extras get budgeted.
            Assert.Equal(9, grid.RowDefinitions.Count);
            Assert.Equal(8, grid.ColumnDefinitions.Count);
            // 8 rows × 8 cells + header row's 5 labels.
            Assert.Equal(8 * 8 + 5, grid.Children.Count);

            // All 7 equipment slots render (placeholder art without a game install).
            Assert.Equal(7, equipment.Children.Count);

            // Perk grid: every tier row renders with its slots.
            Assert.True(perkRows.Children.Count > 0);

            // Footer derived stats are filled from the parsed save.
            Assert.StartsWith("护甲：", card.GetControl<TextBlock>("ArmorLabel").Text);
            Assert.StartsWith("疲劳：", card.GetControl<TextBlock>("FatigueLabel").Text);
            Assert.StartsWith("伤害：", card.GetControl<TextBlock>("DamageLabel").Text);
        }
        finally
        {
            vm.StopWatcher();
        }
    }

    [AvaloniaFact]
    public void LayoutSizeRebuild_KeepsStatValuesPainted()
    {
        var (vm, card) = BuildCard();
        var window = new Window { Width = 1200, Height = 800, Content = card };
        try
        {
            window.Show();
            window.Dispatcher.RunJobs();

            // First measurement fires StatsGrid.SizeChanged → UpdateMaxExtras → the grid
            // rebuilds (extra-column budget). The rebuilt cells must carry the brother's
            // values again, not come up blank.
            var grid = card.GetControl<Grid>("StatsGrid");
            var vals = grid.Children.OfType<TextBlock>()
                .Where(t => Grid.GetColumn(t) == 3 && Grid.GetRow(t) >= 1)
                .ToList();
            Assert.Equal(8, vals.Count);
            Assert.All(vals, v => Assert.False(string.IsNullOrEmpty(v.Text)));
        }
        finally
        {
            window.Close();
            vm.StopWatcher();
        }
    }

    [AvaloniaFact]
    public void RepopulateWithAnotherBrother_RetargetsCard()
    {
        var (vm, card) = BuildCard();
        try
        {
            Assert.True(vm.Brothers.Count > 1);
            card.Populate(vm.Brothers[1]);
            Assert.Equal(vm.Brothers[1].Name, card.GetControl<TextBlock>("NameLabel").Text);
        }
        finally
        {
            vm.StopWatcher();
        }
    }

    [AvaloniaFact]
    public void PerkPlanEdit_ShowsBadgeAndInitButton()
    {
        var (vm, card) = BuildCard();
        try
        {
            var badge = card.GetControl<TextBlock>("PerkPlanBadgeLabel");
            var clear = card.GetControl<Button>("ClearPlanBtn");
            var init = card.GetControl<Button>("InitPlanBtn");
            Assert.False(badge.IsVisible);
            Assert.False(clear.IsVisible);

            // Cycle any perk to PLANNED through the VM (the pane's slot click path).
            var dict = UiContext.Defaults().Dict;
            var hexId = dict.GetAllByType("perk").First().Key;
            var plan = new BrotherCardViewModel(UiContext.Defaults());
            plan.SetBrother(vm.Brothers[0]);
            plan.CyclePerkStatus(hexId); // persists via the shared AnnotationService

            card.Populate(vm.Brothers[0]); // card re-reads the plan on populate
            Assert.True(badge.IsVisible);
            Assert.Equal("计划 1 · 可选 0", badge.Text);
            Assert.True(clear.IsVisible);
            Assert.False(init.IsVisible); // no role template assigned → no init button

            plan.ClearPlan(); // restore
        }
        finally
        {
            vm.StopWatcher();
        }
    }

    [AvaloniaFact]
    public void BackButton_RaisesBackRequested()
    {
        var (vm, card) = BuildCard();
        try
        {
            var back = 0;
            card.BackRequested += () => back++;
            card.GetControl<Button>("BackBtn").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, back);
        }
        finally
        {
            vm.StopWatcher();
        }
    }

    [AvaloniaFact]
    public void ColumnOrderCommit_RebuildsStatRowsInNewOrder()
    {
        var (vm, card) = BuildCard();
        try
        {
            var grid = card.GetControl<Grid>("StatsGrid");
            static IReadOnlyList<string?> StatLabels(Grid g) => g.Children
                .OfType<TextBlock>()
                .Where(t => Grid.GetColumn(t) == 2 && Grid.GetRow(t) >= 1)
                .OrderBy(t => Grid.GetRow(t))
                .Select(t => t.Text)
                .ToList();

            var before = StatLabels(grid);
            Assert.Equal(8, before.Count);

            // Swap the first two stat columns (within-group move passes validation) — the
            // card's ColumnOrderChanged subscription must rebuild the rows in the new order.
            var order = OverviewViewModel.DefaultColumnIds().ToList();
            (order[4], order[5]) = (order[5], order[4]);
            Assert.True(vm.Overview.TryCommitColumnOrder(order));

            Assert.Equal(9, grid.RowDefinitions.Count);
            Assert.Equal(8, grid.ColumnDefinitions.Count);
            var after = StatLabels(grid);
            Assert.Equal(before[1], after[0]);
            Assert.Equal(before[0], after[1]);
        }
        finally
        {
            vm.StopWatcher();
        }
    }
}

// 计划按钮接线钉（留白手验兜底）：ClearPlanBtn/InitPlanBtn 曾漏接 Click——
// VM 方法存在但 UI 无调用。用 RaiseEvent(Click) 钉真实接线，直调 VM 测不到。
[Collection("EnvVar-BroLedgerDataDir")]
public class BrotherCardPlanButtonTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _savedConfig;
    private static string _saveCopy = null!;

    public BrotherCardPlanButtonTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "broledger-planbtn-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", _dataDir);
        _savedConfig = AppConfig.ConfigFileForTest;
        AppConfig.ConfigFileForTest = Path.Combine(_dataDir, "config.json");
        _saveCopy = Path.Combine(_dataDir, "TestSavefile.sav");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "TestSavefile.sav"), _saveCopy, true);
        DictionaryService.Instance.LoadFromEmbeddedResource();
        ImageMapService.Instance.LoadFromEmbeddedResource();
        StatModifierService.Instance.LoadFromEmbeddedResource();
        WeaponStatsService.Instance.LoadFromEmbeddedResource();
    }

    public void Dispose()
    {
        AppConfig.ConfigFileForTest = _savedConfig;
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", null);
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    private static (MainViewModel Vm, BrotherCard Card, Brother Brother) BuildCardWithPlan()
    {
        var vm = new MainViewModel();
        vm.LoadSave(_saveCopy);
        HeadlessPump.Until(() => vm.Brothers.Count > 0, "roster loaded");
        var brother = vm.Brothers[0];
        AnnotationService.Instance.Get(brother.Fingerprint!).PerkPlanStatus =
            new Dictionary<string, string> { ["AAAA1111"] = "PLANNED", ["BBBB2222"] = "OPTIONAL" };
        var card = new BrotherCard(UiContext.Defaults(), vm.Overview, brother);
        return (vm, card, brother);
    }

    [AvaloniaFact]
    public void ClearPlanBtn_ClearsPlanAndPersists()
    {
        var (vm, card, brother) = BuildCardWithPlan();
        var window = new Window { Content = card };
        window.Show();
        try
        {
            var clearBtn = card.GetControl<Button>("ClearPlanBtn");
            Assert.True(clearBtn.IsVisible, "有计划时清除按钮可见");

            clearBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var status = AnnotationService.Instance.Get(brother.Fingerprint!).PerkPlanStatus;
            Assert.Null(status); // 已持久化为 null（Java setPerkPlanStatus(fp, null) 同语义）
            Assert.False(clearBtn.IsVisible);
        }
        finally
        {
            vm.StopWatcher();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InitPlanBtn_SeedsFromRoleTemplate()
    {
        var (vm, card, brother) = BuildCardWithPlan();
        var window = new Window { Content = card };
        window.Show();
        try
        {
            // 造一个带模板的角色并指派给该兄弟
            var role = RoleService.Instance.Add("钉子角色", true);
            role.PerkPlanTemplate = new Dictionary<string, string> { ["CCCC3333"] = "OPTIONAL" };
            RoleService.Instance.Update(role);
            AnnotationService.Instance.SetRole(brother.Fingerprint!, role.Id);
            card.Populate(brother);

            var initBtn = card.GetControl<Button>("InitPlanBtn");
            Assert.True(initBtn.IsVisible);
            Assert.Equal("重置为角色模板", initBtn.Content); // 已有计划 → 重置文案

            initBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var status = AnnotationService.Instance.Get(brother.Fingerprint!).PerkPlanStatus;
            Assert.NotNull(status);
            Assert.Equal("OPTIONAL", status!["CCCC3333"]); // 模板已落为计划
        }
        finally
        {
            vm.StopWatcher();
            window.Close();
        }
    }
}
