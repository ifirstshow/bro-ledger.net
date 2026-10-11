using BroLedger.App.ViewModels;
using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.Tests.ViewModels;

/// <summary>
/// Migration of JAVATEST/ui/RoleManagerControllerTest.java. All three Java cases pin the
/// static deleteConfirmationText and transfer 1:1 (they were never JavaFX-bound). The
/// remainder pin the controller's add/select/save/move/delete choreography at VM level —
/// in Java those ran against live controls; here the form state is plain data on
/// RoleManagerViewModel and the window pushes/pulls it.
/// </summary>
public class RoleManagerViewModelTests : IDisposable
{
    private readonly string _dataDir;

    public RoleManagerViewModelTests()
    {
        // Same redirection trick as BrotherCardViewModelTests: keep roles.json + config.json
        // out of the real ~/.bro-ledger.
        _dataDir = Path.Combine(Path.GetTempPath(), "broledger-role-manager-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", _dataDir);
        RoleService.CreateForTest(Path.Combine(_dataDir, "roles.json"));
    }

    public void Dispose()
    {
        RoleService.ResetInstance();
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", null);
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    private RoleManagerViewModel Vm() => new(UiContext.Defaults());

    // ---- migrated: deleteConfirmationText (RoleManagerControllerTest) ---------

    [Fact]
    public void DeleteConfirmationText_UnknownUsage()
    {
        var text = RoleManagerViewModel.DeleteConfirmationText(null);
        Assert.Contains("Any brothers assigned to it", text);
        Assert.Contains("cannot be undone", text);
    }

    [Fact]
    public void DeleteConfirmationText_ZeroAssigned()
    {
        var text = RoleManagerViewModel.DeleteConfirmationText(0);
        Assert.Contains("No brothers in the current save", text);
    }

    [Fact]
    public void DeleteConfirmationText_SomeAssigned()
    {
        var text = RoleManagerViewModel.DeleteConfirmationText(4);
        Assert.Contains("assigned to 4 brother(s) in the current save", text);
        Assert.Contains("cleared", text);
    }

    // ---- VM equivalents: add / save / populate (Java addRole/saveRole/populateForm)

    [Fact]
    public void AddRole_AppendsSelectsAndNotifies()
    {
        var vm = Vm();
        var changed = 0;
        vm.RolesChanged += () => changed++;

        vm.AddRole();

        var role = Assert.Single(vm.Roles);
        Assert.Equal("New Role", role.Name);
        Assert.True(role.Frontline);
        Assert.Same(role, vm.Selected);
        Assert.True(vm.FormEnabled);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void SaveRole_EmptyName_Refused()
    {
        var vm = Vm();
        vm.AddRole();
        vm.Name = "   ";
        Assert.False(vm.SaveRole());
        Assert.Equal("New Role", vm.Selected!.Name); // untouched (Java: requestFocus + return)
    }

    [Fact]
    public void SaveRole_PersistsFormState()
    {
        var vm = Vm();
        vm.AddRole();
        vm.Name = "  Tank  ";
        vm.Frontline = false;
        vm.StatRows[Stat.Health.Ordinal].Target = "17";
        vm.StatRows[Stat.Health.Ordinal].Priority = 1;
        vm.StatRows[Stat.Initiative.Ordinal].Priority = 2;

        Assert.True(vm.SaveRole());

        var stored = vm.Selected!;
        Assert.Equal("Tank", stored.Name);
        Assert.False(stored.Frontline);
        Assert.Equal(17, stored.TargetStats![Stat.Health.Ordinal]);
        Assert.Equal(0, stored.TargetStats![Stat.Resolve.Ordinal]); // blank target → 0
        Assert.Equal(1, stored.Priority![Stat.Health.Ordinal]);
        Assert.Equal(2, stored.Priority![Stat.Initiative.Ordinal]);
        Assert.Equal(3, stored.Priority![Stat.RangedDefense.Ordinal]); // untouched → default P3
    }

    [Fact]
    public void SaveRole_NonNumericTarget_FallsBackToZero()
    {
        var vm = Vm();
        vm.AddRole();
        vm.StatRows[Stat.Fatigue.Ordinal].Target = "abc";
        Assert.True(vm.SaveRole());
        Assert.Equal(0, vm.Selected!.TargetStats![Stat.Fatigue.Ordinal]);
    }

    [Fact]
    public void Select_PopulatesFormState_AndClearsOnNull()
    {
        var vm = Vm();
        vm.AddRole();
        vm.Name = "Tank";
        vm.Frontline = false;
        vm.StatRows[Stat.Health.Ordinal].Target = "17";
        vm.StatRows[Stat.Health.Ordinal].Priority = 1;
        vm.SaveRole();

        vm.Select(null);
        Assert.False(vm.FormEnabled);
        Assert.Equal("", vm.Name);
        Assert.True(vm.Frontline); // Java setFormDisabled: frontline radio re-selected
        Assert.Equal("", vm.StatRows[Stat.Health.Ordinal].Target);
        Assert.Equal(3, vm.StatRows[Stat.Health.Ordinal].Priority);

        vm.Select(vm.Roles[0]);
        Assert.Equal("Tank", vm.Name);
        Assert.False(vm.Frontline);
        Assert.Equal("17", vm.StatRows[Stat.Health.Ordinal].Target);
        Assert.Equal(1, vm.StatRows[Stat.Health.Ordinal].Priority);
    }

    // ---- VM equivalents: ordering (Java moveRoleUp/moveRoleDown) --------------

    [Fact]
    public void MoveUpDown_ReordersAndKeepsSelection()
    {
        var vm = Vm();
        vm.AddRole();
        vm.AddRole();
        vm.AddRole();
        var third = vm.Roles[2];

        vm.MoveRoleDown(); // last — no-op (Java: idx >= size-1)
        Assert.Equal(2, vm.Roles.IndexOf(third));

        vm.MoveRoleUp();
        Assert.Equal(1, vm.Roles.IndexOf(third));
        Assert.Same(third, vm.Selected);

        vm.MoveRoleUp();
        Assert.Equal(0, vm.Roles.IndexOf(third));

        vm.MoveRoleUp(); // first — no-op (Java: idx <= 0)
        Assert.Equal(0, vm.Roles.IndexOf(third));
    }

    // ---- VM equivalents: delete (Java deleteRole + roleUsageCounter) ----------

    [Fact]
    public void DeleteConfirmation_NoSelection_Null()
    {
        Assert.Null(Vm().DeleteConfirmation());
    }

    [Fact]
    public void DeleteConfirmation_UsesUsageCounter()
    {
        var vm = Vm();
        vm.AddRole();
        var calls = new List<string?>();
        vm.RoleUsageCounter = id => { calls.Add(id); return 4; };

        var body = vm.DeleteConfirmation();

        Assert.NotNull(body);
        Assert.Contains("assigned to 4 brother(s)", body);
        Assert.Equal(vm.Selected!.Id, calls.Single());
    }

    [Fact]
    public void DeleteRole_RemovesClearsSelectionAndNotifies()
    {
        var vm = Vm();
        vm.AddRole();
        vm.AddRole();
        var first = vm.Roles[0];
        var changed = 0;
        vm.RolesChanged += () => changed++;

        vm.Select(first);
        vm.DeleteRole();

        Assert.Single(vm.Roles);
        Assert.DoesNotContain(first, vm.Roles);
        Assert.Null(vm.Selected);
        Assert.False(vm.FormEnabled);
        Assert.Equal(1, changed);
    }

    // ---- perk-plan template editing (Java setUiContext's pane callback + populateForm) --

    [Fact]
    public void CyclePerkStatus_CyclesTemplateWritesBackAndNotifies()
    {
        var vm = Vm();
        vm.AddRole();
        var changed = 0;
        vm.RolesChanged += () => changed++;
        var planEvents = 0;
        vm.PlanChanged += () => planEvents++;

        vm.CyclePerkStatus("0017"); // NOT → PLANNED
        Assert.Equal("PLANNED", vm.PlanStatus["0017"]);
        Assert.Equal("PLANNED", vm.Selected!.PerkPlanTemplate!["0017"]);

        vm.CyclePerkStatus("0017"); // PLANNED → OPTIONAL
        Assert.Equal("OPTIONAL", vm.Selected!.PerkPlanTemplate!["0017"]);

        vm.CyclePerkStatus("0017"); // OPTIONAL → NOT: empty template → null
        Assert.DoesNotContain("0017", vm.PlanStatus);
        Assert.Null(vm.Selected!.PerkPlanTemplate);

        Assert.Equal(3, changed);
        Assert.Equal(3, planEvents);
    }

    [Fact]
    public void Select_RoundTripsTemplate_AndClearsOnNull()
    {
        var vm = Vm();
        vm.AddRole();
        vm.CyclePerkStatus("0017");
        vm.CyclePerkStatus("0022");

        vm.Select(null);
        Assert.Empty(vm.PlanStatus); // Java setFormDisabled: pane.setStatus(null)

        vm.Select(vm.Roles[0]); // populateForm: pane.setStatus(r.perkPlanTemplate)
        Assert.Equal("PLANNED", vm.PlanStatus["0017"]);
        Assert.Equal("PLANNED", vm.PlanStatus["0022"]);
    }
}
