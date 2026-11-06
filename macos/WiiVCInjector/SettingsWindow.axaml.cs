using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace WiiVCInjector;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = "Application Settings";
        var s = AppSettings.Load();
        BannersRepository.Text = s.BannersRepository;
        OutputDir.Text = s.OutputPathFixed;
        Cancel.Click += (_, _) => Close();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        var s = AppSettings.Load();
        s.BannersRepository = BannersRepository.Text ?? "";
        s.OutputPathFixed = OutputDir.Text ?? "";
        s.Save();
        Close();
    }

    private async void OutputDirButton_Click(object? sender, RoutedEventArgs e)
    {
        var s = AppSettings.Load();
        var start = string.IsNullOrEmpty(s.OutputPath) ? null : await StorageProvider.TryGetFolderFromPathAsync(s.OutputPath);
        var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Specify your output folder", SuggestedStartLocation = start });
        if (r.Count > 0) OutputDir.Text = r[0].TryGetLocalPath();
    }
}
