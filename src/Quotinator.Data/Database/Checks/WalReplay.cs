using System.Buffers.Binary;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// The database as a reader sees it: the main file's pages with a <c>-wal</c>'s committed frames applied, built in
/// memory from the documented file format alone (#490, ADR 028, decision D as superseded 2026-11-16), so the
/// check needs no disk space and never lets SQLite open, lock or recover the originals.
/// <list type="bullet">
/// <item>
/// A frame is valid if and only if its salts match the header's and its checksum matches the one computed
/// consecutively over the header's first 24 bytes and the first 8 bytes and content of every frame up to and
/// including it (https://www.sqlite.org/fileformat.html §4.1, §4.2). Nothing documents whether a reader stops at
/// the first invalid frame, so the consecutive checksum runs over every frame, and a later frame is used only if
/// it is itself valid by it.
/// </item>
/// <item>A frame whose second field is not zero is a commit record, carrying the database's size in pages after the commit (§4.1).</item>
/// <item>
/// A page takes "the last valid instance of page P that is followed by a commit frame or is a commit frame
/// itself", and otherwise its version in the main file (§4.5). The image is sized to the last commit's page count.
/// </item>
/// </list>
/// </summary>
internal static class WalReplay
{
    private const int FrameHeaderLength = 24;

    /// <summary>
    /// The main file's bytes with every page the <c>-wal</c> committed replaced by its committed version, sized
    /// to the last commit's page count.
    /// </summary>
    /// <param name="mainFile">The main database file's bytes.</param>
    /// <param name="wal">The <c>-wal</c>'s bytes, header included; its header has passed the Journal layer.</param>
    /// <param name="pageSize">The database's page size.</param>
    internal static byte[] Apply(ReadOnlySpan<byte> mainFile, ReadOnlySpan<byte> wal, int pageSize)
    {
        if (wal.Length < JournalLayer.WalHeaderLength)
            return mainFile.ToArray();

        bool bigEndian = BinaryPrimitives.ReadUInt32BigEndian(wal) == JournalLayer.MagicBigEndianChecksum;
        uint salt1 = BinaryPrimitives.ReadUInt32BigEndian(wal[16..]);
        uint salt2 = BinaryPrimitives.ReadUInt32BigEndian(wal[20..]);
        WalChecksum checksum = WalChecksum.Of(wal[..24], bigEndian);

        Dictionary<uint, int> committed = [];
        Dictionary<uint, int> pending = [];
        uint? committedPages = null;
        int frameLength = FrameHeaderLength + pageSize;

        for (int offset = JournalLayer.WalHeaderLength; offset + frameLength <= wal.Length; offset += frameLength)
        {
            ReadOnlySpan<byte> frame = wal.Slice(offset, frameLength);
            checksum = checksum.Then(frame[..8], bigEndian).Then(frame[FrameHeaderLength..], bigEndian);

            bool valid = BinaryPrimitives.ReadUInt32BigEndian(frame[8..]) == salt1
                && BinaryPrimitives.ReadUInt32BigEndian(frame[12..]) == salt2
                && BinaryPrimitives.ReadUInt32BigEndian(frame[16..]) == checksum.First
                && BinaryPrimitives.ReadUInt32BigEndian(frame[20..]) == checksum.Second;
            if (!valid)
                continue;

            pending[BinaryPrimitives.ReadUInt32BigEndian(frame)] = offset + FrameHeaderLength;
            uint commitSize = BinaryPrimitives.ReadUInt32BigEndian(frame[4..]);
            if (commitSize == 0)
                continue;

            foreach ((uint page, int dataOffset) in pending)
                committed[page] = dataOffset;
            pending.Clear();
            committedPages = commitSize;
        }

        long length = committedPages is uint pages ? (long)pages * pageSize : mainFile.Length;
        byte[] image = new byte[length];
        mainFile[..(int)Math.Min(mainFile.Length, length)].CopyTo(image);
        foreach ((uint page, int dataOffset) in committed)
        {
            long at = ((long)page - 1) * pageSize;
            if (page >= 1 && at + pageSize <= length)
                wal.Slice(dataOffset, pageSize).CopyTo(image.AsSpan((int)at, pageSize));
        }

        return image;
    }
}

/// <summary>
/// The running checksum of a <c>-wal</c> (https://www.sqlite.org/fileformat.html §4.2): over pairs of 32-bit
/// words, s0 += x(i) + s1 and s1 += x(i+1) + s0, wrapping, with the words read in the byte order the magic
/// number names.
/// </summary>
/// <param name="First">s0.</param>
/// <param name="Second">s1.</param>
internal readonly record struct WalChecksum(uint First, uint Second)
{
    /// <summary>The checksum of <paramref name="content"/> from zero.</summary>
    /// <param name="content">Content whose length is a multiple of 8 bytes.</param>
    /// <param name="bigEndian">Whether the words are read big-endian.</param>
    internal static WalChecksum Of(ReadOnlySpan<byte> content, bool bigEndian) => new WalChecksum(0, 0).Then(content, bigEndian);

    /// <summary>This checksum continued over <paramref name="content"/>.</summary>
    /// <param name="content">Content whose length is a multiple of 8 bytes.</param>
    /// <param name="bigEndian">Whether the words are read big-endian.</param>
    internal WalChecksum Then(ReadOnlySpan<byte> content, bool bigEndian)
    {
        uint s0 = First;
        uint s1 = Second;
        for (int i = 0; i < content.Length; i += 8)
        {
            uint x0 = bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(content[i..]) : BinaryPrimitives.ReadUInt32LittleEndian(content[i..]);
            uint x1 = bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(content[(i + 4)..]) : BinaryPrimitives.ReadUInt32LittleEndian(content[(i + 4)..]);
            unchecked
            {
                s0 += x0 + s1;
                s1 += x1 + s0;
            }
        }

        return new WalChecksum(s0, s1);
    }
}
