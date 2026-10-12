using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BroLedger.App.Services;
using BroLedger.App.ViewModels;
using BroLedger.Core.Models;
using BroLedger.Core.Parsing;
using BroLedger.Core.Services;

namespace BroLedger.App.Controls;

/// <summary>
/// Port of Java ui/BrotherCardController.java (brother-card.fxml). Layout skeleton lives in
/// BrotherCard.axaml; the stats grid, perk grid and equipment pane are built in code like the
/// Java controller builds them. Decision/orchestration logic (budget rules, target styling,
/// perk-plan state) lives in <see cref="BrotherCardViewModel"/>.
///
/// Grid geometry (Java constants): row 0 is the header (Val/Pot/Exp labels, the remainder
/// budget label above the editor column, the persistent role name); rows 1–8 follow
/// AppConfig.OrderedStats (Task 3's ColumnOrderChanged sync). Columns 0–7 are the fixed
/// section (stars, icon, label, val, pot, exp, [− n +] editor, persistent-role target);
/// columns 8+ hold the extra role-comparison combos, budgeted by the grid width.
/// </summary>
internal partial class BrotherCard : UserControl
{
    // Java display order: stats[stat.statIndex()] and stars[stat.starIndex()]. 行标签
    // 用 StatLabels（Stat.DisplayName 保持英文，是数据逻辑的破平局键）。
    private static readonly (Stat Stat, string Icon)[] StatRows =
    [
        (Stat.Health, "icon-health.png"),
        (Stat.Resolve, "icon-resolve.png"),
        (Stat.Fatigue, "icon-fatigue.png"),
        (Stat.Initiative, "icon-initiative.png"),
        (Stat.MeleeSkill, "icon-mSkill.png"),
        (Stat.RangedSkill, "icon-rSkill.png"),
        (Stat.MeleeDefense, "icon-mDefense.png"),
        (Stat.RangedDefense, "icon-rDefense.png"),
    ];

    // 槽位键（SlotNames）是 ImageMap 查找键，保持英文；显示标签用 SlotLabels。
    private static readonly string[] SlotNames = ["weapon", "shield", "body", "helmet", "trinket", "quiver", "pouch"];
    private static readonly string[] SlotLabels = ["武器", "盾", "身甲", "头盔", "饰品", "箭袋", "囊"];

    // Java grid layout constants.
    private const int StatHeaderRow = 0;
    private const int FirstStatRow = 1;
    private const int ExpectedCol = 5;
    private const int IncreaseEditorCol = 6;
    private const int PersistentTargetCol = 7;
    private const int FirstExtraCol = 8;
    private const double ExtraColWidth = 70.0;
    private const double LeftSectionWidth = 410.0;

    private static readonly Dictionary<string, Bitmap> StatIconCache = new();

    private readonly UiContext _ctx;
    private readonly BrotherCardViewModel _vm;
    private readonly OverviewViewModel _overview;
    private Brother _brother;

    // Java typed maps: column index (Stat.Ordinal) → row widget.
    private readonly Dictionary<int, StarsWidget> _starsByRow = [];
    private readonly Dictionary<int, TextBlock> _valLabels = [];
    private readonly Dictionary<int, TextBlock> _potLabels = [];
    private readonly Dictionary<int, TextBlock> _expectedLabels = [];
    private readonly Dictionary<int, TextBlock> _increaseCountLabels = [];
    private readonly Dictionary<int, Button> _incPlusBtns = [];
    private readonly Dictionary<int, Button> _incMinusBtns = [];
    private readonly Dictionary<int, TextBlock> _targetLabels = [];
    private readonly Dictionary<int, Dictionary<int, TextBlock>> _extraTargetsByRow = [];

    private int[] _lastStatOrder = [];
    private TextBlock _increasesRemainingLabel = new();
    private TextBlock _persistentRoleHeader = new();
    private bool _updatingRoleCombo;

    /// <summary>Java setOnBack → MainController showOverview.</summary>
    public event Action? BackRequested;

    public BrotherCard(UiContext ctx, OverviewViewModel overview, Brother brother)
    {
        InitializeComponent(); // Avalonia 12 generated init already loads the XAML.
        _ctx = ctx;
        _overview = overview;
        _brother = brother;
        _vm = new BrotherCardViewModel(ctx);
        _vm.SetBrother(brother);

        BackBtn.Click += (_, _) => BackRequested?.Invoke();

        BuildPerkPlanPane();
        SetupRoleCombo();
        SetupRoleButtons();
        RebuildGrid();

        // Java width watcher: recompute the extra-column budget as the grid resizes.
        StatsGrid.SizeChanged += OnStatsGridSizeChanged;
        // Java refreshRoles path: role manager changes re-resolve combos (T6 calls in).
        _vm.ExtrasChanged += RebuildGrid;
        _vm.PlanChanged += OnPlanChanged;
        // Task 3 列序联动: a valid overview reorder re-applies the card's stat row order.
        overview.ColumnOrderChanged += OnColumnOrderChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            overview.ColumnOrderChanged -= OnColumnOrderChanged;
            _vm.ExtrasChanged -= RebuildGrid;
            _vm.PlanChanged -= OnPlanChanged;
        };

        Populate(brother);
    }

    // ---- populate (Java populate) -------------------------------------------

    public void Populate(Brother b)
    {
        _brother = b;
        _vm.SetBrother(b);

        // 1. Header
        PortraitView.Source = LoadPortrait(b);
        NameLabel.Text = b.Name ?? "";
        TitleLabel.Text = b.Title ?? "";
        var bg = b.BackgroundHexId != null ? _ctx.Dict.Get(b.BackgroundHexId) : null;
        BackgroundLabel.Text = (bg?.Name ?? "") + "，等级 " + b.LevelTotal;

        // 2. Traits
        BuildTraitsPane(b);

        // 3. Role combo — load from AnnotationService (guard the SelectionChanged handler).
        _updatingRoleCombo = true;
        var roleId = b.Fingerprint != null ? _ctx.Annotation.Get(b.Fingerprint).RoleId : null;
        RoleCombo.SelectedItem = roleId != null ? _ctx.Roles.GetById(roleId) : null;
        _persistentRoleHeader.Text = RoleCombo.SelectedItem is Role r ? r.Name ?? "" : "";
        _updatingRoleCombo = false;

        // 4. Stats — rebuild rows if the user has reordered stat columns since last build.
        if (!_vm.StatOrder().Select(s => s.Ordinal).SequenceEqual(_lastStatOrder))
            RebuildGrid();
        RefreshStatValues();

        // 5. Perks
        PerkPlanPane.SetOwnedPerks(b.PerkIds);
        PerkPlanPane.RefreshPlan();
        UpdatePerkPointsBadge(b);
        UpdatePerkBadges();
        UpdatePlanButtons();

        // 6. Equipment
        BuildEquipmentPane(b);

        // 7. Footer
        ArmorLabel.Text = "护甲：" + ArmorCalculator.EffectiveArmor(b);
        FatigueLabel.Text = "疲劳：" + ArmorCalculator.TotalFatiguePenalty(b);
        DamageLabel.Text = "伤害：" + ArmorCalculator.WeaponDamageRange(b);
    }

    // ---- stats grid construction (Java buildStatRows + rebuildExtraColumns) ----

    private void OnColumnOrderChanged()
    {
        if (_vm.StatOrder().Select(s => s.Ordinal).SequenceEqual(_lastStatOrder)) return;
        RebuildGrid(); // includes the value refresh
    }

    private void OnStatsGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Java recomputeMaxExtraColumns: budget extra columns by the free width.
        var available = StatsGrid.Bounds.Width - LeftSectionWidth;
        _vm.UpdateMaxExtras(Math.Max(0, (int)(available / ExtraColWidth)));
    }

    private void RebuildGrid()
    {
        var order = _vm.StatOrder();
        _lastStatOrder = order.Select(s => s.Ordinal).ToArray();
        StatsGrid.Children.Clear();
        _starsByRow.Clear();
        _valLabels.Clear();
        _potLabels.Clear();
        _expectedLabels.Clear();
        _increaseCountLabels.Clear();
        _incPlusBtns.Clear();
        _incMinusBtns.Clear();
        _targetLabels.Clear();
        _extraTargetsByRow.Clear();

        // Rows: header + one per stat. Columns: fixed section + one per extra role.
        var rows = new RowDefinitions();
        for (var i = 0; i < order.Length + 1; i++) rows.Add(new RowDefinition(GridLength.Auto));
        var cols = new ColumnDefinitions();
        for (var i = 0; i < FirstExtraCol; i++) cols.Add(new ColumnDefinition(GridLength.Auto));
        for (var i = 0; i < _vm.Extras.Count; i++)
            cols.Add(new ColumnDefinition(new GridLength(ExtraColWidth)));
        StatsGrid.RowDefinitions = rows;
        StatsGrid.ColumnDefinitions = cols;
        StatsGrid.RowSpacing = 5;
        StatsGrid.ColumnSpacing = 10;

        for (var displayPos = 0; displayPos < order.Length; displayPos++)
        {
            var sr = StatRows[order[displayPos].Ordinal];
            var canonIdx = sr.Stat.Ordinal;
            var gridRow = FirstStatRow + displayPos;

            var stars = new StarsWidget(0);
            _starsByRow[canonIdx] = stars;
            AddCell(stars, gridRow, 0);

            var icon = new Image { Width = 20, Height = 20, Source = LoadStatIcon(sr.Icon) };
            AddCell(icon, gridRow, 1);

            var lbl = new TextBlock { Classes = { "stat-label" }, Text = sr.Stat.Label(), MinWidth = 90, VerticalAlignment = VerticalAlignment.Center };
            AddCell(lbl, gridRow, 2);

            var val = new TextBlock { Classes = { "stat-value" }, MinWidth = 28, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            _valLabels[canonIdx] = val;
            AddCell(val, gridRow, 3);

            var pot = new TextBlock { Classes = { "stat-potential" }, MinWidth = 28, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            _potLabels[canonIdx] = pot;
            AddCell(pot, gridRow, 4);

            var exp = new TextBlock { Classes = { "stat-expected" }, MinWidth = 28, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            _expectedLabels[canonIdx] = exp;
            AddCell(exp, gridRow, ExpectedCol);

            // [− count +] increase editor (Bro Developer).
            var minus = new Button { Classes = { "increase-btn" }, Content = "−", Focusable = false };
            var count = new TextBlock { Classes = { "increase-count" }, MinWidth = 18, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var plus = new Button { Classes = { "increase-btn" }, Content = "+", Focusable = false };
            minus.Click += (_, _) => OnIncreaseChange(canonIdx, -1);
            plus.Click += (_, _) => OnIncreaseChange(canonIdx, +1);
            _increaseCountLabels[canonIdx] = count;
            _incMinusBtns[canonIdx] = minus;
            _incPlusBtns[canonIdx] = plus;
            AddCell(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { minus, count, plus },
            }, gridRow, IncreaseEditorCol);

            var target = new TextBlock { Classes = { "stat-target" }, MinWidth = 28, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            _targetLabels[canonIdx] = target;
            AddCell(target, gridRow, PersistentTargetCol);
        }

        // Header row: column labels for val / pot / exp.
        foreach (var (col, text) in new[] { (3, "值"), (4, "潜力"), (ExpectedCol, "期望") })
        {
            AddCell(new TextBlock
            {
                Classes = { "stat-col-header" },
                Text = text,
                TextAlignment = TextAlignment.Right,
            }, StatHeaderRow, col);
        }

        // Header row: remainder budget label above the editor column.
        _increasesRemainingLabel = new TextBlock
        {
            Classes = { "increases-remaining-label" },
            TextAlignment = TextAlignment.Center,
        };
        AddCell(_increasesRemainingLabel, StatHeaderRow, IncreaseEditorCol);

        // Header row: persistent role name (no dropdown — that lives above the grid).
        _persistentRoleHeader = new TextBlock
        {
            Classes = { "field-label" },
            TextAlignment = TextAlignment.Right,
        };
        AddCell(_persistentRoleHeader, StatHeaderRow, PersistentTargetCol);

        BuildExtraColumns();

        // The grid rebuilds outside populate too (size budget, column order, role refresh).
        // Java's rebuildExtraColumns only re-laid the extras; ours recreates the fixed rows
        // as well, so repaint the current brother into the fresh cells.
        RefreshStatValues();
    }

    private void AddCell(Control cell, int row, int col)
    {
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, col);
        StatsGrid.Children.Add(cell);
    }

    // ---- extra role columns (Java rebuildExtraColumns) -------------------------

    private void BuildExtraColumns()
    {
        for (var i = 0; i < _vm.Extras.Count; i++)
        {
            var extraIdx = i;
            var colIdx = FirstExtraCol + i;
            var initialRole = _vm.Extras[i];
            var altBg = i % 2 == 0;

            // Role name shown below the combo; the combo button itself always reads "Choose"
            // (Java buttonCell hack → SelectionBoxItemTemplate, which outranks ItemTemplate).
            var roleName = new TextBlock
            {
                Classes = { "field-label" },
                TextAlignment = TextAlignment.Right,
                Text = initialRole?.Name ?? "",
            };
            var combo = new ComboBox
            {
                Classes = { "role-combo" },
                Width = ExtraColWidth,
                // Java's extras combo has no placeholder — the button always reads "Choose".
                ItemsSource = _ctx.Roles.GetAll(),
                ItemTemplate = new FuncDataTemplate<Role>((r, _) =>
                    new TextBlock { Text = r?.Name ?? "" }),
                SelectionBoxItemTemplate = new FuncDataTemplate<object>((_, _) =>
                    new TextBlock { Text = "选择" }),
            };
            var updating = false;
            combo.SelectionChanged += (_, _) =>
            {
                if (updating) return;
                _vm.SetExtra(extraIdx, combo.SelectedItem as Role);
            };
            updating = true;
            combo.SelectedItem = initialRole;
            updating = false;

            var header = new StackPanel
            {
                Spacing = 2,
                Children = { combo, roleName },
            };
            if (altBg) header.Classes.Add("stat-col-alt");
            AddCell(header, StatHeaderRow, colIdx);

            var order = _vm.StatOrder();
            var colTargets = new Dictionary<int, TextBlock>();
            _extraTargetsByRow[i] = colTargets;
            for (var displayPos = 0; displayPos < order.Length; displayPos++)
            {
                var canonIdx = order[displayPos].Ordinal;
                var target = new TextBlock
                {
                    Classes = { "stat-target" },
                    MinWidth = 28,
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                if (altBg) target.Classes.Add("stat-col-alt");
                colTargets[canonIdx] = target;
                AddCell(target, FirstStatRow + displayPos, colIdx);
            }

            ApplyTargetColumn(i);
        }
    }

    private void ApplyTargetColumn(int extraIdx)
    {
        if (extraIdx >= _vm.Extras.Count) return;
        if (!_extraTargetsByRow.TryGetValue(extraIdx, out var targets)) return;
        var role = _vm.Extras[extraIdx];
        foreach (var (canonIdx, label) in targets)
            ApplyTargetLabel(label, role, canonIdx);
    }

    private void OnIncreaseChange(int canonIdx, int delta)
    {
        if (_vm.TryApplyIncrease(canonIdx, delta)) RefreshIncreaseUi();
    }

    // ---- per-brother stat refresh (Java updateStatValues / refreshIncreaseUi) ---

    private void RefreshStatValues()
    {
        var b = _brother;
        var role = _vm.ResolveRole(b);

        foreach (var (canonIdx, stars) in _starsByRow)
            stars.Update(b.Stars[StatRows[canonIdx].Stat.StarIndex]);

        foreach (var (canonIdx, val) in _valLabels)
        {
            var stat = StatRows[canonIdx].Stat;
            var bd = _ctx.StatModifier.Compute(b, stat);
            val.Text = bd.FinalValue.ToString();
            TooltipFactory.Install(val,
                bd.Contributions.Count > 0 ? TooltipFactory.ForStatBreakdown(bd) : null);
        }

        foreach (var (canonIdx, pot) in _potLabels)
        {
            var stat = StatRows[canonIdx].Stat;
            var p = StatPotentialCalculator.Compute(b, stat);
            pot.Text = p.FinalPotential.ToString();
            TooltipFactory.Install(pot, p.RemainingLevels == 0
                ? null
                : TooltipFactory.ForStatPotential(p, _ctx.StatModifier.ComputeWithBase(b, stat, p.BasePotential)));
        }

        RefreshIncreaseUi();

        foreach (var (canonIdx, label) in _targetLabels)
            ApplyTargetLabel(label, role, canonIdx);
        for (var i = 0; i < _vm.Extras.Count; i++)
            ApplyTargetColumn(i);
    }

    private void RefreshIncreaseUi()
    {
        var ui = _vm.ComputeIncreaseUi();

        _increasesRemainingLabel.Classes.Remove("increases-remaining-green");
        _increasesRemainingLabel.Classes.Remove("increases-remaining-red");
        _increasesRemainingLabel.Text = ui.BudgetText;
        if (ui.BudgetStateClass != null) _increasesRemainingLabel.Classes.Add(ui.BudgetStateClass);

        foreach (var stat in Stat.Values)
        {
            var cell = ui.Cells[stat.Ordinal];
            _expectedLabels[stat.Ordinal].Text = cell.ExpectedText;
            TooltipFactory.Install(_expectedLabels[stat.Ordinal],
                TooltipFactory.ForStatExpected(cell.Naive, cell.Greedy, ui.Used));
            _increaseCountLabels[stat.Ordinal].Text = cell.Count.ToString();
            _incPlusBtns[stat.Ordinal].IsEnabled = cell.PlusEnabled;
            _incMinusBtns[stat.Ordinal].IsEnabled = cell.MinusEnabled;
        }

        AutoAssignBtn.IsEnabled = !ui.AutoAssignDisabled;
    }

    private void ApplyTargetLabel(TextBlock target, Role? role, int canonIdx)
    {
        target.Classes.Remove("stat-target-met");
        target.Classes.Remove("stat-target-potential");
        target.Classes.Remove("stat-target-unmet");
        target.Classes.Remove("stat-target-p2");
        target.Classes.Remove("stat-target-p3");

        var d = _vm.ComputeTarget(role, StatRows[canonIdx].Stat);
        target.Text = d.Text;
        if (d.StateClass != null) target.Classes.Add(d.StateClass);
        if (d.WeightClass != null) target.Classes.Add(d.WeightClass);
    }

    // ---- role combo + buttons (Java setupRoleCombo / setupRoleButtons) ----------

    private void SetupRoleCombo()
    {
        RoleCombo.PlaceholderText = "指定角色…";
        RoleCombo.ItemsSource = RoleItems();
        RoleCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingRoleCombo) return;
            var role = RoleCombo.SelectedItem as Role;
            _vm.AssignRole(role);
            _persistentRoleHeader.Text = role?.Name ?? "";
            var role2 = _vm.ResolveRole();
            foreach (var (canonIdx, label) in _targetLabels)
                ApplyTargetLabel(label, role2, canonIdx);
            RefreshIncreaseUi();
            UpdatePlanButtons();
        };
    }

    private IList<Role?> RoleItems()
    {
        // Java: role list with a null first entry (selecting it clears the assignment).
        var items = new List<Role?> { null };
        items.AddRange(_ctx.Roles.GetAll());
        return items;
    }

    private void SetupRoleButtons()
    {
        ClearRoleBtn.Click += (_, _) => RoleCombo.SelectedItem = null;
        AutoAssignBtn.Click += (_, _) =>
        {
            _vm.AutoAssignByRole();
            RefreshIncreaseUi();
        };
        ResetRolesBtn.Click += (_, _) => _vm.SetExtras([]);
        ShowAllRolesBtn.Click += (_, _) => _vm.SetExtras(_ctx.Roles.GetAll());
        FrontlineRolesBtn.Click += (_, _) => _vm.SetExtras(_ctx.Roles.GetAll().Where(r => r.Frontline));
        BacklineRolesBtn.Click += (_, _) => _vm.SetExtras(_ctx.Roles.GetAll().Where(r => !r.Frontline));
        AutoAssignBtn.IsEnabled = false;
    }

    // ---- perks (Java buildPerkPlanPane + badge/button updates) -------------------

    private PerkPlanPane PerkPlanPane = null!;

    private void BuildPerkPlanPane()
    {
        PerkPlanPane = new PerkPlanPane(_ctx, _vm);
        PerkPlanHost.Children.Add(PerkPlanPane);
    }

    private void OnPlanChanged()
    {
        UpdatePerkBadges();
        UpdatePlanButtons();
        RefreshIncreaseUi();
    }

    private void UpdatePerkPointsBadge(Brother b)
    {
        var text = _vm.PerkPointsText();
        PerkPointsLabel.IsVisible = text != null;
        PerkPointsLabel.Text = text ?? "";
        if (text != null)
            TooltipFactory.Install(PerkPointsLabel,
                $"有 {b.PerkPoints} 个天赋点可用，\n尚未在游戏中消耗。");
        else
            ToolTip.SetTip(PerkPointsLabel, null);
    }

    private void UpdatePerkBadges()
    {
        var text = BrotherCardViewModel.PlanBadgeText(_vm.PlanStatus);
        PerkPlanBadgeLabel.IsVisible = text != null;
        PerkPlanBadgeLabel.Text = text ?? "";
    }

    private void UpdatePlanButtons()
    {
        ClearPlanBtn.IsVisible = _vm.PlanStatus.Count > 0;
        var initText = _vm.InitPlanButtonText();
        InitPlanBtn.IsVisible = initText != null;
        InitPlanBtn.Content = initText ?? "";
    }

    // ---- traits (Java buildTraitsPane) -----------------------------------------

    private void BuildTraitsPane(Brother b)
    {
        TraitsHost.Children.Clear();
        var root = _ctx.AppConfig.GameArtRoot();
        foreach (var t in b.Traits)
        {
            var entry = _ctx.Dict.Get(t.Id);
            if (entry == null || entry.Type is "perk" or "internal") continue;
            if (entry.Name == null || BrotherOverviewPane.TraitNameBlocklist.Contains(entry.Name)) continue;
            var rel = _ctx.ImageMap.ResolveHex(t.Id);
            var iv = new Image
            {
                Width = 40,
                Height = 40,
                Stretch = Stretch.Uniform,
                Source = rel != null && root != null ? _ctx.ImageCache.Get(root, rel) : UnknownTraitImage(),
            };
            TooltipFactory.Install(iv, TooltipFactory.ForHex(t.Id));
            TraitsHost.Children.Add(iv);
        }
    }

    // ---- equipment (Java buildEquipmentPane) ------------------------------------

    private void BuildEquipmentPane(Brother b)
    {
        EquipmentHost.Children.Clear();
        for (var i = 0; i < Math.Min(b.EquippedSlots.Length, SlotNames.Length); i++)
            EquipmentHost.Children.Add(BuildEquipmentCell(b.EquippedSlots[i], SlotNames[i], SlotLabels[i]));
    }

    private Control BuildEquipmentCell(InventorySlot? slot, string slotName, string slotLabel)
    {
        var image = new Image { Width = 64, Height = 64, Stretch = Stretch.Uniform };
        if (slot is { Empty: false, ItemId: not null })
        {
            var icon = slot.Stats?.Icon ?? 0;
            var iconSet = slot.Stats?.IconSet;
            var rel = _ctx.ImageMap.Resolve(slotName, slot.ItemId, icon, iconSet);
            var root = _ctx.AppConfig.GameArtRoot();
            image.Source = rel != null && root != null ? _ctx.ImageCache.Get(root, rel) : _ctx.ImageCache.GetPlaceholder();
            TooltipFactory.Install(image, TooltipFactory.ForSlot(slot));
        }
        else
        {
            image.Source = _ctx.ImageCache.GetPlaceholder(); // 占位图已改为透明（见 ImageCache）
        }

        return new Border
        {
            Classes = { "equip-cell" },
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    image,
                    new TextBlock
                    {
                        Classes = { "equip-slot-label" },
                        Text = slotLabel,
                        TextAlignment = TextAlignment.Center,
                    },
                },
            },
        };
    }

    // ---- image helpers ----------------------------------------------------------

    private Bitmap LoadPortrait(Brother b)
    {
        var rel = _ctx.ImageMap.ResolveHex(b.BackgroundHexId);
        var root = _ctx.AppConfig.GameArtRoot();
        return rel != null && root != null ? _ctx.ImageCache.Get(root, rel) : _ctx.ImageCache.GetPlaceholder();
    }

    private Bitmap UnknownTraitImage()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://BroLedger.App/Assets/unknown-trait.png"));
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return _ctx.ImageCache.GetPlaceholder();
        }
    }

    private static Bitmap? LoadStatIcon(string filename)
    {
        // ponytail: 8 tiny embedded pngs, unbounded cache — assets never change at runtime.
        if (StatIconCache.TryGetValue(filename, out var cached)) return cached;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://BroLedger.App/Assets/" + filename));
            var bmp = new Bitmap(stream);
            StatIconCache[filename] = bmp;
            return bmp;
        }
        catch (Exception)
        {
            return null; // Java: empty ImageView
        }
    }
}
