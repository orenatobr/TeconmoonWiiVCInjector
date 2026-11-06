using System.Security.Cryptography;

namespace WiiVCInjector;

/// <summary>Downloads and validates the Nintendo base files with JNUSTool (same 16 MD5s as Form1.cs).</summary>
public static class BaseFiles
{
    public const string Rhf = "Rhythm Heaven Fever [VAKE01]";
    const string Cdn = "http://ccs.cdn.wup.shop.nintendo.net/ccs/download";
    const string RhfId = "00050000101b0700";

    // title id, needs title key, path inside the title, expected MD5
    static readonly (string Title, bool Keyed, string Rel, string Md5)[] Files =
    [
        ("0005001010004000", false, "/code/deint.txt", "E707A62EE5491DD16E5494631EA9870A"),
        ("0005001010004000", false, "/code/font.bin", "CDDAC70FDDB9428F220B048102DAAD40"),
        ("0005001010004001", false, "/code/c2w.img", "FC5EE480F58796C3681BEE78BD3E5D1C"),
        ("0005001010004001", false, "/code/boot.bin", "F4D5F095CBA9504A5CB8A94A4781114C"),
        ("0005001010004001", false, "/code/dmcu.d.hex", "E32FCBCC817C443E0832DE5CA9032808"),
        (RhfId, true, "/code/cos.xml", "42215713D951C2023F90164ED9DF900F"),
        (RhfId, true, "/code/frisbiiU.rpx", "69E191E8B0DF1D5304B36F1375C4F127"),
        (RhfId, true, "/code/fw.img", "3CAF52A9A440EEE4F125A3AD22E305C8"),
        (RhfId, true, "/code/fw.tmd", "AE4E06CAD3BEF60AE5C49E22CCDC3254"),
        (RhfId, true, "/code/htk.bin", "C99CAF5995E395F39C3FCAB4A8AF20E0"),
        (RhfId, true, "/code/nn_hai_user.rpl", "C4BF586BA0071BD8477986C1AA37E1F1"),
        (RhfId, true, "/content/assets/shaders/cafe/banner.gsh", "5F2FA196DFC158F0FCC69272073AE07E"),
        (RhfId, true, "/content/assets/shaders/cafe/fade.gsh*", "307221985A7B46F0386A2637DC15DA3E"),
        (RhfId, true, "/meta/bootMovie.h264", "CA0DAC3E3C5654209C754357EF5A2507"),
        (RhfId, true, "/meta/bootLogoTex.tga", "67B312145ECB70514D5BD36FCAAE0193"),
        (RhfId, true, "/meta/bootSound.btsnd", "43CD445B8569A445F97ECCC098C93B38"),
    ];

    public static string DownloadsDir
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("WIIVC_DOWNLOADS");
            return !string.IsNullOrEmpty(env) ? env
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "WiiVCInjector", "downloads");
        }
    }

    static string Folder(string title) => title == RhfId ? Rhf : title;
    static string LocalPath(string dest, (string Title, bool Keyed, string Rel, string Md5) f) =>
        Path.Combine(dest, Folder(f.Title), f.Rel.TrimStart('/').TrimEnd('*').Replace('/', Path.DirectorySeparatorChar));

    static bool Ok(string path, string md5) =>
        File.Exists(path) && Convert.ToHexString(MD5.HashData(File.ReadAllBytes(path))) == md5;

    /// <summary>Downloads any missing or corrupt base file. Keys are only passed to JNUSTool through a private temp config, never stored. Returns the downloads dir.</summary>
    public static Task<string> EnsureAsync(string commonKey, string titleKey, IProgress<(int Percent, string Status)> progress, CancellationToken ct = default) =>
        Task.Run(() => Ensure(commonKey, titleKey, progress, ct), ct);

    static string Ensure(string commonKey, string titleKey, IProgress<(int Percent, string Status)> progress, CancellationToken ct)
    {
        var dest = DownloadsDir;
        Directory.CreateDirectory(dest);
        progress.Report((0, "Checking if the necessary files are present..."));
        var missing = Files.Where(f => !Ok(LocalPath(dest, f), f.Md5)).ToList();
        if (missing.Count == 0) { progress.Report((100, "Base files present")); return dest; }
        if (string.IsNullOrWhiteSpace(commonKey) || (missing.Any(f => f.Keyed) && string.IsNullOrWhiteSpace(titleKey)))
            throw new InvalidOperationException("Base files are missing: the Wii U common key and the Rhythm Heaven Fever title key are needed to download them.");

        var work = Path.Combine(Path.GetTempPath(), "jnus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var cfg = Path.Combine(work, "config");
            File.WriteAllText(cfg, $"{Cdn}\n{commonKey}\n");
            File.SetUnixFileMode(cfg, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Copy(Tools.Jar("JNUSTool"), Path.Combine(work, "JNUSTool.jar"));
            for (var i = 0; i < missing.Count; i++)
            {
                var f = missing[i];
                progress.Report((i * 100 / missing.Count, "(One-Time Download) Downloading base files from Nintendo..."));
                var args = new List<string> { "-jar", "JNUSTool.jar", f.Title };
                if (f.Keyed) args.Add(titleKey);
                args.AddRange(["-file", f.Rel]);
                Tools.Run(Tools.Java(), args, work, ct);
                var rel = f.Rel.TrimStart('/').TrimEnd('*').Replace('/', Path.DirectorySeparatorChar);
                var dir = Directory.Exists(Path.Combine(work, Folder(f.Title))) ? Folder(f.Title)
                    : Directory.GetDirectories(work).Select(Path.GetFileName).First()!;
                var local = LocalPath(dest, f);
                Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                File.Move(Path.Combine(work, dir, rel), local, true);
                if (!Ok(local, f.Md5)) { File.Delete(local); throw new InvalidDataException($"MD5 mismatch for {f.Rel} (wrong key?)"); }
            }
            progress.Report((100, "Base files downloaded"));
            return dest;
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }
}
