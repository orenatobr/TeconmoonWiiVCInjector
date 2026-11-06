using Avalonia.Controls;
using Avalonia.Interactivity;

namespace WiiVCInjector;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var tab = Environment.GetEnvironmentVariable("WIIVC_TAB");
        if (int.TryParse(tab, out var ti)) MainTabs.SelectedIndex = ti;
        var snap = Environment.GetEnvironmentVariable("WIIVC_SNAPSHOT");
        if (!string.IsNullOrEmpty(snap))
            Opened += async (_, _) =>
            {
                await Task.Delay(500);
                var size = new Avalonia.PixelSize((int)Width, (int)Height);
                using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
                bmp.Render(this);
                bmp.Save(snap);
                Close();
            };
    }

    private void MainTabs_SelectedIndexChanged(object? s, SelectionChangedEventArgs e) { }
    private void BannerSourceButton_Click(object? s, RoutedEventArgs e) { }
    private void BootSoundButton_Click(object? s, RoutedEventArgs e) { }
    private void BootSoundPreviewButton_Click(object? s, RoutedEventArgs e) { }
    private void C2WPatchFlag_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void CCEmu_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void CustomMainDol_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void DisableGamePad_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void DisablePassthrough_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void DisableTrimming_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void DrcSourceButton_Click(object? s, RoutedEventArgs e) { }
    private void EnablePackedLine2_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void Force43NINTENDONT_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void ForceCC_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void ForceInterlacedNINTENDONT_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void ForceWiiMote_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void GC2SourceButton_Click(object? s, RoutedEventArgs e) { }
    private void GCRetail_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void GameSourceButton_Click(object? s, RoutedEventArgs e) { }
    private void HorWiiMote_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void IconSourceButton_Click(object? s, RoutedEventArgs e) { }
    private void LogoSourceButton_Click(object? s, RoutedEventArgs e) { }
    private void MainDolSourceButton_Click(object? s, RoutedEventArgs e) { }
    private void NintendontAutoboot_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void NoGamePadEmu_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void RepoDownload_Click(object? s, RoutedEventArgs e) { }
    private void SDCardStuff_Click(object? s, RoutedEventArgs e) { }
    private void SaveAncastKeyButton_Click(object? s, RoutedEventArgs e) { }
    private void SaveCommonKeyButton_Click(object? s, RoutedEventArgs e) { }
    private void SaveTitleKeyButton_Click(object? s, RoutedEventArgs e) { }
    private void SettingsButton_Click(object? s, RoutedEventArgs e) { }
    private void TheBigOneTM_Click(object? s, RoutedEventArgs e) { }
    private void ToggleBootSoundLoop_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void TutorialLink_LinkClicked(object? s, RoutedEventArgs e) { }
    private void VerWiiMote_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void WiiHomebrew_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void WiiNAND_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void WiiRetail_CheckedChanged(object? s, RoutedEventArgs e) { }
    private void sign_c2w_patcher_link_LinkClicked(object? s, RoutedEventArgs e) { }
}
