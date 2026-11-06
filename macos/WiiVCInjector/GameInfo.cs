using System.Reflection;
using System.Text;

namespace WiiVCInjector;

/// <summary>Result of reading a Wii/GC/WBFS/NKit/NASOS/DOL header the way Form1.GameSourceButton_Click does.</summary>
public sealed class GameHeader
{
    public bool Wbfs, Nkit, Nasos, IsDol;
    public long GameType;
    public string InternalName = "";
    /// <summary>Game id as text (CucholixRepoID), e.g. RSPE01.</summary>
    public string RepoId = "";
    /// <summary>First four id bytes as hex (TitleIDHex), e.g. 52535045.</summary>
    public string IdHex = "";

    public const long WiiType = 2745048157, GcType = 4440324665927270400;

    public static GameHeader Parse(Stream s)
    {
        var h = new GameHeader();
        var first = ReadAt(s, 0, 4);
        string idString = Encoding.ASCII.GetString(first);
        long start = 0;
        if (idString == "WBFS")
        {
            h.Wbfs = true;
            start = 0x200;
            h.GameType = ReadI64(s, 0x218);
            h.InternalName = ReadCString(s, 0x220);
        }
        else if (first.AsSpan().SequenceEqual(new byte[] { 0, 0, 1, 0 }))
        {
            h.IsDol = true;
            h.InternalName = "N/A";
            start = 0x2A0;
            h.IdHex = Convert.ToHexString(ReadAt(s, start, 4));
            return h;
        }
        else
        {
            if (idString == "WII5") { h.Nasos = true; start = 0x1182800; }
            else if (idString == "WII9") { h.Nasos = true; start = 0x1FB5000; }
            h.GameType = ReadI64(s, start + 0x18);
            h.InternalName = ReadCString(s, start + 0x20);
            if (!h.Nasos && Encoding.ASCII.GetString(ReadAt(s, 0x200, 4)) == "NKIT") h.Nkit = true;
        }
        h.RepoId = ReadCString(s, start);
        h.IdHex = Convert.ToHexString(ReadAt(s, start, 4));
        return h;
    }

    static long ReadI64(Stream s, long pos) => BitConverter.ToInt64(ReadAt(s, pos, 8));

    static byte[] ReadAt(Stream s, long pos, int n)
    {
        var b = new byte[n];
        if (pos + n > s.Length) return b;
        s.Position = pos;
        s.ReadExactly(b);
        return b;
    }

    static string ReadCString(Stream s, long pos)
    {
        var sb = new StringBuilder();
        if (pos >= s.Length) return "";
        s.Position = pos;
        int c;
        while ((c = s.ReadByte()) > 0 && sb.Length < 256) sb.Append((char)c);
        return sb.ToString();
    }

    /// <summary>"RSPE" from "52535045" (TitleIDText).</summary>
    public static string IdText(string hex) =>
        string.Concat(Enumerable.Range(0, hex.Length / 2).Select(i => (char)Convert.ToByte(hex.Substring(i * 2, 2), 16)));
}

public static class StringUtil
{
    public static string RemoveSpecialChars(string? v)
    {
        if (string.IsNullOrEmpty(v)) return v ?? "";
        var n = v.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in n)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark && c < 128)
                sb.Append(c);
        return sb.ToString();
    }

    public static string ReplaceAt(this string s, int i, char c) { var a = s.ToCharArray(); a[i] = c; return new string(a); }
}

/// <summary>wiitdb.txt lookups (embedded resource), same logic as the original GameTdb.</summary>
public static class GameTdb
{
    static readonly Lazy<List<(string Id, string Name)>> Db = new(() =>
    {
        var list = new List<(string, string)>();
        using var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("wiitdb.txt");
        if (st == null) return list;
        using var r = new StreamReader(st);
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            var i = line.IndexOf(" = ", StringComparison.Ordinal);
            if (i > 0) list.Add((line[..i], line[(i + 3)..]));
        }
        return list;
    });

    public static string? GetName(string id) => Db.Value.FirstOrDefault(e => e.Id == id).Name;
    public static List<string> GetIds(string? name) => name == null ? new() : Db.Value.Where(e => e.Name == name).Select(e => e.Id).ToList();
    public static List<string> GetIdsStartingWith(string s) =>
        Db.Value.Where(e => e.Id != "TITLES" && e.Id.StartsWith(s)).Select(e => e.Id).ToList();

    public static IEnumerable<string> GetAlternativeIds(string initialId)
    {
        var tried = new HashSet<string> { initialId, initialId.ReplaceAt(3, 'E'), initialId.ReplaceAt(3, 'P') };
        foreach (var id in tried) yield return id;
        var ids = GetIds(GetName(initialId)).Where(i => !tried.Contains(i)).ToList();
        foreach (var id in ids) yield return id;
        tried.UnionWith(ids);
        foreach (var id in GetIdsStartingWith(initialId[..3]).Where(i => !tried.Contains(i))) yield return id;
    }
}
