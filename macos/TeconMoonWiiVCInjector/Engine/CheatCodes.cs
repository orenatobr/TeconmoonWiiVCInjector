using System.Buffers.Binary;
using System.Text.RegularExpressions;

namespace TeconMoonWiiVCInjector;

/// <summary>One cheat: a name and its Gecko code words (address and value pairs) as Nintendont's code handler runs them.</summary>
public sealed class Cheat
{
    public string Name = "";
    /// <summary>Where it came from: "Gecko" or "Action Replay" (Dolphin game settings), or "Pasted".</summary>
    public string Source = "";
    public List<(uint Address, uint Value)> Lines = new();
    /// <summary>"NA", "PAL" or "JP" when the text it came from labels the codes for one region, else null (fits any).</summary>
    public string? Region;
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

    /// <summary>A code line: two groups of 8 hex digits, optionally followed by a <c>//</c> comment (people annotate their codes that way).</summary>
    [GeneratedRegex(@"^\s*([0-9A-Fa-f]{8})\s+([0-9A-Fa-f]{8})\s*(?://.*)?$")]
    private static partial Regex PairRx();

    [GeneratedRegex(@"^(NA|US|USA|NTSC-U|NTSC|PAL|EU|EUR|JP|JPN|JAP|NTSC-J)\s*:?$", RegexOptions.IgnoreCase)]
    private static partial Regex RegionRx();

    [GeneratedRegex(@"\b60\s*Hz\b", RegexOptions.IgnoreCase)]
    private static partial Regex Hz60Rx();

    /// <summary>A frame rate code by its name: "60 FPS", "60FPS", "30-fps"...</summary>
    [GeneratedRegex(@"\b\d{2,3}\s*-?\s*fps\b", RegexOptions.IgnoreCase)]
    private static partial Regex FpsRx();

    /// <summary>The 6 character id Nintendont names the .gct after (game id plus maker code), safe to use in a file name.</summary>
    public static bool IsValidId(string id) => id.Length == 6 && id.All(char.IsAsciiLetterOrDigit);

    /// <summary>GameCube region letters that are PAL (the 4th id character).</summary>
    public static bool IsPal(string id) => id.Length > 3 && "PDFISHUXYZ".Contains(id[3]);

    /// <summary>
    /// A note for a 60 Hz code on a PAL disc. Nintendont's Force Progressive, which switches the video mode behind the game's back, was measured to delay
    /// the sound (AUDIO-DELAY.md). A code that changes the game's own video mode was not measured, so this only says to listen for it.
    /// </summary>
    public static string? Warning(Cheat c, string id, bool wii = false) =>
        IsPal(id) && Hz60Rx().IsMatch(c.Name) ? "60 Hz code on a PAL disc. Nintendont's own 60 Hz forcing delayed the sound; this code was not measured, so check the audio (see AUDIO-DELAY.md)."
        : wii && FpsRx().IsMatch(c.Name) ? "A frame rate code needs CPU to spare. At the vWii's normal speed the game can run in slow motion instead of at 60 FPS (the Dolphin wiki says this kind of code needs more than 125% CPU). The cafe2wii patching option (Advanced tab, needs your Starbuck ancast key) unlocks the 1.215 GHz Wii U speed; it also needs the c2w_patcher Aroma plugin."
        : null;

    /// <summary>
    /// The name to show and to look up on the wiki: the database name when there is one (it has the proper capitals, like the compat list),
    /// else the name in the disc header, which many games write in capitals ("ARC RISE FANTASIA"), turned into Title Case.
    /// </summary>
    public static string DisplayName(string? databaseName, string headerName, string id)
    {
        if (!string.IsNullOrWhiteSpace(databaseName)) return databaseName;
        if (string.IsNullOrWhiteSpace(headerName)) return id;
        return headerName.Any(char.IsLower) ? headerName : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(headerName.ToLowerInvariant());
    }

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

    /// <summary>
    /// True when every line is a plain write or part of a well formed Gecko insert assembly block (C2 or C3 followed by as many pairs as its value says).
    /// Some authors put Gecko words in Dolphin's [ActionReplay] section (the 16:9 codes with assembly do); a real Action Replay code does not use C2 or C3,
    /// so a block that is exactly shaped like that is a Gecko code and Nintendont's handler runs it.
    /// </summary>
    static bool IsGeckoShaped(List<(uint Address, uint Value)> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var a = lines[i].Address;
            if (IsPlainWrite(a)) continue;
            var n = lines[i].Value;
            if ((a >> 24) is not (0xC2 or 0xC3) || n == 0 || n > lines.Count - 1 - i) return false;
            i += (int)n;
        }
        return true;
    }

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
            else if (kind == null || line.Length == 0 || line[0] == '*' || line.StartsWith("//")) continue;
            else if (line[0] == '$')
            {
                cur = new Cheat { Name = line[1..].Trim(), Source = kind };
                cheats.Add(cur);
            }
            else if (cur != null && PairRx().Match(line) is { Success: true } m)
            {
                var a = Convert.ToUInt32(m.Groups[1].Value, 16);
                cur.Lines.Add((a, Convert.ToUInt32(m.Groups[2].Value, 16)));
            }
        }
        foreach (var c in cheats.Where(c => c.Source == "Action Replay" && !IsGeckoShaped(c.Lines)))
            c.Unusable = c.Lines.Any(l => (l.Address >> 24) is 0xC2 or 0xC3)
                ? "This code looks like a Gecko assembly block but it is cut short or malformed."
                : "This Action Replay code type has no Gecko equivalent.";
        return cheats.Where(c => c.Lines.Count > 0).ToList();
    }

    static string? NormalizeRegion(string label) => label.TrimEnd(':').Trim().ToUpperInvariant() switch
    {
        "NA" or "US" or "USA" or "NTSC-U" or "NTSC" => "NA",
        "PAL" or "EU" or "EUR" => "PAL",
        "JP" or "JPN" or "JAP" or "NTSC-J" => "JP",
        _ => null,
    };

    /// <summary>The region of a disc, from the 4th character of its id: E is NA, J is JP, the PAL letters are PAL, others (Korea, demos) are unknown.</summary>
    public static string? RegionOf(string id) => id.Length < 4 ? null : id[3] == 'E' ? "NA" : id[3] == 'J' ? "JP" : IsPal(id) ? "PAL" : null;

    /// <summary>A line that reads like a heading rather than a sentence.</summary>
    static bool IsHeading(string line) => line.Length <= 48 && !line.EndsWith('.') && !line.Contains(". ");

    /// <summary>
    /// Cheats from text copied off a wiki page or a forum, or a whole page: every run of code lines is a cheat. A "$Name" line names it; otherwise the
    /// nearest short heading above it does ("Normal HUD"), and a region label ("NA:", "PAL:") puts the region in the name and in <see cref="Cheat.Region"/>.
    /// Other lines (sentences, menus) are ignored. Pasted codes are Gecko codes.
    /// </summary>
    public static List<Cheat> ParseText(string text)
    {
        var cheats = new List<Cheat>();
        var pending = new List<string>();
        string heading = "", name = "";
        string? region = null;
        Cheat? cur = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;
            if (PairRx().Match(line) is { Success: true } m)
            {
                if (cur == null)
                {
                    if (pending.LastOrDefault(IsHeading) is { } h) heading = h;
                    var baseName = name.Length > 0 ? name : heading.Length > 0 ? heading : "Pasted code";
                    cur = new Cheat { Name = region != null ? $"{baseName} ({region})" : baseName, Source = "Pasted", Region = region };
                    cheats.Add(cur);
                    pending.Clear(); name = ""; region = null;
                }
                cur.Lines.Add((Convert.ToUInt32(m.Groups[1].Value, 16), Convert.ToUInt32(m.Groups[2].Value, 16)));
                continue;
            }
            cur = null;
            if (line[0] == '$') name = line[1..].Trim();
            else if (RegionRx().IsMatch(line)) region = NormalizeRegion(line);
            else pending.Add(line);
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

    /// <summary>Copies one library .gct into <paramref name="dest"/>. When the game's settings say Widescreen = False the widescreen cheats are left out (read from the saved cheat list; with none saved, or nothing else in it, the card's file is removed). Returns whether a file is on the card afterwards.</summary>
    public static bool CopyFileTo(string gct, string dest, string? settingsLibrary = null, List<string>? skipped = null)
    {
        var id = Path.GetFileNameWithoutExtension(gct);
        var target = Path.Combine(dest, Path.GetFileName(gct));
        if (GameSettings.WidescreenOff(id, settingsLibrary))
        {
            skipped?.Add(id);
            var text = Path.ChangeExtension(gct, ".codes.txt");
            var rest = File.Exists(text) ? ParseText(File.ReadAllText(text)).Where(c => !IsWidescreen(c)).ToList() : new List<Cheat>();
            if (rest.Count == 0) { File.Delete(target); return false; }
            File.WriteAllBytes(target, BuildGct(rest));
            return true;
        }
        File.Copy(gct, target, true);
        return true;
    }

    static bool IsWidescreen(Cheat c) => Regex.IsMatch(c.Name, "widescreen|16[:x]9|aspect ratio", RegexOptions.IgnoreCase);

    /// <summary>Copies every .gct of <paramref name="library"/> to <c>codes/</c> on the card, except the games whose settings say Widescreen = False (those are removed from the card). Returns how many files; the ids left off are added to <paramref name="skipped"/>.</summary>
    public static int CopyLibraryTo(string drive, string? library = null, string? settingsLibrary = null, List<string>? skipped = null)
    {
        library ??= LibraryDir;
        if (!Directory.Exists(library)) return 0;
        var files = Directory.GetFiles(library, "*.gct");
        if (files.Length == 0) return 0;
        var dest = Path.Combine(drive, "codes");
        Directory.CreateDirectory(dest);
        int copied = 0;
        foreach (var f in files)
            if (CopyFileTo(f, dest, settingsLibrary, skipped)) copied++;
        return copied;
    }
}
