using BroLedger.App.ViewModels;
using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.Tests.ViewModels;

/// <summary>
/// Migration of JAVATEST/ui/RoleImportControllerTest.java. The three Java cases pin the
/// static helpers (packTitleText, importCheckboxLabel) and transfer 1:1. The remainder pin
/// the fetch→checklist→import choreography at VM level — including the brief's required
/// nails: the "(updates existing)" flag, the oversized-pack truncation notice, and the
/// Select All / Select None buttons (Java RoleImportController L100/L128/L150-156).
/// The fetch network hop itself is exercised through RoleShareService in Core.Tests.
/// </summary>
public class RoleImportViewModelTests : IDisposable
{
    private readonly string _dataDir;

    public RoleImportViewModelTests()
    {
        // Local role provenance (update-in-place matching) lives in roles.json — redirect.
        _dataDir = Path.Combine(Path.GetTempPath(), "broledger-role-import-tests-" + Guid.NewGuid().ToString("N"));
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

    private RoleImportViewModel Vm() => new(UiContext.Defaults());

    private static Role Role(string id, string name) => new() { Id = id, Name = name };

    private static RolePack Pack(string? label, params Role[] roles) => new(label, [.. roles]);

    // ---- migrated: packTitleText (RoleImportControllerTest) --------------------

    [Fact]
    public void PackTitleText_PrefersLabel()
    {
        Assert.Equal("My Pack", RoleImportViewModel.PackTitleText(Pack("My Pack"), "AB3D9F2K"));
    }

    [Fact]
    public void PackTitleText_FallsBackToCodeWhenUntitled()
    {
        Assert.Equal("Untitled pack (AB3D9F2K)",
            RoleImportViewModel.PackTitleText(Pack(null), "AB3D9F2K"));
        Assert.Equal("Untitled pack (AB3D9F2K)",
            RoleImportViewModel.PackTitleText(Pack(""), "AB3D9F2K"));
    }

    // ---- migrated: importCheckboxLabel ------------------------------------------

    [Fact]
    public void ImportCheckboxLabel_FlagsUpdateInPlaceRoles()
    {
        Assert.Equal("Tank", RoleImportViewModel.ImportCheckboxLabel("Tank", false));
        Assert.Equal("Tank  (updates existing)", RoleImportViewModel.ImportCheckboxLabel("Tank", true));
    }

    // ---- VM equivalents: fetch success population (Java task.setOnSucceeded) ----

    [Fact]
    public void CompleteFetch_LabelsUpdateInPlaceRoles()
    {
        // A previous import from this code: importing it again updates in place.
        var previous = Pack(null, Role("srv-1", "Tank"));
        RoleService.Instance.ImportPack(previous, previous.Roles, "AB3D9F2K");

        var vm = Vm();
        vm.CompleteFetch(Pack("My Pack", Role("srv-1", "Tank"), Role("srv-2", "Healer")), "AB3D9F2K");

        Assert.Equal("Tank  (updates existing)", vm.Items[0].Label);
        Assert.Equal("Healer", vm.Items[1].Label);
        Assert.True(vm.Items[0].IsChecked); // Java populateRoleList: default checked
        Assert.True(vm.Items[1].IsChecked);
        Assert.Equal("My Pack", vm.PackTitle);
        Assert.Equal("Found 2 role(s).", vm.StatusText);
        Assert.False(vm.StatusWarning);
        Assert.True(vm.FetchEnabled);
    }

    [Fact]
    public void CompleteFetch_TruncationFlagged()
    {
        var vm = Vm();
        var pack = Pack(null, Role("srv-1", "Tank"));
        pack.MarkTruncated();

        vm.CompleteFetch(pack, "AB3D9F2K");

        Assert.Equal("Found 1 role(s) (oversized pack — extra roles were dropped).",
            vm.StatusText);
    }

    [Fact]
    public void CompleteFetch_Untitled_ShowsCodeFallback()
    {
        var vm = Vm();
        vm.CompleteFetch(Pack(null, Role("srv-1", "Tank")), "AB3D9F2K");
        Assert.Equal("Untitled pack (AB3D9F2K)", vm.PackTitle);
    }

    // ---- VM equivalents: fetch validation (Java fetch's early return) -----------

    [Fact]
    public void TryBeginFetch_InvalidCode_Warns()
    {
        foreach (var raw in new[] { null, "", "nope", "AB3D9F2" })
        {
            var vm = Vm();
            vm.Code = raw!;
            Assert.Null(vm.TryBeginFetch());
            Assert.Equal("Enter a valid share code — 8 letters/digits, e.g. AB3D9F2K.",
                vm.StatusText);
            Assert.True(vm.StatusWarning);
            Assert.True(vm.FetchEnabled);
        }
    }

    [Fact]
    public void TryBeginFetch_ValidCode_NormalizesAndClearsState()
    {
        var vm = Vm();
        vm.CompleteFetch(Pack("Old", Role("srv-1", "Tank")), "Z2345678");

        vm.Code = "  ab3d9f2k ";
        var code = vm.TryBeginFetch();

        Assert.Equal("AB3D9F2K", code);
        Assert.Empty(vm.Items);
        Assert.Null(vm.PackTitle);
        Assert.Equal("Fetching…", vm.StatusText);
        Assert.False(vm.FetchEnabled);
        Assert.False(vm.ImportEnabled);
    }

    // ---- VM equivalents: checklist gating + import (Java selectAll/None/doImport)

    [Fact]
    public void SelectNone_DisablesImport_SelectAll_Reenables()
    {
        var vm = Vm();
        vm.CompleteFetch(Pack("P", Role("srv-1", "Tank"), Role("srv-2", "Healer")), "AB3D9F2K");
        Assert.True(vm.ImportEnabled); // default all-checked

        vm.SelectNoneRoles();
        Assert.False(vm.ImportEnabled);

        vm.SelectAllRoles();
        Assert.True(vm.ImportEnabled);
    }

    [Fact]
    public void DoImport_ImportsOnlyCheckedRoles_AndFiresImported()
    {
        var vm = Vm();
        vm.CompleteFetch(Pack("P", Role("srv-1", "Tank"), Role("srv-2", "Healer")), "AB3D9F2K");
        vm.Items[1].IsChecked = false;
        var imported = 0;
        vm.Imported += () => imported++;

        vm.DoImport();

        Assert.Equal(1, imported);
        var stored = RoleService.Instance.GetAll();
        var role = Assert.Single(stored);
        Assert.Equal("Tank", role.Name);
        Assert.Equal("AB3D9F2K", role.SourcePackCode);
        Assert.Equal("srv-1", role.SourceRoleId);
        Assert.NotEqual("srv-1", role.Id); // fresh UUID — the sender's id is never reused
    }

    [Fact]
    public void DoImport_UpdatesExistingRoleInPlace()
    {
        var v1 = Pack(null, Role("srv-1", "Tank"));
        RoleService.Instance.ImportPack(v1, v1.Roles, "AB3D9F2K");
        var localId = RoleService.Instance.GetAll()[0].Id;

        var vm = Vm();
        vm.CompleteFetch(Pack(null, Role("srv-1", "Tank v2")), "AB3D9F2K");
        Assert.EndsWith("(updates existing)", vm.Items[0].Label);

        vm.DoImport();

        var updated = RoleService.Instance.GetById(localId);
        Assert.NotNull(updated);
        Assert.Equal("Tank v2", updated!.Name);
        Assert.Single(RoleService.Instance.GetAll()); // in place — no duplicate created
    }

    [Fact]
    public void FetchFailed_ShowsWarningAndReenablesFetch()
    {
        var vm = Vm();
        vm.FetchFailed(new IOException("HTTP 500"));

        Assert.Equal("Fetch failed: HTTP 500", vm.StatusText);
        Assert.True(vm.StatusWarning);
        Assert.True(vm.FetchEnabled);
    }
}
