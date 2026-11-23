using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace WiiVCInjector;

/// <summary>
/// Cheat codes of one GameCube game. The codes Dolphin ships are downloaded from its game settings; the ones people wrote on the game's
/// Dolphin wiki page (behind a bot check, so the page opens in the browser) are pasted here. For a GameCube game the picked codes are saved as
/// the <c>/codes/&lt;id&gt;.gct</c> file the Teconmoon Nintendont loads, which also runs for games started from a WUP. A Wii game in a WUP has no
/// code handler, so its picked codes (plain writes only) are saved as a list the build writes into the game's main.dol.
/// </summary>
public sealed class CheatsWindow : Window
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    readonly string _id, _name;
    readonly bool _wii;
    readonly Button _copyButton;
    readonly List<(Cheat Cheat, CheckBox Box)> _rows = new();
    readonly StackPanel _list = new() { Spacing = 4 };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    readonly TextBox _paste = new() { AcceptsReturn = true, MinHeight = 90, TextWrapping = TextWrapping.Wrap, Watermark = "Paste the code block here, for example:\n$Widescreen\n04345894 3F400000" };

    public CheatsWindow(string id, string name, bool wii = false)
    {
        _id = id; _name = name; _wii = wii;
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

        _copyButton = B("Copy to SD card...", CopyToSd_Click);

        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = $"{name} ({id})", FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap },
                T(wii ? "Gecko and Action Replay codes from the Dolphin project. A Wii game in a WUP has no code handler, so the build writes the ticked codes into the game's main.dol. That works for codes that patch the game's own code (widescreen, frame rate), not for ones that change values the game keeps in memory (lives, health): those are greyed out."
                    : "Gecko and Action Replay codes from the Dolphin project. Nintendont runs them for GameCube games, including a game inside a WUP."),
                _status,
                new ScrollViewer { MaxHeight = 220, Content = _list },
                T("Codes people wrote for this game are on its Dolphin wiki page. The wiki checks for bots, so it opens in your browser: copy a code block there and paste it here.", 0.8),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { B("Open wiki page", (_, _) => MainWindow.Open(CheatCodes.WikiUrl(_name))), B("Add pasted codes", AddPasted_Click) } },
                _paste,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { B("Remove saved codes", Remove_Click), _copyButton, B("Save", Save_Click), B("Close", (_, _) => Close()) },
                },
            },
        };
        _copyButton.IsVisible = !wii;
        Opened += async (_, _) => await LoadAsync();
    }

    string SavedFile => _wii ? CheatCodes.CodesTextPath(_id) : CheatCodes.GctPath(_id);

    async Task LoadAsync()
    {
        var note = File.Exists(SavedFile) ? "\nCodes are already saved for this game. Saving replaces them." : "";
        _status.Text = "Downloading the codes for this game..." + note;
        try
        {
            var found = await CheatCodes.DownloadAsync(_id, Http);
            AddRows(found);
            _status.Text = (found.Count > 0 ? $"Found {found.Count} code(s) for {_id} (Dolphin's game settings and the Gecko code database). Tick the ones you want, nothing is on by default."
                : $"No downloadable codes for {_id}. Codes depend on the disc's region, so another region's list would not fit. The Dolphin wiki page may have some: open it, copy a code block and paste it below.") + note;
        }
        catch (Exception ex)
        {
            _status.Text = "Could not download the codes (" + ex.Message + "). You can still paste codes from the wiki page below." + note;
        }
    }

    void AddRows(IEnumerable<Cheat> cheats)
    {
        foreach (var c in cheats)
        {
            var text = $"{c.Name}  ({c.Source}, {c.Lines.Count} line(s))";
            if (CheatCodes.Warning(c, _id) is { } w) text += "\n" + w;
            var reason = c.Unusable ?? (_wii && DolCheats.Writes(c) == null ? "Needs a code handler, which a Wii game in a WUP does not have." : null);
            var box = new CheckBox { Content = text, IsEnabled = reason == null };
            if (reason != null) ToolTip.SetTip(box, reason);
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
        try
        {
            Directory.CreateDirectory(CheatCodes.LibraryDir);
            if (_wii) File.WriteAllText(SavedFile, CheatCodes.ToText(picked));
            else File.WriteAllBytes(SavedFile, CheatCodes.BuildGct(picked));
        }
        catch (Exception ex) { await Dialogs.Info(this, "Could not save the codes: " + ex.Message, "Error"); return; }
        await Dialogs.Info(this, _wii
            ? $"Saved {picked.Count} code(s) for {_id}. The next build of this game writes them into its main.dol; the build log says which ones fit."
            : $"Saved {picked.Count} code(s) for {_id}. Use \"Copy to SD card...\" here, or install Nintendont from the SD Card menu, to put them on the card.", "Saved");
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
        var saved = SavedFile;
        if (!File.Exists(saved)) { await Dialogs.Info(this, "No codes are saved for this game.", "Nothing saved"); return; }
        if (!await Dialogs.Ask(this, _wii ? "Delete the saved codes for this game? A WUP already built keeps them." : "Delete the saved codes for this game? A copy already on the SD card stays there.", "Remove saved codes")) return;
        File.Delete(saved);
        await Dialogs.Info(this, "Removed.", "Removed");
    }
}
