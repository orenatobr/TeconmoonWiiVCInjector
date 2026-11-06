using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WiiVCInjector;

/// <summary>WinForms-style Label: wrapping text with optional background and border.</summary>
public class WfLabel : Border
{
    private readonly TextBlock _t = new() { TextWrapping = TextWrapping.Wrap };

    public WfLabel() => Child = _t;

    public string Text
    {
        get => _t.Text ?? "";
        set => _t.Text = value;
    }

    public TextTrimming TextTrimming
    {
        get => _t.TextTrimming;
        set => _t.TextTrimming = value;
    }

    public IBrush? Foreground
    {
        get => _t.Foreground;
        set => _t.Foreground = value;
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

    public Avalonia.Layout.HorizontalAlignment HorizontalContentAlignment
    {
        get => _t.HorizontalAlignment;
        set => _t.TextAlignment = value == Avalonia.Layout.HorizontalAlignment.Center ? TextAlignment.Center : TextAlignment.Left;
    }

    public Avalonia.Layout.VerticalAlignment VerticalContentAlignment
    {
        get => _t.VerticalAlignment;
        set => _t.VerticalAlignment = value;
    }

    public IBrush? Background
    {
        get => base.Background;
        set => base.Background = value;
    }
}
