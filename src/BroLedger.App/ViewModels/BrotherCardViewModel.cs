using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.ViewModels;

/// <summary>
/// Perk-plan slot state behind the click cycle — port of Java model/PerkPlanStatus.java
/// (enum constants preserved as the on-disk .broledger.json spelling). UI semantics live in
/// the App layer, matching the PerkSortModeLabels precedent; Core stays untouched.
/// </summary>
internal enum PerkPlanStatus
{
    Not,
    Optional,
    Planned,
}

internal static class PerkPlanStatusLogic
{
    /// <summary>Java next(): NOT → PLANNED → OPTIONAL → NOT.</summary>
    public static PerkPlanStatus Next(this PerkPlanStatus status) => status switch
    {
        PerkPlanStatus.Not => PerkPlanStatus.Planned,
        PerkPlanStatus.Planned => PerkPlanStatus.Optional,
        _ => PerkPlanStatus.Not,
    };

    /// <summary>Java fromString: case-insensitive "OPTIONAL"/"PLANNED", anything else NOT.</summary>
    public static PerkPlanStatus FromString(string? s) => s?.ToUpperInvariant() switch
    {
        "OPTIONAL" => PerkPlanStatus.Optional,
        "PLANNED" => PerkPlanStatus.Planned,
        _ => PerkPlanStatus.Not,
    };
}

/// <summary>
/// State + decisions behind the brother card (Java ui/BrotherCardController's orchestration
/// half): the [− n +] expected-increase editor with its remainder budget, auto-assign by
/// role, the role-fit target styling, the perk-plan edits and the extra role-comparison
/// columns. Visual construction (grids, images, tooltips) lives in <c>BrotherCard</c>.
/// </summary>
internal sealed class BrotherCardViewModel
{
    private readonly UiContext _ctx;
    private readonly Dictionary<string, string> _plan = new();

    public BrotherCardViewModel(UiContext ctx) => _ctx = ctx;

    public Brother? Brother { get; private set; }

    /// <summary>Java populate's data half: current brother + its persisted perk plan.</summary>
    public void SetBrother(Brother b)
    {
        Brother = b;
        _plan.Clear();
        var stored = b.Fingerprint != null ? _ctx.Annotation.Get(b.Fingerprint).PerkPlanStatus : null;
        if (stored != null)
        {
            foreach (var (hexId, status) in stored) _plan[hexId] = status;
        }
    }

    // ---- stat display order (Java computeStatDisplayOrder) -------------------

    /// <summary>User-chosen stat column order (Task 3's ColumnOrderChanged source of truth).</summary>
    public Stat[] StatOrder() => _ctx.AppConfig.OrderedStats();

    // ---- role (Java resolveRole / roleCombo listener) ------------------------

    public Role? ResolveRole() => ResolveRole(Brother);

    public Role? ResolveRole(Brother? b)
    {
        if (b?.Fingerprint == null) return null;
        var roleId = _ctx.Annotation.Get(b.Fingerprint).RoleId;
        return roleId != null ? _ctx.Roles.GetById(roleId) : null;
    }

    /// <summary>Persists a role combo selection (Java listener's SetRole half).</summary>
    public void AssignRole(Role? role)
    {
        if (Brother?.Fingerprint == null) return;
        _ctx.Annotation.SetRole(Brother.Fingerprint, role?.Id);
    }

    // ---- increase editor (Java onIncreaseChange) -----------------------------

    /// <summary>
    /// Applies a ±1 to the planned increase of <paramref name="ordinal"/> (Stat.Ordinal),
    /// honouring the pre-11 budget (Gifted bonus folded in) or the post-11 budget, and
    /// persists. Returns false when the edit is refused.
    /// </summary>
    public bool TryApplyIncrease(int ordinal, int delta)
    {
        var b = Brother;
        if (b?.Fingerprint == null) return false;

        if (b.LevelTotal > 11)
        {
            var ann = _ctx.Annotation.Get(b.Fingerprint);
            var post11 = CopyOrDefault(ann.Post11Increases);
            var cap = b.LevelTotal - 11;
            var free = 3 * cap - OverviewCalc.SumOrZero(post11);
            if (!OverviewCalc.CanApplyIncrease(delta, post11[ordinal], cap, free)) return false;
            post11[ordinal] += delta;
            _ctx.Annotation.SetPost11Increases(b.Fingerprint, post11);
        }
        else
        {
            var remaining = ExpectedStatsCalculator.RemainingLevels(b);
            if (remaining == 0) return false;
            var cap = remaining + (IsGiftedPending() ? 1 : 0);
            var increases = CopyOrDefault(_ctx.Annotation.Get(b.Fingerprint).StatIncreases);
            var free = 3 * cap - OverviewCalc.SumOrZero(increases);
            if (!OverviewCalc.CanApplyIncrease(delta, increases[ordinal], cap, free)) return false;
            increases[ordinal] += delta;
            _ctx.Annotation.SetStatIncreases(b.Fingerprint, increases);
        }
        return true;
    }

    private static int[] CopyOrDefault(int[]? src)
    {
        var dst = new int[Stat.Values.Length];
        if (src != null) Array.Copy(src, dst, Math.Min(src.Length, dst.Length));
        return dst;
    }

    // ---- budget / expected row model (Java refreshIncreaseUi) -----------------

    public sealed record IncreaseCell(int Count, bool PlusEnabled, bool MinusEnabled,
        string ExpectedText,
        ExpectedStatsCalculator.Expected Naive,
        ExpectedStatsCalculator.Expected Greedy);

    public sealed record IncreaseUi(
        bool Lv11, bool Post11, int Cap, int Used, int TotalBudget,
        string BudgetText, string? BudgetStateClass, bool AutoAssignDisabled,
        IReadOnlyList<IncreaseCell> Cells);

    /// <summary>
    /// Java refreshIncreaseUi as a value: remainder budget label + state class, the per-stat
    /// editor cells (count, ± enabled, expected text in the configured mode, both variants
    /// for the tooltip) and the auto-assign button state.
    /// </summary>
    public IncreaseUi ComputeIncreaseUi()
    {
        var b = Brother!;
        var ann = b.Fingerprint != null ? _ctx.Annotation.Get(b.Fingerprint) : null;
        var increases = CopyOrDefault(ann?.StatIncreases);
        var post11 = CopyOrDefault(ann?.Post11Increases);

        var remainingLevels = ExpectedStatsCalculator.RemainingLevels(b);
        var giftedBonus = remainingLevels > 0 && IsGiftedPending() ? 1 : 0;
        var lb = OverviewCalc.GetLevelBudget(b.LevelTotal, remainingLevels + giftedBonus,
            increases, post11);

        // Budget label: "-X/-Y" for post-11, "X/Y" for pre-11; state colour on top.
        string? stateClass = null;
        string text;
        if (b.LevelTotal == 11)
        {
            text = "Lv 11";
        }
        else
        {
            text = OverviewCalc.FormatBudgetLabel(lb.Post11, lb.Used, lb.TotalBudget);
            if (!lb.Post11 && b.LevelPoints > 0 && b.PerkPoints == 0)
                text += "  (" + b.LevelPoints + " levels available)";
            stateClass = OverviewCalc.GetBudgetState(lb.Used, lb.TotalBudget) switch
            {
                OverviewCalc.BudgetState.Over => "increases-remaining-red",
                OverviewCalc.BudgetState.Under => "increases-remaining-green",
                _ => null,
            };
        }

        var mode = ExpectedStatsCalculator.Parse(_ctx.AppConfig.ExpectedStatsMode);
        var cells = new List<IncreaseCell>(Stat.Values.Length);
        foreach (var stat in Stat.Values)
        {
            var ord = stat.Ordinal;
            var count = lb.Post11 ? post11[ord] : increases[ord];
            var naive = ExpectedStatsCalculator.Compute(b, stat, increases, post11,
                ExpectedStatsCalculator.Mode.Naive);
            var greedy = ExpectedStatsCalculator.Compute(b, stat, increases, post11,
                ExpectedStatsCalculator.Mode.Greedy);
            cells.Add(new IncreaseCell(
                count,
                OverviewCalc.CanApplyIncrease(+1, count, lb.Cap, lb.Free),
                OverviewCalc.CanApplyIncrease(-1, count, lb.Cap, lb.Free),
                OverviewCalc.ExpectedDisplay(mode == ExpectedStatsCalculator.Mode.Greedy ? greedy : naive),
                naive,
                greedy));
        }

        return new IncreaseUi(
            b.LevelTotal == 11, lb.Post11, lb.Cap, lb.Used, lb.TotalBudget,
            text, stateClass,
            lb.Cap == 0 || ResolveRole(b) == null,
            cells);
    }

    /// <summary>Java autoAssignBtn handler: role-priority fill of the active budget, persisted.</summary>
    public void AutoAssignByRole()
    {
        var b = Brother;
        if (b?.Fingerprint == null) return;
        var role = ResolveRole(b);
        if (role == null) return;
        if (b.LevelTotal > 11)
        {
            _ctx.Annotation.SetPost11Increases(b.Fingerprint,
                ExpectedStatsCalculator.AutoAssignPost11ByRole(b, role));
        }
        else
        {
            var effective = ExpectedStatsCalculator.RemainingLevels(b) + (IsGiftedPending() ? 1 : 0);
            _ctx.Annotation.SetStatIncreases(b.Fingerprint,
                ExpectedStatsCalculator.AutoAssignByRole(role, effective));
        }
    }

    // ---- role fit targets (Java applyTargetLabel) ------------------------------

    public sealed record TargetDisplay(string Text, string? StateClass, string? WeightClass);

    /// <summary>
    /// The target cell for one stat/role pair: value, colour state (met/potential/unmet)
    /// and priority weight (P1 default bold, P2 normal, P3 light italic).
    /// </summary>
    public TargetDisplay ComputeTarget(Role? role, Stat stat)
    {
        var b = Brother!;
        var tgt = OverviewCalc.PriorityAt(role?.TargetStats, stat.Ordinal, 0);
        if (tgt <= 0) return new TargetDisplay("", null, null);

        var prio = OverviewCalc.PriorityAt(role?.Priority, stat.Ordinal, 2);
        var weight = prio == 2 ? "stat-target-p2"
            : prio == 3 ? "stat-target-p3"
            : null;

        var bd = _ctx.StatModifier.Compute(b, stat);
        var pot = StatPotentialCalculator.Compute(b, stat);
        var state = OverviewCalc.GetTargetState(bd.FinalValue, pot.FinalPotential, tgt) switch
        {
            OverviewCalc.TargetState.Met => "stat-target-met",
            OverviewCalc.TargetState.Reachable => "stat-target-potential",
            _ => "stat-target-unmet",
        };
        return new TargetDisplay(tgt.ToString(), state, weight);
    }

    // ---- perk plan (Java PerkPlanPane callbacks + badge/button updates) ---------

    /// <summary>Current plan map (snapshot copy for rendering).</summary>
    public IReadOnlyDictionary<string, string> PlanStatus => _plan;

    /// <summary>Fired after every plan mutation — the pane refreshes its slots.</summary>
    public event Action? PlanChanged;

    public bool IsGiftedPending() => OverviewCalc.IsGiftedPending(Brother, _plan);

    /// <summary>Java slot click cycle NOT → PLANNED → OPTIONAL → NOT, persisted on each edit.</summary>
    public void CyclePerkStatus(string hexId)
    {
        var next = PerkPlanStatusLogic.FromString(_plan.GetValueOrDefault(hexId)).Next();
        if (next == PerkPlanStatus.Not) _plan.Remove(hexId);
        else _plan[hexId] = next.ToString().ToUpperInvariant();
        PersistPlan();
        PlanChanged?.Invoke();
    }

    public void ClearPlan()
    {
        _plan.Clear();
        PersistPlan();
        PlanChanged?.Invoke();
    }

    /// <summary>Java initPlanBtn: seed the plan from the role template (empty when none).</summary>
    public void InitPlanFromRole()
    {
        _plan.Clear();
        var template = ResolveRole()?.PerkPlanTemplate;
        if (template != null)
        {
            foreach (var (hexId, status) in template) _plan[hexId] = status;
        }
        PersistPlan();
        PlanChanged?.Invoke();
    }

    private void PersistPlan()
    {
        if (Brother?.Fingerprint == null) return;
        _ctx.Annotation.SetPerkPlanStatus(Brother.Fingerprint,
            _plan.Count == 0 ? null : new Dictionary<string, string>(_plan));
    }

    /// <summary>Java updatePerkBadges: "P: n · O: n" badge text, null = hide.</summary>
    public static string? PlanBadgeText(IReadOnlyDictionary<string, string>? plan)
    {
        if (plan == null) return null;
        var planned = 0;
        var optional = 0;
        foreach (var v in plan.Values)
        {
            if (v == "PLANNED") planned++;
            else if (v == "OPTIONAL") optional++;
        }
        return planned == 0 && optional == 0 ? null : "P: " + planned + "  ·  O: " + optional;
    }

    /// <summary>Java updatePerkPointsBadge: "(+n unspent)" text, null = hide.</summary>
    public string? PerkPointsText() =>
        Brother is { PerkPoints: > 0 } ? "(+" + Brother.PerkPoints + " unspent)" : null;

    /// <summary>Java updateInitPlanBtn: null = hide; otherwise the current button label.</summary>
    public string? InitPlanButtonText()
    {
        if (Brother?.Fingerprint == null) return null;
        var role = ResolveRole();
        if (role?.PerkPlanTemplate is not { Count: > 0 }) return null;
        return _plan.Count > 0 ? "Reset to role template" : "Init plan from role";
    }

    // ---- extra role columns (Java rebuildExtraColumns / setExtras) --------------

    /// <summary>
    /// Comparison columns after the persistent one; at most one trailing null placeholder
    /// (Java ensurePlaceholder). Mirrored by the control's grid columns.
    /// </summary>
    public List<Role?> Extras { get; } = [];

    /// <summary>Column budget derived from the grid width (Java recomputeMaxExtraColumns).</summary>
    public int MaxExtras { get; private set; }

    /// <summary>
    /// Java recomputeMaxExtraColumns: re-budget from the grid width, then re-flow the current
    /// selections (rightmost roles drop first when the budget shrinks).
    /// </summary>
    public void UpdateMaxExtras(int max)
    {
        if (MaxExtras == max) return;
        MaxExtras = max;
        var kept = Extras.Where(r => r != null).Take(MaxExtras).ToList();
        Extras.Clear();
        Extras.AddRange(kept);
        EnsurePlaceholder();
        ExtrasChanged?.Invoke();
    }

    /// <summary>Fired whenever the extras list changes shape — the control rebuilds columns.</summary>
    public event Action? ExtrasChanged;

    /// <summary>Java setExtras preset handler (Show All / Frontline / Backline / Reset).</summary>
    public void SetExtras(IEnumerable<Role> roles)
    {
        var persistent = ResolveRole();
        Extras.Clear();
        Extras.AddRange(roles
            .Where(r => persistent == null || r.Id != persistent.Id)
            .Take(MaxExtras));
        EnsurePlaceholder();
        ExtrasChanged?.Invoke();
    }

    /// <summary>Java extra combo listener: select into a placeholder, then re-flow.</summary>
    public void SetExtra(int index, Role? role)
    {
        if (index < 0 || index >= Extras.Count) return;
        Extras[index] = role;
        EnsurePlaceholder();
        ExtrasChanged?.Invoke();
    }

    private void EnsurePlaceholder()
    {
        while (Extras.Count > 0 && Extras[^1] == null) Extras.RemoveAt(Extras.Count - 1);
        if (Extras.Count < MaxExtras) Extras.Add(null);
    }
}
