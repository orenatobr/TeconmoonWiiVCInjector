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

            // --- Nintendont audio experiment build (opt-in, upstream stays the default)
            var expDir = Path.Combine(tmp, "Assets", "nintendont-audio-exp");
            T("nintendont source: upstream when the box is off", SDCardWindow.ChooseNintendontSource(false, tmp) == (SDCardWindow.NintendontSource.Upstream, null));
            T("nintendont source: unavailable when ticked but not shipped", SDCardWindow.ChooseNintendontSource(true, tmp) == (SDCardWindow.NintendontSource.Unavailable, null));
            Directory.CreateDirectory(expDir);
            File.WriteAllBytes(Path.Combine(expDir, "boot.dol"), new byte[] { 1 });
            T("nintendont source: unavailable when only boot.dol is shipped", SDCardWindow.ChooseNintendontSource(true, tmp) == (SDCardWindow.NintendontSource.Unavailable, null));
            File.WriteAllBytes(Path.Combine(expDir, "meta.xml"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(expDir, "icon.png"), new byte[] { 1 });
            T("nintendont source: experiment when ticked and shipped", SDCardWindow.ChooseNintendontSource(true, tmp) == (SDCardWindow.NintendontSource.Experiment, expDir));
            T("nintendont source: the real build output ships the experiment files", SDCardWindow.ChooseNintendontSource(true, AppContext.BaseDirectory).Source == SDCardWindow.NintendontSource.Experiment, AppContext.BaseDirectory);
            T("nintendont source: upstream stays the default even when the experiment is shipped", SDCardWindow.ChooseNintendontSource(false, tmp) == (SDCardWindow.NintendontSource.Upstream, null));
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
