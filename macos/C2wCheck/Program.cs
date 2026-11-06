using System.Security.Cryptography;
using WiiVCInjector;

if (args.Length == 4 && args[0] == "patch")
{
    File.WriteAllBytes(args[3], C2wPatcher.Patch(File.ReadAllBytes(args[1]), File.ReadAllText(args[2])));
    return 0;
}

// Builds a synthetic cafe2wii image (ELF header at 0x804, the two patch patterns), encrypts it
// with a random key, patches it, decrypts the result and checks every byte and the SHA-1 field.
var key = RandomNumberGenerator.GetBytes(16);
var plain = new byte[0x2000];
plain[0x604] = 0x7F; plain[0x605] = 0x45; plain[0x606] = 0x4C; plain[0x607] = 0x46; // file offset 0x804
byte[] memOri = { 0xE3, 0x82, 0x20, 0x20, 0xE5, 0x84, 0x25, 0xB0 }, sysOri = { 0xE3, 0x83, 0x30, 0x99, 0xE5, 0x81, 0x35, 0x14 };
memOri.CopyTo(plain, 0x1000); sysOri.CopyTo(plain, 0x1400);
var img = new byte[0x2200];
new byte[] { 0xEF, 0xA2, 0x82, 0xD9 }.CopyTo(img, 0);
using var aes = Aes.Create(); aes.Key = key;
aes.EncryptCbc(plain, new byte[16], PaddingMode.None).CopyTo(img, 0x200);
var msgs = new List<string>();
var outImg = C2wPatcher.Patch(img, Convert.ToHexString(key), false, msgs.Add);
var back = aes.DecryptCbc(outImg.AsSpan(0x200), new byte[16], PaddingMode.None);
int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }
Check(back[0x1000 + 1] == 0xC2 && back[0x1400 + 3] == 0x9D, "memctrl and sysprot patched");
Check(back.AsSpan(0, 0x1000).SequenceEqual(plain.AsSpan(0, 0x1000)), "unrelated bytes untouched");
Check(SHA1.HashData(outImg.AsSpan(0x200)).AsSpan().SequenceEqual(outImg.AsSpan(0x1B0, 20)), "sha1 field matches");
Check(msgs.Count == 2, "no warning with 2 patches (" + string.Join("; ", msgs) + ")");
try { C2wPatcher.Patch(img, new string('0', 32)); Check(false, "wrong key rejected"); }
catch (InvalidDataException) { Check(true, "wrong key rejected"); }
try { C2wPatcher.Patch(new byte[0x300], Convert.ToHexString(key)); Check(false, "bad header rejected"); }
catch (InvalidDataException) { Check(true, "bad header rejected"); }

// Video mode patcher on a synthetic DOL holding a libogc-style TVNtsc480IntDf table and a PAL 528 table.
byte[] Mode(uint tv, int efb, int xfb, int y, int h, byte field, byte[] filt)
{
    var m = new byte[60];
    void P16(int o, int v) { m[o] = (byte)(v >> 8); m[o + 1] = (byte)v; }
    m[0] = (byte)(tv >> 24); m[1] = (byte)(tv >> 16); m[2] = (byte)(tv >> 8); m[3] = (byte)tv;
    P16(4, 640); P16(6, efb); P16(8, xfb); P16(10, 40); P16(12, y); P16(14, 640); P16(16, h);
    m[23] = 1; m[24] = field;
    for (int i = 0; i < 24; i++) m[26 + i] = 6;
    filt.CopyTo(m, 50);
    return m;
}
var dol = new byte[0x400];
Mode(0, 480, 480, 0, 480, 0, new byte[] { 7, 7, 12, 12, 12, 7, 7 }).CopyTo(dol, 0x100);   // NTSC int
Mode(4, 528, 528, 23, 528, 0, new byte[] { 7, 7, 12, 12, 12, 7, 7 }).CopyTo(dol, 0x200); // PAL int
var d1 = (byte[])dol.Clone();
var r1 = VideoModePatcher.Patch(d1, VideoTarget.Pal60);
Check(r1.Found == 2 && d1[0x103] == (5 << 2) && d1[0x203] == (5 << 2), "Pal60: both tables to EURGB60");
Check(d1[0x206] == 0x01 && d1[0x207] == 0xE0 && d1[0x20D] == 0, "Pal60: PAL table now 480 lines (efb 0x1E0, y 0)");
var d2 = (byte[])dol.Clone();
VideoModePatcher.Patch(d2, VideoTarget.Pal50);
Check(d2[0x103] == (1 << 2) && d2[0x106] == 0x02 && d2[0x107] == 0x10 && d2[0x10D] == 23, "Pal50: NTSC table now 528 lines at y 23");
var d3 = (byte[])dol.Clone();
VideoModePatcher.Patch(d3, VideoTarget.Progressive480);
Check(d3[0x103] == 2 && d3[0x114 + 3] == 0 && d3[0x100 + 50 + 2] == 21, "480p: NTSC table progressive, single field, 480p filter");
Check(d3[0x203] == 0 && d3[0x207] == 0xE0, "480p: PAL table becomes 480-line NTSC interlaced (no 576p exists)");
var junk = new byte[0x400]; new Random(1).NextBytes(junk);
Check(VideoModePatcher.Patch(junk, VideoTarget.Ntsc).Found == 0, "random data has no false positives");
return fails;
