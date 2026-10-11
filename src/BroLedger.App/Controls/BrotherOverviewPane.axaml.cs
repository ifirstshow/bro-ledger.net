using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using BroLedger.App.ViewModels;
using System.ComponentModel;
using BroLedger.Core;
using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.Controls;

/// <summary>
/// Port of Java ui/BrotherOverviewPane.java (TableView roster) with the row-cell rendering
/// from ui/BrotherListCell.java folded in.
///
/// Selection — custom header + body layout over a DataGrid: the Java pane leans on every
/// TableView extension point (fixed cell size, flex-last resize policy, group-constrained
/// column reorder with snap-back, per-cell overlays, row drag). Avalonia's DataGrid fights
/// each of those (per-stat template parameterization, DisplayIndex/group juggling, Fluent
/// row restyling, nested-scroll mounting inside MainWindow's center ScrollViewer), while
/// this pane needs two ScrollViewers and one offset sync to guarantee header/cell column
/// alignment — the same guarantee Java's class comment cites for choosing TableView.
/// Rosters are tens of rows, so virtualization is unnecessary.
///
/// Java refresh() maps to RebuildRows(); the four toggle modes and the level-cell popup
/// (guidePopup — T5) state live in OverviewViewModel.
/// </summary>
internal partial class BrotherOverviewPane : UserControl
{
    // Java column widths (fixedBrotherCol pref=min=max).
    private const double WPortrait = 59;
    private const double WName = 165;
    private const double WRole = 140;
    private const double WLevel = 39;
    private const double WStat = 48;
    private const double WEquip = 42;
    private const double WTrait = 150;
    private const double WPerkMin = 112;
    private const double WPerkPref = 280;

    // Java EQUIP_SLOT_IDX / EQUIP_SLOT_NAME / EQUIP_HDR.
    private static readonly int[] EquipSlotIdx = [2, 3, 0, 1, 4, 5];
    private static readonly string[] EquipSlotName = ["body", "helmet", "weapon", "shield", "trinket", "quiver"];
    private static readonly string[] EquipHdr = ["Arm", "Hlm", "Wpn", "Shd", "Trk", "Quv", "P1", "P2", "P3", "P4"];

    // Java TRAIT_NAME_BLOCKLIST.
    private static readonly HashSet<string> TraitNameBlocklist = ["Unknown", "Morale"];

    private enum ColumnKind { Portrait, Name, Role, Level, Stat, Equip, Traits, Perks }

    private enum EquipKind { Plain, Durability, Tier }

    private sealed record ColumnSpec(string Id, string Header, double Width, ColumnKind Kind, object? Tag)
    {
        public double ActualWidth => Kind == ColumnKind.Perks ? WPerkPref : Width;
    }

    private sealed record SlotAndName(InventorySlot? Slot, string SlotName);

    private sealed class Row(Border container, Brother brother)
    {
        public Border Container = container;
        public Brother Brother = brother;
    }

    private readonly UiContext _ctx;
    private readonly OverviewViewModel _vm;
    private readonly List<Row> _rows = [];
    private List<ColumnSpec> _columns = [];
    private Dictionary<string, long> _perkCounts = [];

    // Column drag state (header cells).
    private Border? _colDragIndicator;
    private ColumnSpec? _colDragSpec;

    public BrotherOverviewPane(UiContext ctx, OverviewViewModel vm, IEnumerable<Brother> brothers)
    {
        InitializeComponent(); // Avalonia 12 generated init already loads the XAML.
        _ctx = ctx;
        _vm = vm;

        _perkCounts = brothers
            .SelectMany(b => b.PerkIds)
            .GroupBy(id => id.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => (long)g.Count());

        // Java doBuild: sort once via sortComparator.
        var sorted = brothers.OrderBy(b => b, _vm.SortComparatorFor(_ctx)).ToList();
        _rows.AddRange(sorted.Select(b => new Row(BuildRow(b), b)));

        ApplyColumnOrder(OverviewViewModel.RestoreColumnOrder(
            AppConfig.Instance.OverviewColumnOrder, OverviewViewModel.DefaultColumnIds()));

        // Java static listener: perkSortMode (and the other three modes) → table refresh.
        _vm.PropertyChanged += OnVmPropertyChanged;

        // A valid reorder anywhere (header drag) re-applies the persisted order.
        _vm.ColumnOrderChanged += ApplyPersistedColumnOrder;
        // Panes are recreated per navigation — drop the VM subscriptions when detached.
        DetachedFromVisualTree += (_, _) =>
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.ColumnOrderChanged -= ApplyPersistedColumnOrder;
        };

        BodyScroll.ScrollChanged += (_, _) => HeaderScroll.Offset = BodyScroll.Offset;
        // Java flex-last policy: the perks column absorbs the remaining viewport width.
        SizeChanged += (_, _) => UpdatePerksWidth();
        DragDrop.AddDragOverHandler(this, OnRowDragOver);
        DragDrop.AddDragLeaveHandler(this, OnRowDragLeave);
        DragDrop.AddDropHandler(this, OnRowDrop);
    }

    // ---- column construction ----------------------------------------------

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverviewViewModel.Potential) or nameof(OverviewViewModel.Armor)
            or nameof(OverviewViewModel.WeaponTier) or nameof(OverviewViewModel.PerkSort))
        {
            RebuildRows();
        }
    }

    private static List<ColumnSpec> BuildColumnSpecs(IReadOnlyList<string> ids)
    {
        var specs = new List<ColumnSpec>(ids.Count);
        foreach (var id in ids)
        {
            specs.Add(id switch
            {
                "portrait" => new ColumnSpec(id, "", WPortrait, ColumnKind.Portrait, null),
                "name" => new ColumnSpec(id, "Name", WName, ColumnKind.Name, null),
                "role" => new ColumnSpec(id, "Role", WRole, ColumnKind.Role, null),
                "level" => new ColumnSpec(id, "Lvl", WLevel, ColumnKind.Level, null),
                "traits" => new ColumnSpec(id, "Traits", WTrait, ColumnKind.Traits, null),
                "perks" => new ColumnSpec(id, "Perks", WPerkPref, ColumnKind.Perks, null),
                _ when id.StartsWith("stat-") => StatSpec(id),
                _ when id.StartsWith("equip-") => EquipSpec(id),
                _ => new ColumnSpec(id, id, WStat, ColumnKind.Stat, null),
            });
        }
        return specs;

        static ColumnSpec StatSpec(string id)
        {
            var abbrev = id["stat-".Length..];
            var stat = Stat.Values.FirstOrDefault(s => s.Abbrev == abbrev) ?? Stat.Values[0];
            return new ColumnSpec(id, abbrev, WStat, ColumnKind.Stat, stat);
        }

        static ColumnSpec EquipSpec(string id)
        {
            var suffix = id["equip-".Length..];
            var p = Array.IndexOf(EquipHdr, suffix);
            if (p is >= 0 and < 6)
            {
                // Java equipCol(i): durability overlay on Arm/Hlm, tier dot on the weapon.
                var kind = p is 0 or 1 ? EquipKind.Durability
                    : EquipSlotName[p] == "weapon" ? EquipKind.Tier : EquipKind.Plain;
                return new ColumnSpec(id, suffix, WEquip, ColumnKind.Equip,
                    new Func<Brother, SlotAndName>(b => SlotAt(b, EquipSlotIdx[p], EquipSlotName[p])));
            }
            // Java pouchCol(p): tier dot overlay.
            return new ColumnSpec(id, suffix, WEquip, ColumnKind.Equip,
                new Func<Brother, SlotAndName>(b =>
                {
                    var pouches = OverviewCalc.GatherPouches(b.EquippedSlots, b.ExtraPouches);
                    var idx = int.Parse(suffix[1..]) - 1;
                    return new SlotAndName(idx < pouches.Count ? pouches[idx] : null, "pouch");
                }));
        }

        static SlotAndName SlotAt(Brother b, int arrayIdx, string slotName) =>
            new(b.EquippedSlots.Length > arrayIdx ? b.EquippedSlots[arrayIdx] : null, slotName);
    }

    /// <summary>Java doBuild's saved-order application + full header/body rebuild.</summary>
    private void ApplyColumnOrder(IReadOnlyList<string> ids)
    {
        _columns = BuildColumnSpecs(ids);
        BuildHeader();
        RebuildRows();
        UpdatePerksWidth();
    }

    private void ApplyPersistedColumnOrder() => ApplyColumnOrder(OverviewViewModel.RestoreColumnOrder(
        AppConfig.Instance.OverviewColumnOrder, OverviewViewModel.DefaultColumnIds()));

    private void BuildHeader()
    {
        HeaderHost.Children.Clear();
        foreach (var col in _columns)
        {
            var cell = new Border
            {
                Classes = { "overview-header-cell" },
                Width = col.ActualWidth,
                Tag = col,
                Child = new TextBlock
                {
                    Classes = { "overview-header-label" },
                    Text = col.Header,
                },
            };
            if (col.Kind is ColumnKind.Stat or ColumnKind.Equip) AttachColumnDrag(cell);
            HeaderHost.Children.Add(cell);
        }
    }

    // ---- rows ---------------------------------------------------------------

    private void RebuildRows()
    {
        RowsHost.Children.Clear();
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            RebuildRowContent(row);
            row.Container.Classes.Set("odd", i % 2 == 1);
            // Java updateItem: thick divider under the last row of the frontline group.
            row.Container.Classes.Remove("frontline-last");
            if (i + 1 < _rows.Count && FrontlineKey(row.Brother) != FrontlineKey(_rows[i + 1].Brother))
                row.Container.Classes.Add("frontline-last");
            RowsHost.Children.Add(row.Container);
        }
        UpdatePerksWidth();
    }

    private int FrontlineKey(Brother b)
    {
        if (b.Fingerprint == null) return 0;
        var roleId = _ctx.Annotation.Get(b.Fingerprint).RoleId;
        return OverviewCalc.FrontlineKey(roleId != null ? _ctx.Roles.GetById(roleId) : null);
    }

    private Border BuildRow(Brother b)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var border = new Border
        {
            Classes = { "overview-row" },
            Child = panel,
            Tag = b,
        };

        border.PointerPressed += OnRowPointerPressed;
        border.PointerMoved += OnRowPointerMoved;
        border.PointerReleased += OnRowPointerReleased;
        return border;
    }

    private void RebuildRowContent(Row row)
    {
        var panel = (StackPanel)row.Container.Child!;
        panel.Children.Clear();
        foreach (var col in _columns)
            panel.Children.Add(WrapCell(BuildCell(col, row.Brother), col));
    }

    private static Border WrapCell(Control content, ColumnSpec col)
    {
        var border = new Border
        {
            Classes = { "overview-cell" },
            Width = col.ActualWidth,
            Child = content,
        };
        if (col.Kind == ColumnKind.Perks) border.Tag = "perks";
        return border;
    }

    private Control BuildCell(ColumnSpec col, Brother b)
    {
        return col.Kind switch
        {
            ColumnKind.Portrait => BuildPortraitCell(b),
            ColumnKind.Name => BuildNameCell(b),
            ColumnKind.Role => BuildRoleCell(b),
            ColumnKind.Level => BuildLevelCell(b),
            ColumnKind.Stat => BuildStatCell((Stat)col.Tag!, b),
            ColumnKind.Equip => BuildEquipCell((Func<Brother, SlotAndName>)col.Tag!, b),
            ColumnKind.Traits => BuildTraitsCell(b),
            _ => BuildPerksCell(b),
        };
    }

    // ---- cell factories (Java column definitions, method-for-method) --------

    private Control BuildPortraitCell(Brother b)
    {
        var image = new Image
        {
            Width = WPortrait - 6,
            Height = WPortrait - 6,
            Stretch = Stretch.Uniform,
        };
        var rel = _ctx.ImageMap.ResolveHex(b.BackgroundHexId);
        var root = AppConfig.Instance.GameArtRoot();
        image.Source = rel != null && root != null
            ? _ctx.ImageCache.Get(root, rel)
            : _ctx.ImageCache.GetPlaceholder();
        var bg = b.BackgroundHexId != null ? _ctx.Dict.Get(b.BackgroundHexId) : null;
        if (bg?.Name != null) ToolTip.SetTip(image, bg.Name);
        return Center(image);
    }

    private static Control BuildNameCell(Brother b)
    {
        var box = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        box.Children.Add(new TextBlock { Classes = { "cell-name" }, Text = b.Name ?? "" });
        if (!string.IsNullOrWhiteSpace(b.Title))
        {
            box.Children.Add(new TextBlock
            {
                Classes = { "overview-title" },
                Text = b.Title,
                MaxWidth = WName - 8,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        return new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(4, 0, 0, 0),
            Child = box,
        };
    }

    private Control BuildRoleCell(Brother b)
    {
        var combo = new ComboBox
        {
            Classes = { "role-combo" },
            MinWidth = WRole - 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        // Java: role list built once per cell, null first (clears the assignment).
        var roleItems = new List<Role?> { null };
        roleItems.AddRange(_ctx.Roles.GetAll());
        combo.ItemsSource = roleItems;

        var updating = false;
        combo.SelectionChanged += (_, _) =>
        {
            if (updating || b.Fingerprint == null) return;
            var role = (Role?)combo.SelectedItem;
            _ctx.Annotation.SetRole(b.Fingerprint, role?.Id);
            // Java: role change re-renders priority backgrounds across the table.
            RebuildRows();
        };

        updating = true;
        var roleId = b.Fingerprint != null ? _ctx.Annotation.Get(b.Fingerprint).RoleId : null;
        combo.SelectedItem = roleId != null ? _ctx.Roles.GetById(roleId) : null;
        updating = false;
        return Center(combo);
    }

    private Control BuildLevelCell(Brother b)
    {
        // Java levelCol cell: level number + pending badge + budget-allotted sub-label.
        var lvlLabel = new TextBlock { Classes = { "overview-stat-val" } };
        var allottedLabel = new TextBlock { Classes = { "level-allotted-label" } };
        var box = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
            Children = { lvlLabel, allottedLabel },
        };

        lvlLabel.Text = b.LevelTotal.ToString();
        if (b.LevelTotal < 11 && b.LevelPoints > 0) lvlLabel.Classes.Add("level-pending-badge");

        var ann = b.Fingerprint != null ? _ctx.Annotation.Get(b.Fingerprint) : null;
        int[]? increases = ann?.StatIncreases;
        int[]? post11 = ann?.Post11Increases;
        var planStatus = ann?.PerkPlanStatus;

        var remainingLevels = OverviewCalc.EffectiveRemainingLevels(
            b, ExpectedStatsCalculator.RemainingLevels(b), planStatus);
        var budget = OverviewCalc.GetLevelBudget(b.LevelTotal, remainingLevels, increases, post11);

        // Sub-label visible only in the two expected-value modes (Java applyAllottedVisibility).
        var mode = _vm.Potential;
        var showAllotted = mode is OverviewViewModel.PotentialMode.CURRENT_EXPECTED
            or OverviewViewModel.PotentialMode.EXPECTED;
        allottedLabel.IsVisible = showAllotted;

        if (b.LevelTotal != 11)
        {
            allottedLabel.Text = OverviewCalc.FormatBudgetLabel(budget.Post11, budget.Used, budget.TotalBudget);
            if (OverviewCalc.GetBudgetState(budget.Used, budget.TotalBudget) == OverviewCalc.BudgetState.Over)
                allottedLabel.Classes.Add("level-allotted-red");
            else if (OverviewCalc.GetBudgetState(budget.Used, budget.TotalBudget) == OverviewCalc.BudgetState.Under)
                allottedLabel.Classes.Add("level-allotted-green");
        }

        var hasPlanned = b.PerkPoints > 0 && HasPlannedPerkEntries(b, planStatus);
        var guideReady = budget.Used == budget.TotalBudget && increases != null
            && budget.Cap > 0 && !budget.Post11
            && OverviewCalc.AllWithinCap(increases, budget.Cap);
        var hasContent = guideReady || hasPlanned;

        // Java: sticky guide popup when content exists, plain progress tooltip otherwise.
        ToolTip.SetTip(box, LevelTooltipText(b, budget));
        if (hasContent)
        {
            box.PointerReleased += (sender, e) =>
            {
                e.Handled = true; // Java consumes the click so the row handler never sees it
                _vm.RequestLevelGuide(b);
            };
        }

        return box;
    }

    /// <summary>Java refreshCell's else-branch tooltip text (the non-popup cases).</summary>
    private static string LevelTooltipText(Brother b, OverviewCalc.LevelBudget budget)
    {
        if (b.LevelTotal == 11 || budget.Cap == 0) return "Max level reached";
        if (budget.Post11)
        {
            return budget.Free > 0
                ? $"Assign post-lv11 increases\n({budget.Used} / {budget.TotalBudget} used)"
                : "Post-lv11 increases fully assigned";
        }
        var pendingNote = b.LevelPoints > 0
            ? b.LevelPoints + " unassigned level-up(s) folded into budget.\n" : "";
        return pendingNote + "Assign all increases to see\nthe roll priority guide.\n("
            + budget.Used + " / " + budget.TotalBudget + " used)";
    }

    private Control BuildStatCell(Stat stat, Brother b)
    {
        var stars = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var sc = b.Stars[stat.StarIndex];
        for (var i = 0; i < 3; i++)
        {
            stars.Children.Add(new TextBlock
            {
                Classes = { i < sc ? "overview-star-filled" : "overview-star-empty" },
                Text = i < sc ? "★" : "☆",
            });
        }

        var val = new TextBlock { Classes = { "overview-stat-val" }, Text = _ctx.StatModifier.Compute(b, stat).FinalValue.ToString() };
        var pot = new TextBlock { Classes = { "overview-potential-val" }, Text = StatPotentialCalculator.Compute(b, stat).FinalPotential.ToString() };

        var ann = b.Fingerprint != null ? _ctx.Annotation.Get(b.Fingerprint) : null;
        int[]? increases = ann?.StatIncreases;
        int[]? post11 = ann?.Post11Increases;
        var expNaive = ExpectedStatsCalculator.Compute(b, stat, increases, post11, ExpectedStatsCalculator.Mode.Naive);
        var expGreedy = ExpectedStatsCalculator.Compute(b, stat, increases, post11, ExpectedStatsCalculator.Mode.Greedy);
        var expMode = ExpectedStatsCalculator.Parse(AppConfig.Instance.ExpectedStatsMode);
        var expected = new TextBlock
        {
            Classes = { "overview-expected-val" },
            Text = OverviewCalc.ExpectedDisplay(expMode == ExpectedStatsCalculator.Mode.Greedy ? expGreedy : expNaive),
        };

        var mode = _vm.Potential;
        val.IsVisible = mode is OverviewViewModel.PotentialMode.CURRENT
            or OverviewViewModel.PotentialMode.BOTH or OverviewViewModel.PotentialMode.CURRENT_EXPECTED;
        pot.IsVisible = mode is OverviewViewModel.PotentialMode.BOTH or OverviewViewModel.PotentialMode.POTENTIAL;
        expected.IsVisible = mode is OverviewViewModel.PotentialMode.CURRENT_EXPECTED
            or OverviewViewModel.PotentialMode.EXPECTED;

        var box = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { stars, val, pot, expected },
        };

        // Java applyPriorityBackground: role priority tints the stat cell.
        var cell = new Border { Child = box };
        var role = ResolveRole(b);
        if (role != null)
        {
            var prio = OverviewCalc.PriorityAt(role.Priority, stat.Ordinal, 0);
            if (prio == 1) cell.Classes.Add("stat-cell-p1");
            else if (prio == 2) cell.Classes.Add("stat-cell-p2");
        }
        return cell;
    }

    private Control BuildEquipCell(Func<Brother, SlotAndName> resolver, Brother b)
    {
        var sn = resolver(b);
        var image = new Image
        {
            Width = WEquip - 6,
            Height = WEquip - 6,
            Stretch = Stretch.Uniform,
        };
        var slot = sn.Slot;
        image.Source = ResolveEquipImage(slot, sn.SlotName);
        if (slot is { Empty: false, ItemId: not null })
            ToolTip.SetTip(image, _ctx.Dict.GetName(slot.ItemId));

        var panel = new Grid { Width = WEquip - 6, Height = WEquip - 6 };
        panel.Children.Add(image);

        // Durability/fatigue overlay (Armor Stats toggle; Arm and Hlm columns only).
        var slotStats = slot?.Stats;
        var dur = new TextBlock
        {
            Classes = { "overview-potential-val" },
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        dur.IsVisible = _vm.Armor != OverviewViewModel.ArmorMode.OFF && slotStats != null;
        if (slotStats != null)
        {
            dur.Text = _vm.Armor switch
            {
                OverviewViewModel.ArmorMode.DURABILITY => slotStats.Durability > 0
                    ? ((int)slotStats.Durability).ToString() : "",
                OverviewViewModel.ArmorMode.FATIGUE => slotStats.Fatigue != 0
                    ? ((int)slotStats.Fatigue).ToString() : "",
                _ => "",
            };
        }
        panel.Children.Add(dur);

        // Weapon tier colour dot (Weapon Tier toggle; weapon + pouch columns).
        var dot = new Border
        {
            Classes = { "weapon-tier-dot" },
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        dot.IsVisible = _vm.WeaponTier == OverviewViewModel.WeaponTierMode.ON
            && slot is { Empty: false, ItemId: not null };
        if (slot is { Empty: false, ItemId: not null })
        {
            var tierClass = OverviewCalc.TierStyleClass(slot.ItemType, _ctx.WeaponStats.GetTier(slot.ItemId));
            if (tierClass != null) dot.Classes.Add(tierClass);
        }
        panel.Children.Add(dot);

        return Center(panel);
    }

    private Control BuildTraitsCell(Brother b)
    {
        var box = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var root = AppConfig.Instance.GameArtRoot();
        foreach (var t in b.Traits)
        {
            var entry = _ctx.Dict.Get(t.Id);
            if (entry == null || entry.Type == "internal") continue;
            if (entry.Name == null || TraitNameBlocklist.Contains(entry.Name)) continue;
            var rel = _ctx.ImageMap.ResolveHex(t.Id);
            var iv = new Image
            {
                Width = 31,
                Height = 31,
                Stretch = Stretch.Uniform,
                Source = rel != null && root != null ? _ctx.ImageCache.Get(root, rel) : _ctx.ImageCache.GetPlaceholder(),
            };
            ToolTip.SetTip(iv, entry.Name);
            box.Children.Add(iv);
        }
        return new Border { Background = Brushes.Transparent, Padding = new Thickness(4, 0, 0, 0), Child = box };
    }

    private Control BuildPerksCell(Brother b)
    {
        var box = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var root = AppConfig.Instance.GameArtRoot();
        foreach (var hexId in SortedPerkIds(b))
        {
            var rel = _ctx.ImageMap.ResolveHex(hexId);
            var iv = new Image
            {
                Width = 31,
                Height = 31,
                Stretch = Stretch.Uniform,
                Source = rel != null && root != null ? _ctx.ImageCache.Get(root, rel) : _ctx.ImageCache.GetPlaceholder(),
            };
            ToolTip.SetTip(iv, _ctx.Dict.GetName(hexId));
            box.Children.Add(iv);
        }
        if (b.PerkPoints > 0)
        {
            var badge = new TextBlock
            {
                Classes = { "level-pending-badge" },
                Text = "+" + b.PerkPoints,
            };
            ToolTip.SetTip(badge, b.PerkPoints + " perk point(s) available but not yet spent.");
            box.Children.Add(badge);
        }
        return new Border { Background = Brushes.Transparent, Padding = new Thickness(4, 0, 0, 0), Child = box };
    }

    private List<string> SortedPerkIds(Brother b)
    {
        var mode = _vm.PerkSort;
        if (mode == PerkSortMode.Off) return b.PerkIds;
        var sorted = new List<string>(b.PerkIds);
        sorted.Sort(OverviewCalc.PerkComparator(
            mode,
            id => OverviewCalc.TierOrMax(_ctx.StatModifier.GetTier(id)),
            id => _perkCounts.GetValueOrDefault(id.ToUpperInvariant()),
            name => _ctx.Dict.GetName(name)));
        return sorted;
    }

    /// <summary>Java buildPlannedPerkEntries, reduced to the count the level-cell click needs.</summary>
    private bool HasPlannedPerkEntries(Brother b, IReadOnlyDictionary<string, string>? plan)
    {
        if (plan == null || plan.Count == 0) return false;
        var owned = b.PerkIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tierCap = b.PerkUsed + 1;
        foreach (var (hexId, status) in plan)
        {
            if (owned.Contains(hexId)) continue;
            if (status is not ("PLANNED" or "OPTIONAL")) continue;
            var tier = _ctx.StatModifier.GetTier(hexId);
            if (tier is not null && tier <= tierCap) return true;
        }
        return false;
    }

    private Role? ResolveRole(Brother b)
    {
        if (b.Fingerprint == null) return null;
        var roleId = _ctx.Annotation.Get(b.Fingerprint).RoleId;
        return roleId != null ? _ctx.Roles.GetById(roleId) : null;
    }

    private Avalonia.Media.Imaging.Bitmap ResolveEquipImage(InventorySlot? slot, string slotName)
    {
        if (slot is not { Empty: false, ItemId: not null })
            return _ctx.ImageCache.GetPlaceholder();
        var icon = slot.Stats?.Icon ?? 0;
        var iconSet = slot.Stats?.IconSet;
        var rel = _ctx.ImageMap.Resolve(slotName, slot.ItemId, icon, iconSet);
        var root = AppConfig.Instance.GameArtRoot();
        return rel != null && root != null ? _ctx.ImageCache.Get(root, rel) : _ctx.ImageCache.GetPlaceholder();
    }

    // ---- flex-last perks width (Java CONSTRAINED_RESIZE_POLICY_FLEX_LAST_COLUMN) ----

    private void UpdatePerksWidth()
    {
        var available = BodyScroll.Viewport.Width;
        if (available <= 0) available = Bounds.Width;
        if (available <= 0) return;
        var others = _columns.Where(c => c.Kind != ColumnKind.Perks).Sum(c => c.Width);
        var perksWidth = Math.Max(WPerkMin, available - others);
        foreach (var header in HeaderHost.Children.OfType<Border>())
            if (header.Tag is ColumnSpec { Kind: ColumnKind.Perks })
                header.Width = perksWidth;
        foreach (var row in _rows)
        {
            if (row.Container.Child is StackPanel panel &&
                panel.Children.LastOrDefault() is Border { Tag: "perks" } perksCell)
                perksCell.Width = perksWidth;
        }
    }

    // ---- row click / drag sort (Java rowFactory) ----------------------------

    private Point _rowPressPos;
    private bool _rowDragging;
    private PointerPressedEventArgs? _rowPressArgs;

    private void OnRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (sender is not Border row || row.Tag is not Brother) return;
        _rowPressPos = e.GetPosition(row);
        _rowPressArgs = e;
        _rowDragging = false;
        e.Pointer.Capture(row);
    }

    private async void OnRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Border row || row.Tag is not Brother b || e.Pointer.Captured != row) return;
        if (_rowDragging || _rowPressArgs == null) return;
        var delta = e.GetPosition(row) - _rowPressPos;
        if (Math.Abs(delta.X) < 5 && Math.Abs(delta.Y) < 5) return;
        if (b.Fingerprint == null) return;

        _rowDragging = true;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(b.Fingerprint));
        // DoDragDropAsync takes the original press args (it drives that pointer through the OLE loop).
        await DragDrop.DoDragDropAsync(_rowPressArgs, transfer, DragDropEffects.Move);
        _rowDragging = false;
        _rowPressArgs = null;
    }

    private void OnRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Border row || row.Tag is not Brother b) return;
        e.Pointer.Capture(null);
        _rowPressArgs = null;
        if (!_rowDragging)
        {
            // Java row.setOnMouseClicked → onRowClick → showCard.
            _vm.RequestShowCard(b);
        }
        _rowDragging = false;
    }

    private Row? FindRowAt(object? eventSource)
    {
        for (var v = eventSource as Visual; v != null; v = v.GetVisualParent())
        {
            if (v is Border { Tag: Brother } border && _rows.Any(r => ReferenceEquals(r.Container, border)))
                return _rows.First(r => ReferenceEquals(r.Container, border));
        }
        return null;
    }

    private void OnRowDragOver(object? sender, DragEventArgs e)
    {
        ClearDragTarget();
        var sourceFp = e.DataTransfer.TryGetText();
        var target = FindRowAt(e.Source);
        if (sourceFp == null || target == null || target.Brother.Fingerprint == null) return;
        if (sourceFp == target.Brother.Fingerprint) return;

        // Java: rows may only be dragged within the same frontline group.
        var source = _rows.FirstOrDefault(r => r.Brother.Fingerprint == sourceFp);
        if (source != null && FrontlineKey(source.Brother) == FrontlineKey(target.Brother))
        {
            e.DragEffects = DragDropEffects.Move;
            target.Container.Classes.Add("drag-target");
        }
        e.Handled = true;
    }

    private void OnRowDragLeave(object? sender, DragEventArgs e) => ClearDragTarget();

    private void OnRowDrop(object? sender, DragEventArgs e)
    {
        ClearDragTarget();
        var sourceFp = e.DataTransfer.TryGetText();
        var target = FindRowAt(e.Source);
        if (sourceFp == null || target == null) return;

        var srcIdx = _rows.FindIndex(r => r.Brother.Fingerprint == sourceFp);
        var tgtIdx = _rows.IndexOf(target);
        if (srcIdx < 0 || srcIdx == tgtIdx) return;

        // Java: remove the source, insert at the target's index.
        var moved = _rows[srcIdx];
        _rows.RemoveAt(srcIdx);
        _rows.Insert(tgtIdx, moved);

        // Java: persist the new order as sortIndex 0..n-1.
        _ctx.Annotation.SetOrder(_rows
            .Select(r => r.Brother.Fingerprint)
            .Where(fp => fp != null)
            .Select(fp => fp!)
            .ToList());
        RebuildRows();
        e.Handled = true;
    }

    private void ClearDragTarget()
    {
        foreach (var row in _rows) row.Container.Classes.Remove("drag-target");
    }

    // ---- header column drag (Java columns listener + snap-back) -------------

    private void AttachColumnDrag(Border headerCell)
    {
        Point start = default;
        bool dragging = false;

        headerCell.PointerPressed += (s, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(HeaderHost);
            dragging = false;
            e.Pointer.Capture(headerCell);
        };

        headerCell.PointerMoved += (s, e) =>
        {
            if (e.Pointer.Captured != headerCell) return;
            var pos = e.GetPosition(HeaderHost);
            if (!dragging)
            {
                if (Math.Abs(pos.X - start.X) < 5 && Math.Abs(pos.Y - start.Y) < 5) return;
                dragging = true;
                _colDragSpec = (ColumnSpec?)headerCell.Tag;
                _colDragIndicator = new Border
                {
                    Classes = { "column-drag-indicator" },
                    Width = 2,
                };
            }
            MoveIndicator(pos.X);
        };

        headerCell.PointerReleased += (s, e) =>
        {
            e.Pointer.Capture(null);
            if (dragging) CommitColumnDrag();
            RemoveIndicator();
            dragging = false;
            _colDragSpec = null;
        };
        headerCell.PointerCaptureLost += (s, e) =>
        {
            RemoveIndicator();
            dragging = false;
            _colDragSpec = null;
        };
    }

    private void MoveIndicator(double x)
    {
        if (_colDragIndicator == null) return;
        var index = ComputeInsertIndex(x);
        HeaderHost.Children.Remove(_colDragIndicator);
        // Keep the indicator inside the group's zone so the user sees where drops land;
        // the group bound itself is enforced on commit.
        index = Math.Clamp(index, 0, HeaderHost.Children.Count);
        HeaderHost.Children.Insert(index, _colDragIndicator);
    }

    private int ComputeInsertIndex(double x)
    {
        double acc = 0;
        for (var i = 0; i < HeaderHost.Children.Count; i++)
        {
            if (HeaderHost.Children[i] == _colDragIndicator) continue;
            var w = ((Border)HeaderHost.Children[i]).Width;
            if (x < acc + w / 2) return i;
            acc += w;
        }
        return HeaderHost.Children.Count;
    }

    private void CommitColumnDrag()
    {
        if (_colDragSpec == null) return;
        var ids = HeaderHost.Children
            .OfType<Border>()
            .Where(c => c != _colDragIndicator)
            .Select(c => ((ColumnSpec)c.Tag!).Id)
            .ToList();
        if (!ids.SequenceEqual(_columns.Select(c => c.Id)))
            _vm.TryCommitColumnOrder(ids); // invalid → ColumnOrderChanged not raised, header rebuilt below
        else
            BuildHeader(); // released back where it started — just drop the indicator state
    }

    private void RemoveIndicator()
    {
        if (_colDragIndicator != null) HeaderHost.Children.Remove(_colDragIndicator);
        _colDragIndicator = null;
    }

    private static Control Center(Control child) => new Border
    {
        Background = Brushes.Transparent,
        Child = child,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
