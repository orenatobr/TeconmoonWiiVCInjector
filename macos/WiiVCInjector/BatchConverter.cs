using System.Text;
using System.Text.RegularExpressions;

namespace WiiVCInjector;

/// <summary>Outcome of one game of a batch: <see cref="Package"/> is set on success, <see cref="Problem"/> otherwise.</summary>
public sealed record BatchResult(string File, string? Package, string? Problem);

/// <summary>
/// Converts several Wii or GameCube games in one go. Every game is built with a copy of the same options (so the GamePad
/// Emulation Mode and the other advanced switches chosen in the window apply to all of them), and its icon and banner
/// always come from Cucholix's repo: a game the repo has no images for is skipped, never built with placeholders.
/// </summary>
public static partial class BatchConverter
{
    [GeneratedRegex("^[A-Za-z0-9]{4,6}$")]
    private static partial Regex GameId();

    /// <summary>Builds every game of <paramref name="games"/> in order; a failure only affects its own game.</summary>
    /// <param name="template">Options of the window. System, DrcUse, NfsPatch and the advanced flags are kept for all games.</param>
    /// <param name="repoUrl">Base URL of the images repo (AppSettings.BannersRepository).</param>
    /// <param name="fetch">Returns the bytes of a repo file, or null when the repo does not have it.</param>
    /// <param name="build">Runs one build and returns the package folder.</param>
    /// <param name="report">Receives one line per step, e.g. "[2/5] Super Mario Galaxy".</param>
    public static async Task<List<BatchResult>> RunAsync(BuildOptions template, IReadOnlyList<string> games, string repoUrl,
        Func<string, Task<byte[]?>> fetch, Func<BuildOptions, Task<string>> build, Action<string> report, CancellationToken ct = default)
    {
        var results = new List<BatchResult>();
        var work = Path.Combine(Path.GetTempPath(), "WiiVCInjector-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            for (var i = 0; i < games.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(games[i]);
                report($"[{i + 1}/{games.Count}] {name}");
                try { results.Add(await ConvertOne(template, games[i], repoUrl, fetch, build, work)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { results.Add(new BatchResult(name, null, ex.Message)); }
            }
        }
        finally { try { Directory.Delete(work, true); } catch { } }
        return results;
    }

    static async Task<BatchResult> ConvertOne(BuildOptions template, string path, string repoUrl,
        Func<string, Task<byte[]?>> fetch, Func<BuildOptions, Task<string>> build, string work)
    {
        var name = Path.GetFileName(path);
        GameHeader h;
        using (var fs = File.OpenRead(path)) h = GameHeader.Parse(fs);
        var wantType = template.System == "gcn" ? GameHeader.GcType : GameHeader.WiiType;
        if (h.GameType != wantType) return new(name, null, $"not a {(template.System == "gcn" ? "GameCube" : "Wii")} image");
        if (h.Nkit || h.Nasos) return new(name, null, "NKit and NASOS images are not supported, convert it to a plain ISO or WBFS first");
        // the id goes into the repo URL, so only accept what a real disc id looks like
        if (!GameId().IsMatch(h.RepoId)) return new(name, null, "the disc id is not valid");

        var images = await RepoImages(template.System, h.RepoId, repoUrl, fetch);
        if (images == null) return new(name, null, "Cucholix's repo has no icon and banner for this game");

        var icon = Path.Combine(work, "iconTex.png");
        var banner = Path.Combine(work, "bootTvTex.png");
        File.WriteAllBytes(icon, images.Value.Icon);
        File.WriteAllBytes(banner, images.Value.Banner);

        var o = template.Clone();
        var title = StringUtil.RemoveSpecialChars(GameTdb.GetName(h.RepoId));
        o.GamePath = path;
        o.Title1 = !string.IsNullOrEmpty(title) ? title : h.InternalName;
        o.Title2 = null;
        o.IconPath = icon;
        o.BannerPath = banner;
        o.DrcPath = null;
        o.LogoPath = null;
        o.Game2Path = null;
        return new(name, await build(o), null);
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
