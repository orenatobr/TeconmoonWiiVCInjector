using SkiaSharp;

namespace WiiVCInjector;

/// <summary>Native replacements for png2tgacmd, sox and wav2btsnd.</summary>
internal static class Media
{
    /// <summary>Header of an uncompressed true-color bottom-left TGA (Nintendo's bootLogoTex.tga layout).</summary>
    public static byte[] TgaHeader(int w, int h, int bpp) =>
        [0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)w, (byte)(w >> 8), (byte)h, (byte)(h >> 8), (byte)bpp, (byte)(bpp == 32 ? 8 : 0)];

    public static void WriteTga(string src, string dst, int w, int h, int bpp)
    {
        using var bmp = SKBitmap.Decode(src) ?? throw new InvalidDataException("cannot decode image: " + src);
        using var scaled = bmp.Resize(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidOperationException("resize failed: " + src);
        var px = scaled.GetPixelSpan();
        var bytes = bpp / 8;
        var outp = new byte[18 + w * h * bytes];
        TgaHeader(w, h, bpp).CopyTo(outp, 0);
        var o = 18;
        for (var y = h - 1; y >= 0; y--)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                int a = px[i + 3];
                if (bpp == 32)
                {   // premultiplied -> straight alpha
                    for (var c = 0; c < 3; c++) outp[o++] = (byte)(a == 0 ? 0 : Math.Min(255, px[i + c] * 255 / a));
                    outp[o++] = (byte)a;
                }
                else
                {   // drop alpha by compositing on white (ImageMagick -alpha remove)
                    for (var c = 0; c < 3; c++) outp[o++] = (byte)Math.Min(255, px[i + c] + 255 - a);
                }
            }
        File.WriteAllBytes(dst, outp);
    }

    /// <summary>PCM WAV (8/16/24/32-bit int or float, any channels and rate) to 16-bit stereo 48 kHz, first <paramref name="maxSeconds"/> s.</summary>
    public static short[] WavToStereo48k(byte[] d, int maxSeconds = 6)
    {
        if (d.Length < 12 || d[0] != 'R' || d[1] != 'I' || d[2] != 'F' || d[3] != 'F' || d[8] != 'W') throw new InvalidDataException("input must be a RIFF WAV");
        int tag = 0, ch = 0, rate = 0, bits = 0, dataOff = -1, dataLen = 0;
        for (var p = 12; p + 8 <= d.Length;)
        {
            var id = System.Text.Encoding.ASCII.GetString(d, p, 4);
            var len = BitConverter.ToInt32(d, p + 4);
            var body = p + 8;
            if (id == "fmt ")
            {
                tag = BitConverter.ToUInt16(d, body); ch = BitConverter.ToUInt16(d, body + 2);
                rate = BitConverter.ToInt32(d, body + 4); bits = BitConverter.ToUInt16(d, body + 14);
                if (tag == 0xFFFE && len >= 26) tag = BitConverter.ToUInt16(d, body + 24);
            }
            else if (id == "data") { dataOff = body; dataLen = (int)Math.Min((uint)len, (uint)(d.Length - body)); break; }
            p = body + len + (len & 1);
        }
        if (dataOff < 0 || ch < 1 || rate < 1) throw new InvalidDataException("WAV has no fmt/data chunk");
        var isFloat = tag == 3;
        if (!(tag == 1 || isFloat) || !(bits is 8 or 16 or 24 or 32 || (isFloat && bits == 64))) throw new NotSupportedException($"unsupported WAV format (tag {tag}, {bits} bit)");
        var bps = bits / 8;
        var frames = dataLen / (bps * ch);
        float Sample(int frame, int c)
        {
            var o = dataOff + (frame * ch + Math.Min(c, ch - 1)) * bps;
            return bits switch
            {
                8 => (d[o] - 128) / 128f,
                16 => BitConverter.ToInt16(d, o) / 32768f,
                24 => ((d[o] << 8 | d[o + 1] << 16 | d[o + 2] << 24) >> 8) / 8388608f,
                32 when isFloat => BitConverter.ToSingle(d, o),
                32 => BitConverter.ToInt32(d, o) / 2147483648f,
                _ => (float)BitConverter.ToDouble(d, o),
            };
        }
        var outFrames = (int)Math.Min((long)maxSeconds * 48000, (long)Math.Round(frames * 48000.0 / rate));
        var res = new short[outFrames * 2];
        for (var i = 0; i < outFrames; i++)
        {
            var pos = i * (double)rate / 48000;
            var i0 = Math.Min((int)pos, frames - 1); var i1 = Math.Min(i0 + 1, frames - 1); var fr = (float)(pos - i0);
            for (var c = 0; c < 2; c++)
            {
                var v = Sample(i0, c) * (1 - fr) + Sample(i1, c) * fr;
                res[i * 2 + c] = (short)Math.Clamp((int)Math.Round(v * 32767f), -32768, 32767);
            }
        }
        return res;
    }

    /// <summary>btsnd: BE int32 hearable (2 = TV and GamePad), BE int32 loop point, BE samples; noLoop points past the end and appends 4 zero bytes.</summary>
    public static byte[] Btsnd(short[] stereo, bool noLoop)
    {
        var frames = stereo.Length / 2;
        var o = new byte[8 + stereo.Length * 2 + (noLoop ? 4 : 0)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(o, 2);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(o.AsSpan(4), noLoop ? frames + 1 : 0);
        for (var i = 0; i < stereo.Length; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(o.AsSpan(8 + i * 2), stereo[i]);
        return o;
    }

    public static void WriteBtsnd(string wav, string dst, bool noLoop) =>
        File.WriteAllBytes(dst, Btsnd(WavToStereo48k(File.ReadAllBytes(wav)), noLoop));
}
