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
                && !w.Force43NINTENDONT.IsEnabled && !w.Force43NAND.IsEnabled && !w.C2WPatchFlag.IsEnabled && !w.GC2SourceButton.IsEnabled);

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
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(shippedDol)).Equals("ef1571fc7e7976c986ee7735f66e4ed3713e52b5ffa46949546eacbd82bb4191", StringComparison.OrdinalIgnoreCase));
            T("nintendont build: the shipped boot.dol carries the PAL guard", System.Text.Encoding.ASCII.GetString(shippedDol).Contains("PAL disc: Force Progressive ignored"));
            T("nintendont source: upstream still available when the build is shipped", SDCardWindow.ChooseNintendontSource(true, tmp) == (SDCardWindow.NintendontSource.Upstream, null));

            // --- Gecko and Action Replay codes (Dolphin game settings and wiki text)
            const string ini = "# GXXX01 - Test\n[Core]\nCPUThread = False\n[ActionReplay]\n$Infinite Rings [Someone]\n04123456 000003E7\n02123458 0000FFFF\n"
                + "$Conditional\n08123456 00000001\n[Gecko]\n*a comment\n$Widescreen\n04345894 3F400000\n[Gecko_Enabled]\n$Widescreen\n[OnFrame]\n$Not a cheat\n04000000 00000000\n";
            var parsed = CheatCodes.ParseIni(ini);
            T("cheats ini: the names of the Gecko and Action Replay sections, nothing from other sections",
                string.Join("|", parsed.Select(c => c.Name)) == "Infinite Rings [Someone]|Conditional|Widescreen", string.Join("|", parsed.Select(c => c.Name)));
            T("cheats ini: plain Action Replay writes are usable, the conditional one is not",
                parsed[0].Unusable == null && parsed[0].Lines.Count == 2 && parsed[1].Unusable != null && parsed[2].Unusable == null && parsed[2].Source == "Gecko");
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
                T("live: Wii Sports codes have names and code lines", Task.Run(() => CheatCodes.DownloadAsync("RSPE01", live)).GetAwaiter().GetResult() is { Count: > 10 } ws && ws.All(c => c.Name.Length > 0 && c.Lines.Count > 0));
            }
            var lib = Path.Combine(tmp, "lib"); var card = Path.Combine(tmp, "card");
            Directory.CreateDirectory(lib); Directory.CreateDirectory(card);
            File.WriteAllBytes(Path.Combine(lib, "GEAP8P.gct"), gct);
            File.WriteAllText(Path.Combine(lib, "notes.txt"), "x");
            T("cheats sd: the .gct files go to codes/ on the card, nothing else",
                CheatCodes.CopyLibraryTo(card, lib) == 1 && File.ReadAllBytes(Path.Combine(card, "codes", "GEAP8P.gct")).SequenceEqual(gct) && !File.Exists(Path.Combine(card, "codes", "notes.txt")));
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
