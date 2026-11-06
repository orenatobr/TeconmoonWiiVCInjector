using System.Diagnostics;
using System.Text;

namespace WiiVCInjector;

/// <summary>Locates the bundled tools and runs them without a shell.</summary>
internal static class Tools
{
    const string DevTools = "/Volumes/Renato/Wii U/tcmoon-mac/tools";

    public static string Root
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("WIIVC_TOOLS");
            if (!string.IsNullOrEmpty(env)) return env;
            // Inside the .app the managed code lives in Contents/Resources/app, tools in Contents/Resources/tools.
            foreach (var rel in new[] { Path.Combine("..", "tools"), Path.Combine("..", "Resources", "tools") })
            {
                var cand = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, rel));
                if (Directory.Exists(Path.Combine(cand, "bin"))) return cand;
            }
            return DevTools;
        }
    }

    static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "WiiVCInjector");

    /// <summary>Native tool from tools/bin. The dev fallback lives on exFAT where binaries get SIGKILLed, so copy and ad-hoc sign.</summary>
    public static string Native(string name)
    {
        var src = Path.Combine(Root, "bin", name);
        if (!File.Exists(src)) throw new FileNotFoundException($"tool '{name}' not found in {Path.Combine(Root, "bin")}");
        if (Root != DevTools) return src;
        var fi = new FileInfo(src);
        var dst = Path.Combine(CacheRoot, "bin", $"{fi.Length}-{new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds()}", name);
        if (!File.Exists(dst))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            var tmp = dst + ".tmp";
            File.Copy(src, tmp, true);
            File.SetUnixFileMode(tmp, (UnixFileMode)0b111_101_101);
            Run("codesign", ["--force", "-s", "-", tmp], null, default);
            File.Move(tmp, dst, true);
        }
        return dst;
    }

    public static string Java()
    {
        foreach (var c in new[] { Path.Combine(Root, "jre", "bin", "java"), "/opt/homebrew/opt/openjdk@17/bin/java", "/opt/homebrew/opt/openjdk/bin/java" })
            if (File.Exists(c)) return c;
        return "java";
    }

    public static string Jar(string name)
    {
        var p = Path.Combine(Root, "jar", name + ".jar");
        return File.Exists(p) ? p : throw new FileNotFoundException(p + " not found");
    }

    /// <summary>Command (exe + leading args) for nfs2iso2nfs: the .dll via dotnet if shipped, else the native executable.</summary>
    public static string[] NfsCommand()
    {
        var dll = Path.Combine(Root, "nfs", "nfs2iso2nfs.dll");
        if (File.Exists(dll))
            return [File.Exists("/opt/homebrew/bin/dotnet") ? "/opt/homebrew/bin/dotnet" : "dotnet", dll];
        var exe = Path.Combine(Root, "nfs", "nfs2iso2nfs");
        return File.Exists(exe) ? [exe] : throw new FileNotFoundException("nfs2iso2nfs not found in " + Path.Combine(Root, "nfs"));
    }

    public static void Run(string exe, IEnumerable<string> args, string? cwd, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true,
        };
        if (cwd != null) psi.WorkingDirectory = cwd;
        foreach (var a in args) psi.ArgumentList.Add(a);
        var tail = new Queue<string>();
        void OnLine(object _, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (tail) { tail.Enqueue(e.Data); if (tail.Count > 30) tail.Dequeue(); }
            BuildEngine.Emit(e.Data);
        }
        BuildEngine.Emit("$ " + exe + " " + string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)));
        using var p = new Process { StartInfo = psi };
        p.OutputDataReceived += OnLine;
        p.ErrorDataReceived += OnLine;
        p.Start();
        p.StandardInput.Close();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
        p.WaitForExit();
        ct.ThrowIfCancellationRequested();
        if (p.ExitCode != 0)
        {
            string t; lock (tail) t = string.Join("\n", tail);
            throw new InvalidOperationException($"{Path.GetFileName(exe)} exited with code {p.ExitCode}:\n{t}");
        }
    }

    public static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}

/// <summary>Synchronous IProgress (Progress&lt;T&gt; posts to a captured context).</summary>
internal sealed class SyncProgress<T>(Action<T> f) : IProgress<T>
{
    public void Report(T value) => f(value);
}
