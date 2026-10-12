using Avalonia.Headless.XUnit;
using BroLedger.App.ViewModels;
using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.Tests.ViewModels;

/// <summary>
/// Orchestration-layer nails for the brother card (Java ui/BrotherCardController's decision
/// points, extracted into BrotherCardViewModel). Java has no UI tests for this controller, so
/// the VM layer is pinned here instead: the [− n +] expected-editor budget/cap rules, the
/// remainder budget label, auto-assign persistence, role-fit target styling, the perk-plan
/// click cycle and the extra-column presets. Plan ② T14 already covers OverviewCalc's pure
/// math; these tests pin the wiring of that math into the card's annotations.
/// </summary>
[Collection("EnvVar-BroLedgerDataDir")]
public class BrotherCardViewModelTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _savedMode;
    private readonly List<string>? _savedOrder;

    public BrotherCardViewModelTests()
    {
        // Same redirection trick as OverviewViewModelTests: keep AnnotationService flushes
        // and AppConfig.Save out of the real ~/.bro-ledger.
        _dataDir = Path.Combine(Path.GetTempPath(), "broledger-card-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", _dataDir);

        _savedMode = AppConfig.Instance.ExpectedStatsMode;
        _savedOrder = AppConfig.Instance.OverviewColumnOrder;
        DictionaryService.Instance.LoadFromEmbeddedResource();
        StatModifierService.Instance.LoadFromEmbeddedResource();
        WeaponStatsService.Instance.LoadFromEmbeddedResource();
    }

    public void Dispose()
    {
        AppConfig.Instance.ExpectedStatsMode = _savedMode!;
        AppConfig.Instance.OverviewColumnOrder = _savedOrder;
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", null);
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    private const string GiftedHexId = "9899E380";

    /// <summary>Level 9 brother: 2 remaining level-ups, 6-point budget, no post-11.</summary>
    private static Brother MakeBrother(string fingerprint, int levelTotal = 9, int levelPoints = 0)
    {
        var b = new Brother
        {
            Name = "Test " + fingerprint,
            Fingerprint = fingerprint,
            LevelTotal = levelTotal,
            LevelPoints = levelPoints,
            PerkPoints = 0,
            BackgroundHexId = null,
        };
        return b;
    }

    private BrotherCardViewModel VmFor(Brother b, string saveName)
    {
        AnnotationService.Instance.LoadFor(Path.Combine(_dataDir, saveName));
        var vm = new BrotherCardViewModel(UiContext.Defaults());
        vm.SetBrother(b);
        return vm;
    }

    // ---- increase editor: Java onIncreaseChange -----------------------------

    [Fact]
    public void TryApplyIncrease_Pre11_RespectsPerStatCapAndBudget()
    {
        var b = MakeBrother("fp1"); // level 9 → cap 2, budget 6
        var vm = VmFor(b, "a.sav");

        // Two increases on Health hit the per-stat cap; the third is refused.
        Assert.True(vm.TryApplyIncrease(Stat.Health.Ordinal, +1));
        Assert.True(vm.TryApplyIncrease(Stat.Health.Ordinal, +1));
        Assert.False(vm.TryApplyIncrease(Stat.Health.Ordinal, +1));

        // Fill the rest of the budget (4 more across other stats); budget-full refuses +.
        Assert.True(vm.TryApplyIncrease(Stat.Resolve.Ordinal, +1));
        Assert.True(vm.TryApplyIncrease(Stat.Fatigue.Ordinal, +1));
        Assert.True(vm.TryApplyIncrease(Stat.MeleeSkill.Ordinal, +1));
        Assert.True(vm.TryApplyIncrease(Stat.RangedSkill.Ordinal, +1));
        Assert.False(vm.TryApplyIncrease(Stat.MeleeDefense.Ordinal, +1));

        // Persisted as annotation statIncreases, indexed by Stat.Ordinal.
        var ann = AnnotationService.Instance.Get("fp1");
        Assert.Equal(2, ann.StatIncreases![Stat.Health.Ordinal]);
        Assert.Equal(6, ann.StatIncreases.Sum());

        // Minus always allowed down to zero and refunds budget.
        Assert.True(vm.TryApplyIncrease(Stat.Health.Ordinal, -1));
        Assert.Equal(1, ann.StatIncreases[Stat.Health.Ordinal]);
        Assert.True(vm.TryApplyIncrease(Stat.Health.Ordinal, -1));
        Assert.False(vm.TryApplyIncrease(Stat.Health.Ordinal, -1)); // never below zero
    }

    [Fact]
    public void TryApplyIncrease_Post11_UsesPost11Budget()
    {
        var b = MakeBrother("fp2", levelTotal: 13); // 2 post-11 levels → cap 2, budget 6
        var vm = VmFor(b, "b.sav");

        Assert.True(vm.TryApplyIncrease(Stat.Initiative.Ordinal, +1));
        Assert.True(vm.TryApplyIncrease(Stat.Initiative.Ordinal, +1));
        Assert.False(vm.TryApplyIncrease(Stat.Initiative.Ordinal, +1)); // per-stat cap

        var ann = AnnotationService.Instance.Get("fp2");
        Assert.Equal(2, ann.Post11Increases![Stat.Initiative.Ordinal]);
        Assert.Null(ann.StatIncreases); // pre-11 store untouched
    }

    [Fact]
    public void TryApplyIncrease_GiftedPending_ExpandsCapAndBudget()
    {
        var b = MakeBrother("fp3"); // cap 2 / budget 6 normally
        var vm = VmFor(b, "c.sav");

        // Plan Gifted → +1 level → cap 3, budget 9.
        vm.CyclePerkStatus(GiftedHexId); // NOT → PLANNED
        Assert.True(vm.IsGiftedPending());

        for (var i = 0; i < 3; i++)
            Assert.True(vm.TryApplyIncrease(Stat.Resolve.Ordinal, +1));
        Assert.False(vm.TryApplyIncrease(Stat.Resolve.Ordinal, +1)); // cap 3 now
    }

    [Fact]
    public void TryApplyIncrease_AtLevel11_NothingApplies()
    {
        var b = MakeBrother("fp4", levelTotal: 11);
        var vm = VmFor(b, "d.sav");

        Assert.False(vm.TryApplyIncrease(Stat.Health.Ordinal, +1));
        Assert.False(vm.TryApplyIncrease(Stat.Health.Ordinal, -1));
    }

    // ---- remainder budget label: Java refreshIncreaseUi ----------------------

    [Fact]
    public void ComputeIncreaseUi_BudgetLabel_StatesAndSuffix()
    {
        var b = MakeBrother("fp5", levelPoints: 2); // remaining 4 → budget 12, unassigned levels
        var vm = VmFor(b, "e.sav");

        var ui = vm.ComputeIncreaseUi();
        Assert.False(ui.Post11);
        Assert.Equal(4, ui.Cap);
        Assert.Equal(12, ui.TotalBudget);
        Assert.Equal("0/12  (2 levels available)", ui.BudgetText);
        Assert.Equal("increases-remaining-green", ui.BudgetStateClass); // under → green
    }

    [Fact]
    public void ComputeIncreaseUi_OverBudget_ShowsRed()
    {
        var b = MakeBrother("fp6");
        AnnotationService.Instance.LoadFor(Path.Combine(_dataDir, "f.sav"));
        // Stale over-budget allocation written directly (the editor itself refuses it).
        AnnotationService.Instance.SetStatIncreases("fp6", [3, 3, 3, 0, 0, 0, 0, 0]); // 9 > 6
        var vm = new BrotherCardViewModel(UiContext.Defaults());
        vm.SetBrother(b);

        var ui = vm.ComputeIncreaseUi();
        Assert.Equal("9/6", ui.BudgetText);
        Assert.Equal("increases-remaining-red", ui.BudgetStateClass);
    }

    [Fact]
    public void ComputeIncreaseUi_Post11_NegativeLabelFormat()
    {
        var b = MakeBrother("fp7", levelTotal: 12);
        var vm = VmFor(b, "g.sav");

        Assert.True(vm.TryApplyIncrease(Stat.Health.Ordinal, +1));
        var ui = vm.ComputeIncreaseUi();
        Assert.True(ui.Post11);
        Assert.Equal("-1/-3", ui.BudgetText);
        Assert.Equal("increases-remaining-green", ui.BudgetStateClass);
    }

    [Fact]
    public void ComputeIncreaseUi_Level11_ShowsLv11Label()
    {
        var b = MakeBrother("fp8", levelTotal: 11);
        var vm = VmFor(b, "h.sav");

        var ui = vm.ComputeIncreaseUi();
        Assert.True(ui.Lv11);
        Assert.Equal("Lv 11", ui.BudgetText);
    }

    [Fact]
    public void ComputeIncreaseUi_Cells_TrackCountAndExpectedAndButtonState()
    {
        var b = MakeBrother("fp9");
        var vm = VmFor(b, "i.sav");

        Assert.True(vm.TryApplyIncrease(Stat.Health.Ordinal, +1)); // cap 2 → + still enabled
        var ui = vm.ComputeIncreaseUi();

        Assert.Equal(8, ui.Cells.Count);
        var hp = ui.Cells[Stat.Health.Ordinal];
        Assert.Equal(1, hp.Count);
        Assert.True(hp.PlusEnabled);
        Assert.True(hp.MinusEnabled);

        // No mode configured → NAIVE display; one increase allotted → numeric text.
        Assert.Equal(
            ExpectedStatsCalculator.Compute(b, Stat.Health, [1, 0, 0, 0, 0, 0, 0, 0],
                ExpectedStatsCalculator.Mode.Naive).FinalExpected.ToString(),
            hp.ExpectedText);
        // Tooltip gets both variants.
        Assert.NotNull(hp.Naive);
        Assert.NotNull(hp.Greedy);

        var untouched = ui.Cells[Stat.MeleeDefense.Ordinal];
        Assert.Equal(0, untouched.Count);
        Assert.Equal("—", untouched.ExpectedText); // remaining>0, count 0 → em dash
        Assert.False(untouched.MinusEnabled);
    }

    [Fact]
    public void ComputeIncreaseUi_GreedyModeFromConfig_IsHonoured()
    {
        AppConfig.Instance.ExpectedStatsMode = "GREEDY";
        var b = MakeBrother("fp10");
        var vm = VmFor(b, "j.sav");
        vm.TryApplyIncrease(Stat.Health.Ordinal, +1);

        var ui = vm.ComputeIncreaseUi();
        Assert.Equal(
            ExpectedStatsCalculator.Compute(b, Stat.Health, [1, 0, 0, 0, 0, 0, 0, 0],
                ExpectedStatsCalculator.Mode.Greedy).FinalExpected.ToString(),
            ui.Cells[Stat.Health.Ordinal].ExpectedText);
    }

    [Fact]
    public void ComputeIncreaseUi_AutoAssignDisabled_WithoutRoleOrBudget()
    {
        var b = MakeBrother("fp11");
        var vm = VmFor(b, "k.sav");
        Assert.True(vm.ComputeIncreaseUi().AutoAssignDisabled); // no role assigned

        // With a role and remaining budget → enabled.
        var role = EnsureRole("Enabled", frontline: true);
        AnnotationService.Instance.SetRole("fp11", role.Id);
        Assert.False(vm.ComputeIncreaseUi().AutoAssignDisabled);
    }

    // ---- auto-assign: Java setupRoleButtons.autoAssignBtn --------------------

    [Fact]
    public void AutoAssignByRole_Pre11_FillsPriorityTiersAndPersists()
    {
        var b = MakeBrother("fp12"); // remaining 2 → budget 6
        var vm = VmFor(b, "l.sav");
        var role = EnsureRole(new[] { 1, 1, 1, 3, 3, 3, 3, 3 });
        AnnotationService.Instance.SetRole("fp12", role.Id);

        vm.AutoAssignByRole();

        var ann = AnnotationService.Instance.Get("fp12");
        Assert.Equal(new[] { 2, 2, 2, 0, 0, 0, 0, 0 }, ann.StatIncreases);
        Assert.Equal(6, ann.StatIncreases!.Sum());
    }

    [Fact]
    public void AutoAssignByRole_Post11_PersistsPost11Increases()
    {
        var b = MakeBrother("fp13", levelTotal: 13);
        var vm = VmFor(b, "m.sav");
        var role = EnsureRole(new[] { 3, 1, 3, 3, 3, 3, 3, 3 });
        AnnotationService.Instance.SetRole("fp13", role.Id);

        vm.AutoAssignByRole();

        // P1 Resolve maxed (cap 2); the remaining 4 points cascade into P3, split equally
        // with the alphabetical tiebreaker (Fatigue, Health, Initiative, Melee Defense).
        var ann = AnnotationService.Instance.Get("fp13");
        Assert.Equal(new[] { 1, 2, 1, 1, 0, 0, 1, 0 }, ann.Post11Increases);
    }

    // ---- role fit: Java applyTargetLabel -------------------------------------

    [Fact]
    public void ComputeTarget_ThreeStates_ColourClasses()
    {
        // Level 9, 3★ Health: potential base = 50 + 2 levels × mean(4.5) ≈ 59.
        var b = MakeBrother("fp14");
        b.Stats[Stat.Health.StatIndex] = 50;
        b.Stars[Stat.Health.StarIndex] = 3;
        var vm = VmFor(b, "n.sav");

        var met = vm.ComputeTarget(EnsureRole(target: 40), Stat.Health);
        Assert.Equal("40", met.Text);
        Assert.Equal("stat-target-met", met.StateClass);

        var reachable = vm.ComputeTarget(EnsureRole(target: 59), Stat.Health);
        Assert.Equal("stat-target-potential", reachable.StateClass);

        var unmet = vm.ComputeTarget(EnsureRole(target: 100), Stat.Health);
        Assert.Equal("stat-target-unmet", unmet.StateClass);
    }

    [Fact]
    public void ComputeTarget_PriorityWeight_Classes_AndZeroTargetEmpty()
    {
        var b = MakeBrother("fp15");
        b.Stats[Stat.Health.StatIndex] = 50;
        var vm = VmFor(b, "o.sav");

        // P1 = bold (no extra class, .stat-target default), P2 = normal, P3 = light italic.
        Assert.Null(vm.ComputeTarget(EnsureRole(target: 40, priority: 1), Stat.Health).WeightClass);
        Assert.Equal("stat-target-p2",
            vm.ComputeTarget(EnsureRole(target: 40, priority: 2), Stat.Health).WeightClass);
        Assert.Equal("stat-target-p3",
            vm.ComputeTarget(EnsureRole(target: 40, priority: 3), Stat.Health).WeightClass);

        // No target set (0) → empty label, no classes.
        var none = vm.ComputeTarget(EnsureRole(target: 0), Stat.Health);
        Assert.Equal("", none.Text);
        Assert.Null(none.StateClass);
        Assert.Null(none.WeightClass);
    }

    // ---- perk plan: Java PerkPlanPane click cycle + badges --------------------

    [Fact]
    public void CyclePerkStatus_NotPlannedOptionalNot_WithPersistence()
    {
        var b = MakeBrother("fp16");
        var vm = VmFor(b, "p.sav");
        var fired = 0;
        vm.PlanChanged += () => fired++;

        vm.CyclePerkStatus(GiftedHexId);
        Assert.Equal("PLANNED", vm.PlanStatus[GiftedHexId]);
        Assert.Equal("PLANNED", AnnotationService.Instance.Get("fp16").PerkPlanStatus![GiftedHexId]);

        vm.CyclePerkStatus(GiftedHexId);
        Assert.Equal("OPTIONAL", vm.PlanStatus[GiftedHexId]);

        vm.CyclePerkStatus(GiftedHexId); // OPTIONAL → NOT → removed from the map
        Assert.False(vm.PlanStatus.ContainsKey(GiftedHexId));
        Assert.Null(AnnotationService.Instance.Get("fp16").PerkPlanStatus); // empty plan → null
        Assert.Equal(3, fired);
    }

    [Fact]
    public void InitPlanFromRole_UsesTemplate_ClearPlan_Empties()
    {
        var b = MakeBrother("fp17");
        var vm = VmFor(b, "q.sav");
        var role = EnsureRole(priority: null, template: new Dictionary<string, string>
        {
            [GiftedHexId] = "PLANNED",
            ["B1C35E23"] = "OPTIONAL",
        });
        AnnotationService.Instance.SetRole("fp17", role.Id);

        vm.InitPlanFromRole();
        Assert.Equal(2, vm.PlanStatus.Count);
        Assert.Equal("PLANNED", vm.PlanStatus[GiftedHexId]);
        Assert.Equal(2, AnnotationService.Instance.Get("fp17").PerkPlanStatus!.Count);

        vm.ClearPlan();
        Assert.Empty(vm.PlanStatus);
        Assert.Null(AnnotationService.Instance.Get("fp17").PerkPlanStatus);
    }

    [Fact]
    public void PlanBadgeText_CountsPlannedAndOptional()
    {
        Assert.Null(BrotherCardViewModel.PlanBadgeText(null));
        Assert.Null(BrotherCardViewModel.PlanBadgeText(new Dictionary<string, string>())); // empty → null

        Assert.Equal("P: 2  ·  O: 1", BrotherCardViewModel.PlanBadgeText(new Dictionary<string, string>
        {
            ["a"] = "PLANNED", ["b"] = "OPTIONAL", ["c"] = "PLANNED",
        }));
    }

    [Fact]
    public void InitPlanButtonText_FollowsRoleTemplateAndExistingPlan()
    {
        var b = MakeBrother("fp18");
        var vm = VmFor(b, "r.sav");

        Assert.Null(vm.InitPlanButtonText()); // no role → hidden

        var role = EnsureRole(priority: null, template: new Dictionary<string, string> { [GiftedHexId] = "PLANNED" });
        AnnotationService.Instance.SetRole("fp18", role.Id);
        Assert.Equal("Init plan from role", vm.InitPlanButtonText());

        vm.InitPlanFromRole();
        Assert.Equal("Reset to role template", vm.InitPlanButtonText());
    }

    // ---- extra columns: Java setExtras presets + ensurePlaceholder ------------

    [Fact]
    public void SetExtras_FiltersPersistentRole_LimitsAndAppendsPlaceholder()
    {
        var b = MakeBrother("fp19");
        var vm = VmFor(b, "s.sav");
        var persistent = EnsureRole("Persistent");
        AnnotationService.Instance.SetRole("fp19", persistent.Id);
        vm.UpdateMaxExtras(3);

        var frontline = EnsureRole("Front A", frontline: true);
        var backline = EnsureRole("Back A", frontline: false);
        vm.SetExtras([frontline, backline, persistent]);

        // Persistent filtered out; 2 remain below the limit 3 → one null placeholder appended.
        Assert.Equal(3, vm.Extras.Count);
        Assert.Same(frontline, vm.Extras[0]);
        Assert.Same(backline, vm.Extras[1]);
        Assert.Null(vm.Extras[2]);
    }

    [Fact]
    public void SetExtra_SelectionReplacesPlaceholder_AndAppendsNewOne()
    {
        var b = MakeBrother("fp20");
        var vm = VmFor(b, "t.sav");
        vm.UpdateMaxExtras(2);
        vm.SetExtras([]);

        Assert.Single(vm.Extras); // one placeholder
        var role = EnsureRole("Chosen", frontline: true);
        vm.SetExtra(0, role);

        Assert.Same(role, vm.Extras[0]);
        Assert.Equal(2, vm.Extras.Count); // new placeholder appended (room for maxExtras 2)
        Assert.Null(vm.Extras[1]);
    }

    // ---- stat display order: Java computeStatDisplayOrder ----------------------

    [Fact]
    public void StatOrder_FollowsOverviewColumnOrder()
    {
        var b = MakeBrother("fp21");
        var vm = VmFor(b, "u.sav");
        Assert.Equal(Stat.Health, vm.StatOrder()[0]);

        // Reorder stat columns in the overview config → card mirrors it.
        var order = OverviewViewModel.DefaultColumnIds().ToList();
        (order[4], order[5]) = (order[5], order[4]); // swap stat-HP / stat-Res
        AppConfig.Instance.OverviewColumnOrder = order;

        Assert.Equal(Stat.Resolve, vm.StatOrder()[0]);
        Assert.Equal(Stat.Health, vm.StatOrder()[1]);
    }

    // ---- fixture helpers -------------------------------------------------------

    private static Role EnsureRole(int[]? priority = null, Dictionary<string, string>? template = null)
    {
        var r = RoleService.Instance.Add("Cardier " + Guid.NewGuid().ToString("N")[..6], frontline: true);
        r.Priority = priority ?? [3, 3, 3, 3, 3, 3, 3, 3];
        r.PerkPlanTemplate = template;
        return r;
    }

    private static Role EnsureRole(int target, int priority = 1)
    {
        var r = EnsureRole(priority: [priority, 3, 3, 3, 3, 3, 3, 3]);
        r.TargetStats = [target, 0, 0, 0, 0, 0, 0, 0];
        return r;
    }

    private static Role EnsureRole(string? name, bool frontline = true) =>
        RoleService.Instance.Add(name ?? "Role", frontline);
}
