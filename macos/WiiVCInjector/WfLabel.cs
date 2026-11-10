using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiiVCInjector;

/// <summary>
/// Wrapping text with an optional status dot. <see cref="Status"/> drives the :good / :bad pseudo-classes that
/// App.axaml turns into theme-aware colours (null = neutral, true = met, false = missing).
/// </summary>
public class WfLabel : Border
{
    private readonly TextBlock _t = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _dot = new()
    {
        Classes = { "dot" }, Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), IsVisible = false, VerticalAlignment = VerticalAlignment.Center,
    };
    private bool? _status;

    public WfLabel()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(_t, 1);
        row.Children.Add(_dot);
        row.Children.Add(_t);
        Child = row;
    }

    public string Text
    {
        get => _t.Text ?? "";
        set
        {
            _t.Text = value;
            if (_t.TextTrimming != TextTrimming.None) ToolTip.SetTip(this, string.IsNullOrEmpty(value) ? null : value);
        }
    }

    /// <summary>Trimmed text (paths) stays on one line and shows in full as a tooltip.</summary>
    public TextTrimming TextTrimming
    {
        get => _t.TextTrimming;
        set
        {
            _t.TextTrimming = value;
            _t.TextWrapping = value == TextTrimming.None ? TextWrapping.Wrap : TextWrapping.NoWrap;
        }
    }

    public double FontSize
    {
        get => _t.FontSize;
        set => _t.FontSize = value;
    }

    public FontWeight FontWeight
    {
        get => _t.FontWeight;
        set => _t.FontWeight = value;
    }

    public TextAlignment TextAlignment
    {
        get => _t.TextAlignment;
        set => _t.TextAlignment = value;
    }

    public bool ShowDot
    {
        get => _dot.IsVisible;
        set => _dot.IsVisible = value;
    }

    public bool? Status
    {
        get => _status;
        set
        {
            _status = value;
            PseudoClasses.Set(":good", value == true);
            PseudoClasses.Set(":bad", value == false);
        }
    }
}
