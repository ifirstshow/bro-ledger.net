using Avalonia.Headless.XUnit;
using BroLedger.App.ViewModels;
using BroLedger.Core.Models;
using BroLedger.Core.Services;
using BroLedger.Core.Utils;

namespace BroLedger.App.Tests.ViewModels;

/// <summary>
/// Orchestration-layer nails for the overview pane (Java ui/BrotherOverviewPane +
/// MainController.buildToolbar toggle segment). Java's side has no UI tests for these, so the
/// pure decision points are pinned here: column-order persistence format, group-boundary
/// validation, the four-toggle cycle (LedDef tables), UiState persistence and the roster
/// sort comparator. Cell rendering itself is exercised by the manual checklist.
/// </summary>
public class OverviewViewModelTests : IDisposable
{
    private readonly string _dataDir;

    public OverviewViewModelTests()
    {
        // Redirect AppPaths.DataDir() so AppConfig.Save / AnnotationService flushes never
        // touch the real ~/.bro-ledger during tests.
        _dataDir = Path.Combine(Path.GetTempPath(), "broledger-app-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", _dataDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BROLEDGER_DATADIR", null);
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    // ---- column order (AppConfig.OverviewColumnOrder, "stat-<abbrev>" IDs) ----

    [Fact]
    public void DefaultColumnIds_FixedThenEightStatsThenTenEquipsThenTraitsPerks()
    {
        var ids = OverviewViewModel.DefaultColumnIds();

        Assert.Equal(
        [
            "portrait", "name", "role", "level",
            "stat-HP", "stat-Res", "stat-Fat", "stat-Init", "stat-MS", "stat-RS", "stat-MD", "stat-RD",
            "equip-Arm", "equip-Hlm", "equip-Wpn", "equip-Shd", "equip-Trk", "equip-Quv",
            "equip-P1", "equip-P2", "equip-P3", "equip-P4",
            "traits", "perks",
        ], ids);
    }

    [Fact]
    public void RestoreColumnOrder_SavedOrderWins_MissingColumnsAppended()
    {
        var defaults = OverviewViewModel.DefaultColumnIds();
        // Move stat-MS before stat-Fat, and drop traits from the saved list entirely.
        List<string> saved =
        [
            "portrait", "name", "role", "level",
            "stat-HP", "stat-Res", "stat-MS", "stat-Fat", "stat-Init", "stat-RS", "stat-MD", "stat-RD",
            "equip-Arm", "equip-Hlm", "equip-Wpn", "equip-Shd", "equip-Trk", "equip-Quv",
            "equip-P1", "equip-P2", "equip-P3", "equip-P4",
            "perks",
        ];

        var restored = OverviewViewModel.RestoreColumnOrder(saved, defaults);

        Assert.Equal("stat-MS", restored[6]);
        Assert.Equal("stat-Fat", restored[7]);
        // Java restore: columns absent from the saved order go at the end.
        Assert.Equal("traits", restored[^1]);
        Assert.Equal(defaults.Count, restored.Count);
    }

    [Fact]
    public void RestoreColumnOrder_NullOrEmpty_ReturnsDefaults()
    {
        var defaults = OverviewViewModel.DefaultColumnIds();
        Assert.Equal(defaults, OverviewViewModel.RestoreColumnOrder(null, defaults));
        Assert.Equal(defaults, OverviewViewModel.RestoreColumnOrder([], defaults));
    }

    [Fact]
    public void IsColumnOrderValid_WithinGroupShuffle_IsValid()
    {
        List<string> reordered =
        [
            "portrait", "name", "role", "level",
            "stat-RD", "stat-HP", "stat-Res", "stat-Fat", "stat-Init", "stat-MS", "stat-RS", "stat-MD",
            "equip-Wpn", "equip-Arm", "equip-Hlm", "equip-Shd", "equip-Trk", "equip-Quv",
            "equip-P1", "equip-P2", "equip-P3", "equip-P4",
            "traits", "perks",
        ];
        Assert.True(OverviewViewModel.IsColumnOrderValid(reordered));
    }

    [Fact]
    public void IsColumnOrderValid_CrossGroupMove_IsRejected()
    {
        var defaults = OverviewViewModel.DefaultColumnIds().ToList();
        // Drag stat-HP into the equipment zone (index 12).
        defaults.Remove("stat-HP");
        defaults.Insert(12, "stat-HP");
        Assert.False(OverviewViewModel.IsColumnOrderValid(defaults));

        // Drag equip-Wpn into the fixed zone (index 1).
        var bad2 = OverviewViewModel.DefaultColumnIds().ToList();
        bad2.Remove("equip-Wpn");
        bad2.Insert(1, "equip-Wpn");
        Assert.False(OverviewViewModel.IsColumnOrderValid(bad2));

        // Drag a stat column before the level column (out of the stat range).
        var bad3 = OverviewViewModel.DefaultColumnIds().ToList();
        bad3.Remove("stat-MS");
        bad3.Insert(2, "stat-MS");
        Assert.False(OverviewViewModel.IsColumnOrderValid(bad3));
    }

    [Fact]
    public void IsColumnOrderValid_DuplicateOrShortList_IsRejected()
    {
        Assert.False(OverviewViewModel.IsColumnOrderValid(["portrait", "name"]));
        Assert.False(OverviewViewModel.IsColumnOrderValid(
            OverviewViewModel.DefaultColumnIds().Concat(["portrait"]).ToList()));
    }

    [Fact]
    public void TryCommitColumnOrder_ValidOrder_PersistsToConfigAndRaises()
    {
        var config = AppConfig.Instance;
        var savedOrder = config.OverviewColumnOrder;
        try
        {
            var vm = new OverviewViewModel();
            List<string>? raised = null;
            vm.ColumnOrderChanged += () => raised = [.. config.OverviewColumnOrder!];

            var order = OverviewViewModel.DefaultColumnIds().ToList();
            order.Remove("stat-RD");
            order.Insert(5, "stat-RD"); // within the stat zone

            Assert.True(vm.TryCommitColumnOrder(order));
            Assert.Equal(order, config.OverviewColumnOrder);
            Assert.NotNull(raised);
            Assert.Equal(order, raised);
        }
        finally
        {
            config.OverviewColumnOrder = savedOrder;
        }
    }

    [Fact]
    public void TryCommitColumnOrder_InvalidOrder_Rejected()
    {
        var config = AppConfig.Instance;
        var savedOrder = config.OverviewColumnOrder;
        try
        {
            var vm = new OverviewViewModel();
            var raised = false;
            vm.ColumnOrderChanged += () => raised = true;

            var order = OverviewViewModel.DefaultColumnIds().ToList();
            order.Remove("traits");
            order.Insert(5, "traits"); // fixed column into the stat zone

            Assert.False(vm.TryCommitColumnOrder(order));
            Assert.False(raised);
            // Rejection leaves the persisted order untouched (compared against what this test
            // saw at its start — sibling test collections may legitimately hold a commit).
            Assert.Equal(savedOrder, config.OverviewColumnOrder);
        }
        finally
        {
            config.OverviewColumnOrder = savedOrder;
        }
    }

    // ---- four toggles (MainController.buildToolbar LedDef tables) ----

    [Fact]
    public void Toggles_MatchJavaLedDefTables()
    {
        var toggles = OverviewViewModel.Toggles;

        Assert.Equal(4, toggles.Count);
        Assert.Equal("Weapon Tier", toggles[0].Title);
        Assert.Equal("Armor Stats", toggles[1].Title);
        Assert.Equal("Perk Sorting", toggles[2].Title);
        Assert.Equal("Potential", toggles[3].Title);

        // Weapon Tier: Off(red) / On(green)
        Assert.Equal(
            [("Off", "led-red"), ("On", "led-green")],
            toggles[0].Options.Select(o => (o.Desc, o.LedClass)).ToArray());
        // Armor Stats: Off(red) / Durability(yellow) / Fatigue(yellow)
        Assert.Equal(
            [("Off", "led-red"), ("Durability", "led-yellow"), ("Fatigue", "led-yellow")],
            toggles[1].Options.Select(o => (o.Desc, o.LedClass)).ToArray());
        // Perk Sorting: Off / Commonality / Tier / "Tier + Cmn" (all red then yellow)
        Assert.Equal(
            [("Off", "led-red"), ("Commonality", "led-yellow"), ("Tier", "led-yellow"), ("Tier + Cmn", "led-yellow")],
            toggles[2].Options.Select(o => (o.Desc, o.LedClass)).ToArray());
        // Potential: 5 states, red / yellow×3 / green×2
        Assert.Equal(
            [("Off", "led-red"), ("Current + Pot.", "led-yellow"), ("Pot. Only", "led-yellow"),
             ("Current + Exp.", "led-green"), ("Exp. Only", "led-green")],
            toggles[3].Options.Select(o => (o.Desc, o.LedClass)).ToArray());
    }

    [Fact]
    public void CycleNext_AdvancesAndWraps_EachToggle()
    {
        var vm = new OverviewViewModel();

        // Weapon Tier: OFF → ON → OFF
        Assert.Equal(OverviewViewModel.WeaponTierMode.OFF, vm.WeaponTier);
        vm.CycleNext(0);
        Assert.Equal(OverviewViewModel.WeaponTierMode.ON, vm.WeaponTier);
        vm.CycleNext(0);
        Assert.Equal(OverviewViewModel.WeaponTierMode.OFF, vm.WeaponTier);

        // Armor Stats: OFF → DURABILITY → FATIGUE → OFF
        vm.CycleNext(1);
        Assert.Equal(OverviewViewModel.ArmorMode.DURABILITY, vm.Armor);
        vm.CycleNext(1);
        Assert.Equal(OverviewViewModel.ArmorMode.FATIGUE, vm.Armor);
        vm.CycleNext(1);
        Assert.Equal(OverviewViewModel.ArmorMode.OFF, vm.Armor);

        // Perk Sorting: Off → Commonality → Tier → TierThenCommon → Off
        vm.CycleNext(2);
        vm.CycleNext(2);
        vm.CycleNext(2);
        Assert.Equal("TierThenCommon", vm.PerkSort.ToString());
        vm.CycleNext(2);
        Assert.Equal("Off", vm.PerkSort.ToString());

        // Potential: CURRENT → BOTH → POTENTIAL → CURRENT_EXPECTED → EXPECTED → CURRENT
        vm.CycleNext(3);
        vm.CycleNext(3);
        vm.CycleNext(3);
        vm.CycleNext(3);
        vm.CycleNext(3);
        Assert.Equal(OverviewViewModel.PotentialMode.CURRENT, vm.Potential);
    }

    [Fact]
    public void CycleNext_PersistsUiState_AndRestoreUiStateRoundTrips()
    {
        var savePath = Path.Combine(_dataDir, "ui-state-roundtrip.sav");
        var vm = new OverviewViewModel();
        var ann = AnnotationService.Instance;
        ann.LoadFor(savePath);

        vm.CycleNext(0); // Weapon Tier: OFF → ON
        vm.CycleNext(1); // Armor: OFF → DURABILITY
        vm.CycleNext(3); // Potential: CURRENT → BOTH
        vm.CycleNext(2); // Perk sort: Off → Commonality

        // Reload from the annotation file the flushes wrote: state survives.
        ann.LoadFor(savePath);
        var stored = ann.GetUiState();
        Assert.Equal("ON", stored.WeaponTierMode);
        Assert.Equal("DURABILITY", stored.ArmorMode);
        Assert.Equal("BOTH", stored.PotentialMode);
        // Core's PerkSortMode is serialized with the Java enum names for file compatibility.
        Assert.Equal("COMMONALITY", stored.PerkSortMode);

        var vm2 = new OverviewViewModel();
        vm2.RestoreUiState(ann);
        Assert.Equal(OverviewViewModel.WeaponTierMode.ON, vm2.WeaponTier);
        Assert.Equal(OverviewViewModel.ArmorMode.DURABILITY, vm2.Armor);
        Assert.Equal(OverviewViewModel.PotentialMode.BOTH, vm2.Potential);
        Assert.Equal("Commonality", vm2.PerkSort.ToString());
    }

    [Fact]
    public void RestoreUiState_InvalidOrMissingNames_AreIgnored()
    {
        var savePath = Path.Combine(_dataDir, "ui-state-invalid.sav");
        var ann = AnnotationService.Instance;
        ann.LoadFor(savePath);
        ann.SetUiState(new AnnotationService.UiState
        {
            PotentialMode = "NOT_A_MODE",
            WeaponTierMode = "ON",
            ArmorMode = null,
            PerkSortMode = "TIER",
        });

        var vm = new OverviewViewModel();
        vm.RestoreUiState(ann);

        // Invalid enum names fall back to the defaults (Java applyEnum catches IAE).
        Assert.Equal(OverviewViewModel.PotentialMode.CURRENT, vm.Potential);
        Assert.Equal(OverviewViewModel.ArmorMode.OFF, vm.Armor);
        Assert.Equal(OverviewViewModel.WeaponTierMode.ON, vm.WeaponTier);
        Assert.Equal("Tier", vm.PerkSort.ToString());
    }

    // ---- roster sort (BrotherOverviewPane.sortComparator) ----

    private static Brother MakeBrother(string name, string? fingerprint) =>
        new() { Name = name, Fingerprint = fingerprint };

    [Fact]
    public void SortComparator_FrontlineFirst_ThenSortIndex_ThenUnsorted()
    {
        static Role? RoleOf(Brother b) =>
            b.Fingerprint switch
            {
                "b1" or "b2" or "b3" => new Role { Id = "back", Frontline = false },
                _ => new Role { Id = "front", Frontline = true },
            };
        static int? SortOf(Brother b) =>
            b.Fingerprint switch
            {
                "b3" => 0, "b1" => 5, "b2" => 1, "f5" => 5,
                _ => null,
            };

        var comparer = OverviewViewModel.SortComparator(RoleOf, SortOf);
        var noRole = MakeBrother("zog", null);   // null fingerprint → frontline group, unsorted
        var back1 = MakeBrother("b3", "b3");     // backline, sortIndex 0
        var back2 = MakeBrother("b1", "b1");     // backline, sortIndex 5
        var back3 = MakeBrother("b2", "b2");     // backline, sortIndex 1
        var front1 = MakeBrother("f5", "f5");    // frontline, sortIndex 5
        var front2 = MakeBrother("f9", "f9");    // frontline, unsorted (MAX_VALUE)

        var sorted = new[] { front2, noRole, back2, front1, back3, back1 }.OrderBy(b => b, comparer).ToList();

        // Stable ties: the two unsorted frontline rows keep input order (front2 then noRole).
        Assert.Equal([front1, front2, noRole, back1, back3, back2], sorted);
    }
}
