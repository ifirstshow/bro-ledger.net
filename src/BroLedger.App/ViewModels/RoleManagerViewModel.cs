using System.Collections.ObjectModel;
using BroLedger.App.Controls;
using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.ViewModels;

/// <summary>
/// UI-free half of the role manager (Java ui/RoleManagerController): the role list CRUD +
/// ordering, the per-stat target/priority form state, the perk-plan template state and the
/// delete-confirmation text. The window (role-manager.fxml half) builds the stat editor grid
/// and pushes/pulls this state; the perk-plan template pane hosts it via IPerkPlanState.
/// </summary>
internal sealed class RoleManagerViewModel : IPerkPlanState
{
    private readonly UiContext _ctx;
    private readonly Dictionary<string, string> _template = new();

    public RoleManagerViewModel(UiContext ctx)
    {
        _ctx = ctx;
        // targetFields/prioGroups are stat-ordinal indexed in Java so save/load stays
        // canonical; here the rows carry their Stat and index by Stat.Ordinal. Visual
        // order comes from OrderedStats().
        StatRows = [.. ctx.AppConfig.OrderedStats().Select(s => new StatRow(s))];
        RefreshList(null);
    }

    /// <summary>One stat row of the editor: trimmed target text (empty = unset) + priority 1–3.</summary>
    public sealed class StatRow(Stat stat)
    {
        public Stat Stat { get; } = stat;
        public string Target { get; set; } = "";
        public int Priority { get; set; } = 3;
    }

    public ObservableCollection<Role> Roles { get; } = [];

    public Role? Selected { get; private set; }

    public bool FormEnabled => Selected != null;

    // ---- form state (the window syncs its controls to this on FormChanged) ----

    public string Name { get; set; } = "";
    public bool Frontline { get; set; } = true;
    public List<StatRow> StatRows { get; }

    /// <summary>Java roleUsageCounter — loaded-save brothers per role id; null = unknown.</summary>
    public Func<string, long?>? RoleUsageCounter { get; set; }

    /// <summary>Java onRolesChanged — fired after every catalogue mutation.</summary>
    public event Action? RolesChanged;

    /// <summary>Fired after selection/form state changed — the window resyncs its controls.</summary>
    public event Action? FormChanged;

    // ---- list actions (Java FXML actions) --------------------------------------

    public void AddRole()
    {
        var r = _ctx.Roles.Add("新角色", true);
        RefreshList(r);
        RolesChanged?.Invoke();
    }

    /// <summary>
    /// The full confirmation body for the selected role, or null when nothing is selected
    /// (Java deleteRole's early return). The window supplies title/header/buttons.
    /// </summary>
    public string? DeleteConfirmation()
    {
        var selected = Selected;
        if (selected == null) return null;
        var assigned = RoleUsageCounter?.Invoke(selected.Id!);
        return DeleteConfirmationText(assigned);
    }

    /// <summary>Java deleteRole's post-confirm half — ClearRoleRefs included.</summary>
    public void DeleteRole()
    {
        var selected = Selected;
        if (selected == null) return;
        _ctx.Roles.Delete(selected.Id);
        // Clear the loaded save's references immediately; other saves are repaired
        // (with a notification) when they are next loaded.
        _ctx.Annotation.ClearRoleRefs(selected.Id);
        RefreshList(null);
        RolesChanged?.Invoke();
    }

    public void MoveRoleUp()
    {
        var selected = Selected;
        if (selected == null) return;
        var idx = Roles.IndexOf(selected);
        if (idx <= 0) return;
        _ctx.Roles.Move(selected.Id!, idx - 1);
        RefreshList(selected);
        RolesChanged?.Invoke();
    }

    public void MoveRoleDown()
    {
        var selected = Selected;
        if (selected == null) return;
        var idx = Roles.IndexOf(selected);
        if (idx < 0 || idx >= Roles.Count - 1) return;
        _ctx.Roles.Move(selected.Id!, idx + 1);
        RefreshList(selected);
        RolesChanged?.Invoke();
    }

    /// <summary>
    /// Java saveRole: false when the name is blank (the window focuses the name field),
    /// otherwise persists name/position/targets/priorities into the selected role.
    /// </summary>
    public bool SaveRole()
    {
        var selected = Selected;
        if (selected == null) return false;

        var name = Name.Trim();
        if (name.Length == 0) return false;

        selected.Name = name;
        selected.Frontline = Frontline;
        selected.TargetStats ??= new int[8];
        selected.Priority ??= [3, 3, 3, 3, 3, 3, 3, 3];
        foreach (var row in StatRows)
        {
            var idx = row.Stat.Ordinal;
            selected.TargetStats[idx] = ParseTarget(row.Target);
            selected.Priority[idx] = row.Priority;
        }

        _ctx.Roles.Update(selected);
        RefreshList(selected);
        RolesChanged?.Invoke();
        return true;
    }

    /// <summary>Java setOnImported's callback half — the import dialog finished.</summary>
    public void OnImportCompleted()
    {
        RefreshList(null);
        RolesChanged?.Invoke();
    }

    // ---- perk plan template (Java setUiContext's pane callback + populateForm half) ----

    /// <summary>Current template map of the selected role (snapshot copy for rendering).</summary>
    public IReadOnlyDictionary<string, string> PlanStatus => _template;

    /// <summary>Fired after every template mutation — pane slots + badge refresh.</summary>
    public event Action? PlanChanged;

    /// <summary>
    /// Java PerkPlanPane.setOnChange: cycle one slot NOT → PLANNED → OPTIONAL → NOT, write the
    /// template back to the selected role (null when empty), persist + notify.
    /// </summary>
    public void CyclePerkStatus(string hexId)
    {
        var selected = Selected;
        if (selected == null) return;
        var next = PerkPlanStatusLogic.FromString(_template.GetValueOrDefault(hexId)).Next();
        if (next == PerkPlanStatus.Not) _template.Remove(hexId);
        else _template[hexId] = next.ToString().ToUpperInvariant();
        selected.PerkPlanTemplate = _template.Count == 0
            ? null
            : new Dictionary<string, string>(_template);
        _ctx.Roles.Update(selected);
        PlanChanged?.Invoke();
        RolesChanged?.Invoke();
    }

    /// <summary>Java selection listener → populateForm / setFormDisabled.</summary>
    public void Select(Role? r)
    {
        Selected = r;
        if (r == null)
        {
            Name = "";
            Frontline = true;
            _template.Clear();
            foreach (var row in StatRows)
            {
                row.Target = "";
                row.Priority = 3;
            }
        }
        else
        {
            Name = r.Name ?? "";
            Frontline = r.Frontline;
            _template.Clear();
            if (r.PerkPlanTemplate != null)
            {
                foreach (var (hexId, status) in r.PerkPlanTemplate) _template[hexId] = status;
            }
            foreach (var row in StatRows)
            {
                var idx = row.Stat.Ordinal;
                var t = r.TargetStats != null && r.TargetStats.Length > idx ? r.TargetStats[idx] : 0;
                row.Target = t > 0 ? t.ToString() : "";
                row.Priority = r.Priority != null && r.Priority.Length > idx ? r.Priority[idx] : 3;
            }
        }
        FormChanged?.Invoke();
    }

    /// <summary>Java refreshList — reload the catalogue, optionally reselecting by id.</summary>
    private void RefreshList(Role? toSelect)
    {
        Roles.Clear();
        foreach (var r in _ctx.Roles.GetAll()) Roles.Add(r);
        Select(toSelect != null ? Roles.FirstOrDefault(x => x.Id == toSelect.Id) : null);
    }

    /// <summary>Java parseIntSafe — blank or unparseable → 0 (negative values parse as-is).</summary>
    private static int ParseTarget(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return 0;
        return int.TryParse(t, out var v) ? v : 0;
    }

    /// <summary>Confirmation body; {@code assigned} is the loaded-save usage count, or null when unknown.</summary>
    public static string DeleteConfirmationText(long? assigned)
    {
        var usage = assigned switch
        {
            null => "已分配该角色的兄弟将失去此分配。",
            0 => "当前存档中没有兄弟分配到此角色。",
            _ => "当前存档中有 " + assigned + " 名兄弟分配到此角色——"
                + "他们的分配将被清除。",
        };
        return usage + "\n\n其他存档中的分配将在其下次载入时清除。此操作无法撤销。";
    }
}
