using System.Buffers.Binary;
using System.Text;

namespace WiiVCInjector;

/// <summary>Container formats a GameCube disc image can come in.</summary>
public enum GcImageFormat { Unknown, Iso, Ciso, Rvz, Nkit }

/// <summary>Detects GameCube disc images (ISO/GCM, CISO, RVZ, NKit) and turns any of them into a plain ISO.</summary>
/// <remarks>ISO is copied and CISO is unpacked here. RVZ and NKit need junk data regenerated, which is what the NKit tool does
/// (<see cref="NkitTool"/>); that is the only reason this class calls an external program.</remarks>
public static class GcImage
{
    /// <summary>Size of a full GameCube disc in bytes.</summary>
    public const long DiscSize = 1_459_978_240;

    const int CisoHeaderSize = 0x8000;
    const int RvzDiscHeaderOffset = 0x58;   // wia_file_head (0x48) + disc_type, compression, level, chunk size (0x10)
    const int RvzDiscHeaderSize = 0x80;
    static readonly byte[] GcMagic = [0xC2, 0x33, 0x9F, 0x3D];   // at 0x1C of a GameCube disc

    /// <summary>Format of the image at <paramref name="path"/>, from its magic bytes (the extension is not trusted).</summary>
    public static GcImageFormat Detect(string path)
    {
        using var f = File.OpenRead(path);
        var b = new byte[0x204];
        var n = f.Read(b, 0, b.Length);
        if (n >= 4 && b.AsSpan(0, 4).SequenceEqual("CISO"u8)) return GcImageFormat.Ciso;
        if (n >= 4 && b.AsSpan(0, 4).SequenceEqual("RVZ\u0001"u8)) return GcImageFormat.Rvz;
        if (n >= 0x20 && b.AsSpan(0x1C, 4).SequenceEqual(GcMagic))
            return n >= 0x204 && b.AsSpan(0x200, 4).SequenceEqual("NKIT"u8) ? GcImageFormat.Nkit : GcImageFormat.Iso;
        return GcImageFormat.Unknown;
    }

    /// <summary>The first bytes of the disc (game id, name, magic) as a stream, wherever the image keeps them.</summary>
    /// <remarks>A CISO starts with its own 0x8000 byte header and an RVZ keeps a copy of the disc header in its file header.</remarks>
    public static Stream OpenHeader(string path)
    {
        switch (Detect(path))
        {
            case GcImageFormat.Ciso:
            {
                using var f = File.OpenRead(path);
                var head = new byte[CisoHeaderSize];
                f.ReadExactly(head);
                if (head[8] == 0) throw new InvalidDataException("This CISO has no first block, it is not a disc image.");
                var disc = new byte[0x1000];
                f.ReadExactly(disc);
                return new MemoryStream(disc);
            }
            case GcImageFormat.Rvz:
            {
                using var f = File.OpenRead(path);
                var disc = new byte[RvzDiscHeaderSize];
                f.Seek(RvzDiscHeaderOffset, SeekOrigin.Begin);
                f.ReadExactly(disc);
                return new MemoryStream(disc);
            }
            default:
                return File.OpenRead(path);
        }
    }

    /// <summary>Writes the plain ISO of <paramref name="input"/> to <paramref name="outputIso"/>.</summary>
    /// <param name="input">ISO, GCM, CISO, RVZ or NKit image of a GameCube disc.</param>
    /// <param name="outputIso">Destination file, replaced if it exists.</param>
    /// <param name="progress">Percent done, when the format can tell.</param>
    /// <param name="ct">Cancels the conversion and removes the partial output.</param>
    /// <exception cref="InvalidDataException">A CISO is damaged.</exception>
    /// <exception cref="InvalidOperationException">The NKit tool failed on an RVZ or NKit image.</exception>
    public static void PrepareIso(string input, string outputIso, IProgress<int>? progress, CancellationToken ct)
    {
        switch (Detect(input))
        {
            case GcImageFormat.Ciso:
                CisoToIso(input, outputIso, progress, ct);
                break;
            case GcImageFormat.Rvz:
            case GcImageFormat.Nkit:
                NkitTool.ExpandToIso(input, outputIso, progress, ct);
                break;
            default:
                // not one of the packed formats: copied as it is, like before (the window already checks the disc magic)
                File.Copy(input, outputIso, true);
                progress?.Report(100);
                break;
        }
    }

    /// <summary>Unpacks a CISO (magic, little endian block size, one map byte per block, then the present blocks) into a plain ISO.</summary>
    /// <param name="input">CISO file.</param>
    /// <param name="output">ISO to write. It is built as <c>output.part</c> and renamed when complete.</param>
    /// <param name="progress">Percent done.</param>
    /// <param name="ct">Cancels the conversion.</param>
    /// <param name="discSize">Size a GameCube disc is padded to. The CISO leaves out trailing zero blocks, so the image would otherwise end early.</param>
    /// <exception cref="InvalidDataException">Not a CISO, an impossible block size, or the file ends before its last present block.</exception>
    public static void CisoToIso(string input, string output, IProgress<int>? progress, CancellationToken ct, long discSize = DiscSize)
    {
        var part = output + ".part";
        try
        {
            using (var src = File.OpenRead(input))
            using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write))
            {
                var head = new byte[CisoHeaderSize];
                if (src.Read(head, 0, head.Length) != head.Length || !head.AsSpan(0, 4).SequenceEqual("CISO"u8))
                    throw new InvalidDataException("This is not a CISO file: " + Path.GetFileName(input));
                var blockSize = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4));
                if (blockSize < 0x8000 || blockSize > 0x1000_0000 || (blockSize & (blockSize - 1)) != 0)
                    throw new InvalidDataException($"This CISO has an impossible block size (0x{blockSize:X}).");
                var map = head.AsSpan(8);
                var count = map.LastIndexOfAnyExcept((byte)0) + 1;
                if (count == 0) throw new InvalidDataException("This CISO has no data blocks.");

                var buf = new byte[blockSize];
                var isGc = false;
                var lastPercent = -1;
                for (var i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (map[i] != 0)
                    {
                        try { src.ReadExactly(buf); }
                        catch (EndOfStreamException) { throw new InvalidDataException($"This CISO ends before block {i}, the file is damaged or incomplete."); }
                        dst.Write(buf);
                        if (i == 0) isGc = buf.AsSpan(0x1C, 4).SequenceEqual(GcMagic);
                    }
                    else dst.Seek(blockSize, SeekOrigin.Current);   // a hole: zeros without writing them

                    var percent = (int)((i + 1) * 100L / count);
                    if (percent != lastPercent) { lastPercent = percent; progress?.Report(percent); }
                }

                var total = (long)count * blockSize;
                // a disc image is exactly the disc size; the last block of a CISO may run past it or stop short of it
                if (isGc && (total < discSize || total - discSize < blockSize)) total = discSize;
                dst.SetLength(total);
            }
            File.Move(part, output, true);
        }
        finally { try { File.Delete(part); } catch { } }
    }
}
