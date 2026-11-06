using System.Text;

namespace WiiVCInjector;

/// <summary>The WiiVC build pipeline (port of Form1.TheBigOneTM_Click and tcmoon-mac injector.py), no UI.</summary>
public static class BuildEngine
{
    public static event Action<string>? Log;
    /// <summary>Test hook: called with the final ISO path before NFS conversion.</summary>
    internal static Action<string>? IsoReady;
    internal static void Emit(string line) => Log?.Invoke(line);

    /// <summary>Runs the whole build and returns the output package folder.</summary>
    public static Task<string> RunAsync(BuildOptions o, IProgress<(int Percent, string Status)> progress, CancellationToken ct = default) =>
        Task.Run(() => Run(o, progress, ct), ct);

    static string Sanitize(string s) =>
        string.Join("_", s.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');

    static string Run(BuildOptions o, IProgress<(int Percent, string Status)> progress, CancellationToken ct)
    {
        void P(int v, string s) { Emit(s); progress.Report((v, s)); }
        void Exec(string exe, IEnumerable<string> args, string? cwd = null) { ct.ThrowIfCancellationRequested(); Tools.Run(exe, args, cwd, ct); }

        if (o.System is not ("wii" or "gcn" or "dol" or "wiiware")) throw new ArgumentException("unknown system: " + o.System);
        if (o.System != "wiiware" && !File.Exists(o.GamePath)) throw new FileNotFoundException("game file not found", o.GamePath);

        P(2, "Preparing base files...");
        var downloads = BaseFiles.EnsureAsync(o.CommonKey, o.TitleKey,
            new SyncProgress<(int Percent, string Status)>(p => progress.Report((2 + p.Percent * 48 / 100, p.Status))), ct).GetAwaiter().GetResult();

        var work = Path.Combine(Path.GetTempPath(), "WiiVCInjector-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            P(50, "Copying base files to temporary build directory...");
            var buildDir = Path.Combine(work, "BUILDDIR");
            Tools.CopyDir(Path.Combine(downloads, BaseFiles.Rhf), buildDir);
            if (o.C2wPatch)
            {
                Tools.CopyDir(Path.Combine(downloads, "0005001010004000"), buildDir);
                Tools.CopyDir(Path.Combine(downloads, "0005001010004001"), buildDir);
                var c2w = Path.Combine(buildDir, "code", "c2w.img");
                File.WriteAllBytes(c2w, C2wPatcher.Patch(File.ReadAllBytes(c2w), o.AncastKey, false, Emit));
            }

            P(52, "Generating app.xml and meta.xml");
            var gameId = o.System == "wiiware" ? o.WiiwareTitle : Metadata.ReadGameId(o.GamePath);
            var ids = o.System == "wiiware" ? Metadata.WiiwareIds(gameId) : Metadata.Ids(gameId, o.System);
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(buildDir, "code", "app.xml"), Metadata.AppXml(ids), utf8);
            File.WriteAllText(Path.Combine(buildDir, "meta", "meta.xml"), Metadata.MetaXml(ids, o.Title1, o.Title2, o.DrcUse), utf8);

            P(53, "Converting all image sources to expected TGA specification...");
            var meta = Path.Combine(buildDir, "meta");
            Media.WriteTga(o.IconPath, Path.Combine(meta, "iconTex.tga"), 128, 128, 32);
            Media.WriteTga(o.BannerPath, Path.Combine(meta, "bootTvTex.tga"), 1280, 720, 24);
            Media.WriteTga(string.IsNullOrEmpty(o.DrcPath) ? o.BannerPath : o.DrcPath, Path.Combine(meta, "bootDrcTex.tga"), 854, 480, 24);
            if (!string.IsNullOrEmpty(o.LogoPath)) Media.WriteTga(o.LogoPath, Path.Combine(meta, "bootLogoTex.tga"), 170, 42, 32);

            if (!string.IsNullOrEmpty(o.BootSoundPath))
            {
                P(55, "Converting user-provided sound to btsnd format...");
                Media.WriteBtsnd(o.BootSoundPath, Path.Combine(meta, "bootSound.btsnd"), noLoop: !o.BootLoop);
            }

            P(60, "Processing game for NFS Conversion...");
            var wit = Tools.Native("wit");
            var iso = PrepareIso(o, work, wit, Exec);
            IsoReady?.Invoke(iso);

            var tik = Path.Combine(work, "TIKTEMP");
            Exec(wit, ["extract", iso, "--psel", "data", "--psel", "-update", "--files", "+tmd.bin", "--files", "+ticket.bin", "--dest", tik, "-vv1"]);
            File.Copy(Path.Combine(tik, "tmd.bin"), Path.Combine(buildDir, "code", "rvlt.tmd"));
            File.Copy(Path.Combine(tik, "ticket.bin"), Path.Combine(buildDir, "code", "rvlt.tik"));

            P(70, "Converting processed game to NFS format...");
            var patch = o.NfsPatch;
            if (o.System == "wii" && o.ForceCC && string.IsNullOrWhiteSpace(patch)) patch = "-instantcc";
            var nfs = Tools.NfsCommand();
            // The nfs2iso2nfs sources do not embed the Wii common key (the original Windows binary does) and look for
            // wii_common_key.bin in the working directory. Write it for the run and remove it so NUSPacker does not pack it.
            var wiiKeyFile = Path.Combine(buildDir, "content", "wii_common_key.bin");
            File.WriteAllBytes(wiiKeyFile, WiiCommonKey);
            try { Exec(nfs[0], [.. nfs.Skip(1), .. Metadata.NfsArgs(o.System, iso, patch, o.LrPatch, o.Passthrough)], Path.Combine(buildDir, "content")); }
            finally { File.Delete(wiiKeyFile); }
            if (iso.StartsWith(work)) File.Delete(iso);
            var nfsFile = Path.Combine(buildDir, "content", "hif_000000.nfs");
            if (!File.Exists(nfsFile) || new FileInfo(nfsFile).Length == 0)
                throw new InvalidOperationException("nfs2iso2nfs did not produce content/hif_000000.nfs, so the package would not contain the game.");

            P(85, "Encrypting contents into installable WUP Package...");
            var outPath = Path.Combine(o.OutputDir, $"{Sanitize(o.Title1)} WUP-N-{ids.Text}_{ids.Packed}");
            Directory.CreateDirectory(outPath);
            Exec(Tools.Java(), ["-jar", Tools.Jar("NUSPacker"), "-in", "BUILDDIR", "-out", outPath, "-encryptKeyWith", o.CommonKey], work);
            foreach (var f in new[] { "title.tmd", "title.tik", "title.cert" })
                if (!File.Exists(Path.Combine(outPath, f))) throw new InvalidOperationException($"NUSPacker did not produce {f}.");
            P(96, "Validating the WUP package...");
            var report = WupValidator.Validate(outPath, o.CommonKey);
            foreach (var l in report.Info) Emit("[wup] " + l);
            foreach (var l in report.Warnings) Emit("[wup] warning: " + l);
            if (!report.Ok)
            {
                foreach (var l in report.Errors) Emit("[wup] ERROR: " + l);
                throw new InvalidOperationException("The generated WUP failed validation, do not install it:\n" + string.Join("\n", report.Errors));
            }
            P(100, "Conversion complete...");
            return outPath;
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    /// <summary>Retail Wii common key (public). Checked by decrypting the first block of a disc partition: it starts with the game id.</summary>
    static readonly byte[] WiiCommonKey = Convert.FromHexString("EBE42A225E8593E448D9C5457381AAF7");

    static string PrepareIso(BuildOptions o, string work, string wit, Action<string, IEnumerable<string>, string?> exec)
    {
        var iso = Path.Combine(work, "game.iso");
        if (o.System == "wii")
        {
            var game = o.GamePath;
            if (game.EndsWith(".wbfs", StringComparison.OrdinalIgnoreCase))
            {
                game = Path.Combine(work, "wbfsconvert.iso");
                exec(wit, ["copy", o.GamePath, "--DEST", game, "--iso", "-ovv"], null);
            }
            if (o.DisableTrimming && !o.ForceCC)
            {
                return game;
            }
            var extract = Path.Combine(work, "ISOEXTRACT");
            exec(wit, ["extract", game, "--DEST", extract, "--psel", "data,-update", "-ovv"], null);
            if (o.ForceCC) exec(Tools.Native("getexttypepatcher"), [Path.Combine(extract, "sys", "main.dol"), "-nc"], null);
            if (o.VideoModeChange)
            {
                var dolPath = Path.Combine(extract, "sys", "main.dol");
                var dol = File.ReadAllBytes(dolPath);
                var (found, changed) = VideoModePatcher.Patch(dol, o.VideoTarget);
                Emit($"Video mode patch ({found} found, {changed} changed)");
                if (found == 0) throw new InvalidOperationException("Video mode change: no video mode table found in main.dol, the game cannot be patched.");
                File.WriteAllBytes(dolPath, dol);
            }
            string[] copy = ["copy", extract, "--DEST", iso, "-ovv", "--links", "--iso"];
            exec(wit, o.Wiimmfi ? [.. copy, "--wiimmfi"] : copy, null);
            return iso;
        }

        var b = Path.Combine(work, "TEMPISOBASE");
        Tools.CopyDir(Path.Combine(Tools.Root, "BASE"), b);
        Directory.CreateDirectory(Path.Combine(b, "files"));
        var main = Path.Combine(b, "sys", "main.dol");
        switch (o.System)
        {
            case "dol":
                File.Copy(o.GamePath, main, true);
                break;
            case "wiiware":
                File.Copy(Path.Combine(Tools.Root, "DOL", o.Force43 ? "FIX94_wiivc_chan_booter_force43.dol" : "FIX94_wiivc_chan_booter.dol"), main, true);
                File.WriteAllText(Path.Combine(b, "files", "title.txt"), o.WiiwareTitle + "\n", new UTF8Encoding(false));
                break;
            case "gcn":
                // original precedence: force flags, then custom DOL, then forwarder, then default autobooter
                var custom = !string.IsNullOrEmpty(o.CustomDolPath) && !o.Force43 && !o.ForceInterlaced;
                File.Copy(custom ? o.CustomDolPath! : Path.Combine(Tools.Root, "DOL", Metadata.GcnBooter(o)), main, true);
                File.Copy(o.GamePath, Path.Combine(b, "files", "game.iso"), true);
                if (!string.IsNullOrEmpty(o.Game2Path)) File.Copy(o.Game2Path, Path.Combine(b, "files", "disc2.iso"), true);
                break;
        }
        exec(wit, ["copy", b, "--DEST", iso, "-ovv", "--links", "--iso"], null);
        Directory.Delete(b, true);
        return iso;
    }
}
