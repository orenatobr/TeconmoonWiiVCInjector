using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace WiiVCInjector;

/// <summary>
/// Cheat codes of one GameCube game. The codes Dolphin ships are downloaded from its game settings; the ones people wrote on the game's
/// Dolphin wiki page (behind a bot check, so the page opens in the browser) are pasted here. The picked codes are saved as the
/// <c>/codes/&lt;id&gt;.gct</c> file the Teconmoon Nintendont loads, which also runs for games started from a WUP.
/// </summary>
public sealed class CheatsWindow : Window
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    readonly string _id, _name;
    readonly List<(Cheat Cheat, CheckBox Box)> _rows = new();
    readonly StackPanel _list = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    readonly TextBox _paste = new() { AcceptsReturn = true, MinHeight = 90, TextWrapping = TextWrapping.Wrap, Watermark = "Paste the code block here, for example:\n$Widescreen\n04345894 3F400000" };

    public CheatsWindow(string id, string name)
    {
        _id = id; _name = name;
        Title = "Cheat codes";
        Width = 580; SizeToContent = SizeToContent.Height; CanResize = false; FontSize = 13;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Button B(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> click)
        {
            var b = new Button { Content = text };
            b.Click += click;
            return b;
        }
        TextBlock T(string text, double opacity = 1) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = opacity };

        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = $"{name} ({id})", FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap },
                T("Gecko and Action Replay codes from the Dolphin project. Nintendont runs them for GameCube games, including a game inside a WUP."),
                _status,
                new ScrollViewer { MaxHeight = 220, Content = _list },
                T("Codes people wrote for this game are on its Dolphin wiki page. The wiki checks for bots, so it opens in your browser: copy a code block there and paste it here.", 0.8),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { B("Open wiki page", (_, _) => MainWindow.Open(CheatCodes.WikiUrl(_name))), B("Add pasted codes", AddPasted_Click) } },
                _paste,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { B("Remove saved codes", Remove_Click), B("Copy to SD card...", CopyToSd_Click), B("Save", Save_Click), B("Close", (_, _) => Close()) },
                },
            },
        };
        Opened += async (_, _) => await LoadAsync();
    }

    async Task LoadAsync()
    {
        var saved = CheatCodes.GctPath(_id);
        var note = File.Exists(saved) ? $"\nA cheat file is already saved for this game ({(new FileInfo(saved).Length - 24) / 8} lines). Saving replaces it." : "";
        _status.Text = "Downloading Dolphin's game settings..." + note;
        try
        {
            var found = await CheatCodes.DownloadAsync(_id, Http);
            AddRows(found);
            _status.Text = (found.Count > 0 ? $"Found {found.Count} code(s) in Dolphin's game settings. Tick the ones you want, nothing is on by default."
                : "Dolphin's game settings have no codes for this game. Open the wiki page, copy a code block and paste it below.") + note;
        }
        catch (Exception ex)
        {
            _status.Text = "Could not reach Dolphin's game settings (" + ex.Message + "). You can still paste codes from the wiki page below." + note;
        }
    }

    void AddRows(IEnumerable<Cheat> cheats)
    {
        foreach (var c in cheats)
        {
            var text = $"{c.Name}  ({c.Source}, {c.Lines.Count} line(s))";
            if (CheatCodes.Warning(c, _id) is { } w) text += "\n" + w;
            var box = new CheckBox { Content = text, IsEnabled = c.Unusable == null };
            if (c.Unusable != null) ToolTip.SetTip(box, c.Unusable);
            _rows.Add((c, box));
            _list.Children.Add(box);
        }
    }

    async void AddPasted_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var added = CheatCodes.ParseText(_paste.Text ?? "");
        if (added.Count == 0) { await Dialogs.Info(this, "No code lines found. A code line is two groups of 8 hex digits, like 04345894 3F400000.", "Nothing to add"); return; }
        AddRows(added);
        _paste.Text = "";
    }

    async void Save_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var picked = _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Cheat).ToList();
        if (picked.Count == 0) { await Dialogs.Info(this, "Tick at least one code first.", "No code picked"); return; }
        if (picked.Select(c => CheatCodes.Warning(c, _id)).FirstOrDefault(w => w != null) is { } warn
            && !await Dialogs.Ask(this, warn + "\nSave anyway?", "Audio delay"))
            return;
        try
        {
            Directory.CreateDirectory(CheatCodes.LibraryDir);
            File.WriteAllBytes(CheatCodes.GctPath(_id), CheatCodes.BuildGct(picked));
        }
        catch (Exception ex) { await Dialogs.Info(this, "Could not save the codes: " + ex.Message, "Error"); return; }
        await Dialogs.Info(this, $"Saved {picked.Count} code(s) for {_id}. Use \"Copy to SD card...\" here, or install Nintendont from the SD Card menu, to put them on the card.", "Saved");
    }

    async void CopyToSd_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!File.Exists(CheatCodes.GctPath(_id))) { await Dialogs.Info(this, "Save the codes first.", "Nothing saved"); return; }
        var dirs = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose the SD card", AllowMultiple = false });
        if (dirs.Count == 0 || dirs[0].TryGetLocalPath() is not { } sd) return;
        try
        {
            Directory.CreateDirectory(Path.Combine(sd, "codes"));
            File.Copy(CheatCodes.GctPath(_id), Path.Combine(sd, "codes", _id + ".gct"), true);
        }
        catch (Exception ex) { await Dialogs.Info(this, "Could not write to the card: " + ex.Message, "Error"); return; }
        await Dialogs.Info(this, $"Copied to codes/{_id}.gct on the card. The Teconmoon Nintendont loads it when the game starts; the plain upstream build only does with Cheats ticked in its menu.", "Copied");
    }

    async void Remove_Click(object? s, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var saved = CheatCodes.GctPath(_id);
        if (!File.Exists(saved)) { await Dialogs.Info(this, "No codes are saved for this game.", "Nothing saved"); return; }
        if (!await Dialogs.Ask(this, "Delete the saved codes for this game? A copy already on the SD card stays there.", "Remove saved codes")) return;
        File.Delete(saved);
        await Dialogs.Info(this, "Removed.", "Removed");
    }
}
