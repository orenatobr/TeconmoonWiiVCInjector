using System.Text;
using SkiaSharp;

namespace WiiVCInjector;

/// <summary>Console self-test (WIIVC_SELFTEST=1) running unit checks and full builds with the real tools.</summary>
internal static class SelfTest
{
    static int _fail;
    static void Check(string name, bool ok) { Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}"); if (!ok) _fail++; }

    static int Count(string xml) => System.Xml.Linq.XDocument.Parse(xml).Root!.Elements().Count();

    public static int Run()
    {
        try { Units(); Builds(); }
        catch (Exception e) { Console.WriteLine("[FAIL] exception: " + e); _fail++; }
        Console.WriteLine(_fail == 0 ? "ALL OK" : $"{_fail} FAILED");
        return _fail == 0 ? 0 : 1;
    }

    static void Units()
    {
        var w = Metadata.Ids("RSPE", "wii");
        Check("wii ids", w.Hex == "52535045" && w.Text == "RSPE" && w.Packed == "0005000252535045");
        var d = Metadata.Ids("HAXX", "dol");
        Check("dol ids", d.Text == "BOOT" && d.Packed == "0005000248415858");
        Check("wiiware ids", Metadata.WiiwareIds("wlzx").Packed == "00050002574C5A58");
        Check("app.xml has 9 fields", Count(Metadata.AppXml(w)) == 9);
        var meta = Metadata.MetaXml(w, "A & B <C>", "L2", "65537");
        Check("meta.xml has 137 fields", Count(meta) == 137);
        var root = System.Xml.Linq.XDocument.Parse(meta).Root!;
        Check("meta escaping + two lines", root.Element("longname_en")!.Value == "A & B <C>\nL2" && root.Element("shortname_en")!.Value == "A & B <C>"
            && root.Element("product_code")!.Value == "WUP-N-RSPE" && root.Element("drc_use")!.Value == "65537");
        Check("nfs wii", string.Join(' ', Metadata.NfsArgs("wii", "g.iso", "-instantcc", true, true)) == "-enc -instantcc -lrpatch -iso g.iso");
        Check("nfs dol", string.Join(' ', Metadata.NfsArgs("dol", "g.iso", "", false, true)) == "-enc -homebrew -passthrough -iso g.iso");
        Check("nfs wiiware", string.Join(' ', Metadata.NfsArgs("wiiware", "g.iso", "-nocc", false, true)) == "-enc -homebrew -nocc -iso g.iso");
        Check("nfs gcn", string.Join(' ', Metadata.NfsArgs("gcn", "g.iso", "", false, false)) == "-enc -homebrew -passthrough -iso g.iso");

        // btsnd: 2 frames mono 16-bit 48k WAV {1,-1}
        static byte[] Wav(int ch, int rate, short[] s)
        {
            var ms = new MemoryStream(); var bw = new BinaryWriter(ms);
            bw.Write("RIFF"u8); bw.Write(36 + s.Length * 2); bw.Write("WAVEfmt "u8); bw.Write(16); bw.Write((short)1); bw.Write((short)ch);
            bw.Write(rate); bw.Write(rate * ch * 2); bw.Write((short)(ch * 2)); bw.Write((short)16); bw.Write("data"u8); bw.Write(s.Length * 2);
            foreach (var v in s) bw.Write(v);
            return ms.ToArray();
        }
        var pcm = Media.WavToStereo48k(Wav(1, 48000, [1, -1]));
        Check("wav mono->stereo", pcm.SequenceEqual(new short[] { 1, 1, -1, -1 }));
        var loop = Media.Btsnd(pcm, false);
        var noLoop = Media.Btsnd(pcm, true);
        Check("btsnd loop layout", loop.Length == 8 + 8 && loop[3] == 2 && loop[7] == 0 && loop[8] == 0 && loop[9] == 1 && loop[12] == 0xFF && loop[13] == 0xFF);
        Check("btsnd noLoop layout", noLoop.Length == 8 + 8 + 4 && noLoop[7] == 3 && noLoop[^4..].All(b => b == 0));
        Check("wav resample 24k->48k length", Media.WavToStereo48k(Wav(2, 24000, new short[2400])).Length == 2 * 2400);
        Check("wav trim 6s", Media.WavToStereo48k(Wav(1, 8000, new short[8000 * 10])).Length == 2 * 288000);

        var h32 = Media.TgaHeader(128, 128, 32);
        Check("tga 32 header", Convert.ToHexString(h32) == "000002000000000000000000" + "8000" + "8000" + "2008");
        Check("tga 24 header", Convert.ToHexString(Media.TgaHeader(1280, 720, 24)) == "000002000000000000000000" + "0005" + "D002" + "1800");
        var tmp = Path.Combine(Path.GetTempPath(), "wiivc-st-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
        try
        {
            var png = Png(Path.Combine(tmp, "a.png"));
            Media.WriteTga(png, Path.Combine(tmp, "i.tga"), 128, 128, 32);
            Media.WriteTga(png, Path.Combine(tmp, "b.tga"), 854, 480, 24);
            var i = File.ReadAllBytes(Path.Combine(tmp, "i.tga")); var b = File.ReadAllBytes(Path.Combine(tmp, "b.tga"));
            Check("tga files size/header", i.Length == 18 + 128 * 128 * 4 && i[2] == 2 && i[16] == 32 && i[17] == 8 && b.Length == 18 + 854 * 480 * 3 && b[16] == 24 && b[17] == 0);
            // bottom-left origin: PNG top half red, bottom half blue => first stored row (bottom) is blue (BGR = FF 00 00)
            Check("tga rows bottom-up", b[18] == 0xFF && b[19] == 0 && b[20] == 0 && b[^3] == 0 && b[^1] == 0xFF);
        }
        finally { Directory.Delete(tmp, true); }
    }

    static string Png(string path)
    {
        using var bmp = new SKBitmap(64, 64);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Blue);
            c.DrawRect(0, 0, 64, 32, new SKPaint { Color = SKColors.Red });
        }
        using var fs = File.Create(path);
        bmp.Encode(fs, SKEncodedImageFormat.Png, 100);
        return path;
    }

    static void Builds()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "wiivc-st-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tmp);
        try
        {
            BuildEngine.Log += l => { if (Environment.GetEnvironmentVariable("WIIVC_VERBOSE") == "1") Console.WriteLine("    " + l); };
            var icon = Png(Path.Combine(tmp, "icon.png"));
            var wav = Path.Combine(tmp, "s.wav");
            var pcm = new short[22050];
            for (var i = 0; i < pcm.Length; i++) pcm[i] = (short)(8000 * Math.Sin(i * 0.1));
            var ms = new MemoryStream(); var bw = new BinaryWriter(ms);
            bw.Write("RIFF"u8); bw.Write(36 + pcm.Length * 2); bw.Write("WAVEfmt "u8); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
            bw.Write(22050); bw.Write(44100); bw.Write((short)2); bw.Write((short)16); bw.Write("data"u8); bw.Write(pcm.Length * 2);
            foreach (var v in pcm) bw.Write(v);
            File.WriteAllBytes(wav, ms.ToArray());

            var commonKey = Environment.GetEnvironmentVariable("WIIVC_COMMON_KEY") ?? "";
            var titleKey = Environment.GetEnvironmentVariable("WIIVC_TITLE_KEY") ?? "";
            Console.WriteLine("downloads: " + BaseFiles.DownloadsDir + ", tools: " + Tools.Root);
            var last = -1;
            var prog = new SyncProgress<(int Percent, string Status)>(p => { if (p.Percent / 10 != last / 10) { last = p.Percent; Console.WriteLine($"    {p.Percent}% {p.Status}"); } });
            BaseFiles.EnsureAsync(commonKey, titleKey, prog).GetAwaiter().GetResult();
            Check("BaseFiles.EnsureAsync", true);

            // synthetic DOL: 0x300 bytes, magic 00 00 01 00, id HAXX at 0x2A0
            var dol = Path.Combine(tmp, "t.dol");
            var dolBytes = new byte[0x300]; dolBytes[2] = 1; Encoding.ASCII.GetBytes("HAXX").CopyTo(dolBytes, 0x2A0);
            File.WriteAllBytes(dol, dolBytes);
            var o = new BuildOptions
            {
                System = "dol", GamePath = dol, OutputDir = Path.Combine(tmp, "out"), Title1 = "Test DOL", IconPath = icon, BannerPath = icon,
                CommonKey = commonKey, TitleKey = titleKey, BootSoundPath = wav,
            };
            VerifyPackage("dol build", BuildEngine.RunAsync(o, prog).GetAwaiter().GetResult(), "0005000248415858", Path.Combine(tmp, "out"));

            // synthetic Wii ISO: wit copy of BASE with a main.dol, header carries the disc id
            var wit = Tools.Native("wit");
            var baseDir = Path.Combine(tmp, "WIIBASE");
            Tools.CopyDir(Path.Combine(Tools.Root, "BASE"), baseDir);
            File.Copy(Path.Combine(Tools.Root, "DOL", "nintendont_default_autobooter.dol"), Path.Combine(baseDir, "sys", "main.dol"), true);
            Directory.CreateDirectory(Path.Combine(baseDir, "files"));
            var iso = Path.Combine(tmp, "test.iso");
            Tools.Run(wit, ["copy", baseDir, iso, "--links", "--iso"], null, default);
            var id = Metadata.ReadGameId(iso);
            Console.WriteLine("synthetic ISO id: " + id);
            o = new BuildOptions
            {
                System = "wii", GamePath = iso, OutputDir = Path.Combine(tmp, "out"), Title1 = "Test Wii", Title2 = "Line 2", IconPath = icon, BannerPath = icon,
                CommonKey = commonKey, TitleKey = titleKey, ForceCC = true,
            };
            VerifyPackage("wii build (ForceCC)", BuildEngine.RunAsync(o, prog).GetAwaiter().GetResult(), "00050002" + Convert.ToHexString(Encoding.Latin1.GetBytes(id)), Path.Combine(tmp, "out"));
            VideoModeBuild(tmp, icon, commonKey, titleKey, prog);
            C2wStep();
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    static byte[] Mode(uint tv, int efb, int xfb, int y, int h, byte field)
    {
        var m = new byte[60];
        void P16(int o, int v) { m[o] = (byte)(v >> 8); m[o + 1] = (byte)v; }
        m[0] = (byte)(tv >> 24); m[1] = (byte)(tv >> 16); m[2] = (byte)(tv >> 8); m[3] = (byte)tv;
        P16(4, 640); P16(6, efb); P16(8, xfb); P16(10, 40); P16(12, y); P16(14, 640); P16(16, h);
        m[23] = 1; m[24] = field;
        for (var i = 0; i < 24; i++) m[26 + i] = 6;
        new byte[] { 7, 7, 12, 12, 12, 7, 7 }.CopyTo(m, 50);
        return m;
    }

    static void VideoModeBuild(string tmp, string icon, string commonKey, string titleKey, IProgress<(int Percent, string Status)> prog)
    {
        var wit = Tools.Native("wit");
        var baseDir = Path.Combine(tmp, "VMBASE");
        Tools.CopyDir(Path.Combine(Tools.Root, "BASE"), baseDir);
        var dol = new byte[0x400]; // minimal DOL header (one 0x300 byte text section at 0x100) so wit keeps the whole file
        void U32(int o, uint v) { dol[o] = (byte)(v >> 24); dol[o + 1] = (byte)(v >> 16); dol[o + 2] = (byte)(v >> 8); dol[o + 3] = (byte)v; }
        U32(0x00, 0x100); U32(0x48, 0x80003100); U32(0x90, 0x300); U32(0xE0, 0x80003100);
        Mode(0, 480, 480, 0, 480, 0).CopyTo(dol, 0x100); // NTSC interlaced table
        File.WriteAllBytes(Path.Combine(baseDir, "sys", "main.dol"), dol);
        Directory.CreateDirectory(Path.Combine(baseDir, "files"));
        var iso = Path.Combine(tmp, "vm.iso");
        Tools.Run(wit, ["copy", baseDir, iso, "--links", "--iso"], null, default);
        string? seen = null;
        BuildEngine.IsoReady = p =>
        {
            var ex = Path.Combine(tmp, "VMBACK");
            Tools.Run(wit, ["extract", p, "--DEST", ex, "--psel", "data,-update", "-ovv"], null, default);
            seen = Path.Combine(ex, "sys", "main.dol");
            File.Copy(seen, Path.Combine(tmp, "vm-main.dol"), true);
        };
        var o = new BuildOptions
        {
            System = "wii", GamePath = iso, OutputDir = Path.Combine(tmp, "out"), Title1 = "Test VM", IconPath = icon, BannerPath = icon,
            CommonKey = commonKey, TitleKey = titleKey, VideoModeChange = true, VideoTarget = VideoTarget.Pal60,
        };
        try { BuildEngine.RunAsync(o, prog).GetAwaiter().GetResult(); } finally { BuildEngine.IsoReady = null; }
        var after = File.ReadAllBytes(Path.Combine(tmp, "vm-main.dol"));
        Check("video mode: table in rebuilt ISO main.dol changed to EURGB60", after.Length >= 0x400 && after[0x103] == (5 << 2) && dol[0x103] == 0);
        // failure path
        File.WriteAllBytes(Path.Combine(baseDir, "sys", "main.dol"), new byte[0x400]);
        var iso2 = Path.Combine(tmp, "vm2.iso");
        Tools.Run(wit, ["copy", baseDir, iso2, "--links", "--iso"], null, default);
        o.GamePath = iso2;
        try { BuildEngine.RunAsync(o, prog).GetAwaiter().GetResult(); Check("video mode: Found==0 fails", false); }
        catch (InvalidOperationException e) { Check("video mode: Found==0 fails clearly", e.Message.Contains("no video mode table")); }
    }

    static void C2wStep()
    {
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var plain = new byte[0x2000];
        plain[0x604] = 0x7F; plain[0x605] = 0x45; plain[0x606] = 0x4C; plain[0x607] = 0x46;
        new byte[] { 0xE3, 0x82, 0x20, 0x20, 0xE5, 0x84, 0x25, 0xB0 }.CopyTo(plain, 0x1000);
        new byte[] { 0xE3, 0x83, 0x30, 0x99, 0xE5, 0x81, 0x35, 0x14 }.CopyTo(plain, 0x1400);
        var img = new byte[0x2200];
        new byte[] { 0xEF, 0xA2, 0x82, 0xD9 }.CopyTo(img, 0);
        using var aes = System.Security.Cryptography.Aes.Create(); aes.Key = key;
        aes.EncryptCbc(plain, new byte[16], System.Security.Cryptography.PaddingMode.None).CopyTo(img, 0x200);
        var dir = Path.Combine(Path.GetTempPath(), "wiivc-c2w-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(dir, "code"));
        try
        {
            // same two lines BuildEngine runs when C2wPatch is on
            var c2w = Path.Combine(dir, "code", "c2w.img");
            File.WriteAllBytes(c2w, img);
            File.WriteAllBytes(c2w, C2wPatcher.Patch(File.ReadAllBytes(c2w), Convert.ToHexString(key), false, BuildEngine.Emit));
            var back = aes.DecryptCbc(File.ReadAllBytes(c2w).AsSpan(0x200), new byte[16], System.Security.Cryptography.PaddingMode.None);
            Check("c2w step: code/c2w.img patched in place", back[0x1001] == 0xC2 && back[0x1403] == 0x9D);
        }
        finally { Directory.Delete(dir, true); }
    }

    static void VerifyPackage(string name, string pkg, string titleId, string outRoot)
    {
        Console.WriteLine($"    package: {Path.GetFileName(pkg)}");
        var files = Directory.GetFiles(pkg);
        var apps = files.Count(f => f.EndsWith(".app"));
        bool has(string n) => File.Exists(Path.Combine(pkg, n));
        var tmd = File.ReadAllBytes(Path.Combine(pkg, "title.tmd"));
        var got = Convert.ToHexString(tmd.AsSpan(0x18C, 8));
        Check($"{name}: title.tmd/tik/cert present", has("title.tmd") && has("title.tik") && has("title.cert"));
        Check($"{name}: 15 .app files (got {apps})", apps == 15);
        Check($"{name}: TMD title id {got} == {titleId}", got.Equals(titleId, StringComparison.OrdinalIgnoreCase));
    }
}
