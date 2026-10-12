using BroLedger.App.ViewModels;
using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.Tests.ViewModels;

/// <summary>
/// Migration of JAVATEST/ui/RoleShareControllerTest.java. The six Java cases pin the two
/// static helpers (normalizedTitle, recentCellText) and transfer 1:1. The remainder pin
/// the controller's update-in-place selection choreography and the publish success path
/// at VM level; Java's network thread itself was untested there and stays untested here
/// (RoleShareService's HTTP seam is covered by Core.Tests against the local stub server).
/// </summary>
[Collection("EnvVar-BroLedgerDataDir")]
public class RoleShareViewModelTests : IDisposable
{
    private readonly string _dataDir;

    public RoleShareViewModelTests()
    {
        // RecentShares history is persisted via AppConfig — keep it out of ~/.bro-ledger.
        _dataDir = Path.Combine(Path.GetTempPath(), "broledger-role-share-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", _dataDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", null);
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    private static RoleShareViewModel Vm() => new(UiContext.Defaults());

    private static Role Role(string name) => new() { Id = Guid.NewGuid().ToString(), Name = name };

    // ---- migrated: normalizedTitle (RoleShareControllerTest) -------------------

    [Fact]
    public void NormalizedTitle_BlankBecomesNull()
    {
        Assert.Null(RoleShareViewModel.NormalizedTitle(null));
        Assert.Null(RoleShareViewModel.NormalizedTitle(""));
        Assert.Null(RoleShareViewModel.NormalizedTitle("   "));
    }

    [Fact]
    public void NormalizedTitle_TrimsAndKeepsShortTitles()
    {
        Assert.Equal("My Pack", RoleShareViewModel.NormalizedTitle("  My Pack  "));
    }

    [Fact]
    public void NormalizedTitle_CapsLength()
    {
        var longTitle = new string('x', RoleShareViewModel.MaxTitleLength + 20);
        var result = RoleShareViewModel.NormalizedTitle(longTitle);
        Assert.Equal(RoleShareViewModel.MaxTitleLength, result!.Length);
    }

    // ---- migrated: recentCellText ----------------------------------------------

    [Fact]
    public void RecentCellText_ShowsTitleCodeCountAndDate()
    {
        var r = new ShareRecord("AB3D9F2K", "tok", "My Pack", "2026-07-05T12:00:00Z", 3);
        Assert.Equal("My Pack — AB3D9F2K · 3 role(s) · 2026-07-05",
            RoleShareViewModel.RecentCellText(r));
    }

    [Fact]
    public void RecentCellText_UntitledAndMissingDate()
    {
        var r = new ShareRecord("AB3D9F2K", "tok", null, null, 1);
        Assert.Equal("Untitled — AB3D9F2K · 1 role(s)", RoleShareViewModel.RecentCellText(r));
    }

    [Fact]
    public void RecentCellText_FlagsTokenlessEntries()
    {
        var r = new ShareRecord("AB3D9F2K", null, "Old", "2026-07-05T12:00:00Z", 2);
        Assert.EndsWith("(not updatable)", RoleShareViewModel.RecentCellText(r));
    }

    // ---- VM equivalents: checklist (Java setRoles/selectAll/selectNone/share) --

    [Fact]
    public void SetRoles_AllCheckedByDefault()
    {
        var vm = Vm();
        vm.SetRoles([Role("Tank"), Role("Healer")]);
        Assert.Equal(2, vm.CheckedRoles().Count);
    }

    [Fact]
    public void SelectNone_ThenSelectAll_TogglesCheckedSet()
    {
        var vm = Vm();
        vm.SetRoles([Role("Tank"), Role("Healer")]);

        vm.SelectNoneRoles();
        Assert.Empty(vm.CheckedRoles());

        vm.SelectAllRoles();
        Assert.Equal(2, vm.CheckedRoles().Count);
    }

    [Fact]
    public void Share_WithoutSelection_Warns()
    {
        var vm = Vm();
        vm.SetRoles([Role("Tank")]);
        vm.SelectNoneRoles();

        Assert.False(vm.Share());
        Assert.Equal("Select at least one role to share.", vm.StatusText);
        Assert.True(vm.StatusWarning);
    }

    // ---- VM equivalents: recent-uploads selection (Java onRecentSelectionChanged)

    [Fact]
    public void RecentSelection_Updatable_TargetsUpdateInPlace()
    {
        var cfg = AppConfig.Instance;
        cfg.RecordShare(new ShareRecord("AB3D9F2K", "tok", "My Pack", "2026-07-05T12:00:00Z", 2));
        cfg.Save();
        var vm = Vm();

        Assert.True(vm.RecentVisible);
        Assert.DoesNotContain("(not updatable)", vm.Recent[0].Text);
        vm.SelectedRecent = vm.Recent[0];

        Assert.Equal("Update AB3D9F2K", vm.ShareButtonLabel);
        Assert.True(vm.NewCodeVisible);
        Assert.Equal("My Pack", vm.PackName);
        Assert.Equal("Publishing will overwrite AB3D9F2K for everyone who has the code.",
            vm.StatusText);
    }

    [Fact]
    public void RecentSelection_Tokenless_CannotUpdate()
    {
        AppConfig.Instance.RecordShare(new ShareRecord("AB3D9F2K", null, "Old", "2026-07-05T12:00:00Z", 2));
        var vm = Vm();
        vm.SelectedRecent = vm.Recent[0];

        Assert.Equal("Share", vm.ShareButtonLabel);
        Assert.False(vm.NewCodeVisible);
        Assert.Equal("This upload can't be updated — Share publishes a new code.", vm.StatusText);
    }

    [Fact]
    public void NewCode_ClearsUpdateTarget()
    {
        AppConfig.Instance.RecordShare(new ShareRecord("AB3D9F2K", "tok", "My Pack", "2026-07-05T12:00:00Z", 2));
        var vm = Vm();
        vm.SelectedRecent = vm.Recent[0];

        vm.NewCode();

        Assert.Null(vm.SelectedRecent);
        Assert.Equal("Share", vm.ShareButtonLabel);
        Assert.False(vm.NewCodeVisible);
    }

    // ---- VM equivalents: publish success (Java task.setOnSucceeded + recordShare)

    [Fact]
    public void ShareSucceeded_RecordsHistoryAndShowsCode()
    {
        var vm = Vm();
        vm.SetRoles([Role("Tank")]);

        vm.ShareSucceeded(new RoleShareService.PublishResult("AB3D9F2K", "tok"), "My Pack", 1, updateMode: false);

        Assert.Equal("AB3D9F2K", vm.Code);
        Assert.Equal("Published — code copied to clipboard.", vm.StatusText);
        Assert.True(vm.ShareEnabled);
        var record = Assert.Single(AppConfig.Instance.RecentShares!);
        Assert.Equal("AB3D9F2K", record.Code);
        Assert.Equal("tok", record.OwnerToken);
        Assert.Equal("My Pack", record.Title);
        Assert.Equal(1, record.RoleCount);
    }

    [Fact]
    public void ShareSucceeded_UpdateMode_ReportsOverwrite()
    {
        var vm = Vm();
        vm.ShareSucceeded(new RoleShareService.PublishResult("AB3D9F2K", "tok"), "My Pack", 1, updateMode: true);
        Assert.Equal("Updated — everyone with this code gets the new version. Code copied.",
            vm.StatusText);
    }
}
