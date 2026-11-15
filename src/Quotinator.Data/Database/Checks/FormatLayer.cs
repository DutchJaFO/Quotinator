using System.Buffers.Binary;
using Quotinator.Data.Enums;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// Format (#490, ADR 028): whether the file is a SQLite database by its documented 100-byte header
/// (https://www.sqlite.org/fileformat.html#the_database_header). It reads the magic string and the page size
/// only. The file's length is not judged: nothing documents whether a file longer or shorter than the
/// in-header size is valid, so missing pages are the Pages layer's question.
/// </summary>
internal sealed class FormatLayer : IDatabaseCheckLayer
{
    /// <summary>The 16-byte string every database file begins with.</summary>
    private static readonly byte[] HeaderMagic = "SQLite format 3\0"u8.ToArray();

    private const int HeaderLength = 100;
    private const int MinPageSize = 512;
    private const int MaxTwoBytePageSize = 32768;

    /// <summary>The stored page size value that stands for 65536, which two bytes cannot hold.</summary>
    private const int PageSize65536Marker = 1;

    /// <inheritdoc/>
    public DatabaseCheckLayer Layer => DatabaseCheckLayer.Format;

    /// <inheritdoc/>
    public bool RunsAtFast => true;

    /// <inheritdoc/>
    public async Task<DatabaseCheckLayerResult> RunAsync(SqliteCheckSubject subject)
    {
        if (!subject.FileExists)
            return LayerResults.NothingToCheck(Layer);

        byte[] header = new byte[HeaderLength];
        int read;
        using (FileStream stream = new(subject.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            read = await stream.ReadAtLeastAsync(header, HeaderLength, throwOnEndOfStream: false);

        if (read < HeaderLength)
            return LayerResults.Failed(Layer, $"The file is {read} bytes long, shorter than the 100-byte database header.");

        if (!header.AsSpan(0, HeaderMagic.Length).SequenceEqual(HeaderMagic))
            return LayerResults.Failed(Layer, "The file does not begin with the SQLite database header string.");

        int stored = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(16, 2));
        if (stored != PageSize65536Marker && (stored < MinPageSize || stored > MaxTwoBytePageSize || (stored & (stored - 1)) != 0))
            return LayerResults.Failed(Layer, $"The header's page size, {stored}, is not one the file format allows.");

        subject.PageSize = stored == PageSize65536Marker ? 65536 : stored;
        return LayerResults.Passed(Layer);
    }
}
