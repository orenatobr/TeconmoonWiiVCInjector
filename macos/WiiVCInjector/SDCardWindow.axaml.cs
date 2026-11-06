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
        Opt(7).IsChecked = true;
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
    private void NintendontOptions_SelectedIndexChanged(object? s, SelectionChangedEventArgs e) => CheckForBoxes();
    private void NintendontOptions_DoubleClick(object? s, Avalonia.Input.TappedEventArgs e) => CheckForBoxes();

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
    private async void NintendontUpdate_Click(object? s, RoutedEventArgs e)
    {
        string downloadPath = Path.Combine(Path.GetTempPath(), "WiiVCInjector", "SOURCETEMP", "Download");
        string tempPath = Path.Combine(downloadPath, "apps", "nintendont");
        try
        {
            ActionStatus.Text = "Downloading...";
            if (Directory.Exists(downloadPath)) Directory.Delete(downloadPath, true);
            Directory.CreateDirectory(tempPath);
            var raw = "https://raw.githubusercontent.com/FIX94/Nintendont/master/";
            foreach (var (url, file) in new[] { ("loader/loader.dol", "boot.dol"), ("nintendont/meta.xml", "meta.xml"), ("nintendont/icon.png", "icon.png") })
                await File.WriteAllBytesAsync(Path.Combine(tempPath, file), await Http.GetByteArrayAsync(raw + url));
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
            var sd = Path.Combine(_selectedDrive, "apps", "nintendont");
            if (Directory.Exists(sd)) Directory.Delete(sd, true);
            Directory.CreateDirectory(sd);
            foreach (var f in new DirectoryInfo(tempPath).GetFiles()) f.CopyTo(Path.Combine(sd, f.Name), true);
            await Info(this, "Download complete.", "Success");
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
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(downloadPath, zip);
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
        if (opt[8])
        {
            video |= VidPatchPal50;
            if (typeMode == 0 || typeMode == 3) video |= VidPal50;
        }
        if (opt[9]) config |= (uint)(NinConfig.ForceWide | NinConfig.WiiUWide);
        On(10, NinConfig.ForceProg); On(11, NinConfig.SkipIpl); On(12, NinConfig.OsReport); On(13, NinConfig.Log);
        if (memcardMulti) config |= (uint)NinConfig.McMulti;
        video |= forceMode switch { 1 => VidForce, 2 => VidForceDf, 3 => VidNone, _ => 0u };
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
        File.WriteAllBytes(savePath, data);
        await Info(this, "Config generation complete.", "Information");
    }

    // The original opened guiformat.exe (Windows only); Disk Utility is the macOS equivalent.
    private void Format_LinkClicked(object? s, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-a", "Disk Utility" }, UseShellExecute = false }); } catch { }
    }
}
