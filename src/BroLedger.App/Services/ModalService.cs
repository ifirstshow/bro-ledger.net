using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace BroLedger.App.Services;

/// <summary>
/// Port of JAVA/ui/Modals.java — single source of truth for the app's custom modal chrome:
/// undecorated application-modal window, amber app-frame + modal-frame border. Deviation
/// from the Java original: keeper.css is attached per-Scene in Java, while here the styles
/// ride Application.Styles (App.axaml), so every window is themed without extra wiring.
/// </summary>
internal static class ModalService
{
    /// <summary>
    /// Wraps <paramref name="root"/> in an undecorated, window-modal dialog, ensuring the
    /// root carries app-frame + modal-frame (added if missing). Returned unshown so callers
    /// can adjust it first (Java buildModal semantics).
    /// </summary>
    public static Window BuildModal(Window owner, Control root)
    {
        if (!root.Classes.Contains("app-frame")) root.Classes.Add("app-frame");
        if (!root.Classes.Contains("modal-frame")) root.Classes.Add("modal-frame");

        return new Window
        {
            WindowDecorations = WindowDecorations.None,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = root,
        };
    }

    /// <summary>Builds and shows the dialog, awaiting its close (Java showModal).</summary>
    public static Task ShowModalAsync(Window owner, Control root) =>
        BuildModal(owner, root).ShowDialog(owner);

    /// <summary>Java Modals.confirm: true only if okText was clicked.</summary>
    public static async Task<bool> ConfirmAsync(Window owner, string title, string? header, string? body, string okText) =>
        await ChoiceAsync(owner, title, header, body, "取消", okText) == 1;

    /// <summary>Java Modals.error: header/body + a single Close button.</summary>
    public static Task ErrorAsync(Window owner, string title, string? header, string? body) =>
        ChoiceAsync(owner, title, header, body, "关闭");

    /// <summary>
    /// Port of Java Modals.choice: themed dialog with an arbitrary set of buttons
    /// (left-to-right); returns the index of the one clicked, or -1 if dismissed via the
    /// title-bar ✕.
    /// </summary>
    public static Task<int> ChoiceAsync(Window owner, string title, string? header, string? body, params string[] buttonLabels)
    {
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var titleBarPanel = new DockPanel { LastChildFill = false };
        titleBarPanel.Children.Add(new TextBlock
        {
            Classes = { "title-bar-label" },
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var closeBtn = new Button
        {
            Classes = { "window-btn", "window-btn-close" },
            Content = "✕",
            Focusable = false,
        };
        DockPanel.SetDock(closeBtn, Dock.Right);
        titleBarPanel.Children.Add(closeBtn);
        var titleBar = new Border { Classes = { "title-bar" }, Child = titleBarPanel };

        var content = new StackPanel { Spacing = 10, Margin = new Thickness(16, 20, 16, 20) };
        if (!string.IsNullOrWhiteSpace(header))
            content.Children.Add(new TextBlock
            {
                Classes = { "section-header" },
                Text = header,
                TextWrapping = TextWrapping.Wrap,
            });
        if (!string.IsNullOrWhiteSpace(body))
            content.Children.Add(new TextBlock
            {
                Classes = { "dim-label" },
                Text = body,
                TextWrapping = TextWrapping.Wrap,
            });

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var root = new StackPanel { MinWidth = 380 };
        root.Children.Add(titleBar);
        root.Children.Add(content);
        content.Children.Add(buttonRow);

        var stage = BuildModal(owner, root);
        for (var i = 0; i < buttonLabels.Length; i++)
        {
            var idx = i;
            var btn = new Button { Classes = { "fancy-btn" }, Content = buttonLabels[i] };
            btn.Click += (_, _) => { result.TrySetResult(idx); stage.Close(); };
            buttonRow.Children.Add(btn);
        }
        closeBtn.Click += (_, _) => { result.TrySetResult(-1); stage.Close(); };
        stage.Closed += (_, _) => result.TrySetResult(-1); // any other dismiss path (Alt+F4)

        _ = stage.ShowDialog(owner);
        return result.Task;
    }
}
