using System.Text;

namespace WiiVCInjector;

/// <summary>
/// Settings of one GameCube game, kept in the format of Dolphin's GameINI and read by the Teconmoon Nintendont from <c>/teconmoon/&lt;id4&gt;.ini</c> on the card.
/// A null value means "leave the Nintendont setting as it is". The keys are the ones <c>loader/source/GameSettings.c</c> reads; keep both in step.
/// </summary>
public sealed class GameSettings
{
    /// <summary>AspectRatio 1 (true) or 2 (false): the Wii U shows the game on the whole 16:9 screen (Nintendont's "WiiU Widescreen").</summary>
    public bool? Fullscreen;
    /// <summary>wideScreenHack: the game draws in 16:9 where Nintendont's patch finds the game's projection code (Nintendont's "Force Widescreen").</summary>
    public bool? WidescreenHack;
    /// <summary>ProgressiveScan (Nintendont's "Force Progressive"). A PAL disc ignores it, see AUDIO-DELAY.md.</summary>
    public bool? ProgressiveScan;
    /// <summary>SkipIPL (Nintendont's "Skip IPL").</summary>
    public bool? SkipIpl;
    /// <summary>PatchPAL50 (Nintendont's "Patch PAL50"): a PAL game's 50 Hz mode becomes 60 Hz. A key of this app, Dolphin has none.</summary>
    public bool? PatchPal50;

    public bool IsEmpty => Fullscreen == null && WidescreenHack == null && ProgressiveScan == null && SkipIpl == null && PatchPal50 == null;

    /// <summary>The file is named by the first 4 characters of the disc id, which also carry the region.</summary>
    public static string? Id4(string id) => id.Length >= 4 && id[..4].All(char.IsAsciiLetterOrDigit) ? id[..4] : null;

    public static string LibraryDir => Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "gamesettings");

    public static string PathFor(string id4) => Path.Combine(LibraryDir, id4 + ".ini");

    static bool? Bool(string v) => v.Trim().ToLowerInvariant() switch { "true" or "1" or "yes" => true, "false" or "0" or "no" => false, _ => null };

    /// <summary>Reads the known keys of a GameINI text (sections are ignored, every other line is skipped, the last value of a key wins).</summary>
    public static GameSettings Parse(string text)
    {
        var s = new GameSettings();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (line.Length == 0 || line[0] is '#' or ';' or '[' || eq < 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            if (key.Equals("AspectRatio", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(val, out var a) && a is 1 or 2) s.Fullscreen = a == 1;
            }
            else if (key.Equals("wideScreenHack", StringComparison.OrdinalIgnoreCase) && Bool(val) is { } w) s.WidescreenHack = w;
            else if (key.Equals("ProgressiveScan", StringComparison.OrdinalIgnoreCase) && Bool(val) is { } p) s.ProgressiveScan = p;
            else if (key.Equals("SkipIPL", StringComparison.OrdinalIgnoreCase) && Bool(val) is { } i) s.SkipIpl = i;
            else if (key.Equals("PatchPAL50", StringComparison.OrdinalIgnoreCase) && Bool(val) is { } p50) s.PatchPal50 = p50;
        }
        return s;
    }

    static string B(bool v) => v ? "True" : "False";

    /// <summary>The GameINI text for this game; only the settings that are set are written.</summary>
    public string ToIni(string id, string name)
    {
        var sb = new StringBuilder();
        sb.Append($"# {id} - {name}\n# Per game settings in Dolphin's GameINI format, read by the Teconmoon Nintendont. A line that is missing keeps the Nintendont setting.\n");
        if (Fullscreen != null || WidescreenHack != null)
        {
            sb.Append("\n[Video_Settings]\n");
            if (Fullscreen != null) sb.Append($"AspectRatio = {(Fullscreen == true ? 1 : 2)}\n");
            if (WidescreenHack != null) sb.Append($"wideScreenHack = {B(WidescreenHack.Value)}\n");
        }
        if (ProgressiveScan != null || SkipIpl != null)
        {
            sb.Append("\n[Core]\n");
            if (ProgressiveScan != null) sb.Append($"ProgressiveScan = {B(ProgressiveScan.Value)}\n");
            if (SkipIpl != null) sb.Append($"SkipIPL = {B(SkipIpl.Value)}\n");
        }
        if (PatchPal50 != null) sb.Append($"\n[Teconmoon]\nPatchPAL50 = {B(PatchPal50.Value)}\n");
        return sb.ToString();
    }

    /// <summary>Copies every settings file of <paramref name="library"/> to <c>teconmoon/</c> on the card. Returns how many files.</summary>
    public static int CopyLibraryTo(string drive, string? library = null)
    {
        library ??= LibraryDir;
        if (!Directory.Exists(library)) return 0;
        var files = Directory.GetFiles(library, "*.ini");
        if (files.Length == 0) return 0;
        var dest = Path.Combine(drive, "teconmoon");
        Directory.CreateDirectory(dest);
        foreach (var f in files) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
        return files.Length;
    }
}
