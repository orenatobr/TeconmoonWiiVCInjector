namespace WiiVCInjector;

/// <summary>Console self-test of the UI logic (WIIVC_UITEST=1). Prints PASS/FAIL lines, returns the exit code.</summary>
public static class UiSelfTest
{
    static int _fail;

    static void T(string name, bool ok, string detail = "")
    {
        if (!ok) _fail++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail != "" ? " (" + detail + ")" : "")}");
    }

    static bool Throws(Action a) { try { a(); return false; } catch { return true; } }

    static bool Throws<TEx>(Action a) where TEx : Exception { try { a(); return false; } catch (TEx) { return true; } catch { return false; } }

    /// <summary>A synthetic CISO: header, block size, one map byte per block, then the present blocks in order.</summary>
    static byte[] Ciso(uint blockSize, byte[] map, params byte[][] blocks)
    {
        var b = new byte[0x8000 + blocks.Sum(x => x.Length)];
        System.Text.Encoding.ASCII.GetBytes("CISO").CopyTo(b, 0);
        BitConverter.GetBytes(blockSize).CopyTo(b, 4);
        map.CopyTo(b, 8);
        var at = 0x8000;
        foreach (var blk in blocks) { blk.CopyTo(b, at); at += blk.Length; }
        return b;
    }

    static byte[] Padded(byte[] head, int size) { var b = new byte[size]; head.CopyTo(b, 0); return b; }

    static byte[] Iso(string id, string name, long magic)
    {
        var b = new byte[0x400];
        System.Text.Encoding.ASCII.GetBytes(id).CopyTo(b, 0);
        BitConverter.GetBytes(magic).CopyTo(b, 0x18);
        System.Text.Encoding.ASCII.GetBytes(name).CopyTo(b, 0x20);
        return b;
    }

    sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(answer(request));
    }

    const string arcBlock = """
$60 FPS
04001820 40000000 //Gecko Register 1820
04535c98 42BA0000 //ship move speed halved 1
04535c9c 42700000 //ship move speed halved 2
04535be8 3C80AE10 //world map camera rotate adjust
04535bf0 3D410518 //Ship camera rotate use 3D75C28F for faster rotation
C21A33DC 00000003 //Halve World Player Move
3E808000 62941820
C2940000 EFDEA024
C058000C 00000000 //end
C21BEA70 00000003 //Halve World Enemy Move
3E808000 62941820
C2940000 EC21A024
FFC00890 00000000 //end
C21A3BD4 00000006 //world map run sound fix
3C608000 6063180C //  using Gecko Register 180C
82830000 2C140000
41820010 38050001
3A800000 4800000C
7CA02B78 3A800001
92830000 00000000
C22135B4 00000003
3D008000 6108180C
3A600001 92680000
39000000 00000000 //end
C2209674 00000003 //Native 60FPS moments
38000002 3E408000
6252182C 90120000
38000001 00000000
C220968C 00000003
38000000 3E408000
6252182C 90120000
38000002 00000000 //end
C20C8328 00000006 //Main Animation and Auto move 30FPS revert
3C608000 60631824 //Uses 182C
80830008 2C040002
41820018 38800000
2C000000 40810008
38800001 90830008
2C000000 00000000
C22135B4 00000003
3D008000 6108180C
3A600001 92680000
39000000 00000000
C20C831C 00000003 //not auto moving
38600000 3D608000
616B1820 906B0008
60000000 00000000
C20C8328 00000006
3C608000 60631824
80830008 2C040002
41820018 38800000
2C000000 40810008
38800001 90830008
2C000000 00000000
C21AF244 00000003 //auto off world
3FC08000 63DE182C
38800000 909E0000
7C7E1B78 00000000 //end Auto Move
C20365A8 00000004 //Elevator event timer
3EA08000 62B51820
8295FFEC 22940001
7C04A214 9295FFEC
60000000 00000000 //end
C2039DD0 00000003 //Camera version 1. Optional
C05D08B0 EC221024
EC21082A EC420824
D05D08B0 00000000
C2039E74 00000003
C01D193C EE800024
EE94A02A EC00A024
60000000 00000000 //end
C203EA78 00000002 //No movement fix
38630001 7C1D1800
60000000 00000000 //end
C203DCE0 00000001 //Run Spaz Fix
EC031828 00000000 //end
C203DD44 00000001 //Run sound fix
2C000014 00000000 //  Change 14 to adjust timing. End
C203E570 00000003 //Movement Speed Setup
7C791B78 3E604000
8246260C 9266260C
60000000 00000000
C203EB18 00000002
38210110 9257F78C
60000000 00000000 
C203DEB8 00000005 //Fluid Movement for 60FPS
3E808044 6294260C
82940000 7C149000
40820010 12831824
1294A514 1021A024
1002082A 00000000
C203DEC8 00000002
40820008 1021A024
1002082A 00000000 //end
04442610 3F800000 //Animation
0444260C 3F800000 //Animation and Walk
04442614 00000000 //60FPS
04442618 00000001 //Battle and UI speed
2000182C 00000001 //Revert to 30FPS some times
22441BB0 00000002 //60FPS cutscenes
04442610 40000000 //undo to 30FPS when needed
0444260C 40000000 //undo
04442618 00000002 //undo
04442614 00000001 //undo
E0000000 80008000 //end
""";

    public static int Run(MainWindow w)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "wiivc-uitest-" + Environment.ProcessId);
        Directory.CreateDirectory(tmp);
        Dialogs.Auto = (_, _) => "Yes";
        try
        {
            // --- mode switches
            w.WiiHomebrew.IsChecked = true;
            T("homebrew: system/drc", w.SystemType == "dol" && w.DRCUSE == "65537");
            T("homebrew: gamepad radios cleared", w.NoGamePadEmu.IsChecked != true && !w.GamePadEmuLayout.IsEnabled && !w.LRPatch.IsEnabled);
            T("homebrew: enabled matrix", w.DisablePassthrough.IsEnabled && w.DisableGamePad.IsEnabled && w.C2WPatchFlag.IsEnabled
                && !w.DisableTrimming.IsEnabled && !w.WiiVMC.IsEnabled && !w.Wiimmfi.IsEnabled && !w.RepoDownload.IsEnabled
                && !w.Force43NINTENDONT.IsEnabled && !w.Force43NAND.IsEnabled && !w.GC2SourceButton.IsEnabled);

            w.GCRetail.IsChecked = true;
            T("gc: system/drc", w.SystemType == "gcn" && w.DRCUSE == "65537");
            T("gc: enabled matrix", w.GC2SourceButton.IsEnabled && w.Force43NINTENDONT.IsEnabled && w.ForceInterlacedNINTENDONT.IsEnabled
                && w.CustomMainDol.IsEnabled && w.DisableNintendontAutoboot.IsEnabled && w.DisableGamePad.IsEnabled
                && !w.DisablePassthrough.IsEnabled && !w.C2WPatchFlag.IsEnabled && !w.DisableTrimming.IsEnabled && w.RepoDownload.IsEnabled);
            T("gc: the cheat codes button tooltip says where the codes really come from", Avalonia.Controls.ToolTip.GetTip(w.CheatsDownload) is string tip && tip.Contains("compat") && tip.Contains("Gecko code database") && tip.Contains("SD card"));
            T("gc: cheat codes button is there with the requested text", w.CheatsDownload.IsEnabled
                && ((w.CheatsDownload.Content as Avalonia.Controls.StackPanel)?.Children.OfType<Avalonia.Controls.TextBlock>().FirstOrDefault()?.Text) == "Download from https://dolphin-emu.org/compat/");

            Dialogs.Auto = (title, _) => title.Contains("Title ID") ? "abcd" : "Yes";
            w.WiiNAND.IsChecked = true;
            T("nand: id accepted", w.SystemType == "wiiware" && w.FlagGameSpecified && w.PackedTitleIDLine.Text == "0005000241424344", w.PackedTitleIDLine.Text);
            T("nand: enabled matrix", w.Force43NAND.IsEnabled && !w.GameSourceButton.IsEnabled && w.NoGamePadEmu.IsChecked == true && w.DRCUSE == "1");
            Dialogs.Auto = (_, _) => null;
            w.WiiHomebrew.IsChecked = true;
            w.WiiNAND.IsChecked = true;
            T("nand: cancel leaves game unspecified", !w.FlagGameSpecified);

            Dialogs.Auto = (_, _) => "Yes";
            w.WiiRetail.IsChecked = true;
            T("wii: system/drc", w.SystemType == "wii" && w.DRCUSE == "1" && w.NoGamePadEmu.IsChecked == true && w.GamePadEmuLayout.IsEnabled);
            T("wii: enabled matrix", w.WiiVMC.IsEnabled && w.Wiimmfi.IsEnabled && w.DisableTrimming.IsEnabled && w.RepoDownload.IsEnabled
                && !w.Force43NINTENDONT.IsEnabled && !w.Force43NAND.IsEnabled && !w.GC2SourceButton.IsEnabled);
            T("wii: the cafe2wii patching option can be ticked (clock unlock for games that need CPU), and starts unticked",
                w.C2WPatchFlag.IsEnabled && w.C2WPatchFlag.IsChecked != true);
            w.C2WPatchFlag.IsChecked = true;
            w.WiiHomebrew.IsChecked = true;
            w.WiiRetail.IsChecked = true;
            T("wii: going back to Wii Retail unticks cafe2wii again", w.C2WPatchFlag.IsEnabled && w.C2WPatchFlag.IsChecked != true);

            // --- gamepad emulation modes
            w.ForceCC.IsChecked = true;
            T("forcecc: patch flag and trimming", w.nfspatchflag == " -instantcc" && !w.DisableTrimming.IsEnabled && w.LRPatch.IsEnabled);
            w.HorWiiMote.IsChecked = true;
            T("horizontal: patch flag", w.nfspatchflag == " -horizontal" && !w.LRPatch.IsEnabled && w.DRCUSE == "65537");
            w.VerWiiMote.IsChecked = true;
            T("vertical: patch flag", w.nfspatchflag == " -wiimote");
            w.ForceNoCC.IsChecked = true;
            T("force no cc: patch flag", w.nfspatchflag == " -nocc");
            w.NoGamePadEmu.IsChecked = true;
            T("no emu: flags reset", w.nfspatchflag == "" && w.DRCUSE == "1");

            // --- header parsing
            var wiiPath = Path.Combine(tmp, "game.iso");
            File.WriteAllBytes(wiiPath, Iso("RSPE01", "Test Game", GameHeader.WiiType));
            using (var fs = File.OpenRead(wiiPath))
            {
                var h = GameHeader.Parse(fs);
                T("header: id/name/type", h.RepoId == "RSPE01" && h.InternalName == "Test Game" && h.IdHex == "52535045" && h.GameType == GameHeader.WiiType);
            }
            w.LoadGameFile(wiiPath).GetAwaiter().GetResult();
            var tdb = GameTdb.GetName("RSPE01");
            T("gametdb resource lookup", tdb != null, tdb ?? "null");
            T("wii load: labels", w.GameNameLabel.Text == "Test Game" && w.TitleIDLabel.Text == "RSPE / 52535045"
                && w.PackedTitleIDLine.Text == "0005000252535045" && w.PackedTitleLine1.Text == (tdb is { Length: > 0 } ? StringUtil.RemoveSpecialChars(tdb) : "Test Game") && w.FlagGameSpecified,
                w.PackedTitleLine1.Text ?? "");
            var o = w.GatherOptions("/tmp/out");
            T("gather options", o.System == "wii" && o.GamePath == wiiPath && o.DrcUse == "1" && o.Passthrough && !o.C2wPatch && !o.VideoModeChange && o.Title2 == null);

            w.GCRetail.IsChecked = true;
            w.LoadGameFile(wiiPath).GetAwaiter().GetResult();
            T("gc rejects a wii image", !w.FlagGameSpecified);

            var wbfs = new byte[0x400];
            "WBFS"u8.CopyTo(wbfs);
            "SABE01"u8.CopyTo(wbfs.AsSpan(0x200));
            "Wbfs Name"u8.CopyTo(wbfs.AsSpan(0x220));
            BitConverter.GetBytes(GameHeader.WiiType).CopyTo(wbfs, 0x218);
            var hw = GameHeader.Parse(new MemoryStream(wbfs));
            T("wbfs header", hw.Wbfs && hw.RepoId == "SABE01" && hw.InternalName == "Wbfs Name" && hw.GameType == GameHeader.WiiType);
            var dol = new byte[0x400];
            new byte[] { 0, 0, 1, 0 }.CopyTo(dol, 0);
            "ABCD"u8.CopyTo(dol.AsSpan(0x2A0));
            var hd = GameHeader.Parse(new MemoryStream(dol));
            T("dol header", hd.IsDol && hd.IdHex == "41424344" && hd.InternalName == "N/A");
            var nk = Iso("GALE01", "Nk", GameHeader.GcType);
            "NKIT"u8.CopyTo(nk.AsSpan(0x200));
            T("nkit header", GameHeader.Parse(new MemoryStream(nk)).Nkit);
            var nkWii = Iso("RSPE01", "Nk", GameHeader.WiiType);
            "NKIT"u8.CopyTo(nkWii.AsSpan(0x200));
            var nkPath = Path.Combine(tmp, "game.nkit.iso");
            File.WriteAllBytes(nkPath, nkWii);
            w.WiiRetail.IsChecked = true;
            string? shown = null;
            Dialogs.Auto = (_, text) => { shown = text; return "Yes"; };
            w.LoadGameFile(nkPath).GetAwaiter().GetResult();
            Dialogs.Auto = (_, _) => "Yes";
            T("nkit image is rejected with a notice", !w.FlagGameSpecified && shown != null && shown.Contains("NKit"), shown ?? "no dialog");

            // --- C2W and Wii-VMC
            w.WiiRetail.IsChecked = true;
            Dialogs.Auto = (title, _) => title == "Video mode" ? "Pal60" : "Yes";
            w.WiiVMC.IsChecked = true;
            o = w.GatherOptions("/tmp/out");
            T("wii-vmc: target stored", o.VideoModeChange && o.VideoTarget == VideoTarget.Pal60);
            w.WiiVMC.IsChecked = false;
            Dialogs.Auto = (_, _) => null;
            w.WiiVMC.IsChecked = true;
            T("wii-vmc: cancel unticks", w.WiiVMC.IsChecked != true);
            w.WiiHomebrew.IsChecked = true;
            w.C2WPatchFlag.IsChecked = true;
            T("c2w: ancast field editable", w.SaveAncastKeyButton.IsEnabled && !w.AncastKey.IsReadOnly);
            T("c2w: gathered", w.GatherOptions("/tmp/out").C2wPatch);
            w.C2WPatchFlag.IsChecked = false;
            T("c2w: ancast field locked", !w.SaveAncastKeyButton.IsEnabled && w.AncastKey.IsReadOnly);

            // --- tabs
            w.MainTabs.SelectedItem = w.BuildTab;
            T("build tab: radios disabled, build blocked", !w.WiiRetail.IsEnabled && !w.GCRetail.IsEnabled && !w.TheBigOneTM.IsEnabled);
            w.MainTabs.SelectedItem = w.SourceFilesTab;
            T("first tab: radios enabled", w.WiiRetail.IsEnabled && w.WiiNAND.IsEnabled);

            // --- batch conversion: one GamePad Emulation Mode for all games, images only from the repo
            w.WiiRetail.IsChecked = true;
            T("batch button: enabled in Wii Retail", w.BatchButton.IsEnabled);
            w.WiiHomebrew.IsChecked = true;
            T("batch button: disabled in Wii Homebrew", !w.BatchButton.IsEnabled);
            w.GCRetail.IsChecked = true;
            T("batch button: enabled in GC Retail", w.BatchButton.IsEnabled);
            w.WiiRetail.IsChecked = true;
            T("batch: blocked without valid keys", w.BatchBlocker() is { } b && b.Contains("Build tab"));
            new AppSettings { OutputPathFixed = Path.Combine(tmp, "batch-out"), BannersRepository = "https://repo/" }.Save();
            w.CCEmu.IsChecked = true;
            w.LRPatch.IsChecked = true;
            T("batch: mode label", w.GamePadModeName() == "Classic Controller Emulation", w.GamePadModeName());
            var games = new List<string>();
            foreach (var (file, id, magic) in new[] { ("a.iso", "RSPE01", GameHeader.WiiType), ("b.iso", "RMGP01", GameHeader.WiiType),
                ("gc.iso", "GALE01", GameHeader.GcType), ("noimg.iso", "ZZZZ99", GameHeader.WiiType),
                ("boom.iso", "ABCD01", GameHeader.WiiType), ("evil.iso", "..//..", GameHeader.WiiType) })
            {
                var p = Path.Combine(tmp, file);
                File.WriteAllBytes(p, Iso(id, "Name " + id, magic));
                games.Add(p);
            }
            var urls = new List<string>();
            Task<byte[]?> Fetch(string url) { urls.Add(url); return Task.FromResult(url.Contains("/ZZZ") ? null : new byte[] { 1, 2, 3 }); }
            var built = new List<BuildOptions>();
            var iconBytes = new List<byte[]>();
            var tabsDuringBuild = new List<bool>();
            var requirements = new List<(bool Source, bool Meta, string Text)>();
            Task<string> Build(BuildOptions bo)
            {
                tabsDuringBuild.Add(w.MainTabs.SelectedItem == w.BuildTab);
                requirements.Add((w.SourceCheck.Status == true, w.MetaCheck.Status == true, w.SourceCheck.Text ?? ""));
                if (bo.GamePath.EndsWith("boom.iso")) throw new InvalidOperationException("tool crashed");
                built.Add(bo);
                iconBytes.Add(File.ReadAllBytes(bo.IconPath));
                return Task.FromResult("/pkg/" + Path.GetFileName(bo.GamePath));
            }
            var asked = new List<string>();
            Dialogs.Auto = (title, text) => { asked.Add(title + "|" + text); return "Yes"; };
            w.MainTabs.SelectedItem = w.SourceFilesTab;
            w.WiiUCommonKey.Text = "";   // as on a fresh start, before the Build tab was ever opened
            var res = w.BatchClick(games, Fetch, Build).GetAwaiter().GetResult();
            Dialogs.Auto = (_, _) => "Yes";
            T("batch: runs on the Build tab and returns to Source files", tabsDuringBuild.Count > 0 && tabsDuringBuild.All(x => x) && w.MainTabs.SelectedItem == w.SourceFilesTab);
            T("batch: keys reach the options even if the Build tab was never opened", built.All(b => b.CommonKey != ""));
            T("batch: final summary does not offer to open the folder", asked.Any(a => a.StartsWith("Batch conversion finished")) && !asked.Any(a => a.Contains("Open the output folder")));
            T("batch: requirements go red when a game has no repo images", requirements.Count > 0 && requirements.All(r => !r.Source && r.Text.Contains("3 of 6")),
                requirements.FirstOrDefault().Text);
            T("batch: one result per game", res is { Count: 6 }, res?.Count.ToString() ?? "null");
            T("batch: only the two good games built", built.Count == 2 && built.All(b => b.GamePath.EndsWith("a.iso") || b.GamePath.EndsWith("b.iso")));
            T("batch: every game gets the window's GamePad mode", built.All(b => b.System == "wii" && b.DrcUse == "65537" && b.NfsPatch == "" && b.LrPatch));
            T("batch: icon and banner come from the repo", iconBytes.All(b => b.SequenceEqual(new byte[] { 1, 2, 3 }))
                && urls.Contains("https://repo/wii/RSPE01/iconTex.png") && urls.Contains("https://repo/wii/RMGP01/bootTvTex.png"));
            T("batch: per game title, no second line", built.All(b => b.Title1 != "" && b.Title2 == null && b.DrcPath == null && b.LogoPath == null));
            T("batch: wrong system rejected", res!.Single(r => r.File == "gc.iso").Problem!.Contains("not a Wii"));
            T("batch: missing repo images skip the game", res!.Single(r => r.File == "noimg.iso").Problem!.Contains("no icon and banner"));
            T("batch: a failing build does not stop the rest", res!.Single(r => r.File == "boom.iso").Problem == "tool crashed" && res!.Single(r => r.File == "b.iso").Package != null);
            T("batch: invalid id never reaches the url", res!.Single(r => r.File == "evil.iso").Problem != null && !urls.Any(u => u.Contains("..")));
            T("batch: summary", BatchConverter.Summary(res!).StartsWith("2 of 6 games converted."));

            // a batch where every game has its repo images: the Requirements card is green
            requirements.Clear();
            var good = games.Where(g => g.EndsWith("a.iso") || g.EndsWith("b.iso")).ToList();
            w.MainTabs.SelectedItem = w.SourceFilesTab;
            w.BatchClick(good, Fetch, Build).GetAwaiter().GetResult();
            T("batch: requirements are green when every game has its repo images", requirements.Count == 2 && requirements.All(r => r.Source && r.Meta && r.Text.Contains("2 of 2")),
                requirements.FirstOrDefault().Text);
            w.MainTabs.SelectedItem = w.BuildTab;
            T("batch: Build tab goes back to the single-game wording", w.SourceCheck.Text == "Source Files are properly specified" && w.MetaCheck.Text == "Meta options are valid");
            w.MainTabs.SelectedItem = w.SourceFilesTab;

            // --- start state: maximized, never full screen; snapshot and test modes keep the XAML size.
            // The decision is tested, not the property read back: a window system without a window manager (the CI xvfb) never reports Maximized.
            T("start: maximized, not full screen", MainWindow.StartState(_ => null) == Avalonia.Controls.WindowState.Maximized);
            T("start: snapshot mode keeps the fixed size", MainWindow.StartState(k => k == "WIIVC_SNAPSHOT" ? "/tmp/x.png" : null) == Avalonia.Controls.WindowState.Normal);
            T("start: self-test mode keeps the fixed size", MainWindow.StartState(k => k == "WIIVC_UITEST" ? "1" : null) == Avalonia.Controls.WindowState.Normal);
            T("start: GUI build test keeps the fixed size", MainWindow.StartState(k => k == "WIIVC_GUIBUILD" ? "1" : null) == Avalonia.Controls.WindowState.Normal);

            // --- keys and settings
            T("key md5 rejects zeros", !KeyCheck.CommonKey(AppSettings.Zeros) && !KeyCheck.TitleKey(AppSettings.Zeros) && !KeyCheck.AncastKey(AppSettings.Zeros));
            var s = new AppSettings { WiiUCommonKey = "AA", TitleKey = "BB", AncastKey = "CC", BannersRepository = "https://x/", OutputPath = "/o", OutputPathFixed = "/f" };
            s.Save();
            var r = AppSettings.Load();
            T("settings roundtrip", r.WiiUCommonKey == "AA" && r.TitleKey == "BB" && r.AncastKey == "CC" && r.BannersRepository == "https://x/"
                && r.OutputPath == "/o" && r.OutputPathFixed == "/f", AppSettings.FilePath);

            // --- nincfg.bin
            var opt = new bool[14]; opt[0] = true; opt[7] = true;
            var cfg = SDCardWindow.BuildConfig(opt, false, 0, 0, 0, 2, 640, 0);
            T("nincfg layout", cfg.Length == 20 + 512 + 8 + 4 + 4 && cfg[0] == 0x01 && cfg[1] == 0x07 && cfg[2] == 0x0C && cfg[3] == 0xF6
                && cfg[11] == 8 /* memcard emu bit in config */ && cfg[15] == 0x10 /* VidProg */ && cfg[^5] == 0, cfg.Length.ToString());

            // "Force (Deflicker)" is FORCE | FORCE_DF in Nintendont (menu.c, Patch.c); DF alone is ignored by the kernel
            uint Video(byte[] c) => (uint)(c[12] << 24 | c[13] << 16 | c[14] << 8 | c[15]);
            T("nincfg: Force (Deflicker) sets the force bit and the deflicker bit", Video(SDCardWindow.BuildConfig(opt, false, 2, 4, 0, 2, 640, 0)) == (0x50000u | 0x10 | 2),
                Video(SDCardWindow.BuildConfig(opt, false, 2, 4, 0, 2, 640, 0)).ToString("X"));
            // Force Progressive leaves a PAL50 game untouched unless "Patch PAL50" is set (Patch.c), while the loader already
            // switched to 480p, which is a black screen on a PAL disc. So Force Progressive implies the patch.
            var prog = new bool[14]; prog[10] = true;
            T("nincfg: Force Progressive also sets Patch PAL50", (Video(SDCardWindow.BuildConfig(prog, false, 0, 0, 0, 2, 640, 0)) & 0x20) != 0);
            T("nincfg: Force Progressive is off by default (PAL 50 Hz stays native)", (BitConverter.ToUInt32(SDCardWindow.BuildConfig(opt, false, 0, 0, 0, 2, 640, 0).Skip(8).Take(4).Reverse().ToArray()) & (1u << 6)) == 0);
            T("nincfg: no Force Progressive, no Patch PAL50", (Video(SDCardWindow.BuildConfig(opt, false, 0, 0, 0, 2, 640, 0)) & 0x20) == 0);

            // --- writing to the SD card must report an error, not crash the app
            var staged = Path.Combine(tmp, "staged", "apps", "nintendont");
            Directory.CreateDirectory(staged);
            File.WriteAllBytes(Path.Combine(staged, "boot.dol"), new byte[] { 1 });
            var goodDrive = Path.Combine(tmp, "sd") + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(goodDrive);
            T("sd write: success returns no error", SDCardWindow.TryFileOp(() => SDCardWindow.InstallToDrive(staged, goodDrive)) == null
                && File.Exists(Path.Combine(goodDrive, "apps", "nintendont", "boot.dol")));
            var notADrive = Path.Combine(tmp, "not-a-drive");   // a file, so nothing can be created under it (also true when run as root)
            File.WriteAllBytes(notADrive, new byte[] { 1 });
            T("sd write: an unwritable drive returns the error instead of throwing",
                SDCardWindow.TryFileOp(() => SDCardWindow.InstallToDrive(staged, notADrive + Path.DirectorySeparatorChar)) is { Length: > 0 });
            T("sd write: other exceptions still surface", Throws(() => SDCardWindow.TryFileOp(() => throw new InvalidOperationException("bug"))));

            // --- GameCube images: CISO, RVZ and NKit are detected, and the game id is read from inside them
            var gcHead = Iso("GEAP", "Skies of Arcadia Legends", GameHeader.GcType);
            var plainIso = Path.Combine(tmp, "plain.iso");
            File.WriteAllBytes(plainIso, Padded(gcHead, 0x8000));
            var nkitHead = Padded(gcHead, 0x8000);
            System.Text.Encoding.ASCII.GetBytes("NKIT v01").CopyTo(nkitHead, 0x200);
            var nkitIso = Path.Combine(tmp, "game.nkit.iso");
            File.WriteAllBytes(nkitIso, nkitHead);
            var cisoPath = Path.Combine(tmp, "game.ciso");
            File.WriteAllBytes(cisoPath, Ciso(0x8000, new byte[] { 1, 0, 1 }, Padded(gcHead, 0x8000), Enumerable.Repeat((byte)0xAB, 0x8000).ToArray()));
            var rvzBytes = new byte[0x200];
            System.Text.Encoding.ASCII.GetBytes("RVZ\x01").CopyTo(rvzBytes, 0);
            gcHead.AsSpan(0, 0x80).CopyTo(rvzBytes.AsSpan(0x58));
            var rvzPath = Path.Combine(tmp, "game.rvz");
            File.WriteAllBytes(rvzPath, rvzBytes);
            var junkPath = Path.Combine(tmp, "junk.bin");
            File.WriteAllBytes(junkPath, new byte[0x300]);
            T("gc format: plain iso", GcImage.Detect(plainIso) == GcImageFormat.Iso);
            T("gc format: nkit.iso", GcImage.Detect(nkitIso) == GcImageFormat.Nkit);
            T("gc format: ciso", GcImage.Detect(cisoPath) == GcImageFormat.Ciso);
            T("gc format: rvz", GcImage.Detect(rvzPath) == GcImageFormat.Rvz);
            T("gc format: anything else is unknown", GcImage.Detect(junkPath) == GcImageFormat.Unknown);
            foreach (var (label, path) in new[] { ("iso", plainIso), ("nkit.iso", nkitIso), ("ciso", cisoPath), ("rvz", rvzPath) })
            {
                var hdr = GameHeader.ParseFile(path);
                T($"gc header: {label} gives the game id and name", hdr.RepoId == "GEAP" && hdr.InternalName == "Skies of Arcadia Legends" && hdr.GameType == GameHeader.GcType,
                    $"{hdr.RepoId} / {hdr.InternalName}");
                T($"gc game id: {label} is read from inside the image", Metadata.ReadGameId(path) == "GEAP");
            }
            T("gc header: nkit.iso is flagged", GameHeader.ParseFile(nkitIso).Nkit && !GameHeader.ParseFile(plainIso).Nkit);

            // --- CISO to ISO: present blocks are copied, absent ones are zeros, and the disc is padded to its size
            var cisoOut = Path.Combine(tmp, "from-ciso.iso");
            var reported = new List<int>();
            GcImage.CisoToIso(cisoPath, cisoOut, new SyncProgress<int>(reported.Add), default, discSize: 0x20000);
            var iso = File.ReadAllBytes(cisoOut);
            T("ciso: padded to the disc size", iso.Length == 0x20000, iso.Length.ToString());
            T("ciso: block 0 is the disc header", iso.AsSpan(0, gcHead.Length).SequenceEqual(gcHead));
            T("ciso: an absent block is zeros", iso.AsSpan(0x8000, 0x8000).ToArray().All(x => x == 0));
            T("ciso: a present block is copied", iso.AsSpan(0x10000, 0x8000).ToArray().All(x => x == 0xAB));
            T("ciso: the padding is zeros", iso.AsSpan(0x18000, 0x8000).ToArray().All(x => x == 0));
            T("ciso: progress ends at 100", reported.Count > 0 && reported[^1] == 100, string.Join(",", reported));
            var plainOut = Path.Combine(tmp, "from-iso.iso");
            GcImage.PrepareIso(plainIso, plainOut, null, default);
            T("prepare: a plain iso is copied as it is", File.ReadAllBytes(plainOut).SequenceEqual(File.ReadAllBytes(plainIso)));
            var badMagic = Path.Combine(tmp, "bad.ciso");
            File.WriteAllBytes(badMagic, new byte[0x9000]);
            T("ciso: a wrong magic is rejected", Throws<InvalidDataException>(() => GcImage.CisoToIso(badMagic, Path.Combine(tmp, "x1.iso"), null, default)));
            var truncated = Path.Combine(tmp, "truncated.ciso");
            File.WriteAllBytes(truncated, Ciso(0x8000, new byte[] { 1, 1 }, Padded(gcHead, 0x8000)));   // the map says 2 blocks, only 1 is there
            T("ciso: a truncated file is rejected", Throws<InvalidDataException>(() => GcImage.CisoToIso(truncated, Path.Combine(tmp, "x2.iso"), null, default)));
            T("ciso: no half written output is left behind", !File.Exists(Path.Combine(tmp, "x2.iso")) && !File.Exists(Path.Combine(tmp, "x2.iso") + ".part"));
            var copiedJunk = Path.Combine(tmp, "x3.iso");
            GcImage.PrepareIso(junkPath, copiedJunk, null, default);
            T("prepare: a file that is not CISO, RVZ or NKit is copied as before", File.ReadAllBytes(copiedJunk).SequenceEqual(File.ReadAllBytes(junkPath)));

            // --- RVZ and NKit go through the NKit tool; a stand-in script checks our side of the call
            var fakeDir = Path.Combine(tmp, "fake-nkit");
            Directory.CreateDirectory(fakeDir);
            var fakeNkit = Path.Combine(fakeDir, "nkit");
            File.WriteAllText(fakeNkit, "#!/bin/bash\n" +
                "echo \"$@\" > \"$(dirname \"$0\")/args.txt\"\n" +
                "while [ $# -gt 0 ]; do case \"$1\" in --output) out=\"$2\";; esac; shift; done\n" +
                "if [ -f \"$(dirname \"$0\")/fail\" ]; then echo 'ERROR: boom' >&2; exit 3; fi\n" +
                "if [ ! -f \"$(dirname \"$0\")/nothing\" ]; then printf 'FAKEISO' > \"$out/Some Game.iso\"; fi\n" +
                "echo done\n");
            File.SetUnixFileMode(fakeNkit, (UnixFileMode)0b111_101_101);
            Environment.SetEnvironmentVariable("WIIVC_NKIT", fakeNkit);
            try
            {
                foreach (var (label, src) in new[] { ("rvz", rvzPath), ("nkit.iso", nkitIso) })
                {
                    var expanded = Path.Combine(tmp, "nkit-" + label + ".iso");
                    GcImage.PrepareIso(src, expanded, null, default);
                    T($"nkit tool: {label} becomes the ISO the tool wrote", File.ReadAllText(expanded) == "FAKEISO");
                    var called = File.ReadAllText(Path.Combine(fakeDir, "args.txt"));
                    T($"nkit tool: {label} is expanded with the right arguments",
                        called.StartsWith("expand ") && called.Contains(src) && called.Contains("--output ") && called.Contains("--temp ") && called.Contains("--config "), called.Trim());
                }
                File.WriteAllText(Path.Combine(fakeDir, "nothing"), "");
                T("nkit tool: no ISO written is an error", Throws<InvalidOperationException>(() => GcImage.PrepareIso(rvzPath, Path.Combine(tmp, "n1.iso"), null, default)));
                File.Delete(Path.Combine(fakeDir, "nothing"));
                File.WriteAllText(Path.Combine(fakeDir, "fail"), "");
                T("nkit tool: a failing tool is an error", Throws<InvalidOperationException>(() => GcImage.PrepareIso(rvzPath, Path.Combine(tmp, "n2.iso"), null, default)));
                T("nkit tool: a failed run leaves no output", !File.Exists(Path.Combine(tmp, "n2.iso")));
                File.Delete(Path.Combine(fakeDir, "fail"));
            }
            finally { Environment.SetEnvironmentVariable("WIIVC_NKIT", null); }

            var zipSrc = Path.Combine(tmp, "nkit-zip-src");
            Directory.CreateDirectory(zipSrc);
            File.WriteAllText(Path.Combine(zipSrc, "nkit"), "not a real binary");
            File.WriteAllText(Path.Combine(zipSrc, "nkit.yaml"), "task:");
            File.WriteAllText(Path.Combine(zipSrc, "nkds"), "should not be installed");
            var fakeZip = Path.Combine(tmp, "nkit.zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(zipSrc, fakeZip);
            var zipHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(fakeZip)));
            var installDir = Path.Combine(tmp, "nkit-installed");
            T("nkit install: a zip with the wrong hash is refused", Throws<InvalidDataException>(() => NkitTool.Install(fakeZip, installDir, new string('0', 64))) && !Directory.Exists(installDir));
            NkitTool.Install(fakeZip, installDir, zipHash);
            T("nkit install: the executable and config are installed", File.Exists(Path.Combine(installDir, "nkit")) && File.Exists(Path.Combine(installDir, "nkit.yaml")));
            T("nkit install: only what is needed is unpacked", !File.Exists(Path.Combine(installDir, "nkds")));
            T("nkit install: the executable is marked executable", (File.GetUnixFileMode(Path.Combine(installDir, "nkit")) & UnixFileMode.UserExecute) != 0);
            T("nkit install: the verified hash is recorded", File.ReadAllText(Path.Combine(installDir, ".verified")) == zipHash);

            // --- Teconmoon Nintendont build (the default, upstream is opt-in)
            var tecDir = Path.Combine(tmp, "Assets", "nintendont-teconmoon");
            T("nintendont source: upstream when the box is ticked", SDCardWindow.ChooseNintendontSource(true, tmp) == (SDCardWindow.NintendontSource.Upstream, null));
            T("nintendont source: unavailable when the build is not shipped", SDCardWindow.ChooseNintendontSource(false, tmp) == (SDCardWindow.NintendontSource.Unavailable, null));
            Directory.CreateDirectory(tecDir);
            File.WriteAllBytes(Path.Combine(tecDir, "boot.dol"), new byte[] { 1 });
            T("nintendont source: unavailable when only boot.dol is shipped", SDCardWindow.ChooseNintendontSource(false, tmp) == (SDCardWindow.NintendontSource.Unavailable, null));
            File.WriteAllBytes(Path.Combine(tecDir, "meta.xml"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(tecDir, "icon.png"), new byte[] { 1 });
            T("nintendont source: the Teconmoon build when shipped", SDCardWindow.ChooseNintendontSource(false, tmp) == (SDCardWindow.NintendontSource.Teconmoon, tecDir));
            T("nintendont source: the real build output ships the Teconmoon files", SDCardWindow.ChooseNintendontSource(false, AppContext.BaseDirectory).Source == SDCardWindow.NintendontSource.Teconmoon, AppContext.BaseDirectory);
            var shippedDol = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "nintendont-teconmoon", "boot.dol"));
            T("nintendont build: the shipped boot.dol is the one nintendont-teconmoon/README.md documents",
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(shippedDol)).Equals("8f7ae2ca2d7a85de4a3ea19c0a456cbd4181c13b09d8c4296d800667f67c1512", StringComparison.OrdinalIgnoreCase));
            T("nintendont build: the shipped boot.dol applies a per game video width", System.Text.Encoding.ASCII.GetString(shippedDol).Contains("Game settings: Video width"));
            T("nintendont build: the shipped boot.dol carries the PAL guard", System.Text.Encoding.ASCII.GetString(shippedDol).Contains("PAL disc: Force Progressive ignored"));
            T("nintendont build: the shipped boot.dol keeps a PAL option the game settings ask for", System.Text.Encoding.ASCII.GetString(shippedDol).Contains("PAL disc: Force Progressive kept"));
            T("nintendont build: the shipped boot.dol applies a forced video mode from the game settings", System.Text.Encoding.ASCII.GetString(shippedDol).Contains("Game settings: Video mode"));
            T("nintendont build: the shipped boot.dol reads the per game settings file", System.Text.Encoding.ASCII.GetString(shippedDol).Contains("%s:/teconmoon/%s.ini"));
            // --- downloading the latest Teconmoon Nintendont from GitHub
            var tnDol = new byte[1_200_000]; tnDol[2] = 1;
            var tnMeta = System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><app version=\"1\"></app>");
            var tnPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
            var tnHtml = System.Text.Encoding.UTF8.GetBytes("<html>404: Not Found</html>");
            T("nintendont download: a real boot.dol, meta.xml and icon.png pass the check",
                SDCardWindow.CheckTeconmoonFile("boot.dol", tnDol) == null && SDCardWindow.CheckTeconmoonFile("meta.xml", tnMeta) == null && SDCardWindow.CheckTeconmoonFile("icon.png", tnPng) == null);
            T("nintendont download: an error page does not pass for any of them",
                SDCardWindow.CheckTeconmoonFile("boot.dol", tnHtml) != null && SDCardWindow.CheckTeconmoonFile("meta.xml", tnHtml) != null && SDCardWindow.CheckTeconmoonFile("icon.png", tnHtml) != null
                && SDCardWindow.CheckTeconmoonFile("other.bin", tnDol) != null);
            var tnDir = Path.Combine(tmp, "nin-dl"); Directory.CreateDirectory(tnDir);
            using (var tnGood = new HttpClient(new StubHandler(req => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new ByteArrayContent(req.RequestUri!.AbsoluteUri.EndsWith("boot.dol") ? tnDol : req.RequestUri!.AbsoluteUri.EndsWith("meta.xml") ? tnMeta : tnPng) })))
                T("nintendont download: the three files are fetched and written", Task.Run(() => SDCardWindow.DownloadTeconmoon(tnDir, tnGood)).GetAwaiter().GetResult() == null
                    && File.ReadAllBytes(Path.Combine(tnDir, "boot.dol")).Length == tnDol.Length && File.Exists(Path.Combine(tnDir, "meta.xml")) && File.Exists(Path.Combine(tnDir, "icon.png")));
            var tnDirBad = Path.Combine(tmp, "nin-dl-bad"); Directory.CreateDirectory(tnDirBad);
            using (var tnBad = new HttpClient(new StubHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(tnHtml) })))
                T("nintendont download: an error page instead of boot.dol fails and writes nothing",
                    Task.Run(() => SDCardWindow.DownloadTeconmoon(tnDirBad, tnBad)).GetAwaiter().GetResult() is { } tnWhy && tnWhy.Contains("boot.dol") && !File.Exists(Path.Combine(tnDirBad, "boot.dol")));
            using (var tnOff = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline"))))
                T("nintendont download: no connection gives a reason, not an exception", Task.Run(() => SDCardWindow.DownloadTeconmoon(tnDirBad, tnOff)).GetAwaiter().GetResult() is { } tnW2 && tnW2.Contains("offline"));
            T("nintendont source: upstream still available when the build is shipped", SDCardWindow.ChooseNintendontSource(true, tmp) == (SDCardWindow.NintendontSource.Upstream, null));

            // --- Gecko and Action Replay codes (Dolphin game settings and wiki text)
            const string ini = "# GXXX01 - Test\n[Core]\nCPUThread = False\n[ActionReplay]\n$Infinite Rings [Someone]\n04123456 000003E7\n02123458 0000FFFF\n"
                + "$Conditional\n08123456 00000001\n[Gecko]\n*a comment\n$Widescreen\n04345894 3F400000\n[Gecko_Enabled]\n$Widescreen\n[OnFrame]\n$Not a cheat\n04000000 00000000\n";
            var parsed = CheatCodes.ParseIni(ini);
            T("cheats ini: the names of the Gecko and Action Replay sections, nothing from other sections",
                string.Join("|", parsed.Select(c => c.Name)) == "Infinite Rings [Someone]|Conditional|Widescreen", string.Join("|", parsed.Select(c => c.Name)));
            T("cheats ini: plain Action Replay writes are usable, the conditional one is not",
                parsed[0].Unusable == null && parsed[0].Lines.Count == 2 && parsed[1].Unusable != null && parsed[2].Unusable == null && parsed[2].Source == "Gecko");
            var arAsm = CheatCodes.ParseIni("[ActionReplay]\n$Wide\n04007188 4800000C\nC20305DC 00000003\nC0228024 C0410008\nEC420072 D0410008\nC0410010 00000000\n042863B4 41D40000\n");
            var arBadAsm = CheatCodes.ParseIni("[ActionReplay]\n$Short\nC20305DC 00000003\nC0228024 C0410008\n");
            T("cheats ini: Gecko assembly blocks in an Action Replay section are usable, a cut off block is not",
                arAsm[0].Unusable == null && arAsm[0].Lines.Count == 6 && arBadAsm[0].Unusable != null);
            var billy = CheatCodes.ParseIni("[ActionReplay]\n$16:9 Widescreen [gamemasterplc]\n04007188 4800000C\nC20305DC 00000003\nC0228024 C0410008\nEC420072 D0410008\nC0410010 00000000\n"
                + "C20CCEE0 00000003\n38810020 C0228024\nC0440000 EC420824\nD0440000 00000000\nC210FC8C 00000003\n933D0060 C0228024\nC05D0048 EC420072\nD05D0048 00000000\n042C6328 3FE22222\n");
            T("cheats ini: the Billy Hatcher 16:9 code (Gecko assembly in [ActionReplay]) is usable", billy.Count == 1 && billy[0].Unusable == null && billy[0].Lines.Count == 14);
            const string wiki = "Skies of Arcadia Legends\nDeveloper(s) Overworks\nEnhancements\nHD Texture\n16:9 Widescreen Gecko Code\nThe following Gecko codes fix clipping issues. Disable the hack before using.\n"
                + "Normal HUD\nThis hack unstretches the HUD but will break cutscenes.\nNA:\n04345894 3F400000\n043463C0 3B190000\nC22AB718 00000003\n90010010 3C003B4D\n3C608034 900363C0\n60000000 00000000\n"
                + "PAL:\n04348E54 3F400000\nStretched HUD\nThe HUD along with other cutscene elements will be stretched.\nNA:\n04345894 3F400000\nPAL:\n04348E54 3F400000\n"
                + "60Hz PAL\nIn the PAL-Version the 60Hz mode cannot be activated at startup. Use this Gecko code to force the 60Hz mode.\n$60Hz\n04241FC8 38600000\n042FEE50 00000014\nConfiguration\nThis title does not need settings.\n";
            var pasted = CheatCodes.ParseText(wiki);
            T("cheats text: a whole wiki page gives the code blocks named by heading and region, the rest is ignored",
                string.Join("|", pasted.Select(c => c.Name + ":" + c.Lines.Count)) == "Normal HUD (NA):6|Normal HUD (PAL):1|Stretched HUD (NA):1|Stretched HUD (PAL):1|60Hz:2"
                && pasted.Select(c => c.Region).SequenceEqual(new string?[] { "NA", "PAL", "NA", "PAL", null }),
                string.Join("|", pasted.Select(c => c.Name + ":" + c.Lines.Count)));
            T("cheats text: the region of a disc comes from its id", CheatCodes.RegionOf("GEAP8P") == "PAL" && CheatCodes.RegionOf("GEAE8P") == "NA" && CheatCodes.RegionOf("GEAJ8P") == "JP" && CheatCodes.RegionOf("GEA") == null);
            T("cheats text: no code lines gives no cheats, bare codes get a default name",
                CheatCodes.ParseText("just words").Count == 0 && CheatCodes.ParseText("04345894 3F400000")[0].Name == "Pasted code");
            var gct = CheatCodes.BuildGct(new[] { parsed[0], parsed[1], parsed[2] });
            T("cheats gct: header, the usable codes only, end marker",
                gct.Length == 8 + 3 * 8 + 8 && Convert.ToHexString(gct[..8]) == "00D0C0DE00D0C0DE" && Convert.ToHexString(gct[8..16]) == "04123456000003E7"
                && Convert.ToHexString(gct[^8..]) == "F000000000000000", Convert.ToHexString(gct));
            T("cheats: ids Nintendont can use as a file name", CheatCodes.IsValidId("GEAP8P") && !CheatCodes.IsValidId("GEAP") && !CheatCodes.IsValidId("../..AB"));
            T("cheats: PAL discs are told apart by the region letter", CheatCodes.IsPal("GEAP8P") && !CheatCodes.IsPal("GEAE8P") && !CheatCodes.IsPal("GEAJ8P"));
            T("cheats: a 60 Hz code on a PAL disc is warned about, on an NTSC disc it is not",
                CheatCodes.Warning(pasted[4], "GEAP8P") != null && CheatCodes.Warning(pasted[4], "GEAE8P") == null && CheatCodes.Warning(pasted[0], "GEAP8P") == null);
            // --- a long code with // comments after the lines (Arc Rise Fantasia, 60 FPS) must be one cheat
            var arc = CheatCodes.ParseText(arcBlock);
            var arcLines = arcBlock.Split('\n').Count(l => System.Text.RegularExpressions.Regex.IsMatch(l.Trim(), @"^[0-9A-Fa-f]{8}\s+[0-9A-Fa-f]{8}"));
            T("cheats text: a long block with // comments after the lines is one cheat named from its $ line",
                arc.Count == 1 && arc[0].Name == "60 FPS" && arc[0].Lines.Count == arcLines && arcLines > 100, $"{arc.Count} cheat(s), {arc.FirstOrDefault()?.Lines.Count} of {arcLines} lines");
            T("cheats text: the first and last lines of that block are kept with their values", arc[0].Lines[0] == (0x04001820u, 0x40000000u) && arc[0].Lines[^1] == (0xE0000000u, 0x80008000u));
            T("cheats text: a comment-only line inside a block does not split it", CheatCodes.ParseText("$A\n04000000 00000001\n// note\n04000004 00000002\n") is { Count: 1 } one && one[0].Lines.Count == 2);
            T("cheats text: a name line after a code still starts a new cheat", CheatCodes.ParseText("$A\n04000000 00000001 //x\n$B\n04000004 00000002\n") is { Count: 2 } two && two[1].Name == "B");
            T("cheats: a game name in capitals is shown in Title Case and used for the wiki",
                CheatCodes.DisplayName(null, "ARC RISE FANTASIA", "RPJE7U") == "Arc Rise Fantasia" && CheatCodes.WikiUrl(CheatCodes.DisplayName(null, "ARC RISE FANTASIA", "RPJE7U")) == "https://wiki.dolphin-emu.org/index.php?title=Arc_Rise_Fantasia");
            T("cheats: the database name wins, a mixed case header name is kept, an empty one falls back to the id",
                CheatCodes.DisplayName("Skies of Arcadia Legends", "SKIES OF ARCADIA", "GEAP8P") == "Skies of Arcadia Legends" && CheatCodes.DisplayName("", "Wii Sports", "RSPE01") == "Wii Sports" && CheatCodes.DisplayName(null, "", "RSPE01") == "RSPE01");
            // --- the code handler tool (GeckoLoader): found by its files, run for real when it is installed (WIIVC_GECKOLOADER_DIR)
            var glFake = Path.Combine(tmp, "gl-fake"); Directory.CreateDirectory(Path.Combine(glFake, "venv", "bin"));
            T("code handler tool: not found without its script and its Python, found with both",
                GeckoLoaderTool.Find(glFake) == null && Task.Run(() => { File.WriteAllText(Path.Combine(glFake, "GeckoLoader.py"), "x"); File.WriteAllText(Path.Combine(glFake, "venv", "bin", "python"), "x"); return GeckoLoaderTool.Find(glFake); }).GetAwaiter().GetResult() is { } found
                && found.Script.EndsWith("GeckoLoader.py") && found.Python.EndsWith(Path.Combine("venv", "bin", "python")));
            T("code handler tool: the install script pins the release and installs what GeckoLoader imports",
                GeckoLoaderTool.SetupScript.Contains("__TAG__") && GeckoLoaderTool.SetupScript.Contains("chardet PyQt5 beautifulsoup4") && GeckoLoaderTool.SetupScript.Contains("setuptools") && GeckoLoaderTool.Tag == "v7.1.1");
            if (GeckoLoaderTool.Find() == null) Console.WriteLine("SKIP code handler tool: GeckoLoader is not installed here, the real run is skipped");
            else
            {
                byte[] GlDol(bool hook)
                {
                    var d = new byte[0x100 + 0xA0];
                    void U32(int o, uint v) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(o), v);
                    U32(0, 0x100); U32(0x48, 0x80003100); U32(0x90, 0xA0); U32(0xD8, 0x80010000); U32(0xDC, 0x1000); U32(0xE0, 0x80003100);
                    for (var o = 0x100; o < d.Length; o += 4) U32(o, 0x60000000);
                    if (hook) new byte[] { 0x7C, 0xE3, 0x3B, 0x78, 0x38, 0x87, 0x00, 0x34, 0x38, 0xA7, 0x00, 0x38, 0x38, 0xC7, 0x00, 0x4C }.CopyTo(d, 0x140);
                    U32(0x160, 0x4E800020);
                    return d;
                }
                var glDolFile = Path.Combine(tmp, "gl-main.dol"); File.WriteAllBytes(glDolFile, GlDol(true));
                var glGct = CheatCodes.BuildGct(CheatCodes.ParseText(arcBlock));
                var glWhy = GeckoLoaderTool.Patch(glDolFile, glGct, tmp, default);
                var glOut = File.ReadAllBytes(glDolFile);
                T("code handler tool: the real GeckoLoader adds the handler and the 60 FPS code to a DOL (new section, new entry point)",
                    glWhy == null && glOut.Length > GlDol(true).Length + glGct.Length && System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(glOut.AsSpan(0xE0)) != 0x80003100, glWhy ?? "");
                var glNoHook = Path.Combine(tmp, "gl-nohook.dol"); File.WriteAllBytes(glNoHook, GlDol(false));
                var glWhy2 = GeckoLoaderTool.Patch(glNoHook, glGct, tmp, default);
                T("code handler tool: a DOL it finds no hook in fails with the reason and is left untouched", glWhy2 != null && glWhy2.Contains("hook") && File.ReadAllBytes(glNoHook).SequenceEqual(GlDol(false)), glWhy2 ?? "");
            }
            T("cheats window: the paste box has a fixed height and a scroll bar", new CheatsWindow("RPJE7U", "Arc Rise Fantasia", true).Content is Avalonia.Controls.StackPanel sp
                && sp.Children.OfType<Avalonia.Controls.TextBox>().FirstOrDefault() is { } tb && tb.Height == 110 && Avalonia.Controls.ScrollViewer.GetVerticalScrollBarVisibility(tb) == Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
            var fpsCode = new Cheat { Name = "60 FPS" };
            T("cheats: a frame rate code on a Wii game warns about CPU and the cafe2wii patch, on a GameCube game or a code that is not about frame rate it does not",
                CheatCodes.Warning(fpsCode, "RPJE7U", true) is { } fw && fw.Contains("slow motion") && fw.Contains("cafe2wii") && fw.Contains("1.215 GHz")
                && CheatCodes.Warning(fpsCode, "GEAE8P", false) == null && CheatCodes.Warning(new Cheat { Name = "Widescreen" }, "RPJE7U", true) == null
                && CheatCodes.Warning(new Cheat { Name = "60Hz" }, "GEAP8P", true) is { } hz && hz.Contains("PAL disc"));
            {
                // a tiny class file: Utf8 "a", a Long (two pool slots), Utf8 "/code/rvlt.tik", then a trailer
                var cls = new List<byte> { 0xCA, 0xFE, 0xBA, 0xBE, 0, 0, 0, 52, 0, 5 };
                cls.AddRange(new byte[] { 1, 0, 1, (byte)'a' });
                cls.AddRange(new byte[] { 5, 0, 0, 0, 0, 0, 0, 0, 1 });
                cls.AddRange(new byte[] { 1, 0, 14 }); cls.AddRange(System.Text.Encoding.UTF8.GetBytes("/code/rvlt.tik"));
                cls.AddRange(new byte[] { 0xAA, 0xBB });
                var outc = NusPackerRules.ReplaceUtf8(cls.ToArray(), new[] { ("/code/rvlt.tik", "/code/dmcu.d.hex") });
                var text = System.Text.Encoding.UTF8.GetString(outc);
                T("nuspacker rules: a class file constant is replaced, the pool and the trailer stay intact",
                    text.Contains("/code/dmcu.d.hex") && !text.Contains("rvlt") && outc[9] == 5 && outc[^2] == 0xAA && outc[^1] == 0xBB && outc[(10 + 4 + 9 + 1)..(10 + 4 + 9 + 3)].SequenceEqual(new byte[] { 0, 16 }));
                T("nuspacker rules: a pattern that is not in the class is an error", Throws(() => NusPackerRules.ReplaceUtf8(cls.ToArray(), new[] { ("/nope", "/x") })));
            }
            T("cheats: wiki page address of a game", CheatCodes.WikiUrl("Skies of Arcadia Legends") == "https://wiki.dolphin-emu.org/index.php?title=Skies_of_Arcadia_Legends");
            var cheatUrls = new List<string>();
            using (var http = new HttpClient(new StubHandler(req =>
            {
                cheatUrls.Add(req.RequestUri!.AbsoluteUri.Replace(CheatCodes.GameSettingsBase, ""));
                return req.RequestUri!.AbsoluteUri.EndsWith("/GXX.ini")
                    ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(ini) }
                    : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            })))
            {
                var got = CheatCodes.DownloadAsync("GXXE01", http).GetAwaiter().GetResult();
                T("cheats download: asks for the 3 and 6 character settings files and the code database, keeps what exists",
                    string.Join(",", cheatUrls) == "GXX.ini,GXXE01.ini," + CheatCodes.CodesTxtBase + "GXXE01" && got.Count == 3, string.Join(",", cheatUrls));
                T("cheats download: a game Dolphin has no file for gives no cheats", CheatCodes.DownloadAsync("ZZZE01", http).GetAwaiter().GetResult().Count == 0);
            }
            T("cheats window: builds for a game", new CheatsWindow("GEAP8P", "Skies of Arcadia Legends").Title == "Cheat codes");
            // --- cheat codes written into a Wii game's main.dol: text section 0x100 bytes at 0x80003100, data section 0x80 bytes at 0x80004000
            var dolImg = new byte[0x100 + 0x100 + 0x80];
            void Be(int at, uint v) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(dolImg.AsSpan(at), v);
            Be(0, 0x100); Be(0x1C, 0x200); Be(0x48, 0x80003100); Be(0x64, 0x80004000); Be(0x90, 0x100); Be(0xAC, 0x80);
            Cheat Mk(string n, params (uint, uint)[] l) { var c = new Cheat { Name = n }; c.Lines.AddRange(l); return c; }
            var (okDol, noDol) = DolCheats.Apply(dolImg, new[]
            {
                Mk("word", (0x04003104, 0x3F400000u)),
                Mk("halves", (0x02003110, 0x0002ABCDu)),
                Mk("bytes", (0x00004004, 0x00000707u)),
                Mk("outside", (0x04200000, 1u)),
                Mk("half in half out", (0x04003108, 1u), (0x04200000, 2u)),
                Mk("conditional", (0x20003100, 5u), (0x04003108, 1u)),
            });
            T("dol cheats: writes inside the sections are applied, the rest is reported",
                string.Join(",", okDol) == "word,halves,bytes" && noDol.Count == 3 && noDol[0].StartsWith("outside") && noDol[2].StartsWith("conditional"), string.Join("|", noDol));
            T("dol cheats: the bytes land at the section offset (32 bit, 16 bit repeated, 8 bit repeated)",
                Convert.ToHexString(dolImg[0x104..0x108]) == "3F400000" && Convert.ToHexString(dolImg[0x110..0x116]) == "ABCDABCDABCD" && Convert.ToHexString(dolImg[0x204..0x20C]) == "0707070707070707"
                , Convert.ToHexString(dolImg[0x204..0x20C]));
            T("dol cheats: a cheat with a write outside the file is not written at all", dolImg[0x108] == 0 && dolImg[0x109] == 0);
            T("dol cheats: Action Replay plain writes pass through as the same words, a short file is refused",
                CheatCodes.ParseIni("[ActionReplay]\n$A\n04003100 00000001\n")[0].Unusable == null && DolCheats.Apply(new byte[10], new[] { Mk("x", (0x04003100, 1u)) }).Skipped.Count == 1);
            var oldSettings = Environment.GetEnvironmentVariable("WIIVC_SETTINGS");
            Environment.SetEnvironmentVariable("WIIVC_SETTINGS", Path.Combine(tmp, "appdata", "settings.json"));
            try
            {
                var wiiIso = Path.Combine(tmp, "wiicheats.iso");
                File.WriteAllBytes(wiiIso, Iso("RSPE01", "Wii Sports", GameHeader.WiiType));
                T("dol cheats: no saved list gives no cheats", BuildEngine.SavedWiiCheats(wiiIso).Count == 0);
                Directory.CreateDirectory(CheatCodes.LibraryDir);
                File.WriteAllText(CheatCodes.CodesTextPath("RSPE01"), CheatCodes.ToText(new[] { Mk("Widescreen", (0x04003104, 0x3F400000u)) }));
                var saved = BuildEngine.SavedWiiCheats(wiiIso);
                T("dol cheats: the saved list of the disc's 6 character id is read back", saved.Count == 1 && saved[0].Name == "Widescreen" && saved[0].Lines[0] == (0x04003104u, 0x3F400000u));
                T("cheats window: builds in Wii mode", new CheatsWindow("RSPE01", "Wii Sports", true).Title == "Cheat codes");
            }
            finally { Environment.SetEnvironmentVariable("WIIVC_SETTINGS", oldSettings); }
            const string dbTxt = "GXXE01\r\nSome Game \r\n\r\nVersion 1.0\r\n\r\n9 Outs/Side [Someone]\r\n044BE1E4 00000009\r\n0426868C 60000000\r\nNote: each side gets 27 outs\r\nContact: someone@example.com\r\n\r\n"
                + "Shadow Ball\r\n42000000 92000000\r\n042771A4 42C80000\r\nE0000000 80008000\r\nVideo: http://example.com/v\r\n\r\nInfinite Gold\r\n0430BB40 05F5E0FF";
            var fromDb = CheatCodes.ParseCodesTxt(dbTxt);
            T("cheats database: names from the first line of a block, notes dropped, header blocks skipped",
                string.Join("|", fromDb.Select(c => c.Name + ":" + c.Lines.Count)) == "9 Outs/Side [Someone]:2|Shadow Ball:3|Infinite Gold:1" && fromDb[0].Source == "Gecko code database",
                string.Join("|", fromDb.Select(c => c.Name + ":" + c.Lines.Count)));
            T("cheats database: a pointer code is kept for Nintendont but is not a plain write for a Wii main.dol", fromDb[1].Unusable == null && DolCheats.Writes(fromDb[1]) == null && DolCheats.Writes(fromDb[0]) != null);
            using (var db = new HttpClient(new StubHandler(req => req.RequestUri!.AbsoluteUri.StartsWith(CheatCodes.CodesTxtBase)
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(dbTxt) } : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound))))
                T("cheats download: the code database list is returned for a game Dolphin has no file for", CheatCodes.DownloadAsync("GXXE01", db).GetAwaiter().GetResult().Count == 3);
            using (var half = new HttpClient(new StubHandler(req => req.RequestUri!.AbsoluteUri.StartsWith(CheatCodes.CodesTxtBase)
                ? throw new HttpRequestException("offline") : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(ini) })))
                T("cheats download: one source offline still gives the codes of the other", CheatCodes.DownloadAsync("GXXE01", half).GetAwaiter().GetResult().Count > 0);
            using (var none = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline"))))
                T("cheats download: everything offline is an error, not an empty list", Throws<HttpRequestException>(() => CheatCodes.DownloadAsync("GXXE01", none).GetAwaiter().GetResult()));
            if (Environment.GetEnvironmentVariable("WIIVC_LIVE") == "1")
            {
                using var live = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var ntsc = Task.Run(() => CheatCodes.DownloadAsync("GEAE8P", live)).GetAwaiter().GetResult();
                var pal = Task.Run(() => CheatCodes.DownloadAsync("GEAP8P", live)).GetAwaiter().GetResult();
                T("live: Skies of Arcadia NTSC-U has downloadable codes, PAL has none", ntsc.Count > 10 && pal.Count == 0, $"{ntsc.Count} / {pal.Count}");
                var liveDir = Path.Combine(tmp, "nin-live"); Directory.CreateDirectory(liveDir);
                var liveWhy = Task.Run(() => SDCardWindow.DownloadTeconmoon(liveDir, live)).GetAwaiter().GetResult();
                var bundledDol = Path.Combine(AppContext.BaseDirectory, "Assets", "nintendont-teconmoon", "boot.dol");
                T("live: the Teconmoon Nintendont downloads from GitHub and is the bundled boot.dol", liveWhy == null && File.ReadAllBytes(Path.Combine(liveDir, "boot.dol")).SequenceEqual(File.ReadAllBytes(bundledDol)), liveWhy ?? "");
                T("live: Wii Sports codes have names and code lines", Task.Run(() => CheatCodes.DownloadAsync("RSPE01", live)).GetAwaiter().GetResult() is { Count: > 10 } ws && ws.All(c => c.Name.Length > 0 && c.Lines.Count > 0));
            }
            // --- per game settings (Dolphin GameINI format, read by the Teconmoon Nintendont)
            var gsDolphin = GameSettings.Parse("[Core]\r\nCPUThread = False\r\nProgressiveScan = True\r\n[Video_Settings]\r\nAspectRatio = 1\r\nwideScreenHack = False\r\nSafeTextureCacheColorSamples = 512\r\n");
            T("game settings: the known keys of a Dolphin GameINI are read, the others skipped",
                gsDolphin.Fullscreen == true && gsDolphin.WidescreenHack == false && gsDolphin.ProgressiveScan == true && gsDolphin.SkipIpl == null);
            T("game settings: AspectRatio 2 is 4:3, other values leave it", GameSettings.Parse("AspectRatio = 2").Fullscreen == false && GameSettings.Parse("AspectRatio = 3").Fullscreen == null && GameSettings.Parse("AspectRatio = 0").Fullscreen == null);
            T("game settings: keys and values are not case sensitive, comments and sections are skipped",
                GameSettings.Parse("# SkipIPL = 1\n[SkipIPL = 1]\nskipipl = YES").SkipIpl == true && GameSettings.Parse("; SkipIPL = 1\nSkipIPL = maybe").IsEmpty);
            var gsFull = new GameSettings { Fullscreen = true, WidescreenHack = false, ProgressiveScan = false, SkipIpl = true };
            T("game settings: the GameINI text has only the settings that are set, in their sections",
                gsFull.ToIni("GEAP8P", "Skies of Arcadia Legends") == "# GEAP8P - Skies of Arcadia Legends\n# Per game settings in Dolphin's GameINI format, read by the Teconmoon Nintendont. A line that is missing keeps the Nintendont setting.\n"
                    + "\n[Video_Settings]\nAspectRatio = 1\nwideScreenHack = False\n\n[Core]\nProgressiveScan = False\nSkipIPL = True\n"
                && !new GameSettings { Fullscreen = false }.ToIni("X", "Y").Contains("[Core]") && new GameSettings { Fullscreen = false }.ToIni("X", "Y").Contains("AspectRatio = 2"));
            T("game settings: what is written is read back the same", GameSettings.Parse(gsFull.ToIni("GEAP8P", "x")) is { Fullscreen: true, WidescreenHack: false, ProgressiveScan: false, SkipIpl: true }
                && GameSettings.Parse(new GameSettings { Fullscreen = false }.ToIni("GEAP8P", "x")) is { Fullscreen: false, WidescreenHack: null, ProgressiveScan: null, SkipIpl: null } && new GameSettings().IsEmpty);
            var gsVid = GameSettings.Parse("[Teconmoon]\nVideoScale = 110\nVideoOffset = -10\n");
            T("game settings: VideoScale and VideoOffset are read, written and read back, a value out of range is ignored",
                gsVid is { VideoScale: 110, VideoOffset: -10 } && GameSettings.Parse(gsVid.ToIni("GMSE01", "x")) is { VideoScale: 110, VideoOffset: -10 }
                && GameSettings.Parse("VideoScale = 30\nVideoOffset = 25").IsEmpty);
            var gsPal = GameSettings.Parse("[Core]\nProgressiveScan = True\n[Teconmoon]\nPatchPAL50 = False\n");
            T("game settings: Patch PAL50 is read, written in its own section and read back",
                gsPal.ProgressiveScan == true && gsPal.PatchPal50 == false
                && new GameSettings { PatchPal50 = true }.ToIni("X", "Y").Contains("[Teconmoon]\nPatchPAL50 = True\n") && !new GameSettings { PatchPal50 = true }.IsEmpty
                && GameSettings.Parse(new GameSettings { PatchPal50 = true, ProgressiveScan = true }.ToIni("X", "Y")) is { PatchPal50: true, ProgressiveScan: true });
            var gsVm = GameSettings.Parse("[Teconmoon]\nVideoMode = pal50\nDeflicker = False\n");
            T("game settings: VideoMode and Deflicker are read, written and read back",
                gsVm.VideoMode == "PAL50" && gsVm.Deflicker == false && GameSettings.Parse("VideoMode = PAL576").VideoMode == null
                && new GameSettings { VideoMode = "PAL50", Deflicker = false }.ToIni("X", "Y").Contains("[Teconmoon]\nVideoMode = PAL50\nDeflicker = False\n")
                && GameSettings.Parse(new GameSettings { VideoMode = "NTSC", PatchPal50 = true }.ToIni("X", "Y")) is { VideoMode: "NTSC", PatchPal50: true } && !new GameSettings { Deflicker = true }.IsEmpty);
            T("game settings: the file is named by the first 4 id characters", GameSettings.Id4("GEAP8P") == "GEAP" && GameSettings.Id4("GEA") == null && GameSettings.Id4("../AB") == null);
            var gsLib = Path.Combine(tmp, "gslib"); var gsCard = Path.Combine(tmp, "gscard");
            Directory.CreateDirectory(gsLib); Directory.CreateDirectory(gsCard);
            File.WriteAllText(Path.Combine(gsLib, "GEAP.ini"), gsFull.ToIni("GEAP8P", "x")); File.WriteAllText(Path.Combine(gsLib, "readme.txt"), "x");
            T("game settings sd: the .ini files go to teconmoon/ on the card, nothing else",
                GameSettings.CopyLibraryTo(gsCard, gsLib) == 1 && File.Exists(Path.Combine(gsCard, "teconmoon", "GEAP.ini")) && !File.Exists(Path.Combine(gsCard, "teconmoon", "readme.txt")));
            T("game settings sd: no library copies nothing and makes no folder", GameSettings.CopyLibraryTo(Path.Combine(tmp, "gscard2"), Path.Combine(tmp, "nogslib")) == 0 && !Directory.Exists(Path.Combine(tmp, "gscard2", "teconmoon")));
            T("game settings window: builds for a game", new GameSettingsWindow("GEAP8P", "Skies of Arcadia Legends").Title == "Game settings");
            T("game settings: the button is on the game tab", w.GameSettingsButton != null && (w.GameSettingsButton.Content as string) == "Game settings...");
            var lib = Path.Combine(tmp, "lib"); var card = Path.Combine(tmp, "card");
            Directory.CreateDirectory(lib); Directory.CreateDirectory(card);
            File.WriteAllBytes(Path.Combine(lib, "GEAP8P.gct"), gct);
            File.WriteAllText(Path.Combine(lib, "notes.txt"), "x");
            T("cheats sd: the .gct files go to codes/ on the card, nothing else",
                CheatCodes.CopyLibraryTo(card, lib) == 1 && File.ReadAllBytes(Path.Combine(card, "codes", "GEAP8P.gct")).SequenceEqual(gct) && !File.Exists(Path.Combine(card, "codes", "notes.txt")));
            var offSettings = Path.Combine(tmp, "offsettings"); Directory.CreateDirectory(offSettings);
            File.WriteAllBytes(Path.Combine(lib, "GMSE01.gct"), gct);
            File.WriteAllText(Path.Combine(lib, "GMSE01.codes.txt"), "$16:9 Widescreen\n04003104 3F400000\n$Infinite Lives\n04003108 00000063\n");
            File.WriteAllBytes(Path.Combine(card, "codes", "GMSE01.gct"), gct);
            File.WriteAllText(Path.Combine(offSettings, "GMSE.ini"), new GameSettings { Fullscreen = true, Widescreen = false }.ToIni("GMSE01", "Super Mario Sunshine"));
            T("game settings: Widescreen = False is written under [Teconmoon] and read back", GameSettings.Parse(File.ReadAllText(Path.Combine(offSettings, "GMSE.ini"))) is { Fullscreen: true, Widescreen: false } && !new GameSettings { Widescreen = false }.IsEmpty);
            T("cheats sd: a game with Widescreen = False keeps its other cheats on the card, without the widescreen one",
                CheatCodes.CopyLibraryTo(card, lib, offSettings) == 2 && File.Exists(Path.Combine(card, "codes", "GEAP8P.gct"))
                && File.ReadAllBytes(Path.Combine(card, "codes", "GMSE01.gct")) is { } kept && kept.SequenceEqual(CheatCodes.BuildGct(CheatCodes.ParseText("$Infinite Lives\n04003108 00000063\n"))));
            T("cheats sd: an empty library copies nothing and makes no folder", CheatCodes.CopyLibraryTo(Path.Combine(tmp, "card2"), Path.Combine(tmp, "nolib")) == 0 && !Directory.Exists(Path.Combine(tmp, "card2", "codes")));
        }
        catch (Exception ex)
        {
            T("no exception", false, ex.ToString());
        }
        finally
        {
            Dialogs.Auto = null;
            try { Directory.Delete(tmp, true); File.Delete(AppSettings.FilePath); } catch { }
        }
        Console.WriteLine(_fail == 0 ? "ALL PASS" : $"{_fail} FAILED");
        return _fail == 0 ? 0 : 1;
    }
}
