using System.Buffers.Binary;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// Journal (#490, ADR 028): whether the database's <c>-wal</c>, when it has one of non-zero length, has a
/// header the file format documents as valid (https://www.sqlite.org/fileformat.html#wal_file_format and
/// #checksum_algorithm). Nothing documents what SQLite does with a <c>-wal</c> whose header is invalid, so the
/// worst case is assumed, that it is discarded with the transactions it holds, and the layer fails. A
/// <c>-wal</c> whose page size differs from the database's is treated the same way: nothing documents it either.
/// </summary>
internal sealed class JournalLayer : IDatabaseCheckLayer
{
    /// <summary>The length of a <c>-wal</c>'s header (fileformat.html §4.1): anything beyond it is a frame.</summary>
    internal const int WalHeaderLength = 32;
    private const uint MagicLittleEndianChecksum = 0x377f0682;

    /// <summary>The magic number of a <c>-wal</c> whose checksums read their words big-endian (fileformat.html §4.2).</summary>
    internal const uint MagicBigEndianChecksum = 0x377f0683;
    private const uint FileFormatVersion = 3007000;

    /// <inheritdoc/>
    public DatabaseCheckLayer Layer => DatabaseCheckLayer.Journal;

    /// <inheritdoc/>
    public bool RunsAtFast => true;

    /// <inheritdoc/>
    public async Task<DatabaseCheckLayerResult> RunAsync(SqliteCheckSubject subject)
    {
        if (!subject.FileExists)
            return LayerResults.NothingToCheck(Layer);

        FileInfo wal = new(subject.WalPath);
        if (!wal.Exists || wal.Length == 0)
            return LayerResults.Passed(Layer);

        if (wal.Length < WalHeaderLength)
            return LayerResults.Failed(Layer, $"The -wal is {wal.Length} bytes long, shorter than its 32-byte header.");

        byte[] header = new byte[WalHeaderLength];
        using (FileStream stream = new(subject.WalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            await stream.ReadExactlyAsync(header);

        uint magic = Word(header, 0);
        if (magic is not (MagicLittleEndianChecksum or MagicBigEndianChecksum))
            return LayerResults.Failed(Layer, $"The -wal's magic number, 0x{magic:x8}, is not one the file format allows.");

        uint version = Word(header, 4);
        if (version != FileFormatVersion)
            return LayerResults.Failed(Layer, $"The -wal's file format version, {version}, is not {FileFormatVersion}.");

        uint pageSize = Word(header, 8);
        if (pageSize != subject.PageSize)
            return LayerResults.Failed(Layer, $"The -wal's page size, {pageSize}, differs from the database's, {subject.PageSize}: it does not belong to this database.");

        WalChecksum checksum = WalChecksum.Of(header.AsSpan(0, 24), bigEndian: magic == MagicBigEndianChecksum);
        if (checksum.First != Word(header, 24) || checksum.Second != Word(header, 28))
            return LayerResults.Failed(Layer, "The -wal's header checksum does not match its header.");

        return LayerResults.Passed(Layer);
    }

    // Every value in the WAL header is a big-endian 32-bit word (fileformat.html §4.1).
    private static uint Word(byte[] header, int offset) => BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(offset, 4));
}
