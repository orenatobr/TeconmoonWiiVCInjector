using System.Diagnostics;
using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace WiiVCInjector;

/// <summary>Port of SDCardMenu (Form2): drives are the mounted volumes under /Volumes.</summary>
public partial class SDCardWindow : Window
{
    string _selectedDrive = "";
    bool _driveSpecified;
    static readonly HttpClient Http = new();

    public SDCardWindow()
    {
        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = "Nintendont SD Card Menu...";
        foreach (var i in Enumerable.Range(0, 14)) Opt(i).IsCheckedChanged += (_, _) => CheckForBoxes();
        ReloadDriveList();
        SpecifyDrive();
        MemcardBlocks.SelectedIndex = 0;
        VideoForceMode.SelectedIndex = 0;
        VideoTypeMode.SelectedIndex = 0;
        LanguageBox.SelectedIndex = 0;
        wiiUGamepadSlotBox.SelectedIndex = 0;
        Opt(0).IsChecked = true;
        // 720 fills the whole 720 pixel line; Auto gives 640 and thin black bars at the sides.
        VideoWidth.Value = 720;
        CheckForBoxes();
    }

    CheckBox Opt(int i) => this.FindControl<CheckBox>($"NintendontOptions_{i}")!;
    internal bool Checked(int i) => Opt(i).IsChecked == true;

    static Task Info(Window w, string text, string title) => Dialogs.Info(w, text, title);

    // ---- drives (mounted volumes instead of removable drive letters)
    void SpecifyDrive()
    {
        if (DriveBox.SelectedItem is ComboBoxItem { Content: string name })
        {
            _selectedDrive = "/Volumes/" + name[..name.LastIndexOf(" (", StringComparison.Ordinal)] + "/";
            _driveSpecified = true;
        }
        else { _selectedDrive = ""; _driveSpecified = false; }
    }

    void ReloadDriveList()
    {
        DriveBox.Items.Clear();
        if (!Directory.Exists("/Volumes")) return;
        foreach (var d in new DirectoryInfo("/Volumes").GetDirectories().Where(d => d.LinkTarget == null))
            DriveBox.Items.Add(new ComboBoxItem { Content = d.Name + " (" + d.FullName + ")" });
        if (DriveBox.Items.Count > 0) DriveBox.SelectedIndex = 0;
    }

    void CheckForBoxes()
    {
        bool mc = Checked(0);
        MemcardText.IsEnabled = MemcardBlocks.IsEnabled = MemcardMulti.IsEnabled = mc;
        bool auto = Checked(7);
        VideoWidth.IsEnabled = VideoWidthText.IsEnabled = !auto;
        WidthNumber.Text = auto ? "Auto" : ((int)VideoWidth.Value).ToString();
    }

    private void ReloadDrives_Click(object? s, RoutedEventArgs e) { ReloadDriveList(); SpecifyDrive(); }
    private void DriveBox_SelectedIndexChanged(object? s, SelectionChangedEventArgs e) => SpecifyDrive();

    private void VideoForceMode_SelectedIndexChanged(object? s, SelectionChangedEventArgs e)
    {
        if (VideoForceMode.SelectedIndex == 0 || VideoForceMode.SelectedIndex == 3)
        {
            VideoTypeMode.SelectedIndex = 0;
            VideoTypeMode.IsEnabled = false;
        }
        else
        {
            VideoTypeMode.SelectedIndex = 1;
            VideoTypeMode.IsEnabled = true;
        }
    }

    private void VideoWidth_Scroll(object? s, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e) {
        // fires while the XAML is still being populated, before WidthNumber exists
        if (WidthNumber != null) WidthNumber.Text = ((int)VideoWidth.Value).ToString();
    }

    private void VideoTypeMode_SelectedIndexChanged(object? s, SelectionChangedEventArgs e)
    {
        if (VideoForceMode.SelectedIndex != 0 && VideoForceMode.SelectedIndex != 3 && VideoTypeMode.SelectedIndex == 0)
            VideoTypeMode.SelectedIndex = 1;
    }

    // ---- Nintendont download
    /// <summary>Where the Nintendont files come from.</summary>
    internal enum NintendontSource { Upstream, Teconmoon, Unavailable }

    /// <summary>Picks the Nintendont source. The bundled Teconmoon build (PAL discs kept at 50 Hz) is the default; upstream is opt-in.</summary>
    /// <param name="upstream">Whether the user ticked the upstream box.</param>
    /// <param name="baseDir">Folder the app runs from (<see cref="AppContext.BaseDirectory"/>).</param>
    /// <returns>The source and, for the Teconmoon build, the folder holding its boot.dol, meta.xml and icon.png.</returns>
    internal static (NintendontSource Source, string? Dir) ChooseNintendontSource(bool upstream, string baseDir)
    {
        if (upstream) return (NintendontSource.Upstream, null);
        var dir = Path.Combine(baseDir, "Assets", "nintendont-teconmoon");
        return NintendontFiles.All(f => File.Exists(Path.Combine(dir, f))) ? (NintendontSource.Teconmoon, dir) : (NintendontSource.Unavailable, null);
    }

    /// <summary>The only files an install puts under apps/nintendont on the SD card.</summary>
    static readonly string[] NintendontFiles = { "boot.dol", "meta.xml", "icon.png" };

    /// <summary>Where this project keeps the Teconmoon Nintendont build (the same files the app bundles).</summary>
    internal const string TeconmoonRaw = "https://raw.githubusercontent.com/orenatobr/TeconmoonWiiVCInjector/main/macos/WiiVCInjector/Assets/nintendont-teconmoon/";

    /// <summary>Checks a downloaded file looks like what its name says (not an error page). Returns the problem, or null.</summary>
    internal static string? CheckTeconmoonFile(string name, byte[] data) => name switch
    {
        "boot.dol" => data.Length > 1_000_000 && data[0] == 0 && data[1] == 0 && data[2] == 1 && data[3] == 0 ? null : "boot.dol is not a DOL file",
        "meta.xml" => System.Text.Encoding.UTF8.GetString(data).Contains("<app") ? null : "meta.xml is not the app metadata",
        "icon.png" => data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 ? null : "icon.png is not a PNG",
        _ => "unknown file " + name,
    };

    /// <summary>Downloads the three files of the latest Teconmoon Nintendont into <paramref name="dest"/>. Returns why it failed, or null when all three arrived and look right.</summary>
    internal static async Task<string?> DownloadTeconmoon(string dest, HttpClient http, string baseUrl = TeconmoonRaw)
    {
        foreach (var f in NintendontFiles)
        {
            byte[] data;
            try { data = await http.GetByteArrayAsync(baseUrl + f); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return $"{f}: {ex.Message}"; }
            if (CheckTeconmoonFile(f, data) is { } bad) return bad;
            await File.WriteAllBytesAsync(Path.Combine(dest, f), data);
        }
        return null;
    }

    /// <summary>Runs a file operation on the drive and returns the error text, or null on success.</summary>
    /// <remarks>A drive that cannot be written (a system volume such as Recovery, or a volume the app may not access)
    /// has to produce a message: an exception that escapes an <c>async void</c> handler ends the whole process.</remarks>
    /// <param name="op">The file operation to run.</param>
    internal static string? TryFileOp(Action op)
    {
        try { op(); return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
    }

    /// <summary>Copies the staged Nintendont files to <c>apps/nintendont</c> on the drive, replacing what is there.</summary>
    /// <param name="stagedDir">Folder holding the files to install.</param>
    /// <param name="drive">Mount point of the SD card, ending with a slash.</param>
    internal static void InstallToDrive(string stagedDir, string drive)
    {
        var sd = Path.Combine(drive, "apps", "nintendont");
        if (Directory.Exists(sd)) Directory.Delete(sd, true);
        Directory.CreateDirectory(sd);
        foreach (var f in new DirectoryInfo(stagedDir).GetFiles()) f.CopyTo(Path.Combine(sd, f.Name), true);
    }

    const string DriveHint = "Pick another drive (system volumes such as Recovery are not writable). If this is your SD card, check that it is writable and that WiiVCInjector may access removable volumes.";

    private async void NintendontUpdate_Click(object? s, RoutedEventArgs e)
    {
        string downloadPath = Path.Combine(Path.GetTempPath(), "WiiVCInjector", "SOURCETEMP", "Download");
        string tempPath = Path.Combine(downloadPath, "apps", "nintendont");
        var (source, bundledDir) = ChooseNintendontSource(NintendontUpstream.IsChecked == true, AppContext.BaseDirectory);
        string? note = null;
        try
        {
            ActionStatus.Text = "Downloading...";
            if (Directory.Exists(downloadPath)) Directory.Delete(downloadPath, true);
            Directory.CreateDirectory(tempPath);
            if (source != NintendontSource.Upstream)
            {
                // The latest Teconmoon build from this project's GitHub. boot.dol, meta.xml and icon.png come together, so the version shown
                // in the Homebrew Channel matches the loader. Without a connection the build bundled with the app is installed.
                var latestDir = Path.Combine(Path.GetTempPath(), "WiiVCInjector", "SOURCETEMP", "Latest");
                if (Directory.Exists(latestDir)) Directory.Delete(latestDir, true);
                Directory.CreateDirectory(latestDir);
                var why = await DownloadTeconmoon(latestDir, Http);
                if (why == null)
                {
                    foreach (var f in NintendontFiles) File.Copy(Path.Combine(latestDir, f), Path.Combine(tempPath, f), true);
                    note = "This is the latest Teconmoon Nintendont from GitHub.";
                }
                else if (bundledDir != null)
                {
                    foreach (var f in NintendontFiles) File.Copy(Path.Combine(bundledDir, f), Path.Combine(tempPath, f), true);
                    note = $"Could not get the latest build from GitHub ({why}), so this is the Teconmoon build bundled with the app.";
                }
                else throw new InvalidOperationException($"the latest Teconmoon build could not be downloaded ({why}) and this install does not bundle one. Tick the upstream box to install the plain upstream build instead.");
            }
            else
            {
                var raw = "https://raw.githubusercontent.com/FIX94/Nintendont/master/";
                foreach (var (url, file) in new[] { ("loader/loader.dol", "boot.dol"), ("nintendont/meta.xml", "meta.xml"), ("nintendont/icon.png", "icon.png") })
                    await File.WriteAllBytesAsync(Path.Combine(tempPath, file), await Http.GetByteArrayAsync(raw + url));
            }
        }
        catch (Exception ex)
        {
            ActionStatus.Text = "";
            await Info(this, "Could not download Nintendont: " + ex.Message, "Error");
            return;
        }
        ActionStatus.Text = "";

        if (_driveSpecified)
        {
            int codes = 0, games = 0; var skippedCodes = new List<string>();
            if (TryFileOp(() => { InstallToDrive(tempPath, _selectedDrive); codes = CheatCodes.CopyLibraryTo(_selectedDrive, null, null, skippedCodes); games = GameSettings.CopyLibraryTo(_selectedDrive); }) is { } err)
            {
                await Info(this, $"Could not write to {_selectedDrive}: {err}\n{DriveHint}", "Error");
                return;
            }
            await Info(this, "Download complete."
                + (note != null ? "\n" + note : "")
                + (codes > 0 ? $"\n{codes} saved cheat file(s) copied to codes/ on the card." : "")
                + (skippedCodes.Count > 0 ? $"\nWidescreen cheats left out (Widescreen = Off in their game settings): {string.Join(", ", skippedCodes)}." : "")
                + (games > 0 ? $"\n{games} game settings file(s) copied to teconmoon/ on the card." : ""), "Success");
        }
        else if (await Dialogs.Ask(this, "SD Card not specified.\nDo you wish to save Nintendont somewhere else?", "Drive not specified"))
        {
            var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Nintendont zip file", DefaultExtension = "zip",
                SuggestedFileName = "Nintendont-" + DateTime.UtcNow.ToString("dd.MMM.yyyy") + ".zip",
                FileTypeChoices = new[] { new FilePickerFileType("Zip Files") { Patterns = new[] { "*.zip" } } },
            });
            if (f?.TryGetLocalPath() is not { } zip) return;
            if (TryFileOp(() => { if (File.Exists(zip)) File.Delete(zip); ZipFile.CreateFromDirectory(downloadPath, zip); }) is { } zipErr)
            {
                await Info(this, $"Could not save {zip}: {zipErr}", "Error");
                return;
            }
            await Info(this, "Download complete.", "Success");
        }
    }

    // ---- nincfg.bin
    [Flags]
    enum NinConfig : uint
    {
        Cheats = 1, MemcardEmu = 1 << 3, CheatPath = 1 << 4, ForceWide = 1 << 5, ForceProg = 1 << 6,
        RemLimit = 1 << 8, OsReport = 1 << 9, Log = 1 << 12, McMulti = 1 << 13, WiiUWide = 1 << 15,
        ArcadeMode = 1 << 16, CcRumble = 1 << 17, SkipIpl = 1 << 18, BbaEmu = 1 << 19,
    }

    const uint VidForce = 1 << 16, VidNone = 2 << 16, VidForceDf = 4 << 16;
    const uint VidPal50 = 1, VidPal60 = 2, VidNtsc = 4, VidMpal = 8, VidProg = 1 << 4, VidPatchPal50 = 1 << 5;

    /// <summary>Serialize the Nintendont config exactly as the original (big endian fields, 0x1D0 bytes).</summary>
    internal static byte[] BuildConfig(bool[] opt, bool memcardMulti, int forceMode, int typeMode, int language,
                                       int memcardBlocks, int videoWidth, int gamepadSlot)
    {
        uint config = 0, video = VidProg;
        void On(int i, NinConfig f) { if (opt[i]) config |= (uint)f; }
        On(0, NinConfig.MemcardEmu); On(1, NinConfig.Cheats); On(2, NinConfig.CheatPath); On(3, NinConfig.RemLimit);
        On(4, NinConfig.CcRumble); On(5, NinConfig.ArcadeMode); On(6, NinConfig.BbaEmu);
        // Force Progressive only converts a game's 60 Hz modes; a PAL50 one stays untouched unless the patch is on, while the
        // loader has already switched to 480p. That mismatch is a black screen on PAL discs, so Force Progressive implies it.
        if (opt[8] || opt[10]) video |= VidPatchPal50;
        if (opt[8])
        {
            if (typeMode == 0 || typeMode == 3) video |= VidPal50;
        }
        if (opt[9]) config |= (uint)(NinConfig.ForceWide | NinConfig.WiiUWide);
        On(10, NinConfig.ForceProg); On(11, NinConfig.SkipIpl); On(12, NinConfig.OsReport); On(13, NinConfig.Log);
        if (memcardMulti) config |= (uint)NinConfig.McMulti;
        video |= forceMode switch { 1 => VidForce, 2 => VidForce | VidForceDf, 3 => VidNone, _ => 0u };
        video |= typeMode switch { 1 => VidNtsc, 2 => VidMpal, 3 => VidPal50, 4 => VidPal60, _ => 0u };
        uint lang = language == 0 ? 0xFFFFFFFF : (uint)language - 1;
        sbyte scale = opt[7] ? (sbyte)0 : (sbyte)(videoWidth - 600);

        using var ms = new MemoryStream();
        void BE(uint v) { var b = BitConverter.GetBytes(v); Array.Reverse(b); ms.Write(b); }
        BE(0x01070CF6); BE(10); BE(config); BE(video); BE(lang);
        ms.Write(new byte[256]); ms.Write(new byte[256]);
        BE(4); BE(0);
        ms.WriteByte((byte)memcardBlocks); ms.WriteByte((byte)scale); ms.WriteByte(0); ms.WriteByte(0);
        BE((uint)gamepadSlot);
        return ms.ToArray();
    }

    private async void GenerateConfig_Click(object? s, RoutedEventArgs e)
    {
        var data = BuildConfig(Enumerable.Range(0, 14).Select(Checked).ToArray(), MemcardMulti.IsChecked == true,
                               VideoForceMode.SelectedIndex, VideoTypeMode.SelectedIndex, LanguageBox.SelectedIndex,
                               MemcardBlocks.SelectedIndex, (int)VideoWidth.Value, wiiUGamepadSlotBox.SelectedIndex);
        string savePath = _selectedDrive + "nincfg.bin";
        if (!_driveSpecified)
        {
            if (!await Dialogs.Ask(this, "SD card not specified.\nDo you wish to save the file somewhere else?", "Drive not specified")) return;
            var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save nincfg.bin", DefaultExtension = "bin", SuggestedFileName = "nincfg.bin",
                FileTypeChoices = new[] { new FilePickerFileType("nintendont config files") { Patterns = new[] { "*.bin" } } },
            });
            if (f?.TryGetLocalPath() is not { } p) return;
            savePath = p;
        }
        if (TryFileOp(() => File.WriteAllBytes(savePath, data)) is { } cfgErr)
        {
            await Info(this, $"Could not write {savePath}: {cfgErr}\n{DriveHint}", "Error");
            return;
        }
        await Info(this, "Config generation complete.", "Information");
    }

    // The original opened guiformat.exe (Windows only); Disk Utility is the macOS equivalent.
    private void Format_LinkClicked(object? s, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-a", "Disk Utility" }, UseShellExecute = false }); } catch { }
    }
}
