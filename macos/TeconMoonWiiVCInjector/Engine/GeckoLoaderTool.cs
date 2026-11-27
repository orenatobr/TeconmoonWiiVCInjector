namespace TeconMoonWiiVCInjector;

/// <summary>
/// The GeckoLoader command line tool (github.com/JoshuaMKW/GeckoLoader, GPL-3.0) puts a Gecko code handler and a code list into a game's main.dol, so that
/// every Gecko code works in a Wii game inside a WUP, not only the plain memory writes <see cref="DolCheats"/> can write. It is a Python program with its own
/// virtual environment; the app does not bundle it, it installs it on request into the app's own folder and runs it as a separate program.
/// </summary>
internal static class GeckoLoaderTool
{
    /// <summary>The GeckoLoader release the install script checks out.</summary>
    public const string Tag = "v7.1.1";

    /// <summary>Where the tool lives: <c>tools/GeckoLoader</c> next to settings.json, or the folder in WIIVC_GECKOLOADER_DIR (tests).</summary>
    public static string Dir =>
        Environment.GetEnvironmentVariable("WIIVC_GECKOLOADER_DIR") is { Length: > 0 } d ? d : Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "tools", "GeckoLoader");

    /// <summary>The Python of the tool's own environment and its script, or null when the tool is not installed.</summary>
    public static (string Python, string Script)? Find(string? dir = null)
    {
        dir ??= Dir;
        var script = Path.Combine(dir, "GeckoLoader.py");
        var python = Path.Combine(dir, "venv", "bin", "python");
        return File.Exists(script) && File.Exists(python) ? (python, script) : null;
    }

    /// <summary>
    /// Adds the code handler and <paramref name="gct"/> to the DOL, in place. Returns null on success, or why it failed (for example no hook address was found in
    /// the DOL), in which case the DOL file is left as it was.
    /// </summary>
    public static string? Patch(string dolFile, byte[] gct, string workDir, CancellationToken ct, string? dir = null)
    {
        if (Find(dir) is not var (python, script)) return "the code handler tool is not installed";
        var work = Path.Combine(workDir, "geckoloader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var gctFile = Path.Combine(work, "codes.gct");
            var outFile = Path.Combine(work, "main.dol");
            File.WriteAllBytes(gctFile, gct);
            Tools.Run(python, [script, dolFile, gctFile, "--dest", outFile, "-q"], work, ct);
            if (!File.Exists(outFile) || new FileInfo(outFile).Length <= new FileInfo(dolFile).Length) return "the tool did not write a larger DOL";
            File.Copy(outFile, dolFile, true);
            return null;
        }
        catch (InvalidOperationException ex) { return ex.Message; }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    /// <summary>Installs the tool into the folder given as the first argument: a pinned checkout and a virtual environment with what it imports.</summary>
    public const string SetupScript = """
        #!/bin/bash
        set -euo pipefail
        DIR="$1"
        for need in git python3; do
          command -v "$need" >/dev/null || { echo "$need is needed and was not found. Install the Xcode command line tools (xcode-select --install) and try again."; exit 1; }
        done
        mkdir -p "$(dirname "$DIR")"
        if [ ! -d "$DIR/.git" ]; then
          echo "Downloading GeckoLoader __TAG__..."
          git clone -q --depth 1 --branch __TAG__ https://github.com/JoshuaMKW/GeckoLoader "$DIR"
        fi
        echo "Creating its Python environment (this downloads PyQt5, about 60 MB)..."
        python3 -m venv "$DIR/venv"
        # setuptools brings the distutils module that Python 3.12 and later no longer have, which GeckoLoader imports
        "$DIR/venv/bin/pip" install -q --disable-pip-version-check setuptools chardet PyQt5 beautifulsoup4
        "$DIR/venv/bin/python" "$DIR/GeckoLoader.py" --help >/dev/null
        echo "GeckoLoader is installed in $DIR"
        """;

    /// <summary>Runs the install script. Returns the last lines of its output and whether it worked.</summary>
    public static async Task<(bool Ok, string Output)> InstallAsync(string? dir = null)
    {
        dir ??= Dir;
        var script = Path.Combine(Path.GetTempPath(), "wiivc-geckoloader-setup-" + Guid.NewGuid().ToString("N") + ".sh");
        await File.WriteAllTextAsync(script, SetupScript.Replace("__TAG__", Tag));
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add(script);
            psi.ArgumentList.Add(dir);
            using var p = System.Diagnostics.Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            var text = (await stdout + "\n" + await stderr).Trim();
            var tail = string.Join("\n", text.Split('\n').TakeLast(8));
            return (p.ExitCode == 0 && Find(dir) != null, tail);
        }
        finally { try { File.Delete(script); } catch { } }
    }
}
