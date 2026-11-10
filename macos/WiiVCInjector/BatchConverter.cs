using System.Text;
using System.Text.RegularExpressions;

namespace WiiVCInjector;

/// <summary>Outcome of one game of a batch: <see cref="Package"/> is set on success, <see cref="Problem"/> otherwise.</summary>
public sealed record BatchResult(string File, string? Package, string? Problem);

/// <summary>One game of a batch after the check: ready to build (icon and banner downloaded) or with the reason it is skipped.</summary>
public sealed record BatchItem(string File, string Path, string Title, string? Icon, string? Banner, string? Problem);

/// <summary>The checked games of a batch plus the temporary folder that holds their downloaded images.</summary>
public sealed class BatchPlan : IDisposable
{
    public List<BatchItem> Items { get; } = new();
    public string Work { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WiiVCInjector-batch-" + Guid.NewGuid().ToString("N"));
    public int Ready => Items.Count(i => i.Problem == null);
    /// <summary>True when every game has its icon and banner from the repo.</summary>
    public bool AllHaveImages => Items.All(i => i.Icon != null);
    /// <summary>True when every game has a title (a game whose header could not be read has none).</summary>
    public bool AllHaveTitles => Items.All(i => i.Title != "");

    public BatchPlan() => Directory.CreateDirectory(Work);
    public void Dispose() { try { Directory.Delete(Work, true); } catch { } }
}

/// <summary>
/// Converts several Wii or GameCube games in one go. Every game is built with a copy of the same options (so the GamePad
/// Emulation Mode and the other advanced switches chosen in the window apply to all of them), and its icon and banner
/// always come from Cucholix's repo: a game the repo has no images for is skipped, never built with placeholders.
/// </summary>
public static partial class BatchConverter
{
    [GeneratedRegex("^[A-Za-z0-9]{4,6}$")]
    private static partial Regex GameId();

    /// <summary>Checks every game (header, system, disc id) and downloads its icon and banner from the repo.</summary>
    /// <param name="system">"wii" or "gcn", the mode of the window.</param>
    /// <param name="repoUrl">Base URL of the images repo (AppSettings.BannersRepository).</param>
    /// <param name="fetch">Returns the bytes of a repo file, or null when the repo does not have it.</param>
    public static async Task<BatchPlan> PrepareAsync(string system, IReadOnlyList<string> games, string repoUrl,
        Func<string, Task<byte[]?>> fetch, Action<string> report, CancellationToken ct = default)
    {
        var plan = new BatchPlan();
        try
        {
            for (var i = 0; i < games.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                report($"Checking {i + 1}/{games.Count}: {System.IO.Path.GetFileName(games[i])}");
                try { plan.Items.Add(await Check(system, games[i], repoUrl, fetch, System.IO.Path.Combine(plan.Work, i.ToString()))); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { plan.Items.Add(new(System.IO.Path.GetFileName(games[i]), games[i], "", null, null, ex.Message)); }
            }
            return plan;
        }
        catch { plan.Dispose(); throw; }
    }

    static async Task<BatchItem> Check(string system, string path, string repoUrl, Func<string, Task<byte[]?>> fetch, string dir)
    {
        var name = System.IO.Path.GetFileName(path);
        BatchItem Skip(string why, string title = "") => new(name, path, title, null, null, why);
        GameHeader h;
        using (var fs = File.OpenRead(path)) h = GameHeader.Parse(fs);
        var wantType = system == "gcn" ? GameHeader.GcType : GameHeader.WiiType;
        if (h.GameType != wantType) return Skip($"not a {(system == "gcn" ? "GameCube" : "Wii")} image");
        if (h.Nkit || h.Nasos) return Skip("NKit and NASOS images are not supported, convert it to a plain ISO or WBFS first");
        // the id goes into the repo URL, so only accept what a real disc id looks like
        if (!GameId().IsMatch(h.RepoId)) return Skip("the disc id is not valid");

        var tdb = StringUtil.RemoveSpecialChars(GameTdb.GetName(h.RepoId));
        var title = !string.IsNullOrEmpty(tdb) ? tdb : h.InternalName;
        var images = await RepoImages(system, h.RepoId, repoUrl, fetch);
        if (images == null) return Skip("Cucholix's repo has no icon and banner for this game", title);

        Directory.CreateDirectory(dir);
        var icon = System.IO.Path.Combine(dir, "iconTex.png");
        var banner = System.IO.Path.Combine(dir, "bootTvTex.png");
        File.WriteAllBytes(icon, images.Value.Icon);
        File.WriteAllBytes(banner, images.Value.Banner);
        return new(name, path, title, icon, banner, null);
    }

    /// <summary>Builds every ready game of <paramref name="plan"/> in order; a failure only affects its own game.</summary>
    /// <param name="template">Options of the window. System, DrcUse, NfsPatch and the advanced flags are kept for all games.</param>
    /// <param name="build">Runs one build and returns the package folder.</param>
    /// <param name="report">Receives one line per game, e.g. "[2/5] Super Mario Galaxy".</param>
    public static async Task<List<BatchResult>> RunAsync(BuildOptions template, BatchPlan plan,
        Func<BuildOptions, Task<string>> build, Action<string> report, CancellationToken ct = default)
    {
        var results = new List<BatchResult>();
        for (var i = 0; i < plan.Items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = plan.Items[i];
            report($"[{i + 1}/{plan.Items.Count}] {item.File}");
            if (item.Problem != null) { results.Add(new(item.File, null, item.Problem)); continue; }
            try
            {
                var o = template.Clone();
                o.GamePath = item.Path;
                o.Title1 = item.Title;
                o.Title2 = null;
                o.IconPath = item.Icon!;
                o.BannerPath = item.Banner!;
                o.DrcPath = null;
                o.LogoPath = null;
                o.Game2Path = null;
                results.Add(new(item.File, await build(o), null));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { results.Add(new(item.File, null, ex.Message)); }
        }
        return results;
    }

    /// <summary>Same lookup order as the repo button of the window: the disc id, then its regional and wiitdb alternatives.</summary>
    static async Task<(byte[] Icon, byte[] Banner)?> RepoImages(string system, string repoId, string repoUrl, Func<string, Task<byte[]?>> fetch)
    {
        foreach (var id in GameTdb.GetAlternativeIds(repoId))
        {
            var dir = repoUrl + system + "/" + id + "/";
            var icon = await fetch(dir + "iconTex.png");
            if (icon == null) continue;
            var banner = await fetch(dir + "bootTvTex.png");
            if (banner != null) return (icon, banner);
        }
        return null;
    }

    /// <summary>Text of the final dialog.</summary>
    public static string Summary(IReadOnlyList<BatchResult> results)
    {
        var ok = results.Count(r => r.Package != null);
        var sb = new StringBuilder($"{ok} of {results.Count} games converted.");
        foreach (var r in results.Where(r => r.Package == null)) sb.Append($"\n- {r.File}: {r.Problem}");
        return sb.ToString();
    }
}
