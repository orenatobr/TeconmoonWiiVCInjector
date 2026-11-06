namespace WiiVCInjector;

/// <summary>
/// Drives the real window through a whole homebrew injection (WIIVC_GUIBUILD=1): mode radio, game load,
/// image import, title, Build tab requirement check and the BUILD button handler. Keys come from
/// WIIVC_COMMON_KEY / WIIVC_TITLE_KEY and go to a temporary settings file only.
/// </summary>
public static class GuiBuildTest
{
    public static async Task<int> Run(MainWindow w)
    {
        var fail = 0;
        void T(string name, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}"); if (!ok) fail++; }
        var tmp = Path.Combine(Path.GetTempPath(), "wiivc-guibuild-" + Environment.ProcessId);
        Directory.CreateDirectory(tmp);
        var dialogs = new List<string>();
        Dialogs.Auto = (title, text) => { dialogs.Add(title); return "No"; };
        try
        {
            var dol = Path.Combine(tmp, "boot.dol");
            var b = new byte[0x300]; b[2] = 1; System.Text.Encoding.ASCII.GetBytes("HAXX").CopyTo(b, 0x2A0);
            File.WriteAllBytes(dol, b);
            var png = Path.Combine(tmp, "pic.png");
            using (var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(64, 64)))
                bmp.Save(png);

            var s = new AppSettings
            {
                WiiUCommonKey = Environment.GetEnvironmentVariable("WIIVC_COMMON_KEY") ?? "",
                TitleKey = Environment.GetEnvironmentVariable("WIIVC_TITLE_KEY") ?? "",
                OutputPathFixed = Path.Combine(tmp, "out"),
            };
            s.Save();

            w.WiiHomebrew.IsChecked = true;
            await w.LoadGameFile(dol);
            T("game loaded through the window", w.FlagGameSpecified && w.SystemType == "dol");
            T("icon imported", await w.ImportImage(png, "iconTex.png", w.IconPreviewBoxImg));
            w.FlagIconSpecified = true;
            T("banner imported", await w.ImportImage(png, "bootTvTex.png", w.BannerPreviewBoxImg));
            w.FlagBannerSpecified = true;
            w.PackedTitleLine1.Text = "GUI Build Test";
            w.MainTabs.SelectedItem = w.BuildTab;
            T("Build tab: requirements met, BUILD enabled", w.TheBigOneTM.IsEnabled);

            await w.BuildClick();
            var pkgs = Directory.Exists(s.OutputPathFixed) ? Directory.GetDirectories(s.OutputPathFixed) : Array.Empty<string>();
            var pkg = pkgs.FirstOrDefault();
            T("BUILD produced a package folder", pkg != null);
            if (pkg != null)
            {
                T("package has tmd/tik/cert", File.Exists(Path.Combine(pkg, "title.tmd")) && File.Exists(Path.Combine(pkg, "title.tik")) && File.Exists(Path.Combine(pkg, "title.cert")));
                T("package has 15 .app files", Directory.GetFiles(pkg, "*.app").Length == 15);
            }
            T("completion dialog was shown", dialogs.Any(d => d.Contains("Conversion Complete")));
            T("no error dialog", !dialogs.Contains("Error"));
        }
        catch (Exception ex) { Console.WriteLine("FAIL exception: " + ex); fail++; }
        Console.WriteLine(fail == 0 ? "ALL PASS" : "FAILURES: " + fail);
        return fail;
    }
}
