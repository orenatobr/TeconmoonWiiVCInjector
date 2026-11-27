using System.IO.Compression;
using System.Security.Cryptography;

namespace TeconMoonWiiVCInjector;

/// <summary>Runs the NKit command line tool (https://github.com/Nanook/NKit, MIT) to turn an RVZ or an NKit image into a plain ISO.</summary>
/// <remarks>Both formats drop the "junk" padding of a GameCube disc and regenerate it on read, so they cannot be unpacked by copying
/// blocks. The tool is downloaded once into the user's cache, from a pinned release, and checked against a fixed SHA-256 before it is used.</remarks>
internal static class NkitTool
{
    public const string Version = "3.2.2";
    const string AssetName = "NKit_macOS-AppleSilicon_CLI_" + Version + ".zip";
    const string AssetUrl = "https://github.com/Nanook/NKit/releases/download/v" + Version + "/" + AssetName;
    /// <summary>SHA-256 of the release zip, as GitHub lists it for the asset.</summary>
    const string AssetSha256 = "9722b4cf9e876488e167f69f6af606453709b425d1ac27090ba26285cbff6eb9";
    /// <summary>What is kept from the zip: the CLI and its portable config. The DataStore tool (nkds) is not needed.</summary>
    static readonly string[] Wanted = ["nkit", "nkit.yaml", "ConfigInfo.txt"];

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Caches", "TeconMoonWiiVCInjector", "tools", "nkit-" + Version);

    /// <summary>Path of the tool when it is already available. <c>WIIVC_NKIT</c> points at a different executable (tests, manual installs).</summary>
    public static string? Find()
    {
        var env = Environment.GetEnvironmentVariable("WIIVC_NKIT");
        if (!string.IsNullOrEmpty(env)) return File.Exists(env) ? env : throw new FileNotFoundException("WIIVC_NKIT does not point to a file: " + env);
        var exe = Path.Combine(InstallDir, "nkit");
        return File.Exists(exe) && File.Exists(Path.Combine(InstallDir, ".verified")) ? exe : null;
    }

    /// <summary>The tool's path, downloading and installing it first when needed.</summary>
    public static async Task<string> EnsureAsync(CancellationToken ct)
    {
        if (Find() is { } found) return found;
        BuildEngine.Emit($"Downloading the NKit converter {Version} (one time, about 18 MB)...");
        var zip = Path.Combine(Path.GetTempPath(), "TeconMoonWiiVCInjector-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var resp = await Http.GetAsync(AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var net = await resp.Content.ReadAsStreamAsync(ct);
                await using var file = File.Create(zip);
                await net.CopyToAsync(file, ct);
            }
            Install(zip, InstallDir);
        }
        finally { try { File.Delete(zip); } catch { } }
        return Find() ?? throw new InvalidOperationException("The NKit converter was installed but cannot be found in " + InstallDir);
    }

    /// <summary>Checks the zip against the pinned hash and unpacks the wanted files into <paramref name="dest"/>.</summary>
    /// <param name="zip">The downloaded release zip.</param>
    /// <param name="dest">Install folder. It is replaced.</param>
    /// <exception cref="InvalidDataException">The zip does not match the pinned SHA-256, or has no <c>nkit</c> executable.</exception>
    internal static void Install(string zip, string dest, string expectedSha256 = AssetSha256)
    {
        string actual;
        using (var f = File.OpenRead(zip)) actual = Convert.ToHexString(SHA256.HashData(f));
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The downloaded NKit file does not match the expected SHA-256 and was not used (got {actual}).");

        var tmp = dest + ".tmp-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(tmp);
        try
        {
            using (var za = ZipFile.OpenRead(zip))
                foreach (var e in za.Entries.Where(e => Wanted.Contains(e.FullName)))
                    e.ExtractToFile(Path.Combine(tmp, e.FullName), true);
            var exe = Path.Combine(tmp, "nkit");
            if (!File.Exists(exe)) throw new InvalidDataException("The NKit zip has no 'nkit' executable.");
            File.SetUnixFileMode(exe, (UnixFileMode)0b111_101_101);
            // an arm64 binary needs a signature to run; the release is not guaranteed to carry one
            try { Tools.Run("codesign", ["--force", "-s", "-", exe], null, default); }
            catch (Exception ex) { BuildEngine.Emit("codesign skipped: " + ex.Message); }
            File.WriteAllText(Path.Combine(tmp, ".verified"), actual);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            Directory.Move(tmp, dest);
        }
        finally { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } }
    }

    /// <summary>Arguments that expand <paramref name="input"/> to a full ISO in <paramref name="outDir"/>.</summary>
    /// <param name="input">RVZ or NKit image.</param>
    /// <param name="outDir">Where the tool writes the ISO.</param>
    /// <param name="tempDir">The tool's scratch folder.</param>
    /// <param name="config">Its config file, so it never creates one in the user's Documents.</param>
    internal static string[] ExpandArgs(string input, string outDir, string tempDir, string config) =>
        ["expand", input, "--output", outDir, "--temp", tempDir, "--config", config];

    /// <summary>Writes the plain ISO of an RVZ or NKit image to <paramref name="outputIso"/>.</summary>
    /// <param name="input">RVZ or NKit image of a GameCube disc.</param>
    /// <param name="outputIso">Destination file, replaced if it exists.</param>
    /// <param name="progress">Reports 100 when done; the tool does not give a usable percentage.</param>
    /// <param name="ct">Cancels the tool.</param>
    /// <exception cref="InvalidOperationException">The tool failed or did not produce exactly one ISO.</exception>
    public static void ExpandToIso(string input, string outputIso, IProgress<int>? progress, CancellationToken ct)
    {
        var exe = EnsureAsync(ct).GetAwaiter().GetResult();
        var scratch = Path.Combine(Path.GetTempPath(), "TeconMoonWiiVCInjector-nkit-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(scratch, "out");
        var tempDir = Path.Combine(scratch, "tmp");
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = Path.Combine(Path.GetDirectoryName(exe)!, "nkit.yaml");
            BuildEngine.Emit("Expanding " + Path.GetFileName(input) + " to a full ISO with NKit...");
            Tools.Run(exe, ExpandArgs(input, outDir, tempDir, config), Path.GetDirectoryName(exe), ct);

            var isos = Directory.GetFiles(outDir, "*.iso", SearchOption.AllDirectories);
            if (isos.Length != 1)
                throw new InvalidOperationException($"NKit finished but wrote {isos.Length} ISO files instead of one: " +
                    string.Join(", ", Directory.GetFileSystemEntries(outDir, "*", SearchOption.AllDirectories).Select(Path.GetFileName)));
            File.Move(isos[0], outputIso, true);
            progress?.Report(100);
        }
        finally { try { Directory.Delete(scratch, true); } catch { } }
    }
}
