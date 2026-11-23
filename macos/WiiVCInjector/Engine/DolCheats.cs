using System.Buffers.Binary;

namespace WiiVCInjector;

/// <summary>
/// Writes cheat codes into a game's main.dol, for Wii games in a WUP (there is no code handler there, unlike Nintendont for GameCube).
/// Only codes made of plain 8, 16 and 32 bit writes (Gecko 00, 02, 04, which Action Replay writes share) can be written this way, and only
/// where every address falls inside a section of the DOL, so it fits patches of the game's own code (widescreen, region, frame rate) and not
/// codes that change values the game keeps in memory while it runs (lives, health).
/// </summary>
public static class DolCheats
{
    const int Sections = 18;

    /// <summary>Gecko word to the address it writes to: the low 25 bits on top of 0x80000000.</summary>
    static uint Address(uint word) => 0x80000000 | (word & 0x01FFFFFF);

    /// <summary>The writes of a cheat as (address, bytes), or null when a line is not a plain write.</summary>
    public static List<(uint Address, byte[] Bytes)>? Writes(Cheat c)
    {
        var list = new List<(uint, byte[])>();
        foreach (var (a, v) in c.Lines)
        {
            var addr = Address(a);
            switch ((a >> 24) & 0xFE)
            {
                case 0x00: list.Add((addr, Enumerable.Repeat((byte)v, (int)((v >> 8) & 0xFFFFFF) + 1).ToArray())); break;
                case 0x02: list.Add((addr, Enumerable.Repeat((ushort)v, (int)(v >> 16) + 1).SelectMany(h => new[] { (byte)(h >> 8), (byte)h }).ToArray())); break;
                case 0x04: list.Add((addr, new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v })); break;
                default: return null;
            }
        }
        return list;
    }

    /// <summary>
    /// Applies every cheat whose writes all land in the DOL, in place. Returns what was written and why the others were not.
    /// A cheat is written whole or not at all.
    /// </summary>
    public static (List<string> Applied, List<string> Skipped) Apply(byte[] dol, IEnumerable<Cheat> cheats)
    {
        var applied = new List<string>();
        var skipped = new List<string>();
        if (dol.Length < 0x100) { skipped.AddRange(cheats.Select(c => c.Name + ": not a DOL file")); return (applied, skipped); }
        var sections = Enumerable.Range(0, Sections)
            .Select(i => (Offset: BinaryPrimitives.ReadUInt32BigEndian(dol.AsSpan(4 * i)), Addr: BinaryPrimitives.ReadUInt32BigEndian(dol.AsSpan(0x48 + 4 * i)),
                          Size: BinaryPrimitives.ReadUInt32BigEndian(dol.AsSpan(0x90 + 4 * i))))
            .Where(s => s.Size > 0 && s.Offset + (long)s.Size <= dol.Length).ToList();

        foreach (var c in cheats)
        {
            if (Writes(c) is not { } writes) { skipped.Add($"{c.Name}: needs a code handler (not a plain write)"); continue; }
            var spots = new List<(long At, byte[] Bytes)>();
            foreach (var (addr, bytes) in writes)
            {
                var s = sections.FirstOrDefault(s => addr >= s.Addr && (long)addr + bytes.Length <= (long)s.Addr + s.Size);
                if (s.Size == 0) { spots.Clear(); break; }
                spots.Add((s.Offset + (addr - s.Addr), bytes));
            }
            if (spots.Count == 0 || spots.Count != writes.Count) { skipped.Add($"{c.Name}: writes outside main.dol (data kept in memory while the game runs)"); continue; }
            foreach (var (at, bytes) in spots) bytes.CopyTo(dol, at);
            applied.Add(c.Name);
        }
        return (applied, skipped);
    }
}
