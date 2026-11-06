using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WiiVCInjector;

/// <summary>Image import: any Avalonia-readable format or TGA to PNG (replaces System.Drawing and tga2pngcmd.exe).</summary>
public static class ImageUtil
{
    /// <summary>Convert <paramref name="src"/> to a PNG at <paramref name="destPng"/>.</summary>
    public static void ToPng(string src, string destPng)
    {
        if (File.Exists(destPng)) File.Delete(destPng);
        Directory.CreateDirectory(Path.GetDirectoryName(destPng)!);
        if (Path.GetExtension(src).Equals(".tga", StringComparison.OrdinalIgnoreCase))
        {
            using var bmp = DecodeTga(File.ReadAllBytes(src));
            bmp.Save(destPng);
        }
        else
        {
            using var bmp = new Bitmap(src);
            bmp.Save(destPng);
        }
    }

    public static Bitmap Load(string png)
    {
        using var fs = File.OpenRead(png);
        return new Bitmap(fs);
    }

    /// <summary>Uncompressed and RLE true color TGA, 24 or 32 bit.</summary>
    public static WriteableBitmap DecodeTga(byte[] d)
    {
        int idLen = d[0], type = d[2], w = d[12] | d[13] << 8, h = d[14] | d[15] << 8, bpp = d[16];
        bool topDown = (d[17] & 0x20) != 0;
        if ((type != 2 && type != 10) || (bpp != 24 && bpp != 32)) throw new NotSupportedException("Unsupported TGA format");
        int bytes = bpp / 8, pos = 18 + idLen + (d[1] == 1 ? (d[5] | d[6] << 8) * ((d[7] + 7) / 8) : 0);
        var px = new byte[w * h * 4];
        int i = 0;
        void Put(int o)
        {
            px[i * 4] = d[o]; px[i * 4 + 1] = d[o + 1]; px[i * 4 + 2] = d[o + 2];
            px[i * 4 + 3] = bytes == 4 ? d[o + 3] : (byte)255;
            i++;
        }
        while (i < w * h)
        {
            if (type == 2) { Put(pos); pos += bytes; continue; }
            int hdr = d[pos++], n = (hdr & 0x7F) + 1;
            for (int k = 0; k < n && i < w * h; k++) { Put(pos); if ((hdr & 0x80) == 0) pos += bytes; }
            if ((hdr & 0x80) != 0) pos += bytes;
        }
        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = wb.Lock();
        for (int y = 0; y < h; y++)
        {
            int sy = topDown ? y : h - 1 - y;
            System.Runtime.InteropServices.Marshal.Copy(px, sy * w * 4, fb.Address + y * fb.RowBytes, w * 4);
        }
        return wb;
    }
}
