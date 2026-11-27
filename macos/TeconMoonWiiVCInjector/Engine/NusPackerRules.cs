using System.IO.Compression;
using System.Text;

namespace TeconMoonWiiVCInjector;

/// <summary>
/// The bundled NUSPacker (0.3-i) packs only the files its built-in content rules know and stops with "The file ... is not
/// assigned to any content" for any other file under code/. The cafe2wii option adds five such files (c2w.img, boot.bin,
/// dmcu.d.hex, deint.txt, font.bin), so the build failed with "NUSPacker did not produce title.tmd". This makes a copy of the
/// jar whose .rpx/.rpl rule also matches them (a class file keeps its strings in a constant pool, so replacing a string needs
/// no change in the code); each of them then gets a content of its own, like the .rpx and .rpl files. The other rules stay
/// untouched, in particular code/rvlt.tik and code/rvlt.tmd, which nfs2iso2nfs writes for every Wii game.
/// </summary>
public static class NusPackerRules
{
    const string RulesClass = "com/tim/nuspacker/nuspackage/packaging/ContentRules.class";

    /// <summary>Old rule pattern to the pattern that replaces it.</summary>
    public static readonly (string Old, string New)[] Cafe2wi =
    {
        ("/code/.*(.rpx|.rpl)", "/code/.*(.rpx|.rpl|c2w.img|boot.bin|dmcu.d.hex|deint.txt|font.bin)"),
    };

    /// <summary>Writes a copy of <paramref name="jar"/> with the cafe2wii rules and returns its path.</summary>
    public static string Cafe2wiJar(string jar, string dir)
    {
        var dest = Path.Combine(dir, "NUSPacker-cafe2wii.jar");
        File.Delete(dest);
        using (var src = ZipFile.OpenRead(jar))
        using (var zip = ZipFile.Open(dest, ZipArchiveMode.Create))
        {
            if (src.GetEntry(RulesClass) == null) throw new InvalidDataException("NUSPacker.jar has no " + RulesClass);
            foreach (var e in src.Entries)
            {
                // copy every entry (a directory entry has no data), the rules class with its strings replaced
                var copy = zip.CreateEntry(e.FullName, CompressionLevel.Optimal);
                if (e.FullName.EndsWith('/')) continue;
                using var inp = e.Open(); using var outp = copy.Open();
                if (e.FullName != RulesClass) { inp.CopyTo(outp); continue; }
                using var ms = new MemoryStream(); inp.CopyTo(ms);
                outp.Write(ReplaceUtf8(ms.ToArray(), Cafe2wi));
            }
        }
        return dest;
    }

    /// <summary>Replaces whole CONSTANT_Utf8 strings of a Java class file. Every replaced string must be present.</summary>
    public static byte[] ReplaceUtf8(byte[] cls, IEnumerable<(string Old, string New)> map)
    {
        var want = map.ToDictionary(m => m.Old, m => m.New);
        if (cls.Length < 10 || cls[0] != 0xCA || cls[1] != 0xFE || cls[2] != 0xBA || cls[3] != 0xBE)
            throw new InvalidDataException("not a Java class file");
        int count = (cls[8] << 8) | cls[9];
        var o = new MemoryStream();
        o.Write(cls, 0, 10);
        int p = 10, i = 1;
        while (i < count)
        {
            int tag = cls[p];
            int len = tag switch
            {
                1 => 3 + ((cls[p + 1] << 8) | cls[p + 2]),
                3 or 4 or 9 or 10 or 11 or 12 or 17 or 18 => 5,
                5 or 6 => 9,
                7 or 8 or 16 or 19 or 20 => 3,
                15 => 4,
                _ => throw new InvalidDataException("unknown constant pool tag " + tag),
            };
            if (tag == 1 && want.Remove(Encoding.UTF8.GetString(cls, p + 3, len - 3), out var repl))
            {
                var b = Encoding.UTF8.GetBytes(repl);
                o.WriteByte(1); o.WriteByte((byte)(b.Length >> 8)); o.WriteByte((byte)b.Length); o.Write(b);
            }
            else o.Write(cls, p, len);
            p += len;
            i += tag is 5 or 6 ? 2 : 1;
        }
        if (want.Count > 0) throw new InvalidDataException("rule pattern not found in NUSPacker: " + string.Join(", ", want.Keys));
        o.Write(cls, p, cls.Length - p);
        return o.ToArray();
    }
}
