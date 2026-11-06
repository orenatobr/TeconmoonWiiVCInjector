using System.Text;

namespace WiiVCInjector;

internal sealed record TitleIds(string Hex, string Text, string Packed);

/// <summary>Title ids, app.xml, meta.xml and nfs2iso2nfs arguments (port of injector.py / Form1.cs).</summary>
internal static class Metadata
{
    public static TitleIds Ids(string gameId, string system)
    {
        if (gameId.Length != 4) throw new ArgumentException($"game id must be 4 characters: '{gameId}'");
        var hex = Convert.ToHexString(Encoding.Latin1.GetBytes(gameId));
        return new TitleIds(hex, system == "dol" ? "BOOT" : gameId, "00050002" + hex);
    }

    public static TitleIds WiiwareIds(string title)
    {
        var t = title.ToUpperInvariant();
        if (t.Length != 4) throw new ArgumentException($"WiiWare title code must be 4 characters: '{title}'");
        return new TitleIds(Convert.ToHexString(Encoding.ASCII.GetBytes(t)), t, "00050002" + Convert.ToHexString(Encoding.ASCII.GetBytes(t)));
    }

    /// <summary>WBFS id at 0x200, DOL (magic 00 00 01 00) at 0x2A0, NASOS WII5/WII9 at their data offsets, else 0.</summary>
    public static string ReadGameId(string path)
    {
        using var f = File.OpenRead(path);
        var magic = new byte[4];
        f.ReadExactly(magic);
        var m = Encoding.Latin1.GetString(magic);
        long off = m == "WBFS" ? 0x200 : magic.SequenceEqual(new byte[] { 0, 0, 1, 0 }) ? 0x2A0 : m == "WII5" ? 0x1182800 : m == "WII9" ? 0x1FB5000 : 0;
        f.Seek(off, SeekOrigin.Begin);
        var id = new byte[4];
        if (f.Read(id) != 4) throw new InvalidDataException("file too short to contain a game id: " + path);
        return Encoding.Latin1.GetString(id);
    }

    static string Z(int n) => new('0', n);
    static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string AppXml(TitleIds ids)
    {
        (string, string, int, string)[] rows =
        [
            ("version", "unsignedInt", 4, "16"),
            ("os_version", "hexBinary", 8, "000500101000400A"),
            ("title_id", "hexBinary", 8, ids.Packed),
            ("title_version", "hexBinary", 2, "0000"),
            ("sdk_version", "unsignedInt", 4, "21204"),
            ("app_type", "hexBinary", 4, "8000002E"),
            ("group_id", "hexBinary", 4, ids.Hex),
            ("os_mask", "hexBinary", 32, Z(64)),
            ("common_id", "hexBinary", 8, Z(16)),
        ];
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<app type=\"complex\" access=\"777\">\n");
        foreach (var (n, t, l, v) in rows) sb.Append($"  <{n} type=\"{t}\" length=\"{l}\">{v}</{n}>\n");
        return sb.Append("</app>\n").ToString();
    }

    static readonly string[] Langs = ["ja", "en", "fr", "de", "it", "es", "zhs", "ko", "nl", "pt", "ru", "zht"];

    public static string MetaXml(TitleIds ids, string line1, string? line2, string drcUse)
    {
        var longName = string.IsNullOrEmpty(line2) ? line1 : line1 + "\n" + line2;
        var rows = new List<(string Name, string Type, int Len, string Val)>
        {
            ("version", "unsignedInt", 4, "33"),
            ("product_code", "string", 32, "WUP-N-" + ids.Text),
            ("content_platform", "string", 32, "WUP"),
            ("company_code", "string", 8, "0001"),
            ("mastering_date", "string", 32, ""),
            ("logo_type", "unsignedInt", 4, "0"),
            ("app_launch_type", "hexBinary", 4, "00000000"),
            ("invisible_flag", "hexBinary", 4, "00000000"),
            ("no_managed_flag", "hexBinary", 4, "00000000"),
            ("no_event_log", "hexBinary", 4, "00000002"),
            ("no_icon_database", "hexBinary", 4, "00000000"),
            ("launching_flag", "hexBinary", 4, "00000004"),
            ("install_flag", "hexBinary", 4, "00000000"),
            ("closing_msg", "unsignedInt", 4, "0"),
            ("title_version", "unsignedInt", 4, "0"),
            ("title_id", "hexBinary", 8, ids.Packed),
            ("group_id", "hexBinary", 4, ids.Hex),
            ("boss_id", "hexBinary", 8, Z(16)),
            ("os_version", "hexBinary", 8, "000500101000400A"),
            ("app_size", "hexBinary", 8, Z(16)),
            ("common_save_size", "hexBinary", 8, Z(16)),
            ("account_save_size", "hexBinary", 8, Z(16)),
            ("common_boss_size", "hexBinary", 8, Z(16)),
            ("account_boss_size", "hexBinary", 8, Z(16)),
            ("save_no_rollback", "unsignedInt", 4, "0"),
            ("join_game_id", "hexBinary", 4, "00000000"),
            ("join_game_mode_mask", "hexBinary", 8, Z(16)),
            ("bg_daemon_enable", "unsignedInt", 4, "0"),
            ("olv_accesskey", "unsignedInt", 4, "3921400692"),
            ("wood_tin", "unsignedInt", 4, "0"),
            ("e_manual", "unsignedInt", 4, "0"),
            ("e_manual_version", "unsignedInt", 4, "0"),
            ("region", "hexBinary", 4, "00000002"),
            ("pc_cero", "unsignedInt", 4, "128"),
            ("pc_esrb", "unsignedInt", 4, "6"),
            ("pc_bbfc", "unsignedInt", 4, "192"),
            ("pc_usk", "unsignedInt", 4, "128"),
            ("pc_pegi_gen", "unsignedInt", 4, "128"),
            ("pc_pegi_fin", "unsignedInt", 4, "192"),
            ("pc_pegi_prt", "unsignedInt", 4, "128"),
            ("pc_pegi_bbfc", "unsignedInt", 4, "128"),
            ("pc_cob", "unsignedInt", 4, "128"),
            ("pc_grb", "unsignedInt", 4, "128"),
            ("pc_cgsrr", "unsignedInt", 4, "128"),
            ("pc_oflc", "unsignedInt", 4, "128"),
            ("pc_reserved0", "unsignedInt", 4, "192"),
            ("pc_reserved1", "unsignedInt", 4, "192"),
            ("pc_reserved2", "unsignedInt", 4, "192"),
            ("pc_reserved3", "unsignedInt", 4, "192"),
            ("ext_dev_nunchaku", "unsignedInt", 4, "0"),
            ("ext_dev_classic", "unsignedInt", 4, "0"),
            ("ext_dev_urcc", "unsignedInt", 4, "0"),
            ("ext_dev_board", "unsignedInt", 4, "0"),
            ("ext_dev_usb_keyboard", "unsignedInt", 4, "0"),
            ("ext_dev_etc", "unsignedInt", 4, "0"),
            ("ext_dev_etc_name", "string", 512, ""),
            ("eula_version", "unsignedInt", 4, "0"),
            ("drc_use", "unsignedInt", 4, drcUse),
            ("network_use", "unsignedInt", 4, "0"),
            ("online_account_use", "unsignedInt", 4, "0"),
            ("direct_boot", "unsignedInt", 4, "0"),
            ("reserved_flag0", "hexBinary", 4, "00010001"),
            ("reserved_flag1", "hexBinary", 4, "00080023"),
            ("reserved_flag2", "hexBinary", 4, ids.Hex),
            ("reserved_flag3", "hexBinary", 4, "00000000"),
            ("reserved_flag4", "hexBinary", 4, "00000000"),
            ("reserved_flag5", "hexBinary", 4, "00000000"),
            ("reserved_flag6", "hexBinary", 4, "00000003"),
            ("reserved_flag7", "hexBinary", 4, "00000005"),
        };
        foreach (var l in Langs) rows.Add(($"longname_{l}", "string", 512, longName));
        foreach (var l in Langs) rows.Add(($"shortname_{l}", "string", 512, line1));
        foreach (var l in Langs) rows.Add(($"publisher_{l}", "string", 256, ""));
        for (var i = 0; i < 32; i++) rows.Add(($"add_on_unique_id{i}", "hexBinary", 4, "00000000"));
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<menu type=\"complex\" access=\"777\">\n");
        foreach (var (n, t, l, v) in rows) sb.Append($"  <{n} type=\"{t}\" length=\"{l}\">{Esc(v)}</{n}>\n");
        return sb.Append("</menu>\n").ToString();
    }

    /// <summary>nfs2iso2nfs arguments, same flags and order as the original app.</summary>
    public static List<string> NfsArgs(string system, string iso, string nfsPatch, bool lrPatch, bool passthrough)
    {
        var patches = nfsPatch.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (lrPatch) patches.Add("-lrpatch");
        var a = new List<string> { "-enc" };
        switch (system)
        {
            case "wii": a.AddRange(patches); break;
            case "wiiware": a.Add("-homebrew"); a.AddRange(patches); break;
            case "dol": a.Add("-homebrew"); if (passthrough) a.Add("-passthrough"); break;
            default: a.Add("-homebrew"); a.Add("-passthrough"); break; // gcn: always
        }
        a.Add("-iso"); a.Add(iso);
        return a;
    }

    public static string GcnBooter(BuildOptions o)
    {
        if (o.Force43 && o.ForceInterlaced) return "nintendont_force_43_interlaced_autobooter.dol";
        if (o.Force43) return "nintendont_force_4_by_3_autobooter.dol";
        if (o.ForceInterlaced) return "nintendont_force_interlaced_autobooter.dol";
        if (o.DisableNintendontAutoboot) return "nintendont_forwarder.dol";
        return "nintendont_default_autobooter.dol";
    }
}
