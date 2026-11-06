using System.Security.Cryptography;

namespace WiiVCInjector;

/// <summary>
/// Port of FIX94's c2w_patcher v1.2 (MIT): unlocks the Wii PPC clock multiplier in a cafe2wii
/// (c2w.img) using the Wii U starbuck ancast key. Same byte patterns and SHA-1 fixup as the C tool.
/// </summary>
public static class C2wPatcher
{
    private static readonly byte[] ImgHdr = { 0xEF, 0xA2, 0x82, 0xD9 };
    private static readonly byte[] ElfHdr = { 0x7F, 0x45, 0x4C, 0x46 };
    private static readonly byte[] MemctrlOri = { 0xE3, 0x82, 0x20, 0x20, 0xE5, 0x84, 0x25, 0xB0 };
    private static readonly byte[] MemctrlPatch = { 0xE3, 0xC2, 0x20, 0x20, 0xE5, 0x84, 0x25, 0xB0 };
    private static readonly byte[] SysprotOri = { 0xE3, 0x83, 0x30, 0x99, 0xE5, 0x81, 0x35, 0x14 };
    private static readonly byte[] SysprotPatch = { 0xE3, 0x83, 0x30, 0x9D, 0xE5, 0x81, 0x35, 0x14 };
    private static readonly byte[] Iop2xOri = { 0xE1, 0x94, 0x40, 0x00, 0x1A, 0xFF, 0xFF, 0xBD };
    private static readonly byte[] Iop2xPatch = { 0xE2, 0x8D, 0xD0, 0x10, 0xE8, 0xBD, 0x8F, 0xF0 };

    /// <summary>Returns the patched image (the c2p.img of the original tool).</summary>
    /// <param name="c2w">Contents of c2w.img.</param>
    /// <param name="keyHex">Ancast key, at least 32 hex characters.</param>
    /// <param name="iop2x">Also patch the ARM/bus clock (experimental).</param>
    /// <param name="log">Receives the tool's console messages.</param>
    /// <exception cref="InvalidDataException">Bad key, header or wrong key.</exception>
    public static byte[] Patch(byte[] c2w, string keyHex, bool iop2x = false, Action<string>? log = null)
    {
        keyHex = keyHex.Trim();
        if (keyHex.Length < 32) throw new InvalidDataException("Key seems to be too small!");
        var key = Convert.FromHexString(keyHex[..32]);
        if (c2w.Length < 0x200 || !c2w.AsSpan(0, 4).SequenceEqual(ImgHdr))
            throw new InvalidDataException("Invalid c2w.img header!");
        int len = c2w.Length - 0x200;
        if (len % 16 != 0) throw new InvalidDataException("c2w.img size is not block aligned");

        using var aes = Aes.Create();
        aes.Key = key;
        var iv = new byte[16];
        var dec = (byte[])c2w.Clone();
        var plain = aes.DecryptCbc(c2w.AsSpan(0x200), iv, PaddingMode.None);
        plain.CopyTo(dec, 0x200);
        if (c2w.Length < 0x808 || !dec.AsSpan(0x804, 4).SequenceEqual(ElfHdr))
            throw new InvalidDataException("c2w.img does not appear to be decrypted! Please verify your key.");

        int cnt = 0, p = 0;
        for (int i = 0x200; i + 8 <= dec.Length; i += 4)
        {
            var w = dec.AsSpan(i, 8);
            if (w.SequenceEqual(MemctrlOri)) { log?.Invoke($"Patched LT_COMPAT_MEMCTRL_STATE at {i:x}"); MemctrlPatch.CopyTo(w); cnt++; p |= 1; }
            else if (w.SequenceEqual(SysprotOri)) { log?.Invoke($"Patched LT_SYSPROT at {i:x}"); SysprotPatch.CopyTo(w); cnt++; p |= 2; }
            else if (iop2x && w.SequenceEqual(Iop2xOri)) { log?.Invoke($"Patched LT_IOP2X at {i:x}"); Iop2xPatch.CopyTo(w); cnt++; p |= 4; }
        }
        if ((iop2x && (cnt != 3 || p != 7)) || (!iop2x && (cnt != 2 || p != 3)))
            log?.Invoke("WARNING: Did not apply c2w.img patches as expected!");

        var enc = (byte[])c2w.Clone();
        aes.EncryptCbc(dec.AsSpan(0x200), iv, PaddingMode.None).CopyTo(enc, 0x200);
        var sha = SHA1.HashData(enc.AsSpan(0x200));
        sha.CopyTo(enc, 0x1B0);
        return enc;
    }
}
