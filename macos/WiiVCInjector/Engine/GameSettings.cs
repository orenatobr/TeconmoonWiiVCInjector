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
    /// <summary>The video modes a game can be forced to: Auto leaves it to the game, PAL50 keeps a PAL game at 50 Hz. Keys of this app, Dolphin has none.</summary>
    public static readonly string[] VideoModes = { "Auto", "PAL50", "PAL60", "NTSC", "MPAL" };
    /// <summary>VideoMode: one of <see cref="VideoModes"/>.</summary>
    public string? VideoMode;
    /// <summary>Deflicker: with a forced VideoMode, the GX deflicker filter on or off. Off is sharper on interlaced output.</summary>
    public bool? Deflicker;
    /// <summary>Widescreen (a key of this app, Dolphin has none): false keeps the widescreen cheats (names with "widescreen", "16:9" or "aspect") out of this game's Gecko file on the card, so the Wii U only stretches the 4:3 picture to 16:9. For a widescreen code that leaves bars or shifts the picture; the other cheats of a saved cheat list stay (a game with only a .gct and no saved list gets no file).</summary>
    public bool? Widescreen;
    /// <summary>VideoScale, 40 to 120: the width of the picture is 600 plus this many of the 720 pixels (Nintendont's "Video Width"; 40 is the normal 640). A key of this app. Wider fills a screen that shows side bars.</summary>
    public int? VideoScale;
    /// <summary>VideoOffset, -20 to 20: moves the picture left (negative) or right (Nintendont's "Video Offset"). A key of this app. For a picture shifted to one side.</summary>
    public int? VideoOffset;

    public bool IsEmpty => Fullscreen == null && WidescreenHack == null && ProgressiveScan == null && SkipIpl == null && PatchPal50 == null && VideoMode == null && Deflicker == null && Widescreen == null && VideoScale == null && VideoOffset == null;

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
            else if (key.Equals("Deflicker", StringComparison.OrdinalIgnoreCase) && Bool(val) is { } df) s.Deflicker = df;
            else if (key.Equals("Widescreen", StringComparison.OrdinalIgnoreCase) && Bool(val) is { } cd) s.Widescreen = cd;
            else if (key.Equals("VideoScale", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var vs) && vs is >= 40 and <= 120) s.VideoScale = vs;
            else if (key.Equals("VideoOffset", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var vo) && vo is >= -20 and <= 20) s.VideoOffset = vo;
            else if (key.Equals("VideoMode", StringComparison.OrdinalIgnoreCase) && VideoModes.FirstOrDefault(m => m.Equals(val, StringComparison.OrdinalIgnoreCase)) is { } vm) s.VideoMode = vm;
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
        if (PatchPal50 != null || VideoMode != null || Deflicker != null || Widescreen != null || VideoScale != null || VideoOffset != null)
        {
            sb.Append("\n[Teconmoon]\n");
            if (PatchPal50 != null) sb.Append($"PatchPAL50 = {B(PatchPal50.Value)}\n");
            if (VideoMode != null) sb.Append($"VideoMode = {VideoMode}\n");
            if (Deflicker != null) sb.Append($"Deflicker = {B(Deflicker.Value)}\n");
            if (Widescreen != null) sb.Append($"Widescreen = {B(Widescreen.Value)}\n");
            if (VideoScale != null) sb.Append($"VideoScale = {VideoScale.Value}\n");
            if (VideoOffset != null) sb.Append($"VideoOffset = {VideoOffset.Value}\n");
        }
        return sb.ToString();
    }

    /// <summary>True when the settings of this game (first 4 characters of <paramref name="id"/>) say Widescreen = False.</summary>
    public static bool WidescreenOff(string id, string? library = null)
    {
        var id4 = Id4(id);
        var path = id4 == null ? null : Path.Combine(library ?? LibraryDir, id4 + ".ini");
        return path != null && File.Exists(path) && Parse(File.ReadAllText(path)).Widescreen == false;
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
