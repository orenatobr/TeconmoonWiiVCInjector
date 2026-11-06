using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace WiiVCInjector;

/// <summary>Result of <see cref="WupValidator.Validate"/>: Errors make the package unusable, Warnings are suspicious but not fatal.</summary>
public sealed class WupReport
{
    public List<string> Info { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Static checks of a NUS package (WUP) written by NUSPacker: TMD, ticket, every content hash (including the full
/// H0/H1/H2/H3 hash tree of hashed contents), the FST, required files, XML and TGA files, and the NFS payload header.
/// It cannot prove the console accepts the title, but it catches broken packages before they are installed.
/// </summary>
public static class WupValidator
{
    private static readonly byte[] DefaultTitleKey = Convert.FromHexString("13371337133713371337133713371337");

    private sealed record Content(uint Id, ushort Index, ushort Type, ulong Size, byte[] Hash)
    {
        public bool Hashed => (Type & 0x2) != 0;
    }

    private sealed record FstFile(string Path, ushort Content, ulong Offset, ulong Length);

    private static readonly string[] Required =
    [
        "code/app.xml", "code/cos.xml", "code/htk.bin", "code/fw.img", "code/fw.tmd", "code/rvlt.tik", "code/rvlt.tmd",
        "code/frisbiiU.rpx", "code/nn_hai_user.rpl", "meta/meta.xml", "meta/iconTex.tga", "meta/bootTvTex.tga",
        "meta/bootDrcTex.tga", "content/hif_000000.nfs",
    ];

    private static uint Be32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
    private static ushort Be16(byte[] b, int o) => (ushort)(b[o] << 8 | b[o + 1]);

    private static byte[] CbcDecrypt(byte[] key, byte[] iv, ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(data, iv, PaddingMode.None);
    }

    private static byte[] ContentIv(uint contentId)
    {
        var iv = new byte[16];
        iv[0] = (byte)(contentId >> 8);
        iv[1] = (byte)contentId;
        return iv;
    }

    /// <param name="dir">Package folder (title.tmd, title.tik, title.cert and the .app/.h3 files).</param>
    /// <param name="wiiUCommonKey">Optional Wii U common key (32 hex): used to decrypt the title key of the ticket.</param>
    /// <param name="progress">Optional percent and status callback.</param>
    public static WupReport Validate(string dir, string? wiiUCommonKey = null, Action<int, string>? progress = null)
    {
        var r = new WupReport();
        try { Run(dir, wiiUCommonKey, progress, r); }
        catch (Exception e) { r.Errors.Add($"validator crashed: {e.GetType().Name}: {e.Message}"); }
        return r;
    }

    private static void Run(string dir, string? commonKeyHex, Action<int, string>? progress, WupReport r)
    {
        foreach (var f in new[] { "title.tmd", "title.tik", "title.cert" })
            if (!File.Exists(Path.Combine(dir, f))) r.Errors.Add($"missing {f}");
        if (!r.Ok) return;

        // ---- TMD
        var tmd = File.ReadAllBytes(Path.Combine(dir, "title.tmd"));
        if (tmd.Length < 0xB04 || Be32(tmd, 0) != 0x00010004) { r.Errors.Add("title.tmd is not a Wii U TMD (signature type 0x00010004)"); return; }
        var titleId = Convert.ToHexString(tmd.AsSpan(0x18C, 8));
        int count = Be16(tmd, 0x1DE);
        if (tmd.Length < 0xB04 + 0x30 * count) { r.Errors.Add("title.tmd is truncated"); return; }
        var contents = new List<Content>();
        for (int i = 0; i < count; i++)
        {
            int o = 0xB04 + 0x30 * i;
            contents.Add(new Content(Be32(tmd, o), Be16(tmd, o + 4), Be16(tmd, o + 6),
                (ulong)Be32(tmd, o + 8) << 32 | Be32(tmd, o + 12), tmd.AsSpan(o + 16, 20).ToArray()));
        }
        r.Info.Add($"title id {titleId}, {count} contents");

        // ---- ticket
        var tik = File.ReadAllBytes(Path.Combine(dir, "title.tik"));
        if (tik.Length < 0x2A4) r.Errors.Add("title.tik is too small");
        else
        {
            var tikId = Convert.ToHexString(tik.AsSpan(0x1DC, 8));
            if (tikId != titleId) r.Errors.Add($"ticket title id {tikId} differs from the TMD {titleId}");
        }
        var titleKey = DefaultTitleKey;
        if (!string.IsNullOrWhiteSpace(commonKeyHex) && tik.Length >= 0x2A4)
        {
            var iv = new byte[16];
            Array.Copy(tik, 0x1DC, iv, 0, 8);
            var key = CbcDecrypt(Convert.FromHexString(commonKeyHex.Trim()), iv, tik.AsSpan(0x1BF, 16));
            if (key.AsSpan().SequenceEqual(DefaultTitleKey)) r.Info.Add("ticket title key decrypts with the Wii U common key");
            else { r.Errors.Add("ticket title key does not decrypt with the given Wii U common key (wrong key?)"); titleKey = key; }
        }

        // ---- contents
        int done = 0;
        foreach (var c in contents)
        {
            progress?.Invoke(done++ * 70 / Math.Max(1, count), $"Checking content {c.Id:X8}");
            var f = Path.Combine(dir, $"{c.Id:X8}.app");
            if (!File.Exists(f)) { r.Errors.Add($"missing content file {c.Id:X8}.app"); continue; }
            var len = new FileInfo(f).Length;
            if ((ulong)len != c.Size) { r.Errors.Add($"{c.Id:X8}.app is {len} bytes, the TMD says {c.Size}"); continue; }
            if (c.Hashed) CheckHashed(dir, c, titleKey, r);
            else CheckPlain(f, c, titleKey, r);
        }
        if (!r.Ok) return;

        // ---- FST and files
        progress?.Invoke(72, "Reading the file table");
        var c0 = contents.FirstOrDefault(c => c.Index == 0);
        if (c0 == null) { r.Errors.Add("no content with index 0 (the FST)"); return; }
        var fst = CbcDecrypt(titleKey, ContentIv(c0.Id), File.ReadAllBytes(Path.Combine(dir, $"{c0.Id:X8}.app")));
        var files = ParseFst(fst, r);
        if (files == null) return;
        var byPath = files.ToDictionary(f => f.Path, StringComparer.Ordinal);
        foreach (var req in Required)
            if (!byPath.ContainsKey(req)) r.Errors.Add($"the FST has no {req}");
        foreach (var f in files)
        {
            var c = contents.FirstOrDefault(x => x.Index == f.Content);
            if (c == null) { r.Errors.Add($"{f.Path} points to content {f.Content}, which is not in the TMD"); continue; }
            ulong space = c.Hashed ? c.Size / 0x10000 * 0xFC00 : c.Size;
            if (f.Offset + f.Length > space) r.Errors.Add($"{f.Path} ends after its content ({f.Offset + f.Length} > {space})");
        }
        if (!r.Ok) return;
        r.Info.Add($"FST: {files.Count} files, all required files present");

        // ---- XML, TGA, NFS
        progress?.Invoke(80, "Checking metadata");
        byte[] Read(string p, long max = long.MaxValue) => ReadFile(dir, contents, byPath[p], titleKey, max);
        CheckXml(titleId, Read("code/app.xml"), Read("code/cos.xml"), Read("meta/meta.xml"), r);
        CheckTga("meta/iconTex.tga", Read("meta/iconTex.tga", 18), byPath["meta/iconTex.tga"].Length, 128, 128, 32, r);
        CheckTga("meta/bootTvTex.tga", Read("meta/bootTvTex.tga", 18), byPath["meta/bootTvTex.tga"].Length, 1280, 720, 24, r);
        CheckTga("meta/bootDrcTex.tga", Read("meta/bootDrcTex.tga", 18), byPath["meta/bootDrcTex.tga"].Length, 854, 480, 24, r);
        progress?.Invoke(90, "Checking the NFS payload");
        CheckNfs(titleId, files, Read("code/htk.bin"), Read("content/hif_000000.nfs", 0x220), r);
        progress?.Invoke(100, "Validation finished");
    }

    // ------------------------------------------------------------------ contents

    private static void CheckPlain(string file, Content c, byte[] key, WupReport r)
    {
        var plain = CbcDecrypt(key, ContentIv(c.Id), File.ReadAllBytes(file));
        if (!SHA1.HashData(plain).AsSpan().SequenceEqual(c.Hash))
            r.Errors.Add($"{c.Id:X8}.app: SHA-1 of the decrypted data differs from the TMD (wrong title key or corrupt file)");
    }

    private static void CheckHashed(string dir, Content c, byte[] key, WupReport r)
    {
        var h3Path = Path.Combine(dir, $"{c.Id:X8}.h3");
        if (!File.Exists(h3Path)) { r.Errors.Add($"missing {c.Id:X8}.h3"); return; }
        var h3 = File.ReadAllBytes(h3Path);
        if (!SHA1.HashData(h3).AsSpan().SequenceEqual(c.Hash)) { r.Errors.Add($"{c.Id:X8}.h3: SHA-1 differs from the TMD"); return; }
        if (c.Size % 0x10000 != 0) { r.Errors.Add($"{c.Id:X8}.app size is not a multiple of 0x10000"); return; }
        int blocks = (int)(c.Size / 0x10000);
        var tables = new byte[blocks][];
        var h0 = new byte[blocks][];
        using var fs = new FileStream(Path.Combine(dir, $"{c.Id:X8}.app"), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        const int batch = 256;
        var buf = new byte[batch * 0x10000];
        for (int start = 0; start < blocks; start += batch)
        {
            int n = Math.Min(batch, blocks - start);
            fs.ReadExactly(buf, 0, n * 0x10000);
            Parallel.For(0, n, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                int b = start + i;
                using var aes = Aes.Create();
                aes.Key = key;
                var span = buf.AsSpan(i * 0x10000, 0x10000);
                var table = aes.DecryptCbc(span[..0x400], ContentIv(c.Id), PaddingMode.None);
                table[1] ^= (byte)c.Id;
                var iv = table.AsSpan((b % 16) * 20, 16).ToArray();
                var data = aes.DecryptCbc(span[0x400..], iv, PaddingMode.None);
                tables[b] = table;
                h0[b] = SHA1.HashData(data);
            });
        }
        byte[][] Level(byte[][] input)
        {
            var o = new byte[input.Length / 16 + 1][];
            for (int j = 0; j < o.Length; j++)
            {
                var grp = new byte[320];
                for (int k = 0; k < 16; k++)
                    if (j * 16 + k < input.Length) Array.Copy(input[j * 16 + k], 0, grp, k * 20, 20);
                o[j] = SHA1.HashData(grp);
            }
            return o;
        }
        var h1 = Level(h0); var h2 = Level(h1); var h3calc = Level(h2);
        byte[] Group(byte[][] src, int start)
        {
            var g = new byte[320];
            for (int k = 0; k < 16; k++)
                if (start + k < src.Length) Array.Copy(src[start + k], 0, g, k * 20, 20);
            return g;
        }
        int bad = 0, firstBad = -1;
        for (int b = 0; b < blocks; b++)
        {
            var t = tables[b];
            if (!t.AsSpan(0, 320).SequenceEqual(Group(h0, b / 16 * 16)) || !t.AsSpan(320, 320).SequenceEqual(Group(h1, b / 256 * 16))
                || !t.AsSpan(640, 320).SequenceEqual(Group(h2, b / 4096 * 16)))
            { bad++; if (firstBad < 0) firstBad = b; }
        }
        if (bad > 0) r.Errors.Add($"{c.Id:X8}.app: {bad} of {blocks} blocks have a wrong hash table (first: block {firstBad})");
        var expected = new byte[h3.Length];
        for (int j = 0; j < h3.Length / 20 && j < h3calc.Length; j++) Array.Copy(h3calc[j], 0, expected, j * 20, 20);
        if (!expected.AsSpan().SequenceEqual(h3)) r.Errors.Add($"{c.Id:X8}.h3 does not match the hashes computed from the content");
    }

    // ------------------------------------------------------------------ FST

    private static List<FstFile>? ParseFst(byte[] fst, WupReport r)
    {
        if (fst.Length < 0x30 || fst[0] != 'F' || fst[1] != 'S' || fst[2] != 'T') { r.Errors.Add("content 0 is not an FST (wrong title key?)"); return null; }
        uint factor = Be32(fst, 4), secondary = Be32(fst, 8);
        int entries = 0x20 + (int)secondary * 0x20;
        uint total = Be32(fst, entries + 8);
        int names = entries + (int)total * 0x10;
        string Name(int o) { int e = Array.IndexOf(fst, (byte)0, names + o); return Encoding.UTF8.GetString(fst, names + o, e - names - o); }
        var list = new List<FstFile>();
        void Walk(int i, string path, uint end)
        {
            int j = i + 1;
            while (j < end)
            {
                int o = entries + j * 0x10;
                bool dir = (fst[o] & 1) != 0;
                var name = Name((fst[o + 1] << 16) | (fst[o + 2] << 8) | fst[o + 3]);
                if (dir) { var next = Be32(fst, o + 8); Walk(j, path + name + "/", next); j = (int)next; }
                else
                {
                    ushort flags = Be16(fst, o + 12);
                    ulong off = (ulong)Be32(fst, o + 4) * ((flags & 4) != 0 ? 1UL : factor);
                    list.Add(new FstFile(path + name, Be16(fst, o + 14), off, Be32(fst, o + 8)));
                    j++;
                }
            }
        }
        Walk(0, "", total);
        return list;
    }

    private static byte[] ReadFile(string dir, List<Content> contents, FstFile f, byte[] key, long max)
    {
        var c = contents.First(x => x.Index == f.Content);
        long take = (long)Math.Min((ulong)max, f.Length);
        using var fs = new FileStream(Path.Combine(dir, $"{c.Id:X8}.app"), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!c.Hashed)
        {
            // CBC: decrypt from the 16-byte block before the range (or the content IV for the first block)
            ulong first = f.Offset / 16 * 16, last = (f.Offset + (ulong)take + 15) / 16 * 16;
            var iv = ContentIv(c.Id);
            if (first >= 16) { fs.Position = (long)first - 16; fs.ReadExactly(iv, 0, 16); }
            var buf = new byte[last - first];
            fs.Position = (long)first; fs.ReadExactly(buf, 0, buf.Length);
            return CbcDecrypt(key, iv, buf).AsSpan((int)(f.Offset - first), (int)take).ToArray();
        }
        var outp = new byte[take];
        long pos = 0;
        while (pos < take)
        {
            ulong at = f.Offset + (ulong)pos;
            long block = (long)(at / 0xFC00); int inBlock = (int)(at % 0xFC00);
            var blk = new byte[0x10000];
            fs.Position = block * 0x10000; fs.ReadExactly(blk, 0, blk.Length);
            var table = CbcDecrypt(key, ContentIv(c.Id), blk.AsSpan(0, 0x400));
            table[1] ^= (byte)c.Id;
            var data = CbcDecrypt(key, table.AsSpan((int)(block % 16) * 20, 16).ToArray(), blk.AsSpan(0x400));
            int n = (int)Math.Min(take - pos, 0xFC00 - inBlock);
            Array.Copy(data, inBlock, outp, pos, n);
            pos += n;
        }
        return outp;
    }

    // ------------------------------------------------------------------ files

    private static void CheckXml(string titleId, byte[] app, byte[] cos, byte[] meta, WupReport r)
    {
        foreach (var (name, raw) in new[] { ("app.xml", app), ("cos.xml", cos), ("meta.xml", meta) })
        {
            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) r.Errors.Add($"{name} starts with a UTF-8 BOM");
            var doc = new XmlDocument();
            try { doc.LoadXml(Encoding.UTF8.GetString(raw).TrimEnd('\0', '\n', '\r', ' ')); }
            catch (XmlException e) { r.Errors.Add($"{name} is not valid XML: {e.Message}"); continue; }
            if (name != "cos.xml")
            {
                var id = doc.SelectSingleNode("//title_id")?.InnerText.Trim();
                if (!string.Equals(id, titleId, StringComparison.OrdinalIgnoreCase)) r.Errors.Add($"{name} title_id {id} differs from the TMD {titleId}");
            }
            if (name == "meta.xml" && string.IsNullOrWhiteSpace(doc.SelectSingleNode("//longname_en")?.InnerText))
                r.Errors.Add("meta.xml has an empty longname_en (the Wii U Menu would show ???)");
        }
    }

    private static void CheckTga(string name, byte[] head, ulong length, int w, int h, int bpp, WupReport r)
    {
        if (head.Length < 18) { r.Errors.Add($"{name} is too small"); return; }
        int tw = head[12] | head[13] << 8, th = head[14] | head[15] << 8;
        if (head[2] != 2 || tw != w || th != h || head[16] != bpp)
            r.Errors.Add($"{name}: expected an uncompressed {w}x{h} {bpp}-bit TGA, found type {head[2]} {tw}x{th} {head[16]}-bit");
        ulong pixels = 18UL + (ulong)(w * h * bpp / 8);
        if (length != pixels && length != pixels + 26) r.Errors.Add($"{name}: {length} bytes, expected {pixels} (or {pixels + 26} with a TGA footer)");
        else if (length == pixels) r.Warnings.Add($"{name} has no TGA 2.0 footer (Nintendo's own TGA files and the original tool's output have one)");
    }

    private static void CheckNfs(string titleId, List<FstFile> files, byte[] htk, byte[] head, WupReport r)
    {
        var hif = files.Where(f => f.Path.StartsWith("content/hif_", StringComparison.Ordinal)).OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        for (int i = 0; i < hif.Count; i++)
            if (hif[i].Path != $"content/hif_{i:D6}.nfs") { r.Errors.Add($"NFS files are not numbered consecutively (missing hif_{i:D6}.nfs)"); return; }
        var last = hif[^1];
        if (hif.Take(hif.Count - 1).Any(f => f.Length != 262144000) || last.Length > 262144000 || last.Length == 0)
            r.Errors.Add("NFS parts do not have the expected sizes (250 MiB each, the last one smaller)");
        r.Info.Add($"NFS: {hif.Count} parts, {hif.Sum(f => (long)f.Length) / 1048576.0:F0} MiB");
        if (head.Length < 0x210) { r.Errors.Add("hif_000000.nfs is too small"); return; }
        if (head[0] != 'E' || head[1] != 'G' || head[2] != 'G' || head[3] != 'S') r.Errors.Add("hif_000000.nfs does not start with the EGGS header");
        if (htk.Length != 16) { r.Errors.Add("code/htk.bin is not 16 bytes"); return; }
        var first = CbcDecrypt(htk, new byte[16], head.AsSpan(0x200, 16));
        var gameId = Encoding.ASCII.GetString(Convert.FromHexString(titleId[8..]));
        var found = Encoding.ASCII.GetString(first, 0, 4);
        if (found == gameId) r.Info.Add($"NFS: decrypts with htk.bin to the disc header {Encoding.ASCII.GetString(first, 0, 6)}");
        else r.Warnings.Add($"NFS: the first block decrypts to \"{found}\", the title id says \"{gameId}\"");
    }
}
