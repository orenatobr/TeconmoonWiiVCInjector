using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace WiiVCInjector;

/// <summary>
/// Settings for one GameCube game, in the spirit of Dolphin's GameINI: each one can follow the Nintendont setting, be forced on or be forced off.
/// They are saved as <c>teconmoon/&lt;id4&gt;.ini</c>, which the Teconmoon Nintendont applies when that game starts, also from a WUP.
/// </summary>
public sealed class GameSettingsWindow : Window
{
    readonly string _id, _name, _id4;
    readonly ComboBox _fullscreen, _hack, _progressive, _skipIpl;
    readonly TextBox _preview = new() { IsReadOnly = true, AcceptsReturn = true, MinHeight = 120, FontFamily = new FontFamily("Menlo,Monaco,monospace"), FontSize = 12 };

    static ComboBox Combo(params string[] items)
    {
        var c = new ComboBox { MinWidth = 170, SelectedIndex = 0 };
        foreach (var i in items) c.Items.Add(i);
        return c;
    }

    static bool? FromIndex(ComboBox c) => c.SelectedIndex switch { 1 => true, 2 => false, _ => null };
    static int ToIndex(bool? v) => v == null ? 0 : v == true ? 1 : 2;

    public GameSettingsWindow(string id, string name)
    {
        _id = id; _name = name; _id4 = GameSettings.Id4(id) ?? "";
        Title = "Game settings";
        Width = 600; SizeToContent = SizeToContent.Height; CanResize = false; FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _fullscreen = Combo("Nintendont setting", "16:9 fullscreen", "4:3");
        _hack = Combo("Nintendont setting", "On", "Off");
        _progressive = Combo("Nintendont setting", "On", "Off");
        _skipIpl = Combo("Nintendont setting", "On", "Off");

        Control Row(string label, string help, ComboBox box)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
            grid.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = label, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 },
                },
            });
            Grid.SetColumn(box, 1);
            box.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(box);
            return grid;
        }
        Button B(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> click)
        {
            var b = new Button { Content = text };
            b.Click += click;
            return b;
        }

        var existing = _id4 != "" && File.Exists(GameSettings.PathFor(_id4)) ? GameSettings.Parse(File.ReadAllText(GameSettings.PathFor(_id4))) : new GameSettings();
        _fullscreen.SelectedIndex = ToIndex(existing.Fullscreen);
        _hack.SelectedIndex = ToIndex(existing.WidescreenHack);
        _progressive.SelectedIndex = ToIndex(existing.ProgressiveScan);
        _skipIpl.SelectedIndex = ToIndex(existing.SkipIpl);
        foreach (var c in new[] { _fullscreen, _hack, _progressive, _skipIpl }) c.SelectionChanged += (_, _) => UpdatePreview();

        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 14,
            Children =
            {
                new TextBlock { Text = $"{name} ({id})", FontWeight = FontWeight.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Settings for just this game, in the format of Dolphin's GameINI. \"Nintendont setting\" leaves the option as Nintendont has it. They are applied by the Teconmoon Nintendont when the game starts, also from a WUP, and do not change nincfg.bin.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
                Row("Fullscreen (Wii U Widescreen)", "The Wii U shows this game on the whole 16:9 screen. The picture is stretched unless the game draws in 16:9 (a widescreen Gecko code, or the hack below).", _fullscreen),
                Row("Widescreen hack (Force Widescreen)", "Nintendont makes the game draw in 16:9. It only works where it finds the game's projection code: not in Skies of Arcadia, use a widescreen Gecko code there.", _hack),
                Row("Progressive scan (Force Progressive)", "A PAL disc ignores this in the Teconmoon Nintendont, because forcing 60 Hz delays the sound.", _progressive),
                Row("Skip IPL", "Skips the GameCube boot screen.", _skipIpl),
                new TextBlock { Text = "File saved as:", Opacity = 0.8 },
                _preview,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { B("Remove", Remove_Click), B("Copy to SD card...", CopyToSd_Click), B("Save", Save_Click), B("Close", (_, _) => Close()) },
                },
            },
        };
        UpdatePreview();
    }

    GameSettings Current() => new()
    {
        Fullscreen = FromIndex(_fullscreen), WidescreenHack = FromIndex(_hack), ProgressiveScan = FromIndex(_progressive), SkipIpl = FromIndex(_skipIpl),
    };

    void UpdatePreview() => _preview.Text = Current().IsEmpty ? $"teconmoon/{_id4}.ini\n(nothing set: every option follows Nintendont)" : $"teconmoon/{_id4}.ini\n\n" + Current().ToIni(_id, _name);

    async void Save_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_id4 == "") { await Dialogs.Info(this, "This game has no usable id.", "Error"); return; }
        var cur = Current();
        try
        {
            if (cur.IsEmpty) { if (File.Exists(GameSettings.PathFor(_id4))) File.Delete(GameSettings.PathFor(_id4)); }
            else
            {
                Directory.CreateDirectory(GameSettings.LibraryDir);
                File.WriteAllText(GameSettings.PathFor(_id4), cur.ToIni(_id, _name));
            }
        }
        catch (Exception ex) { await Dialogs.Info(this, "Could not save: " + ex.Message, "Error"); return; }
        await Dialogs.Info(this, cur.IsEmpty ? "Nothing is set, so no file is kept for this game." : $"Saved. Use \"Copy to SD card...\" here, or install Nintendont from the SD Card menu, to put teconmoon/{_id4}.ini on the card.", "Saved");
    }

    async void CopyToSd_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_id4 == "" || !File.Exists(GameSettings.PathFor(_id4))) { await Dialogs.Info(this, "Save the settings first.", "Nothing saved"); return; }
        var dirs = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose the SD card", AllowMultiple = false });
        if (dirs.Count == 0 || dirs[0].TryGetLocalPath() is not { } sd) return;
        try
        {
            Directory.CreateDirectory(Path.Combine(sd, "teconmoon"));
            File.Copy(GameSettings.PathFor(_id4), Path.Combine(sd, "teconmoon", _id4 + ".ini"), true);
        }
        catch (Exception ex) { await Dialogs.Info(this, "Could not write to the card: " + ex.Message, "Error"); return; }
        await Dialogs.Info(this, $"Copied to teconmoon/{_id4}.ini on the card. Only the Teconmoon Nintendont reads it.", "Copied");
    }

    async void Remove_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_id4 == "" || !File.Exists(GameSettings.PathFor(_id4))) { await Dialogs.Info(this, "No settings are saved for this game.", "Nothing saved"); return; }
        if (!await Dialogs.Ask(this, "Delete the saved settings of this game? A copy already on the SD card stays there.", "Remove")) return;
        File.Delete(GameSettings.PathFor(_id4));
        _fullscreen.SelectedIndex = _hack.SelectedIndex = _progressive.SelectedIndex = _skipIpl.SelectedIndex = 0;
    }
}
