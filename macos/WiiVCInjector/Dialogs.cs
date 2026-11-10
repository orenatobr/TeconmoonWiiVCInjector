using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WiiVCInjector;

/// <summary>MessageBox, InputBox and video mode pickers as small Avalonia windows.</summary>
public static class Dialogs
{
    /// <summary>Self-test hook: answers dialogs without showing them. Gets (title, text); "Yes" means yes, null cancels an input.</summary>
    public static Func<string, string, string?>? Auto;

    static Window Make(string title) => new()
    {
        Title = title, CanResize = false, SizeToContent = SizeToContent.WidthAndHeight,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, FontSize = 13,
    };

    static StackPanel Buttons(Window w, params (string Text, object? Result)[] b)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (var (text, result) in b)
        {
            var btn = new Button { Content = text, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (p.Children.Count == 0) btn.Classes.Add("accent");
            btn.Click += (_, _) => w.Close(result);
            p.Children.Add(btn);
        }
        return p;
    }

    static StackPanel Body(string text) => new()
    {
        Margin = new Thickness(24), Spacing = 18,
        Children = { new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 } },
    };

    public static async Task Info(Window owner, string text, string title)
    {
        if (Auto != null) { Auto(title, text); return; }
        var w = Make(title);
        var body = Body(text);
        body.Children.Add(Buttons(w, ("OK", null)));
        w.Content = body;
        await w.ShowDialog<object?>(owner);
    }

    public static async Task<bool> Ask(Window owner, string text, string title)
    {
        if (Auto != null) return Auto(title, text) == "Yes";
        var w = Make(title);
        var body = Body(text);
        body.Children.Add(Buttons(w, ("Yes", true), ("No", false)));
        w.Content = body;
        return await w.ShowDialog<bool?>(owner) == true;
    }

    /// <summary>Microsoft.VisualBasic.Interaction.InputBox replacement; null or empty means cancelled.</summary>
    public static async Task<string?> InputBox(Window owner, string prompt, string title, string def)
    {
        if (Auto != null) return Auto(title, prompt);
        var w = Make(title);
        var tb = new TextBox { Text = def };
        var body = Body(prompt);
        body.Children.Add(tb);
        body.Children.Add(Buttons(w, ("OK", "ok"), ("Cancel", null)));
        w.Content = body;
        w.Opened += (_, _) => { tb.Focus(); tb.SelectAll(); };
        return await w.ShowDialog<string?>(owner) == null ? null : tb.Text;
    }

    public static readonly (string Label, VideoTarget Target)[] VideoModes =
    {
        ("NTSC 480i (60Hz)", VideoTarget.Ntsc), ("PAL60 (EuRGB60)", VideoTarget.Pal60),
        ("PAL50", VideoTarget.Pal50), ("480p progressive", VideoTarget.Progressive480),
    };

    /// <summary>Replacement for the external wii-vmc.exe window: choose the target video mode (null = cancel).</summary>
    public static async Task<VideoTarget?> PickVideoMode(Window owner)
    {
        if (Auto != null) return Enum.TryParse<VideoTarget>(Auto("Video mode", "pick"), out var t) ? t : null;
        var w = Make("Patch video mode");
        var body = Body("This patches the video mode tables of the game's main.dol to the mode you choose. Experimental: "
                        + "if the game misbehaves, build again without this option.");
        var list = new ComboBox { ItemsSource = VideoModes.Select(m => m.Label).ToList(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        body.Children.Add(list);
        var ok = new Button { Content = "OK", MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "accent" } };
        var cancel = new Button { Content = "Cancel", MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => w.Close(true);
        cancel.Click += (_, _) => w.Close(false);
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { ok, cancel } });
        w.Content = body;
        return await w.ShowDialog<bool?>(owner) == true ? VideoModes[Math.Max(0, list.SelectedIndex)].Target : null;
    }
}
