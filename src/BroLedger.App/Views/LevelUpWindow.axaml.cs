using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BroLedger.App.ViewModels;
using BroLedger.Core.Services;

namespace BroLedger.App;

/// <summary>
/// Port of JAVA level-up-modal.fxml + ui/LevelUpModalController.java, opened through
/// Modals.buildModal semantics (ModalService's undecorated window-modal chrome — the
/// app-frame/modal-frame classes ride the root Border here directly).
/// Deviation from Java: MODAL used showAndWait (blocking) and AUTO_CLOSE stage.show();
/// Avalonia dialogs are awaited fire-and-forget either way — observable behaviour is the
/// same (app-modal window; AUTO_CLOSE closes via MainViewModel's reload countdown).
/// </summary>
public partial class LevelUpWindow : Window
{
    public LevelUpWindow()
    {
        InitializeComponent();
    }

    /// <summary>Java MainController.openLevelUpModal: populate events, cap height at 90%
    /// of the owner, show app-modal.</summary>
    public void OpenFor(Window owner, IReadOnlyList<AnnotationService.LevelUpEvent> events)
    {
        var maxH = owner.Height * 0.9;
        MaxHeight = maxH;

        var host = EventsHost;
        host.Children.Clear();
        foreach (var card in LevelUpViewModel.BuildCards(events))
            host.Children.Add(BuildCard(card));

        _ = ShowDialog(owner);
    }

    /// <summary>Java LevelUpModalController.buildCard — name header, stat deltas (amber
    /// when under-consumed), perk lines, status text (green when adjusted).</summary>
    private static Control BuildCard(LevelUpViewModel.LevelUpCard card)
    {
        var box = new StackPanel { Spacing = 4 };
        box.Classes.Add("card-section");

        box.Children.Add(new TextBlock
        {
            Classes = { "section-header" },
            Text = card.Name ?? "",
        });

        foreach (var stat in card.Stats)
        {
            var label = new TextBlock { Classes = { "stat-label" }, Text = stat.Text };
            if (stat.UnderConsumed) label.Foreground = new SolidColorBrush(Color.FromUInt32(0xFFc87820));
            box.Children.Add(label);
        }

        foreach (var perk in card.Perks)
        {
            box.Children.Add(new TextBlock
            {
                Classes = { "stat-label" },
                Text = perk,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var status = new TextBlock
        {
            Classes = { "stat-label" },
            Text = card.StatusText,
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (card.Adjusted) status.Foreground = new SolidColorBrush(Color.FromUInt32(0xFF78c87a));
        box.Children.Add(status);

        return box;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // Java initialize: title-bar drag.
        TitleBar.PointerPressed += (_, e2) =>
        {
            if (e2.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e2);
        };
    }

    // Java initialize: scene ESC filter closes the modal.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
