using Avalonia.Controls;
using Avalonia.Layout;

namespace BroLedger.App.Controls;

/// <summary>
/// Displays 0–3 filled stars for a single stat's talent rating — port of Java ui/StarsWidget.java.
/// Java swaps whole instances because JavaFX Labels are immutable; here <see cref="Update"/>
/// retints the three glyphs instead (same grid output, no child churn).
/// </summary>
internal sealed class StarsWidget : StackPanel
{
    private readonly TextBlock[] _glyphs = new TextBlock[3];

    public StarsWidget(int stars)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 1;
        for (var i = 0; i < 3; i++)
        {
            var l = new TextBlock();
            _glyphs[i] = l;
            Children.Add(l);
        }
        Update(stars);
    }

    public void Update(int stars)
    {
        for (var i = 0; i < 3; i++)
        {
            var filled = i < stars;
            _glyphs[i].Classes.Set("star-filled", filled);
            _glyphs[i].Classes.Set("star-empty", !filled);
            _glyphs[i].Text = filled ? "★" : "☆";
        }
    }
}
