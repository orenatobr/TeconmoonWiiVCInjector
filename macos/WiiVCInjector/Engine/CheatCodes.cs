using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace WiiVCInjector;

/// <summary>One cheat: a name and its Gecko code words (address and value pairs) as Nintendont's code handler runs them.</summary>
public sealed class Cheat
{
    public string Name = "";
    /// <summary>Where it came from: "Gecko" or "Action Replay" (Dolphin game settings), or "Pasted".</summary>
    public string Source = "";
    public List<(uint Address, uint Value)> Lines = new();
    /// <summary>Why Nintendont cannot run it, or null when it can.</summary>
    public string? Unusable;
}

/// <summary>
/// Gecko and Action Replay codes for a GameCube game: read from Dolphin's game settings (<c>GameSettings/&lt;id&gt;.ini</c>) or from text
/// copied off the game's Dolphin wiki page, and written as the <c>/codes/&lt;id6&gt;.gct</c> file the Teconmoon Nintendont loads.
/// </summary>
public static partial class CheatCodes
{
    /// <summary>Dolphin's own game settings, where the codes that ship with the emulator live (open data, no bot check).</summary>
    public const string GameSettingsBase = "https://raw.githubusercontent.com/dolphin-emu/dolphin/master/Data/Sys/GameSettings/";

    /// <summary>
    /// The Gecko code database the Wii loaders (USB Loader GX, WiiFlow) download from, one text file per 6 character game id. It has no bot check.
    /// Only the public disc id is sent. Codes depend on the disc's region, so a game only gets codes when its own id is listed.
    /// </summary>
    public const string CodesTxtBase = "https://codes.rc24.xyz/txt.php?txt=";

    public const string WikiBase = "https://wiki.dolphin-emu.org/index.php?title=";

    [GeneratedRegex(@"^\s*([0-9A-Fa-f]{8})\s+([0-9A-Fa-f]{8})\s*$")]
    private static partial Regex PairRx();

    [GeneratedRegex(@"\b60\s*Hz\b", RegexOptions.IgnoreCase)]
    private static partial Regex Hz60Rx();

    /// <summary>The 6 character id Nintendont names the .gct after (game id plus maker code), safe to use in a file name.</summary>
    public static bool IsValidId(string id) => id.Length == 6 && id.All(char.IsAsciiLetterOrDigit);

    /// <summary>GameCube region letters that are PAL (the 4th id character).</summary>
    public static bool IsPal(string id) => id.Length > 3 && "PDFISHUXYZ".Contains(id[3]);

    /// <summary>
    /// A note for a 60 Hz code on a PAL disc. Nintendont's Force Progressive, which switches the video mode behind the game's back, was measured to delay
    /// the sound (AUDIO-DELAY.md). A code that changes the game's own video mode was not measured, so this only says to listen for it.
    /// </summary>
    public static string? Warning(Cheat c, string id) =>
        IsPal(id) && Hz60Rx().IsMatch(c.Name) ? "60 Hz code on a PAL disc. Nintendont's own 60 Hz forcing delayed the sound; this code was not measured, so check the audio (see AUDIO-DELAY.md)." : null;

    /// <summary>The page of the game on the Dolphin wiki, which the compatibility list links to and which holds the codes people wrote.</summary>
    public static string WikiUrl(string gameName) => WikiBase + Uri.EscapeDataString(gameName.Trim().Replace(' ', '_'));

    /// <summary>Where the app keeps the .gct files it made, one per game.</summary>
    public static string LibraryDir => Path.Combine(Path.GetDirectoryName(AppSettings.FilePath)!, "codes");

    public static string GctPath(string id) => Path.Combine(LibraryDir, id + ".gct");

    /// <summary>The ticked cheats of a Wii game as text (read back by <see cref="ParseText"/>), which the build writes into main.dol.</summary>
    public static string CodesTextPath(string id) => Path.Combine(LibraryDir, id + ".codes.txt");

    public static string ToText(IEnumerable<Cheat> cheats) =>
        string.Concat(cheats.Where(c => c.Unusable == null).Select(c => "$" + c.Name + "\n" + string.Concat(c.Lines.Select(l => $"{l.Address:X8} {l.Value:X8}\n"))));

    /// <summary>
    /// Action Replay "normal" codes that write 8, 16 or 32 bits (first byte 00 to 05) have the same words as the Gecko 00, 02 and 04 codes,
    /// so they pass through. The other Action Replay types (conditional, fill, master code) have no Gecko equivalent here.
    /// </summary>
    static bool IsPlainWrite(uint address) => (address >> 24) <= 0x05;

    /// <summary>Cheats of the [Gecko] and [ActionReplay] sections of a Dolphin game settings file.</summary>
    public static List<Cheat> ParseIni(string text)
    {
        var cheats = new List<Cheat>();
        string? kind = null;
        Cheat? cur = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                kind = line switch { "[Gecko]" => "Gecko", "[ActionReplay]" => "Action Replay", _ => null };
                cur = null;
            }
            else if (kind == null || line.Length == 0 || line[0] == '*') continue;
            else if (line[0] == '$')
            {
                cur = new Cheat { Name = line[1..].Trim(), Source = kind };
                cheats.Add(cur);
            }
            else if (cur != null && PairRx().Match(line) is { Success: true } m)
            {
                var a = Convert.ToUInt32(m.Groups[1].Value, 16);
                cur.Lines.Add((a, Convert.ToUInt32(m.Groups[2].Value, 16)));
                if (kind == "Action Replay" && !IsPlainWrite(a))
                    cur.Unusable = "This Action Replay code type has no Gecko equivalent.";
            }
        }
        return cheats.Where(c => c.Lines.Count > 0).ToList();
    }

    /// <summary>
    /// Cheats from text copied off a wiki page or a forum: each run of code lines is one cheat, named by the lines above it
    /// ("$60Hz", "Normal HUD", "NA:"). Pasted codes are Gecko codes.
    /// </summary>
    public static List<Cheat> ParseText(string text)
    {
        var cheats = new List<Cheat>();
        Cheat? cur = null;
        var label = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (PairRx().Match(line) is { Success: true } m)
            {
                if (cur == null)
                {
                    cur = new Cheat { Name = label.Length > 0 ? label : "Pasted code", Source = "Pasted" };
                    cheats.Add(cur);
                    label = "";
                }
                cur.Lines.Add((Convert.ToUInt32(m.Groups[1].Value, 16), Convert.ToUInt32(m.Groups[2].Value, 16)));
            }
            else
            {
                var name = line.TrimStart('$').TrimEnd(':').Trim();
                label = label.Length > 0 ? label + " " + name : name;
                cur = null;
            }
        }
        return cheats;
    }

    /// <summary>The .gct file: the 00D0C0DE header, every usable cheat, the F0000000 end marker.</summary>
    public static byte[] BuildGct(IEnumerable<Cheat> cheats)
    {
        var ms = new MemoryStream();
        void W(uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); ms.Write(b); }
        W(0x00D0C0DE); W(0x00D0C0DE);
        foreach (var c in cheats.Where(c => c.Unusable == null))
            foreach (var (a, v) in c.Lines) { W(a); W(v); }
        W(0xF0000000); W(0);
        return ms.ToArray();
    }

    /// <summary>
    /// Cheats of a code database text file: blocks split by blank lines, the first line of a block is the name, the lines that are two groups
    /// of 8 hex digits are the codes, and anything else in the block (Note:, Limitation:, Contact:, Video:) is dropped.
    /// The first block (game id and title) has no codes and is skipped.
    /// </summary>
    public static List<Cheat> ParseCodesTxt(string text)
    {
        var cheats = new List<Cheat>();
        Cheat? cur = null;
        var named = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) { cur = null; named = false; continue; }
            if (PairRx().Match(line) is { Success: true } m)
            {
                if (cur == null) { cur = new Cheat { Name = "Unnamed code", Source = "Gecko code database" }; cheats.Add(cur); }
                cur.Lines.Add((Convert.ToUInt32(m.Groups[1].Value, 16), Convert.ToUInt32(m.Groups[2].Value, 16)));
            }
            else if (!named)
            {
                named = true;
                cur = new Cheat { Name = line, Source = "Gecko code database" };
                cheats.Add(cur);
            }
        }
        return cheats.Where(c => c.Lines.Count > 0).ToList();
    }

    /// <summary>
    /// Downloads the cheats listed for a game: Dolphin's game settings (the 3 character file for all regions and the 6 character file) and the Gecko
    /// code database. A missing file is skipped; a source that cannot be reached is skipped too unless nothing at all could be read, then the error is thrown.
    /// </summary>
    public static async Task<List<Cheat>> DownloadAsync(string id, HttpClient http, CancellationToken ct = default)
    {
        var cheats = new List<Cheat>();
        Exception? failure = null;
        var reached = false;
        foreach (var (url, parse) in new (string, Func<string, List<Cheat>>)[]
        {
            (GameSettingsBase + id[..3] + ".ini", ParseIni), (GameSettingsBase + id + ".ini", ParseIni), (CodesTxtBase + id, ParseCodesTxt),
        })
        {
            try
            {
                using var r = await http.GetAsync(url, ct);
                reached = true;
                if (r.IsSuccessStatusCode) cheats.AddRange(parse(await r.Content.ReadAsStringAsync(ct)));
            }
            catch (HttpRequestException ex) { failure = ex; }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { failure = ex; }
        }
        if (!reached && failure != null) throw failure;
        return cheats;
    }

    /// <summary>Copies every .gct of <paramref name="library"/> to <c>codes/</c> on the card. Returns how many files.</summary>
    public static int CopyLibraryTo(string drive, string? library = null)
    {
        library ??= LibraryDir;
        if (!Directory.Exists(library)) return 0;
        var files = Directory.GetFiles(library, "*.gct");
        if (files.Length == 0) return 0;
        var dest = Path.Combine(drive, "codes");
        Directory.CreateDirectory(dest);
        foreach (var f in files) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
        return files.Length;
    }
}
