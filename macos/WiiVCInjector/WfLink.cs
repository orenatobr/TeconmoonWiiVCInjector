using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace WiiVCInjector;

/// <summary>WinForms-style LinkLabel with a Click event.</summary>
public class WfLink : TextBlock
{
    public static readonly RoutedEvent<RoutedEventArgs> ClickEvent =
        RoutedEvent.Register<WfLink, RoutedEventArgs>(nameof(Click), RoutingStrategies.Bubble);

    public WfLink() => TextDecorations = Avalonia.Media.TextDecorations.Underline;

    public event EventHandler<RoutedEventArgs>? Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        RaiseEvent(new RoutedEventArgs(ClickEvent));
    }
}
