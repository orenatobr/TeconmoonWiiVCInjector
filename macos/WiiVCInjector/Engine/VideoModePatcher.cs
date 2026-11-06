namespace WiiVCInjector;

/// <summary>Target TV format for <see cref="VideoModePatcher"/>.</summary>
public enum VideoTarget { Ntsc, Pal60, Pal50, Progressive480 }

/// <summary>
/// Open replacement for the closed-source Wii Video Mode Changer ("smart patcher"): finds the
/// libogc/SDK GXRModeObj render-mode tables inside a Wii executable (DOL) and moves them to the
/// chosen TV format. Experimental: written from the public GXRModeObj layout, not from the
/// original tool, and only verified on synthetic tables.
/// </summary>
public static class VideoModePatcher
{
    // GXRModeObj layout (big endian, 60 bytes).
    private const int Size = 60, TvMode = 0, FbWidth = 4, EfbH = 6, XfbH = 8, ViX = 10, ViY = 12, ViW = 14, ViH = 16,
        XfbMode = 20, Field = 24, Aa = 25, Pattern = 26, Filter = 50;
    private const int FormatNtsc = 0, FormatPal = 1, FormatEurgb60 = 5;
    private static readonly byte[] FilterProg = { 0, 0, 21, 22, 21, 0, 0 };
    private static readonly byte[] FilterInt = { 7, 7, 12, 12, 12, 7, 7 };

    private static int U16(byte[] b, int o) => (b[o] << 8) | b[o + 1];
    private static uint U32(byte[] b, int o) => (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
    private static void P16(byte[] b, int o, int v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
    private static void P32(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    private static bool Is576(uint tv) => (tv >> 2) is FormatPal or 4;

    // Heights of the standard tables per kind: full frame, full with AA, field rendered, double strike.
    private static readonly (int Efb, int Xfb, int Y, int H)[] L480 = { (480, 480, 0, 480), (242, 480, 0, 480), (240, 240, 0, 480), (240, 240, 0, 480) };
    private static readonly (int Efb, int Xfb, int Y, int H)[] L576 = { (528, 528, 23, 528), (264, 524, 23, 524), (264, 264, 23, 528), (264, 264, 11, 528) };

    private static bool IsMode(byte[] b, int p)
    {
        uint tv = U32(b, p + TvMode);
        if (tv >= 24 || (tv & 3) == 3) return false;
        int fb = U16(b, p + FbWidth), efb = U16(b, p + EfbH), xfb = U16(b, p + XfbH);
        int x = U16(b, p + ViX), y = U16(b, p + ViY), w = U16(b, p + ViW), h = U16(b, p + ViH);
        if (fb < 320 || fb > 720 || fb % 16 != 0 || efb < 200 || efb > 528 || xfb < 200 || xfb > 576) return false;
        if (w < 320 || w > 720 || x + w > 720 || w % 2 != 0) return false;
        int lines = Is576(tv) ? 574 : 480;
        if ((tv & 3) == 1) lines /= 2;
        int full = (tv & 3) == 1 ? lines * 2 : lines;
        if (h < full / 2 || y + h > full + 2) return false;
        if (b[p + XfbMode - 2] != 0 || b[p + XfbMode - 1] != 0 || U32(b, p + XfbMode) > 1 || b[p + Field] > 1 || b[p + Aa] > 1) return false;
        for (int i = 0; i < 24; i++) if (b[p + Pattern + i] > 12) return false;
        int taps = 0;
        for (int i = 0; i < 7; i++) taps += b[p + Filter + i];
        return taps == 64;
    }

    private static int KindOf(byte[] b, int p, uint tv) =>
        (tv & 3) == 1 ? 3 : b[p + Field] == 1 ? 2 : b[p + Aa] == 1 ? 1 : 0;

    private static bool IsStandard(byte[] b, int p, uint tv, int kind)
    {
        var want = Is576(tv) ? L576[kind] : L480[kind];
        int y = U16(b, p + ViY);
        return U16(b, p + EfbH) == want.Efb && U16(b, p + XfbH) == want.Xfb && U16(b, p + ViH) == want.H
               && (y == want.Y || y == want.Y + 1);
    }

    /// <summary>Patches the tables in place and returns (tablesFound, tablesChanged).</summary>
    public static (int Found, int Changed) Patch(byte[] dol, VideoTarget target)
    {
        uint format = target switch { VideoTarget.Pal60 => FormatEurgb60, VideoTarget.Pal50 => FormatPal, _ => FormatNtsc };
        bool to576 = target == VideoTarget.Pal50, prog = target == VideoTarget.Progressive480;
        int found = 0, changed = 0;
        for (int p = 0; p + Size <= dol.Length; p += 4)
        {
            if (!IsMode(dol, p)) continue;
            found++;
            uint tv = U32(dol, p + TvMode);
            uint mode = tv & 3;
            int kind = KindOf(dol, p, tv);
            bool standard = IsStandard(dol, p, tv, kind);
            bool from576 = Is576(tv);
            if (from576 != to576 && !standard) { p += Size - 4; continue; }  // unknown heights: leave alone
            bool full = kind <= 1;
            bool toProg = prog && mode == 0 && full && standard && !from576;
            bool toInt = mode == 2 && !prog;
            if (from576 != to576 || toProg)
            {
                var h = to576 ? L576[kind] : L480[kind];
                P16(dol, p + EfbH, h.Efb); P16(dol, p + XfbH, h.Xfb); P16(dol, p + ViY, h.Y); P16(dol, p + ViH, h.H);
            }
            if (toProg) { mode = 2; P32(dol, p + XfbMode, 0); FilterProg.CopyTo(dol, p + Filter); }
            else if (toInt) { mode = 0; P32(dol, p + XfbMode, full ? 1u : 0u); FilterInt.CopyTo(dol, p + Filter); }
            uint now = (format << 2) | mode;
            if (now != tv || from576 != to576 || toProg || toInt) changed++;
            P32(dol, p + TvMode, now);
            p += Size - 4;
        }
        return (found, changed);
    }
}
