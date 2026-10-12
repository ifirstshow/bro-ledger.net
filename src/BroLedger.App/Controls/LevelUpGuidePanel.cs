using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using BroLedger.App.Services;
using BroLedger.App.ViewModels;
using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.Controls;

/// <summary>
/// The level-cell sticky guide popup — port of Java BrotherOverviewPane's guidePopup
/// (L423-484) + TooltipFactory.guideContent / PlannedPerkEntry. One shared Popup per
/// overview pane: clicking a level cell with content opens it anchored to that cell,
/// clicking the same cell again closes it (Java toggles a per-cell popup; the in-app
/// press filter below closes the old one first, so the net behaviour is identical).
///
/// Java stickiness semantics preserved: the close triggers are ESC and any pointer press
/// inside the app outside the anchor cell — switching to another application keeps the
/// popup (Java's Popup has no autohide either; IsLightDismissEnabled stays false).
/// </summary>
internal sealed class LevelUpGuidePanel
{
    /// <summary>Java TooltipFactory.PlannedPerkEntry — a planned-perk row of the popup.</summary>
    public sealed record PlannedPerkEntry(string HexId, PerkPlanStatus Status, string Name);

    // ---- verbatim texts (Java guideContent label literals) -------------------

    public static string HeaderText(int remaining) =>
        "加点优先级指南（剩余 " + remaining + " 级）";
    public const string AlwaysPickHeader = "必选：";
    public const string RollOutcomeHeader = "按投骰结果选择（越高越好）：";
    public const string PlannedPerksHeader = "计划天赋（下一次选择）：";

    /// <summary>Java String.format("%+.1f") — explicit sign, one decimal.</summary>
    public static string FormatDeviation(double deviation) =>
        string.Format("{0:+0.0;-0.0;+0.0}", deviation);

    private readonly UiContext _ctx;
    private readonly OverviewViewModel _vm;
    private readonly Func<Brother, Control?> _anchorOf;

    private readonly Popup _popup = new()
    {
        // Java: guidePopup.show(box, bounds.getMaxX() + 4, bounds.getMinY())
        Placement = PlacementMode.Right,
        HorizontalOffset = 4,
        IsLightDismissEnabled = false, // sticky: only our own triggers close it
    };

    private TopLevel? _topLevel;
    private Control? _anchor;

    public LevelUpGuidePanel(UiContext ctx, OverviewViewModel vm, Func<Brother, Control?> anchorOf)
    {
        _ctx = ctx;
        _vm = vm;
        _anchorOf = anchorOf;
    }

    public void Attach()
    {
        _vm.LevelGuideRequested += OnGuideRequested;
    }

    public void Detach()
    {
        Hide();
        _vm.LevelGuideRequested -= OnGuideRequested;
    }

    /// <summary>Cells are rebuilt on refresh — drop the popup so its anchor never dangles
    /// (Java updateItem: guidePopup.hide()).</summary>
    public void Hide()
    {
        _popup.IsOpen = false;
        if (_topLevel != null)
        {
            _topLevel.RemoveHandler(InputElement.KeyDownEvent, OnPreviewKey);
            _topLevel.RemoveHandler(InputElement.PointerPressedEvent, OnPreviewPress);
            _topLevel = null;
        }
        _anchor = null;
    }

    private void OnGuideRequested(Brother b)
    {
        var anchor = _anchorOf(b);
        if (anchor == null) return;
        if (_popup.IsOpen && ReferenceEquals(_anchor, anchor))
        {
            Hide(); // second click on the same cell toggles closed
            return;
        }

        var (entries, remaining, perks) = BuildData(b);
        if ((entries == null || entries.Count == 0) && perks.Count == 0) return;

        _anchor = anchor;
        _popup.Child = BuildContent(entries, remaining, perks);
        _popup.PlacementTarget = anchor;
        _topLevel = TopLevel.GetTopLevel(anchor);
        _popup.IsOpen = true;

        // Java setOnShown: scene-wide ESC filter + any-press-outside-cell filter.
        if (_topLevel != null)
        {
            _topLevel.AddHandler(InputElement.KeyDownEvent, OnPreviewKey, RoutingStrategies.Tunnel);
            _topLevel.AddHandler(InputElement.PointerPressedEvent, OnPreviewPress, RoutingStrategies.Tunnel);
        }
    }

    private void OnPreviewKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Hide();
        e.Handled = true; // Java consumes — ESC closes the guide, not navigateBack
    }

    private void OnPreviewPress(object? sender, PointerPressedEventArgs e)
    {
        // Java appPressHandler: close on any in-app press not within the anchor cell
        // (the cell's own click toggles instead).
        for (Visual? v = e.Source as Visual; v != null; v = v.GetVisualParent())
            if (v == _anchor) return;
        Hide();
    }

    // ---- content assembly (Java buildGuideEntries / buildPlannedPerkEntries) --

    /// <summary>Java refreshCell's guideReady predicate, extracted so the level cell
    /// (T3) and the popup share one definition.</summary>
    public static bool IsGuideReady(OverviewCalc.LevelBudget budget, int[]? increases) =>
        budget.Used == budget.TotalBudget && increases != null
        && budget.Cap > 0 && !budget.Post11
        && OverviewCalc.AllWithinCap(increases, budget.Cap);

    /// <summary>(entries, remaining, planned perks) for the popup; entries null when the
    /// allocation is not guide-ready. Java caches these per cell; we rebuild per click.</summary>
    public (List<ExpectedStatsCalculator.PriorityEntry>? Entries, int Remaining,
        List<PlannedPerkEntry> Perks) BuildData(Brother b)
    {
        var ann = b.Fingerprint != null ? _ctx.Annotation.Get(b.Fingerprint) : null;
        int[]? increases = ann?.StatIncreases;
        int[]? post11 = ann?.Post11Increases;

        var remainingLevels = OverviewCalc.EffectiveRemainingLevels(
            b, ExpectedStatsCalculator.RemainingLevels(b), ann?.PerkPlanStatus);
        var budget = OverviewCalc.GetLevelBudget(b.LevelTotal, remainingLevels, increases, post11);

        var perks = b.PerkPoints > 0 ? BuildPlannedPerks(b) : [];
        List<ExpectedStatsCalculator.PriorityEntry>? entries = null;
        var remaining = 0;
        if (IsGuideReady(budget, increases))
        {
            entries = BuildEntries(b, budget.Cap, increases!);
            remaining = budget.Cap;
        }
        return (entries, remaining, perks);
    }

    private List<ExpectedStatsCalculator.PriorityEntry> BuildEntries(
        Brother b, int remaining, int[] rolls)
    {
        var stars = new int[Stat.Values.Length];
        for (var i = 0; i < Stat.Values.Length; i++) stars[i] = b.Stars[Stat.Values[i].StarIndex];
        var role = ResolveRole(b);
        var priority = new int[Stat.Values.Length];
        Array.Fill(priority, 3);
        if (role?.Priority != null)
            Array.Copy(role.Priority, priority, Math.Min(role.Priority.Length, priority.Length));
        return ExpectedStatsCalculator.RollPriorityGuideEntries(remaining, stars, rolls, priority);
    }

    /// <summary>Java buildPlannedPerkEntries: unowned PLANNED/OPTIONAL perks within the
    /// next pick's tier ceiling, PLANNED first, then alphabetical by name.</summary>
    public List<PlannedPerkEntry> BuildPlannedPerks(Brother b)
    {
        if (b.Fingerprint == null) return [];
        var plan = _ctx.Annotation.Get(b.Fingerprint).PerkPlanStatus;
        if (plan == null || plan.Count == 0) return [];

        var owned = b.PerkIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tierCap = b.PerkUsed + 1; // next pick tier ceiling

        var result = new List<(string HexId, PerkPlanStatus Status, string Name, int Rank, string Lower)>();
        foreach (var (hexId, status) in plan)
        {
            if (owned.Contains(hexId)) continue;
            var ps = PerkPlanStatusLogic.FromString(status);
            if (ps is not (PerkPlanStatus.Planned or PerkPlanStatus.Optional)) continue;
            var tier = _ctx.StatModifier.GetTier(hexId);
            if (tier is null || tier > tierCap) continue; // only picks within reach
            var name = _ctx.Dict.GetName(hexId) ?? hexId;
            result.Add((hexId, ps, name,
                ps == PerkPlanStatus.Planned ? 0 : 1,
                name.ToLowerInvariant()));
        }
        return result
            .OrderBy(e => e.Rank)
            .ThenBy(e => e.Lower, StringComparer.Ordinal)
            .Select(e => new PlannedPerkEntry(e.HexId, e.Status, e.Name))
            .ToList();
    }

    private Role? ResolveRole(Brother b)
    {
        if (b.Fingerprint == null) return null;
        var roleId = _ctx.Annotation.Get(b.Fingerprint).RoleId;
        return roleId != null ? _ctx.Roles.GetById(roleId) : null;
    }

    // ---- visual construction (Java guideContent) ------------------------------

    /// <summary>Builds the popup content: priority rows + planned-perk rows.</summary>
    public Control BuildContent(List<ExpectedStatsCalculator.PriorityEntry>? entries,
                                int remaining, List<PlannedPerkEntry> perks)
    {
        var root = new Border { Classes = { "priority-guide-tooltip" }, Child = new StackPanel { Spacing = 4 } };
        var list = (StackPanel)root.Child!;

        if (entries is { Count: > 0 })
        {
            list.Children.Add(new TextBlock
            {
                Classes = { "priority-guide-header" },
                Text = HeaderText(remaining),
            });

            var hadFixed = false;
            var hadFlex = false;
            foreach (var e in entries)
            {
                if (e.Fixed && !hadFixed)
                {
                    list.Children.Add(new TextBlock
                    {
                        Classes = { "priority-guide-section" },
                        Text = AlwaysPickHeader,
                    });
                    hadFixed = true;
                }
                if (!e.Fixed && !hadFlex)
                {
                    list.Children.Add(new TextBlock
                    {
                        Classes = { "priority-guide-section" },
                        Text = RollOutcomeHeader,
                    });
                    hadFlex = true;
                }

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var icon = LoadStatIcon(e.Stat);
                if (icon != null) row.Children.Add(icon);
                row.Children.Add(new TextBlock
                {
                    Classes = { "priority-guide-name" },
                    Text = e.Stat.Label(),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                if (!e.Fixed)
                {
                    row.Children.Add(new TextBlock
                    {
                        Classes = { "priority-guide-roll" },
                        Text = e.RollValue.ToString(),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                    row.Children.Add(new TextBlock
                    {
                        Classes = { e.Deviation >= 0 ? "priority-guide-dev-pos" : "priority-guide-dev-neg" },
                        Text = FormatDeviation(e.Deviation),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
                list.Children.Add(row);
            }
        }

        if (perks.Count > 0)
        {
            list.Children.Add(new TextBlock
            {
                Classes = { "perk-plan-section" },
                Text = PlannedPerksHeader,
            });
            foreach (var pe in perks)
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                row.Children.Add(BuildStatusIcon(pe.HexId, pe.Status, 24, 14, 30));
                row.Children.Add(new TextBlock
                {
                    Classes = { "perk-plan-perk-name" },
                    Text = pe.Name,
                    VerticalAlignment = VerticalAlignment.Center,
                });
                list.Children.Add(row);
            }
        }

        return root;
    }

    /// <summary>Java TooltipFactory.statIcon — 16x16 preserved-ratio stat icon.</summary>
    private static Image? LoadStatIcon(Stat stat)
    {
        if (!TooltipFactory.StatIcons.TryGetValue(stat, out var file)) return null;
        try
        {
            var stream = AssetLoader.Open(new Uri("avares://BroLedger.App/Assets/" + file));
            return new Image { Source = new Bitmap(stream), Width = 16, Height = 16 };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Java PerkPlanPane.buildStatusIcon(ctx, hexId, status, 24, 14, 30) —
    /// perk image with the status ring, fixed 30x30 slot.</summary>
    private Control BuildStatusIcon(string hexId, PerkPlanStatus status,
                                    double iconSize, double circleRadius, double slotSize)
    {
        var circle = new Ellipse
        {
            Classes = { "perk-status-circle" },
            Width = circleRadius * 2,
            Height = circleRadius * 2,
            IsVisible = false,
        };
        if (status == PerkPlanStatus.Planned) { circle.IsVisible = true; circle.Classes.Add("perk-status-planned"); }
        else if (status == PerkPlanStatus.Optional) { circle.IsVisible = true; circle.Classes.Add("perk-status-optional"); }

        var iv = new Image
        {
            Width = iconSize,
            Height = iconSize,
            Source = PerkPlanPane.LoadHexImage(_ctx, hexId),
        };
        return new Panel
        {
            Width = slotSize,
            Height = slotSize,
            Children = { circle, iv },
        };
    }
}
