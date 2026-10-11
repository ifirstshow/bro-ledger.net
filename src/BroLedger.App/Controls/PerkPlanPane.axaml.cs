using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using BroLedger.App.Services;
using BroLedger.App.ViewModels;
using BroLedger.Core.Models;
using BroLedger.Core.Services;

namespace BroLedger.App.Controls;

/// <summary>
/// Shared perk plan grid used by the brother detail card (and the Role Manager, T6) — port of
/// Java ui/PerkPlanPane.java. All perks grouped by tier row (alphabetical within tier, tier 4
/// weapons follow the in-game mastery order). LMB click cycles NOT → PLANNED → OPTIONAL via
/// <see cref="BrotherCardViewModel.CyclePerkStatus"/>, which owns the plan state and persists
/// it. <see cref="SetOwnedPerks"/> applies the brother-card opacity (1.0 owned / 0.3 not);
/// skip it where there is no ownership context (Role Manager).
/// </summary>
internal partial class PerkPlanPane : UserControl
{
    private const double IconSize = 40;
    private const double CircleDiameter = 46; // Java CIRCLE_R 23
    private const double SlotSize = 50;

    private readonly UiContext _ctx;
    private readonly BrotherCardViewModel _vm;
    private readonly Dictionary<string, Panel> _slots = new();

    public PerkPlanPane(UiContext ctx, BrotherCardViewModel vm)
    {
        InitializeComponent(); // Avalonia 12 generated init already loads the XAML.
        _ctx = ctx;
        _vm = vm;
        Build();
        _vm.PlanChanged += RefreshAll;
        DetachedFromVisualTree += (_, _) => _vm.PlanChanged -= RefreshAll;
    }

    // ---- build (once) -------------------------------------------------------

    private void Build()
    {
        // Java build(): group all perks by tier; TreeMap → SortedDictionary.
        var byTier = new SortedDictionary<int, List<KeyValuePair<string, DictionaryEntry>>>();
        foreach (var e in _ctx.Dict.GetAllByType("perk"))
        {
            var t = _ctx.StatModifier.GetTier(e.Key) ?? 99;
            if (!byTier.TryGetValue(t, out var list)) byTier[t] = list = [];
            list.Add(e);
        }

        foreach (var (tier, list) in byTier)
        {
            if (tier == 4)
                list.Sort((a, b) =>
                {
                    var r = OverviewCalc.MasteryOrderRank(a.Value.Name)
                        .CompareTo(OverviewCalc.MasteryOrderRank(b.Value.Name));
                    return r != 0 ? r : string.CompareOrdinal(a.Value.Name, b.Value.Name);
                });
            // Others keep DictionaryService.GetAllByType's name order.

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(1, 0),
            };
            row.Children.Add(new TextBlock
            {
                Classes = { "perk-tier-label" },
                Text = tier == 99 ? "?" : "T" + tier,
                MinWidth = 26,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            });
            foreach (var e in list)
            {
                var slot = BuildSlot(e.Key);
                _slots[e.Key] = slot;
                row.Children.Add(slot);
            }
            RowsHost.Children.Add(row);
        }
    }

    private Panel BuildSlot(string hexId)
    {
        var circle = new Ellipse
        {
            Classes = { "perk-status-circle" },
            Width = CircleDiameter,
            Height = CircleDiameter,
            IsVisible = false,
        };

        var iv = new Image
        {
            Width = IconSize,
            Height = IconSize,
            Source = LoadHexImage(_ctx, hexId),
            Opacity = 1.0, // default: full opacity (no ownership context)
        };

        var sp = new Panel
        {
            Width = SlotSize,
            Height = SlotSize,
            Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
            Children = { circle, iv },
        };
        TooltipFactory.Install(sp, TooltipFactory.ForHex(hexId));
        sp.Tapped += (_, _) => _vm.CyclePerkStatus(hexId);
        return sp;
    }

    // ---- refresh ------------------------------------------------------------

    private void RefreshAll()
    {
        foreach (var (hexId, slot) in _slots) ApplySlotStatus(hexId, slot);
    }

    /// <summary>Manual re-paint (Java rebuilds the pane on populate; we just refresh).</summary>
    public void RefreshPlan() => RefreshAll();

    private void ApplySlotStatus(string hexId, Panel slot)
    {
        var circle = (Ellipse)slot.Children[0];
        var status = PerkPlanStatusLogic.FromString(_vm.PlanStatus.GetValueOrDefault(hexId));
        circle.Classes.Remove("perk-status-optional");
        circle.Classes.Remove("perk-status-planned");
        switch (status)
        {
            case PerkPlanStatus.Not:
                circle.IsVisible = false;
                break;
            case PerkPlanStatus.Optional:
                circle.IsVisible = true;
                circle.Classes.Add("perk-status-optional");
                break;
            case PerkPlanStatus.Planned:
                circle.IsVisible = true;
                circle.Classes.Add("perk-status-planned");
                break;
        }
        ApplyOpacity(hexId, slot);
    }

    private HashSet<string>? _owned;

    /// <summary>Applies owned-state opacity (1.0 owned / 0.3 not); null clears the context.</summary>
    public void SetOwnedPerks(IReadOnlyCollection<string>? owned)
    {
        _owned = owned == null
            ? null
            : new HashSet<string>(owned, StringComparer.OrdinalIgnoreCase);
        foreach (var (hexId, slot) in _slots) ApplyOpacity(hexId, slot);
    }

    private void ApplyOpacity(string hexId, Panel slot)
    {
        var iv = (Image)slot.Children[1];
        iv.Opacity = _owned == null || _owned.Contains(hexId) ? 1.0 : 0.3;
    }

    // ---- image loading (Java PerkPlanPane.loadHexImage) ----------------------

    /// <summary>Static so the guide popup (LevelUpGuidePanel) shares the perk-image path.</summary>
    internal static Bitmap LoadHexImage(UiContext ctx, string hexId)
    {
        var rel = ctx.ImageMap.ResolveHex(hexId);
        var root = ctx.AppConfig.GameArtRoot();
        if (rel != null && root != null) return ctx.ImageCache.Get(root, rel);
        try
        {
            using var is2 = AssetLoader.Open(
                new Uri("avares://BroLedger.App/Assets/unknown-trait.png"));
            return new Bitmap(is2);
        }
        catch (Exception)
        {
            return ctx.ImageCache.GetPlaceholder();
        }
    }
}
