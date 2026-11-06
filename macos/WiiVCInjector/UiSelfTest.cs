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
