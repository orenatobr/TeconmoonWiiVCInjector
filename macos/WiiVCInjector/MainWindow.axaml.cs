using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace WiiVCInjector;

public partial class MainWindow : Window
{
    // State of the original Form1 (names kept)
    string _systemType = "wii";
    /// <summary>The selected mode; the batch button only makes sense for Wii and GameCube retail discs.</summary>
    internal string SystemType
    {
        get => _systemType;
        set { _systemType = value; BatchButton.IsEnabled = value is "wii" or "gcn"; }
    }
    internal string TitleIDHex = "", TitleIDText = "", InternalGameName = "", CucholixRepoID = "";
    internal bool FlagWBFS, FlagNKIT, FlagNASOS, FlagGameSpecified, FlagGC2Specified, FlagIconSpecified, FlagBannerSpecified;
    internal bool FlagDrcSpecified, FlagLogoSpecified, FlagBootSoundSpecified, FlagRepo;
    internal bool BuildFlagSource, BuildFlagMeta, BuildFlagAdvance = true, BuildFlagKeys;
    internal bool CommonKeyGood, TitleKeyGood, AncastKeyGood;
    internal string DRCUSE = "1", nfspatchflag = "";
    internal VideoTarget videoTarget = VideoTarget.Ntsc;
    internal string GamePath = "", GC2Path = "", MainDolPath = "", BootSoundPath = "";
    internal long GameType;

    readonly string TempRootPath = Path.Combine(Path.GetTempPath(), "WiiVCInjector");
    string TempSourcePath => Path.Combine(TempRootPath, "SOURCETEMP");
    internal AppSettings Settings = AppSettings.Load();
    bool _ready, _closeConfirmed;
    CancellationTokenSource? _cts;

    // Filters from the OpenFileDialog definitions of the designer (OpenIcon.Filter etc. come from Form1.resx)
    const string ImageFilter = "Supported Image Files (*.png,*.tga,*.jpg,*.jpeg,*.bmp,*.gif)|*.png;*.tga;*.jpg;*.jpeg;*.bmp;*.gif|PNG Files (*.png)|*.png|TGA Files (*.tga)|*.tga|JPEG Files (*.jpg,*.jpeg)|*.jpg;*.jpeg|BMP Files (*.bmp)|*.bmp|GIF Files (*.gif)|*.gif";
    string gameFilter = "Wii Dumps (*.iso,*.wbfs,*.iso.dec)|*.iso;*.wbfs;*.iso.dec";

    public MainWindow()
    {
        InitializeComponent();
        var version = typeof(MainWindow).Assembly.GetName().Version;
        Title = $"TeconMoon's WiiVC Injector - [{version}]";
        VersionText.Text = $"TeconMoon's injector for macOS, version {version?.ToString(3)}";
        // macOS ignores a window state set before the window is shown, so it is applied once the window has opened
        Opened += (_, _) => ApplyStartState(Environment.GetEnvironmentVariable);
        Directory.CreateDirectory(TempSourcePath);
        WiiVMC.IsCheckedChanged += WiiVMC_CheckedChanged;
        _ready = true;
        // grey out the options that do not apply to the initial Wii Retail mode, like switching to it does
        WiiRetail_CheckedChanged(null, null!);

        if (Environment.GetEnvironmentVariable("WIIVC_UITEST") == "1")
            Opened += (_, _) => Environment.Exit(UiSelfTest.Run(this));
        if (Environment.GetEnvironmentVariable("WIIVC_GUIBUILD") == "1")
            Opened += async (_, _) => Environment.Exit(await GuiBuildTest.Run(this));
        var tab = Environment.GetEnvironmentVariable("WIIVC_TAB");
        if (int.TryParse(tab, out var ti)) MainTabs.SelectedIndex = ti;
        var snap = Environment.GetEnvironmentVariable("WIIVC_SNAPSHOT");
        // WIIVC_WINDOW=settings|sdcard renders that window instead of the main one
        var other = Environment.GetEnvironmentVariable("WIIVC_WINDOW");
        if (!string.IsNullOrEmpty(snap) && !string.IsNullOrEmpty(other))
            Opened += async (_, _) =>
            {
                Window win = other == "sdcard" ? new SDCardWindow() : new SettingsWindow();
                win.Show();
                await Task.Delay(500);
                using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)win.Width, (int)win.Height));
                bmp.Render(win);
                bmp.Save(snap);
                _closeConfirmed = true;
                Environment.Exit(0);
            };
        if (!string.IsNullOrEmpty(snap))
            Opened += async (_, _) =>
            {
                await Task.Delay(500);
                var size = new PixelSize((int)Width, (int)Height);
                using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
                bmp.Render(this);
                bmp.Save(snap);
                _closeConfirmed = true;
                Close();
            };
    }

    /// <summary>
    /// The window opens maximized (not full screen, so the title bar and the Dock stay). The snapshot and self-test modes keep
    /// the fixed size from the XAML because they render and measure the window at exactly that size.
    /// </summary>
    internal void ApplyStartState(Func<string, string?> env) => WindowState = StartState(env);

    /// <summary>The state to open in: maximized, or the XAML size when a snapshot or self-test mode is on.</summary>
    internal static WindowState StartState(Func<string, string?> env) =>
        new[] { "WIIVC_SNAPSHOT", "WIIVC_UITEST", "WIIVC_GUIBUILD" }.Any(k => !string.IsNullOrEmpty(env(k)))
            ? WindowState.Normal : WindowState.Maximized;

    // ---------------------------------------------------------------- helpers

    static void SetPath(WfLabel l, string text, bool good)
    {
        l.Text = text;
        l.Status = good;
    }

    void ResetGameInfo()
    {
        GameNameLabel.Text = "";
        TitleIDLabel.Text = "";
        TitleIDHex = "";
        GameType = 0;
        CucholixRepoID = "";
        PackedTitleLine1.Text = "";
        PackedTitleIDLine.Text = "";
    }

    void ResetGame()
    {
        SetPath(GameSourceDirectory, "Game file has not been specified", false);
        FlagGameSpecified = false;
        ResetGameInfo();
    }

    void ResetGc2()
    {
        GC2SourceButton.IsEnabled = false;
        SetPath(GC2SourceDirectory, "2nd GameCube Disc Image has not been specified", false);
        FlagGC2Specified = false;
    }

    void ClearGamePadRadios()
    {
        NoGamePadEmu.IsChecked = false; CCEmu.IsChecked = false; HorWiiMote.IsChecked = false;
        VerWiiMote.IsChecked = false; ForceCC.IsChecked = false; ForceNoCC.IsChecked = false;
        GamePadEmuLayout.IsEnabled = false;
    }

    bool NoGamePadRadioChecked() =>
        NoGamePadEmu.IsChecked == true || CCEmu.IsChecked == true || HorWiiMote.IsChecked == true ||
        VerWiiMote.IsChecked == true || ForceCC.IsChecked == true || ForceNoCC.IsChecked == true;

    static void Off(CheckBox c) { c.IsChecked = false; c.IsEnabled = false; }

    async Task<string?> PickFile(string title, string filter, string? suggested = null) =>
        (await PickFiles(title, filter, false, suggested)).FirstOrDefault();

    async Task<List<string>> PickFiles(string title, string filter, bool multiple, string? suggested = null)
    {
        var types = filter.Split('|').Chunk(2).Where(c => c.Length == 2).Select(c =>
            new FilePickerFileType(c[0]) { Patterns = c[1].Split(';') }).ToList();
        var r = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = multiple, FileTypeFilter = types, SuggestedFileName = suggested,
        });
        return r.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    static void Open(string urlOrPath)
    {
        try { Process.Start(new ProcessStartInfo("open") { ArgumentList = { urlOrPath }, UseShellExecute = false }); }
        catch { }
    }

    // ---------------------------------------------------------------- closing

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed) return;
        e.Cancel = true;
        if (await Dialogs.Ask(this, "Are you sure you want to close?", "Closing"))
        {
            _closeConfirmed = true;
            StopSound();
            try { Directory.Delete(TempRootPath, true); } catch { }
            Close();
        }
    }

    // ---------------------------------------------------------------- mode radio buttons

    private void WiiRetail_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready || WiiRetail.IsChecked != true) return;
        WiiVMC.IsEnabled = true;
        Wiimmfi.IsEnabled = true;
        RepoDownload.IsEnabled = true;
        GameSourceButton.IsEnabled = true;
        GameSourceButton.Content = ButtonText("Browse...");
        gameFilter = "Wii Dumps (*.iso,*.wbfs,*.iso.dec)|*.iso;*.wbfs;*.iso.dec";
        ResetGame();
        SystemType = "wii";
        ResetGc2();
        if (!NoGamePadRadioChecked())
        {
            NoGamePadEmu.IsChecked = true;
            GamePadEmuLayout.IsEnabled = true;
            DRCUSE = "1";
        }
        Off(Force43NINTENDONT); Off(ForceInterlacedNINTENDONT); Off(CustomMainDol);
        Off(DisableNintendontAutoboot); Off(DisablePassthrough); Off(DisableGamePad); Off(C2WPatchFlag);
        // original: C2W is disabled for Wii Retail
        if (ForceCC.IsChecked == true) { DisableTrimming.IsChecked = false; DisableTrimming.IsEnabled = false; } else DisableTrimming.IsEnabled = true;
        Off(Force43NAND);
    }

    static TextBlock ButtonText(string t) => new() { Text = t, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };

    private void WiiHomebrew_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready || WiiHomebrew.IsChecked != true) return;
        Off(WiiVMC); Off(Wiimmfi);
        RepoDownload.IsEnabled = false;
        GameSourceButton.IsEnabled = true;
        GameSourceButton.Content = ButtonText("Browse...");
        gameFilter = "DOL Files (*.dol)|*.dol";
        ResetGame();
        SystemType = "dol";
        DRCUSE = "65537";
        ResetGc2();
        ClearGamePadRadios();
        Off(LRPatch); Off(Force43NINTENDONT); Off(ForceInterlacedNINTENDONT); Off(CustomMainDol); Off(DisableNintendontAutoboot);
        DisablePassthrough.IsEnabled = true;
        DisableGamePad.IsEnabled = true;
        C2WPatchFlag.IsEnabled = true;
        Off(DisableTrimming); Off(Force43NAND);
    }

    private async void WiiNAND_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready || WiiNAND.IsChecked != true) return;
        Off(WiiVMC); Off(Wiimmfi);
        RepoDownload.IsEnabled = true;
        GameSourceButton.IsEnabled = false;
        GameSourceButton.Content = ButtonText("TitleID...");
        ResetGameInfo();
        ResetGc2();
        Off(Force43NINTENDONT); Off(ForceInterlacedNINTENDONT); Off(CustomMainDol); Off(DisableNintendontAutoboot);
        Off(DisablePassthrough); Off(DisableGamePad); Off(C2WPatchFlag); Off(DisableTrimming);
        Force43NAND.IsEnabled = true;
        if (!NoGamePadRadioChecked())
        {
            NoGamePadEmu.IsChecked = true;
            GamePadEmuLayout.IsEnabled = true;
            DRCUSE = "1";
        }
        while (true)
        {
            var id = await Dialogs.InputBox(this, "Enter your installed Wii Channel's 4-letter Title ID. If you don't know it, open a WAD for the channel in something like ShowMiiWads to view it.", "Enter your WAD's Title ID", "XXXX");
            if (string.IsNullOrEmpty(id))
            {
                SetPath(GameSourceDirectory, "Title ID specification cancelled, reselect vWii NAND Title Launcher to specify", false);
                FlagGameSpecified = false;
                return;
            }
            if (id.Length == 4)
            {
                id = id.ToUpperInvariant();
                SetPath(GameSourceDirectory, id, true);
                FlagGameSpecified = true;
                SystemType = "wiiware";
                GameNameLabel.Text = "N/A";
                TitleIDLabel.Text = "N/A";
                TitleIDText = id;
                CucholixRepoID = id;
                TitleIDHex = string.Concat(id.Select(c => ((int)c).ToString("X")));
                PackedTitleIDLine.Text = "00050002" + TitleIDHex;
                return;
            }
            SetPath(GameSourceDirectory, "Invalid Title ID", false);
            FlagGameSpecified = false;
            await Dialogs.Info(this, "Only 4 characters can be used, try again. Example: The Star Fox 64 (USA) Channel's Title ID is NADE01, so you would specify NADE as the Title ID", "Invalid Title ID");
        }
    }

    private void GCRetail_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready || GCRetail.IsChecked != true) return;
        Off(WiiVMC); Off(Wiimmfi);
        RepoDownload.IsEnabled = true;
        GameSourceButton.IsEnabled = true;
        GameSourceButton.Content = ButtonText("Browse...");
        gameFilter = "GameCube Dumps (*.gcm,*.iso)|*.gcm;*.iso";
        ResetGame();
        SystemType = "gcn";
        DRCUSE = "65537";
        GC2SourceButton.IsEnabled = true;
        ClearGamePadRadios();
        Off(LRPatch);
        Force43NINTENDONT.IsEnabled = true;
        ForceInterlacedNINTENDONT.IsEnabled = true;
        CustomMainDol.IsEnabled = true;
        DisableNintendontAutoboot.IsEnabled = true;
        Off(DisablePassthrough);
        DisableGamePad.IsEnabled = true;
        Off(C2WPatchFlag); Off(DisableTrimming); Off(Force43NAND);
    }

    // ---------------------------------------------------------------- other windows

    private async void SettingsButton_Click(object? s, RoutedEventArgs e)
    {
        await new SettingsWindow().ShowDialog(this);
        Settings = AppSettings.Load();
    }

    private void SDCardStuff_Click(object? s, RoutedEventArgs e) => new SDCardWindow().Show();

    // ---------------------------------------------------------------- tabs

    internal void RefreshBuildRequirements()
    {
        Settings = AppSettings.Load();
        WiiUCommonKey.Text = Settings.WiiUCommonKey.ToUpperInvariant();
        TitleKey.Text = Settings.TitleKey.ToUpperInvariant();
        AncastKey.Text = AncastKey.Text?.ToUpperInvariant();

        CommonKeyGood = KeyCheck.CommonKey(WiiUCommonKey.Text!);
        WiiUCommonKey.IsReadOnly = CommonKeyGood;
        WiiUCommonKey.Classes.Set("valid", CommonKeyGood);
        TitleKeyGood = KeyCheck.TitleKey(TitleKey.Text!);
        TitleKey.IsReadOnly = TitleKeyGood;
        TitleKey.Classes.Set("valid", TitleKeyGood);
        AncastKeyGood = KeyCheck.AncastKey(AncastKey.Text ?? "");

        // a batch rewrites these two texts with its own counts; every visit to the Build tab starts from the single-game wording
        SourceCheck.Text = "Source Files are properly specified";
        MetaCheck.Text = "Meta options are valid";
        BuildFlagSource = FlagGameSpecified && FlagIconSpecified && FlagBannerSpecified;
        SourceCheck.Status = BuildFlagSource;
        BuildFlagMeta = PackedTitleLine1.Text != "" && (PackedTitleIDLine.Text?.Length ?? 0) == 16;
        MetaCheck.Status = BuildFlagMeta;
        BuildFlagAdvance = CustomMainDol.IsChecked != true || Path.GetExtension(MainDolPath) == ".dol";
        AdvanceCheck.Status = BuildFlagAdvance;
        BuildFlagKeys = C2WPatchFlag.IsChecked != true ? CommonKeyGood && TitleKeyGood : CommonKeyGood && TitleKeyGood && AncastKeyGood;
        KeysCheck.Status = BuildFlagKeys;
        TheBigOneTM.IsEnabled = BuildFlagSource && BuildFlagMeta && BuildFlagAdvance && BuildFlagKeys;
    }

    private void MainTabs_SelectedIndexChanged(object? s, SelectionChangedEventArgs e)
    {
        if (!_ready || e.Source != MainTabs) return;
        bool first = MainTabs.SelectedItem == SourceFilesTab;
        WiiRetail.IsEnabled = first; WiiHomebrew.IsEnabled = first; WiiNAND.IsEnabled = first; GCRetail.IsEnabled = first;
        if (MainTabs.SelectedItem == BuildTab) RefreshBuildRequirements();
    }

    // ---------------------------------------------------------------- required source files

    private async void GameSourceButton_Click(object? s, RoutedEventArgs e)
    {
        var path = await PickFile("Specify your game file", gameFilter, SystemType == "dol" ? "boot.dol" : "game");
        if (path == null) { ResetGame(); return; }
        await LoadGameFile(path);
    }

    /// <summary>The body of GameSourceButton_Click after the file dialog.</summary>
    internal async Task LoadGameFile(string path)
    {
        CleanUp();
        GamePath = path;
        SetPath(GameSourceDirectory, path, true);
        FlagGameSpecified = true;
        GameHeader h;
        using (var fs = File.OpenRead(path)) h = GameHeader.Parse(fs);
        FlagWBFS = h.Wbfs; FlagNKIT = h.Nkit; FlagNASOS = h.Nasos;
        GameType = h.GameType;
        InternalGameName = h.InternalName;
        CucholixRepoID = h.RepoId;
        TitleIDHex = h.IdHex;
        string? error = null;
        if (SystemType == "wii" && GameType != GameHeader.WiiType) error = "This is not a Wii image. It will not be loaded.";
        if (SystemType == "gcn" && GameType != GameHeader.GcType) error = "This is not a GameCube image. It will not be loaded.";
        if (error == null && (FlagNKIT || FlagNASOS))
            error = "NKit and NASOS images are not supported on macOS yet (the Windows app converts them with NKit). " +
                    "Convert the game to a plain ISO or WBFS first, then select that file.";
        if (error != null)
        {
            ResetGame();
            await Dialogs.Info(this, error, "Error");
            return;
        }
        GameNameLabel.Text = InternalGameName;
        var title = StringUtil.RemoveSpecialChars(GameTdb.GetName(CucholixRepoID));
        PackedTitleLine1.Text = !string.IsNullOrEmpty(title) ? title : InternalGameName;
        if (SystemType == "dol")
        {
            TitleIDLabel.Text = TitleIDHex;
            TitleIDText = "BOOT";
        }
        else
        {
            TitleIDText = GameHeader.IdText(TitleIDHex);
            TitleIDLabel.Text = TitleIDText + " / " + TitleIDHex;
        }
        PackedTitleIDLine.Text = "00050002" + TitleIDHex;
    }

    void CleanUp()
    {
        try { foreach (var f in Directory.EnumerateFiles(TempSourcePath, "*.*", SearchOption.AllDirectories)) File.Delete(f); } catch { }
        IconPreviewBoxImg.Source = null;
        BannerPreviewBoxImg.Source = null;
        FlagIconSpecified = false;
        FlagBannerSpecified = false;
        IconSourceDirectory.Text = "Icon file has not been specified";
        BannerSourceDirectory.Text = "Banner file has not been specified";
    }

    string TempPng(string name) => Path.Combine(TempSourcePath, name);

    /// <summary>Convert the chosen image to the temp PNG and show it; false on failure.</summary>
    internal async Task<bool> ImportImage(string src, string tempName, Image preview)
    {
        try
        {
            ImageUtil.ToPng(src, TempPng(tempName));
            preview.Source = ImageUtil.Load(TempPng(tempName));
            return true;
        }
        catch (Exception ex)
        {
            await Dialogs.Info(this, "Could not read this image: " + ex.Message, "Error");
            return false;
        }
    }

    private async void IconSourceButton_Click(object? s, RoutedEventArgs e)
    {
        if (FlagRepo)
        {
            IconPreviewBoxImg.Source = null; BannerPreviewBoxImg.Source = null;
            FlagIconSpecified = false; FlagBannerSpecified = false; FlagRepo = false;
        }
        await Dialogs.Info(this, "Make sure your icon is 128x128 (1:1) to prevent distortion", "Icon Size Information");
        var f = await PickFile("Your image will be converted to PNG (Lossless) to be later processed.", ImageFilter, "iconTex");
        if (f != null && await ImportImage(f, "iconTex.png", IconPreviewBoxImg))
        {
            SetPath(IconSourceDirectory, f, true);
            FlagIconSpecified = true;
        }
        else
        {
            IconPreviewBoxImg.Source = null;
            SetPath(IconSourceDirectory, "Icon has not been specified", false);
            FlagIconSpecified = false;
        }
        FlagRepo = false;
    }

    private async void BannerSourceButton_Click(object? s, RoutedEventArgs e)
    {
        if (FlagRepo)
        {
            IconPreviewBoxImg.Source = null; BannerPreviewBoxImg.Source = null;
            FlagIconSpecified = false; FlagBannerSpecified = false; FlagRepo = false;
        }
        await Dialogs.Info(this, "Make sure your Banner is 1280x720 (16:9) to prevent distortion", "Banner Size Information");
        var f = await PickFile("Your image will be converted to PNG (Lossless) to be later processed.", ImageFilter, "bootTvTex");
        if (f != null && await ImportImage(f, "bootTvTex.png", BannerPreviewBoxImg))
        {
            SetPath(BannerSourceDirectory, f, true);
            FlagBannerSpecified = true;
        }
        else
        {
            BannerPreviewBoxImg.Source = null;
            SetPath(BannerSourceDirectory, "Banner has not been specified", false);
            FlagBannerSpecified = false;
        }
        FlagRepo = false;
    }

    static readonly HttpClient Http = new();

    static async Task<bool> RemoteFileExists(string url)
    {
        try
        {
            using var r = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    async Task DownloadFromRepo(string id)
    {
        var baseUrl = Settings.BannersRepository + SystemType + "/" + id + "/";
        foreach (var (file, img, label, text) in new[]
        {
            ("iconTex.png", IconPreviewBoxImg, IconSourceDirectory, "iconTex.png downloaded from Cucholix's Repo"),
            ("bootTvTex.png", BannerPreviewBoxImg, BannerSourceDirectory, "bootTvTex.png downloaded from Cucholix's Repo"),
        })
        {
            File.WriteAllBytes(TempPng(file), await Http.GetByteArrayAsync(baseUrl + file));
            img.Source = ImageUtil.Load(TempPng(file));
            SetPath(label, text, true);
        }
        FlagIconSpecified = true;
        FlagBannerSpecified = true;
        FlagRepo = true;
    }

    private async void RepoDownload_Click(object? s, RoutedEventArgs e)
    {
        if (CucholixRepoID == "")
        {
            await Dialogs.Info(this, "Please select your game before using this option", "No game specified");
            FlagRepo = false;
            return;
        }
        foreach (var id in GameTdb.GetAlternativeIds(CucholixRepoID))
        {
            if (!await RemoteFileExists(Settings.BannersRepository + SystemType + "/" + id + "/iconTex.png")) continue;
            try { await DownloadFromRepo(id); return; }
            catch (Exception ex) { await Dialogs.Info(this, "Download failed: " + ex.Message, "Error"); FlagRepo = false; return; }
        }
        FlagRepo = false;
        if (await Dialogs.Ask(this, "Cucholix's Repo does not have assets for your game. You will need to provide your own. Would you like to visit the GBAtemp request thread?", "Game not found on Repo"))
            Open("https://gbatemp.net/threads/483080/");
    }

    // ---------------------------------------------------------------- optional source files

    private async void GC2SourceButton_Click(object? s, RoutedEventArgs e)
    {
        var f = await PickFile("Specify your GameCube game's 2nd disc", "GameCube Disk 2 (*.gcm,*.iso)|*.gcm;*.iso", "disc2");
        if (f == null) { ResetGc2(); GC2SourceButton.IsEnabled = true; return; }
        using var fs = File.OpenRead(f);
        if (GameHeader.Parse(fs).GameType != GameHeader.GcType)
        {
            await Dialogs.Info(this, "This is not a GameCube image. It will not be loaded.", "Error");
            SetPath(GC2SourceDirectory, "2nd GameCube Disc Image has not been specified", false);
            FlagGC2Specified = false;
        }
        else
        {
            GC2Path = f;
            SetPath(GC2SourceDirectory, f, true);
            FlagGC2Specified = true;
        }
    }

    private async void DrcSourceButton_Click(object? s, RoutedEventArgs e)
    {
        await Dialogs.Info(this, "Make sure your GamePad Banner is 854x480 (16:9) to prevent distortion", "Banner Information");
        var f = await PickFile("Your image will be converted to PNG (Lossless) to be later processed.", ImageFilter, "bootDrcTex");
        if (f != null && await ImportImage(f, "bootDrcTex.png", DrcPreviewBoxImg))
        {
            SetPath(DrcSourceDirectory, f, true);
            FlagDrcSpecified = true;
            FlagRepo = false;
        }
        else
        {
            DrcPreviewBoxImg.Source = null;
            SetPath(DrcSourceDirectory, "GamePad Banner has not been specified", false);
            FlagDrcSpecified = false;
        }
    }

    private async void LogoSourceButton_Click(object? s, RoutedEventArgs e)
    {
        await Dialogs.Info(this, "Make sure your Logo is 170x42 to prevent distortion", "Logo Information");
        var f = await PickFile("Your image will be converted to PNG (Lossless) to be later processed.", ImageFilter, "bootLogoTex");
        if (f != null && await ImportImage(f, "bootLogoTex.png", LogoPreviewBoxImg))
        {
            SetPath(LogoSourceDirectory, f, true);
            FlagLogoSpecified = true;
            FlagRepo = false;
        }
        else
        {
            LogoPreviewBoxImg.Source = null;
            SetPath(LogoSourceDirectory, "Boot Logo has not been specified", false);
            FlagLogoSpecified = false;
        }
    }

    private async void BootSoundButton_Click(object? s, RoutedEventArgs e)
    {
        await Dialogs.Info(this, "Your sound file will be cut off if it's longer than 6 seconds to prevent the Wii U from not loading it. When the Wii U plays the boot sound, it will fade out once it's done loading the game (usually after about 5 seconds). You can not change this.", "Boot Sound Information");
        var f = await PickFile("Specify your boot sound", "Supported Sound Files (*.wav)|*.wav", "BootSound");
        if (f != null)
        {
            if (IsWav(f))
            {
                BootSoundPath = f;
                SetPath(BootSoundDirectory, f, true);
                BootSoundPreviewButton.IsEnabled = true;
                FlagBootSoundSpecified = true;
            }
            else
            {
                await Dialogs.Info(this, "This is not a valid WAV file. It will not be loaded. \nConsider converting it with something like Audacity.", "Not a WAV File");
                SetPath(BootSoundDirectory, "Boot Sound has not been specified", false);
                BootSoundPreviewButton.IsEnabled = false;
                FlagBootSoundSpecified = false;
            }
        }
        else if (!IsPlaying)
        {
            SetPath(BootSoundDirectory, "Boot Sound has not been specified", false);
            BootSoundPreviewButton.IsEnabled = false;
            FlagBootSoundSpecified = false;
        }
    }

    internal static bool IsWav(string path)
    {
        using var fs = File.OpenRead(path);
        var b = new byte[12];
        return fs.Read(b, 0, 12) == 12 && BitConverter.ToInt32(b, 0) == 1179011410 && BitConverter.ToInt32(b, 8) == 1163280727;
    }

    private void ToggleBootSoundLoop_CheckedChanged(object? s, RoutedEventArgs e) { /* read when building (BootLoop) */ }

    Process? _afplay;
    bool _stopRequested;
    bool IsPlaying => _afplay is { HasExited: false };

    void StopSound()
    {
        _stopRequested = true;
        try { if (_afplay is { HasExited: false }) _afplay.Kill(); } catch { }
    }

    private async void BootSoundPreviewButton_Click(object? s, RoutedEventArgs e)
    {
        string label() => (BootSoundPreviewButton.Content as TextBlock)?.Text ?? BootSoundPreviewButton.Content?.ToString() ?? "";
        if (label() == "Stop Sound")
        {
            StopSound();
            BootSoundPreviewButton.Content = ButtonText("Play Sound");
            return;
        }
        bool loop = ToggleBootSoundLoop.IsChecked == true;
        _stopRequested = false;
        if (loop) BootSoundPreviewButton.Content = ButtonText("Stop Sound");
        do
        {
            try
            {
                _afplay = Process.Start(new ProcessStartInfo("afplay") { ArgumentList = { BootSoundPath }, UseShellExecute = false });
                if (_afplay != null) await _afplay.WaitForExitAsync();
            }
            catch { break; }
        } while (loop && !_stopRequested);
        if (loop) BootSoundPreviewButton.Content = ButtonText("Play Sound");
    }

    // ---------------------------------------------------------------- GamePad / Meta

    private void EnablePackedLine2_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (EnablePackedLine2.IsChecked == true)
        {
            PackedTitleLine2.Text = "";
            PackedTitleLine2.Classes.Set("locked", false);
            PackedTitleLine2.IsReadOnly = false;
        }
        else
        {
            PackedTitleLine2.Text = "(Optional) Line 2";
            PackedTitleLine2.Classes.Set("locked", true);
            PackedTitleLine2.IsReadOnly = true;
        }
    }

    private void NoGamePadEmu_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready || NoGamePadEmu.IsChecked != true) return;
        DRCUSE = "1"; nfspatchflag = ""; Off(LRPatch);
    }

    private void CCEmu_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready || CCEmu.IsChecked != true) return;
        DRCUSE = "65537"; nfspatchflag = ""; LRPatch.IsEnabled = true;
    }

    private void HorWiiMote_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready || HorWiiMote.IsChecked != true) return;
        DRCUSE = "65537"; nfspatchflag = " -horizontal"; Off(LRPatch);
    }

    private void VerWiiMote_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready || VerWiiMote.IsChecked != true) return;
        DRCUSE = "65537"; nfspatchflag = " -wiimote"; Off(LRPatch);
    }

    private void ForceCC_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready || ForceCC.IsChecked != true) return;
        DRCUSE = "65537"; nfspatchflag = " -instantcc";
        DisableTrimming.IsChecked = false; DisableTrimming.IsEnabled = false;
        LRPatch.IsEnabled = true;
    }

    // ForceNoCC handler is named ForceWiiMote_CheckedChanged in the original designer
    private void ForceWiiMote_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready || ForceNoCC.IsChecked != true) return;
        DRCUSE = "65537"; nfspatchflag = " -nocc"; Off(LRPatch);
    }

    private void TutorialLink_LinkClicked(object? s, RoutedEventArgs e) => Open("http://www.google.com");

    // ---------------------------------------------------------------- advanced

    private void Force43NINTENDONT_CheckedChanged(object? s, RoutedEventArgs e) => NintendontFlags();
    private void ForceInterlacedNINTENDONT_CheckedChanged(object? s, RoutedEventArgs e) => NintendontFlags();

    void NintendontFlags()
    {
        if (!_ready) return;
        if (Force43NINTENDONT.IsChecked == true || ForceInterlacedNINTENDONT.IsChecked == true)
        {
            Off(CustomMainDol); Off(DisableNintendontAutoboot);
        }
        else
        {
            CustomMainDol.IsEnabled = true; DisableNintendontAutoboot.IsEnabled = true;
        }
    }

    private void CustomMainDol_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (CustomMainDol.IsChecked == true)
        {
            MainDolSourceButton.IsEnabled = true;
            Off(Force43NINTENDONT); Off(ForceInterlacedNINTENDONT); Off(DisableNintendontAutoboot);
        }
        else
        {
            MainDolSourceButton.IsEnabled = false;
            MainDolLabel.Text = "<- Specify custom main.dol file";
            Force43NINTENDONT.IsEnabled = true; ForceInterlacedNINTENDONT.IsEnabled = true; DisableNintendontAutoboot.IsEnabled = true;
            MainDolPath = "";
        }
    }

    private void NintendontAutoboot_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (DisableNintendontAutoboot.IsChecked == true)
        {
            Off(Force43NINTENDONT); Off(ForceInterlacedNINTENDONT); Off(CustomMainDol);
        }
        else
        {
            Force43NINTENDONT.IsEnabled = true; ForceInterlacedNINTENDONT.IsEnabled = true; CustomMainDol.IsEnabled = true;
        }
    }

    private async void MainDolSourceButton_Click(object? s, RoutedEventArgs e)
    {
        var f = await PickFile("Specify your replacement Nintendont Forwarder", "Nintendont Forwarder (*.dol)|*.dol", "main");
        MainDolPath = f ?? "";
        MainDolLabel.Text = f ?? "<- Specify custom main.dol file";
    }

    private void DisablePassthrough_CheckedChanged(object? s, RoutedEventArgs e) { /* read when building (Passthrough) */ }

    private void DisableGamePad_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (SystemType == "gcn" || SystemType == "dol") DRCUSE = DisableGamePad.IsChecked == true ? "1" : "65537";
    }

    private void C2WPatchFlag_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (C2WPatchFlag.IsChecked == true)
        {
            AncastKey.IsReadOnly = false;
            AncastKey.Classes.Set("locked", false);
            SaveAncastKeyButton.IsEnabled = true;
            Settings = AppSettings.Load();
            AncastKey.Text = Settings.AncastKey.ToUpperInvariant();
            if (KeyCheck.AncastKey(AncastKey.Text)) { AncastKey.IsReadOnly = true; AncastKey.Classes.Set("valid", true); }
        }
        else
        {
            AncastKey.Classes.Set("locked", true);
            AncastKey.Classes.Set("valid", false);
            AncastKey.IsReadOnly = true;
            SaveAncastKeyButton.IsEnabled = false;
        }
    }

    private async void WiiVMC_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready || WiiVMC.IsChecked != true) return;
        var t = await Dialogs.PickVideoMode(this);
        if (t is { } target) videoTarget = target; else WiiVMC.IsChecked = false;
    }

    private async void SaveAncastKeyButton_Click(object? s, RoutedEventArgs e)
    {
        AncastKey.Text = AncastKey.Text?.ToUpperInvariant();
        if (KeyCheck.AncastKey(AncastKey.Text ?? ""))
        {
            Settings = AppSettings.Load(); Settings.AncastKey = AncastKey.Text!; Settings.Save();
            await Dialogs.Info(this, "The Wii U Starbuck Ancast Key has been verified.", "Success");
            AncastKey.IsReadOnly = true;
            AncastKey.Classes.Set("valid", true);
        }
        else
            await Dialogs.Info(this, "The Wii U Starbuck Ancast Key you have provided is incorrect\n(MD5 Hash verification failed)", "Invalid Starbuck Ancast Key");
    }

    private void sign_c2w_patcher_link_LinkClicked(object? s, RoutedEventArgs e) => Open("https://github.com/FIX94/sign_c2w_patcher");

    private void DisableTrimming_CheckedChanged(object? s, RoutedEventArgs e)
    {
        if (!_ready) return;
        if (DisableTrimming.IsChecked == true || SystemType != "wii") { Off(WiiVMC); Off(Wiimmfi); }
        else { WiiVMC.IsEnabled = true; Wiimmfi.IsEnabled = true; }
    }


    // ---------------------------------------------------------------- keys

    async Task SaveKey(TextBox box, Func<string, bool> check, Action<AppSettings, string> store, string ok, string badText, string badTitle)
    {
        box.Text = box.Text?.ToUpperInvariant();
        if (check(box.Text ?? ""))
        {
            Settings = AppSettings.Load(); store(Settings, box.Text!); Settings.Save();
            await Dialogs.Info(this, ok, "Success");
            MainTabs.SelectedItem = AdvancedTab;
            MainTabs.SelectedItem = BuildTab;
        }
        else
            await Dialogs.Info(this, badText + "\n(MD5 Hash verification failed)", badTitle);
    }

    private Task SaveCommonKeyButton_ClickAsync() =>
        SaveKey(WiiUCommonKey, KeyCheck.CommonKey, (a, k) => a.WiiUCommonKey = k, "The Wii U Common Key has been verified.",
                "The Wii U Common Key you have provided is incorrect", "Invalid Wii U Common Key");

    private async void SaveCommonKeyButton_Click(object? s, RoutedEventArgs e) => await SaveCommonKeyButton_ClickAsync();

    private async void SaveTitleKeyButton_Click(object? s, RoutedEventArgs e) =>
        await SaveKey(TitleKey, KeyCheck.TitleKey, (a, k) => a.TitleKey = k, "The Title Key has been verified.",
                      "The Title Key you have provided is incorrect", "Invalid Title Key");

    // ---------------------------------------------------------------- build

    internal BuildOptions GatherOptions(string outputDir)
    {
        bool gcn = SystemType == "gcn";
        return new BuildOptions
        {
            System = SystemType,
            GamePath = GamePath,
            OutputDir = outputDir,
            Title1 = PackedTitleLine1.Text ?? "",
            Title2 = EnablePackedLine2.IsChecked == true ? PackedTitleLine2.Text : null,
            IconPath = TempPng("iconTex.png"),
            BannerPath = TempPng("bootTvTex.png"),
            DrcPath = FlagDrcSpecified ? TempPng("bootDrcTex.png") : null,
            LogoPath = FlagLogoSpecified ? TempPng("bootLogoTex.png") : null,
            CommonKey = WiiUCommonKey.Text ?? "",
            TitleKey = TitleKey.Text ?? "",
            DrcUse = DRCUSE,
            NfsPatch = nfspatchflag.Trim(),
            LrPatch = LRPatch.IsChecked == true,
            Passthrough = DisablePassthrough.IsChecked != true,
            Wiimmfi = Wiimmfi.IsChecked == true,
            ForceCC = ForceCC.IsChecked == true,
            DisableTrimming = DisableTrimming.IsChecked == true,
            Force43 = gcn ? Force43NINTENDONT.IsChecked == true : Force43NAND.IsChecked == true,
            ForceInterlaced = ForceInterlacedNINTENDONT.IsChecked == true,
            WiiwareTitle = SystemType == "wiiware" ? TitleIDText : "",
            BootSoundPath = FlagBootSoundSpecified ? BootSoundPath : null,
            BootLoop = ToggleBootSoundLoop.IsChecked == true,
            Game2Path = FlagGC2Specified ? GC2Path : null,
            CustomDolPath = CustomMainDol.IsChecked == true ? MainDolPath : null,
            DisableNintendontAutoboot = DisableNintendontAutoboot.IsChecked == true,
            C2wPatch = C2WPatchFlag.IsChecked == true,
            AncastKey = C2WPatchFlag.IsChecked == true ? AncastKey.Text ?? "" : "",
            VideoModeChange = WiiVMC.IsChecked == true,
            VideoTarget = videoTarget,
        };
    }

    async Task<bool> LowSpaceOk(long? gameSize = null)
    {
        try
        {
            long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(TempRootPath)) ?? "/").AvailableFreeSpace;
            long size = SystemType is "wii" or "gcn" ? gameSize ?? new FileInfo(GamePath).Length : 0;
            long need = SystemType switch { "wii" => size * 2 + 5000000000, "gcn" => size * 2 + 6000000000, _ => 6000000000 };
            if (free >= need) return true;
            var msg = SystemType is "wii" or "gcn"
                ? "Your hard drive may be low on space. The conversion process involves temporary files that can amount to more than double the size of your game. If you continue without clearing some hard drive space, the conversion may fail. Do you want to continue anyway?"
                : "Your hard drive may be low on space. Even for small programs, the conversion process can use almost 5 GB of temporary storage. If you continue without clearing some hard drive space, the conversion may fail. Do you want to continue anyway?";
            return await Dialogs.Ask(this, msg, "Check your hard drive space");
        }
        catch { return true; }
    }

    private async void TheBigOneTM_Click(object? s, RoutedEventArgs e) => await BuildClick();

    /// <summary>The body of the BUILD button (also driven by the GUI build self-test).</summary>
    internal async Task BuildClick()
    {
        MainTabs.IsEnabled = false;
        void Done() { MainTabs.IsEnabled = true; BuildStatus.Text = ""; BuildProgress.Value = 0; }
        try
        {
            if (!await LowSpaceOk()) { Done(); return; }
            var output = await ResolveOutput();
            if (output == null) { MainTabs.IsEnabled = true; return; }
            BuildProgress.Value = 2;
            var progress = new Progress<(int Percent, string Status)>(p => { BuildProgress.Value = p.Percent; BuildStatus.Text = p.Status; });
            _cts = new CancellationTokenSource();
            var o = GatherOptions(output);
            await BaseFiles.EnsureAsync(o.CommonKey, o.TitleKey, progress, _cts.Token);
            var outputPath = await BuildEngine.RunAsync(o, progress, _cts.Token);
            BuildProgress.Value = 100;
            BuildStatus.Text = "Conversion complete...";
            if (await Dialogs.Ask(this, "Conversion Complete! Your packed game can be found here:\n" + outputPath + "\n\n" +
                "Install your title using WUP Installer GX2 with signature patches enabled (CBHC, Haxchi, etc)." +
                "Make sure you have signature patches enabled when launching your title.\n\nOpen the output folder now?",
                o.Title1 + " Conversion Complete"))
                Open(outputPath);
            BuildStatus.Text = "";
            MainTabs.IsEnabled = true;
            MainTabs.SelectedItem = SourceFilesTab;
        }
        catch (Exception ex)
        {
            Done();
            await Dialogs.Info(this, "The conversion failed:\n" + ex.Message, "Error");
        }
    }

    /// <summary>The fixed output folder of the settings, or the folder picker. Null when the user cancels.</summary>
    async Task<string?> ResolveOutput()
    {
        Settings = AppSettings.Load();
        if (!string.IsNullOrEmpty(Settings.OutputPathFixed)) return Settings.OutputPathFixed;
        var start = string.IsNullOrEmpty(Settings.OutputPath) ? null : await StorageProvider.TryGetFolderFromPathAsync(Settings.OutputPath);
        var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Specify your output folder", SuggestedStartLocation = start });
        if (r.Count == 0)
        {
            await Dialogs.Info(this, "Output folder selection has been cancelled, conversion will not continue.", "Cancelled");
            return null;
        }
        var output = r[0].TryGetLocalPath() ?? "";
        Settings.OutputPath = output;
        Settings.Save();
        return output;
    }

    /// <summary>Label of the GamePad Emulation Mode currently selected, which a batch applies to every game.</summary>
    internal string GamePadModeName() =>
        new[] { NoGamePadEmu, CCEmu, VerWiiMote, HorWiiMote, ForceCC, ForceNoCC }
            .FirstOrDefault(r => r.IsChecked == true)?.Content?.ToString()?.TrimStart('*') ?? "default";

    /// <summary>Why a batch cannot start yet (the keys live on the Build tab), or null.</summary>
    internal string? BatchBlocker()
    {
        Settings = AppSettings.Load();
        if (!KeyCheck.CommonKey(Settings.WiiUCommonKey.ToUpperInvariant()) || !KeyCheck.TitleKey(Settings.TitleKey.ToUpperInvariant()))
            return "Save a valid Wii U Common Key and Title Key on the Build tab first, then try again.";
        if (CustomMainDol.IsChecked == true && Path.GetExtension(MainDolPath) != ".dol")
            return "The custom main.dol you ticked in the Advanced tab is not specified. Untick it or choose the file.";
        return null;
    }

    private async void BatchButton_Click(object? s, RoutedEventArgs e)
    {
        if (BatchBlocker() is { } why) { await Dialogs.Info(this, why, "Convert several games"); return; }
        await BatchClick();
    }

    static async Task<byte[]?> FetchRepoFile(string url)
    {
        using var r = await Http.GetAsync(url);
        return r.IsSuccessStatusCode ? await r.Content.ReadAsByteArrayAsync() : null;
    }

    /// <summary>
    /// The BATCH button: converts several Wii or GameCube games with the options and GamePad Emulation Mode set in the
    /// window and the icons and banners of Cucholix's repo. The parameters let the UI self-test replace the picker, the
    /// downloads and the build.
    /// </summary>
    internal async Task<List<BatchResult>?> BatchClick(IReadOnlyList<string>? files = null,
        Func<string, Task<byte[]?>>? fetch = null, Func<BuildOptions, Task<string>>? build = null)
    {
        MainTabs.IsEnabled = false;
        void Done() { MainTabs.IsEnabled = true; BuildStatus.Text = ""; BuildProgress.Value = 0; MainTabs.SelectedItem = SourceFilesTab; }
        try
        {
            files ??= await PickFiles("Specify the games to convert", gameFilter, true);
            if (files.Count == 0) { Done(); return null; }
            if (!await Dialogs.Ask(this, $"Convert {files.Count} game(s) with GamePad Emulation Mode \"{GamePadModeName()}\"?\n\n" +
                "Icons and banners come from Cucholix's repo. A game the repo has no images for is skipped.", "Convert several games"))
            { Done(); return null; }
            // progress and status live on the Build tab; this also loads the saved keys into its fields, which GatherOptions reads
            MainTabs.SelectedItem = BuildTab;
            if (!await LowSpaceOk(files.Max(f => new FileInfo(f).Length))) { Done(); return null; }
            var output = await ResolveOutput();
            if (output == null) { Done(); return null; }

            var label = "";
            var progress = new Progress<(int Percent, string Status)>(p => { BuildProgress.Value = p.Percent; BuildStatus.Text = label + p.Status; });
            _cts = new CancellationTokenSource();
            using var plan = await BatchConverter.PrepareAsync(SystemType, files, Settings.BannersRepository, fetch ?? FetchRepoFile,
                line => BuildStatus.Text = line, _cts.Token);
            // the Requirements card describes the whole batch: green when every game has its repo images
            SourceCheck.Text = $"Source Files: {plan.Ready} of {plan.Items.Count} games have icon and banner from Cucholix's repo";
            SourceCheck.Status = plan.AllHaveImages;
            MetaCheck.Status = plan.AllHaveTitles;
            var template = GatherOptions(output);
            var results = await BatchConverter.RunAsync(template, plan,
                build ?? (o => BuildEngine.RunAsync(o, progress, _cts.Token)),
                line => { label = line + ": "; BuildStatus.Text = line; BuildProgress.Value = 0; }, _cts.Token);
            Done();
            // many packages in one folder: just report, no "open the folder" prompt
            await Dialogs.Info(this, BatchConverter.Summary(results) + "\n\nPackages are in:\n" + output, "Batch conversion finished");
            return results;
        }
        catch (Exception ex)
        {
            Done();
            await Dialogs.Info(this, "The batch conversion failed:\n" + ex.Message, "Error");
            return null;
        }
    }
}
