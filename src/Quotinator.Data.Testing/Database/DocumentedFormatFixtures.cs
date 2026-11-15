using System.Buffers.Binary;
using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Testing.Database;

/// <summary>
/// Builds damaged SQLite files for the database check's tests (#490, ADR 028), each through the byte
/// layout that SQLite's documented file format defines: <see href="https://www.sqlite.org/fileformat.html"/>.
/// <para>
/// SQLite documents no procedure for damaging a file on purpose (<c>howtocorrupt.html</c> describes only
/// accidental causes), so a fixture never relies on how SQLite happens to react to a misuse. It writes the
/// bytes the format defines, and each method first asserts the precondition its damage depends on, so a
/// fixture that no longer builds what it claims fails loudly instead of testing something else.
/// </para>
/// </summary>
public static class DocumentedFormatFixtures
{
    /// <summary>The 16-byte string every database file begins with (fileformat.html §1.3).</summary>
    private static readonly byte[] HeaderMagic = "SQLite format 3\0"u8.ToArray();

    /// <summary>The length of the database header (fileformat.html §1.3).</summary>
    private const int HeaderLength = 100;

    /// <summary>The length of the WAL header (fileformat.html §4.1).</summary>
    private const int WalHeaderLength = 32;

    /// <summary>The two values the WAL header's magic number may hold (fileformat.html §4.1).</summary>
    private static readonly uint[] WalMagics = [0x377f0682, 0x377f0683];

    /// <summary>The b-tree page types the format allows (fileformat.html §1.6).</summary>
    private static readonly byte[] BTreePageTypes = [0x02, 0x05, 0x0a, 0x0d];

    /// <summary>A page type the format forbids: "Any other value for the b-tree page type is an error."</summary>
    private const byte ForbiddenPageType = 0x07;

    /// <summary>The page type of a leaf index b-tree page (fileformat.html §1.6).</summary>
    private const byte LeafIndexPageType = 0x0a;

    /// <summary>The serial type of an 8-bit twos-complement integer in a record (fileformat.html §2.1).</summary>
    private const long SerialTypeInt8 = 1;

    /// <summary>
    /// Creates a database at <paramref name="path"/> in rollback-journal mode and runs each statement in
    /// order. No <c>-wal</c> or journal is left behind.
    /// </summary>
    /// <param name="path">Where to create the database. Must not exist yet.</param>
    /// <param name="statements">DDL and data statements, run in order.</param>
    public static void Create(string path, params IEnumerable<string> statements)
    {
        if (File.Exists(path))
            throw new InvalidOperationException($"Fixture precondition: {path} already exists.");

        using SqliteConnection connection = Open(path);
        foreach (string statement in statements)
            connection.Execute(statement);
    }

    /// <summary>
    /// Creates a database at <paramref name="path"/> with a <c>-wal</c> beside it that holds committed
    /// transactions: <paramref name="mainStatements"/> are checkpointed into the main file, and
    /// <paramref name="walStatements"/> exist only in the <c>-wal</c>. This is the state an unclean stop leaves
    /// (wal.html §4).
    /// <para>
    /// The files are copied while a connection is still open, because the last connection to close a WAL
    /// database checkpoints it and deletes the <c>-wal</c>. The copy opens the files for shared reading and
    /// writing, as SQLite itself does, so it does not conflict with SQLite's own handles.
    /// </para>
    /// </summary>
    /// <param name="path">Where to create the database. Must not exist yet.</param>
    /// <param name="mainStatements">Statements whose result is checkpointed into the main file.</param>
    /// <param name="walStatements">Statements whose result exists only in the <c>-wal</c>.</param>
    public static void CreateWithWalHoldingData(string path, IEnumerable<string> mainStatements, IEnumerable<string> walStatements)
    {
        if (File.Exists(path))
            throw new InvalidOperationException($"Fixture precondition: {path} already exists.");

        string staging = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $"staging-{Guid.NewGuid():N}.db");
        try
        {
            using (SqliteConnection connection = Open(staging))
            {
                connection.Execute("PRAGMA journal_mode = WAL;");
                connection.Execute("PRAGMA wal_autocheckpoint = 0;");
                foreach (string statement in mainStatements)
                    connection.Execute(statement);
                connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");

                foreach (string statement in walStatements)
                    connection.Execute(statement);

                CopyShared(staging, path);
                CopyShared(staging + "-wal", path + "-wal");
            }

            if (new FileInfo(path + "-wal").Length < WalHeaderLength)
                throw new InvalidOperationException("Fixture precondition: the -wal holds no frames.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string sidecar in new[] { staging, staging + "-wal", staging + "-shm" })
                File.Delete(sidecar);
        }
    }

    /// <summary>
    /// Runs <paramref name="statements"/> against the existing database at <paramref name="path"/>, closing the
    /// connection afterwards so no <c>-wal</c> is left behind.
    /// </summary>
    /// <param name="path">The database to write to.</param>
    /// <param name="statements">The statements to run.</param>
    public static void Execute(string path, params IEnumerable<string> statements)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Fixture precondition: {path} does not exist.");

        using SqliteConnection connection = Open(path);
        foreach (string statement in statements)
            connection.Execute(statement);
    }

    /// <summary>
    /// Leaves the existing database at <paramref name="path"/> with a <c>-wal</c> holding committed
    /// transactions: <paramref name="walStatements"/> exist only in the <c>-wal</c>, as after an unclean stop
    /// (wal.html §4). The database is put in WAL mode if it is not already.
    /// <para>
    /// Works on a copy, for the reason <see cref="CreateWithWalHoldingData"/> gives: closing the last connection
    /// would checkpoint and delete the <c>-wal</c>. The copy and its <c>-wal</c> then replace the original.
    /// </para>
    /// </summary>
    /// <param name="path">The database to leave a <c>-wal</c> beside. It must have no sidecars of its own.</param>
    /// <param name="walStatements">Statements whose result exists only in the <c>-wal</c>.</param>
    public static void AddWalHoldingData(string path, params IEnumerable<string> walStatements)
    {
        if (File.Exists(path + "-wal") || File.Exists(path + "-shm"))
            throw new InvalidOperationException($"Fixture precondition: {path} already has sidecars.");

        string staging = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $"staging-{Guid.NewGuid():N}.db");
        string held = staging + ".held";
        try
        {
            File.Copy(path, staging);
            using (SqliteConnection connection = Open(staging))
            {
                connection.Execute("PRAGMA journal_mode = WAL;");
                connection.Execute("PRAGMA wal_autocheckpoint = 0;");
                connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
                foreach (string statement in walStatements)
                    connection.Execute(statement);

                CopyShared(staging, held);
                CopyShared(staging + "-wal", held + "-wal");
            }

            File.Copy(held, path, overwrite: true);
            File.Copy(held + "-wal", path + "-wal");

            if (new FileInfo(path + "-wal").Length < WalHeaderLength)
                throw new InvalidOperationException("Fixture precondition: the -wal holds no frames.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string file in new[] { staging, staging + "-wal", staging + "-shm", held, held + "-wal" })
                File.Delete(file);
        }
    }

    /// <summary>
    /// Writes a file at <paramref name="path"/> that does not begin with the header's magic string, so by the
    /// documented format it is not a database.
    /// </summary>
    /// <param name="path">Where to write the file.</param>
    public static void WriteNotADatabase(string path)
    {
        byte[] content = new byte[4096];
        byte[] text = "This file is not a database."u8.ToArray();
        for (int i = 0; i < content.Length; i++)
            content[i] = text[i % text.Length];

        if (content.AsSpan(0, HeaderMagic.Length).SequenceEqual(HeaderMagic))
            throw new InvalidOperationException("Fixture precondition: the content begins with the header magic.");

        File.WriteAllBytes(path, content);
    }

    /// <summary>
    /// Cuts the database at <paramref name="path"/> to half its length, rounded down to a whole page. Asserts
    /// first that the in-header database size is valid, because only then does SQLite take the size from the
    /// header rather than from the file's length (fileformat.html §1.3.7), which is what makes the missing
    /// pages detectable.
    /// </summary>
    /// <param name="path">The database to truncate.</param>
    public static void TruncateToHalf(string path)
    {
        byte[] header = ReadHeader(path);
        if (!InHeaderSizeIsValid(header))
            throw new InvalidOperationException("Fixture precondition: the in-header database size is not valid.");

        int pageSize = PageSize(header);
        long pages = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(28, 4));
        if (pages < 4)
            throw new InvalidOperationException($"Fixture precondition: the database has only {pages} pages, too few to halve.");

        long length = new FileInfo(path).Length / 2 / pageSize * pageSize;
        using FileStream stream = new(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
    }

    /// <summary>
    /// Writes a page-type byte the format forbids into the b-tree page that is the root of
    /// <paramref name="tableOrIndexName"/>, located through its <c>rootpage</c> in the schema table
    /// (fileformat.html §1.6, §2.6).
    /// </summary>
    /// <param name="path">The database to damage.</param>
    /// <param name="tableOrIndexName">The table or index whose root page is damaged.</param>
    public static void DamageRootPageType(string path, string tableOrIndexName)
    {
        long rootPage = RootPage(path, tableOrIndexName);
        int pageSize = PageSize(ReadHeader(path));
        long offset = PageHeaderOffset(rootPage, pageSize);

        using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = offset;
        int current = stream.ReadByte();
        if (Array.IndexOf(BTreePageTypes, (byte)current) < 0)
            throw new InvalidOperationException($"Fixture precondition: page {rootPage} holds page type {current}, not a b-tree page.");

        stream.Position = offset;
        stream.WriteByte(ForbiddenPageType);
    }

    /// <summary>
    /// Rewrites the key of the last entry in the index <paramref name="indexName"/>, in place, so the index no
    /// longer agrees with its table. The index must fit on its root page, which must be a leaf, and the key's
    /// first column must be stored as an 8-bit integer (fileformat.html §1.6, §2.1). The new value is larger
    /// than the old, so the entries stay in key order and the b-tree itself remains well-formed: only the
    /// agreement with the table is broken.
    /// </summary>
    /// <param name="path">The database to damage.</param>
    /// <param name="indexName">The index whose last entry is rewritten.</param>
    /// <param name="newValue">The value written in place of the last key's first column.</param>
    public static void RewriteLastIndexKey(string path, string indexName, sbyte newValue)
    {
        long rootPage = RootPage(path, indexName);
        int pageSize = PageSize(ReadHeader(path));
        long pageStart = (rootPage - 1) * pageSize;
        long headerOffset = PageHeaderOffset(rootPage, pageSize);

        byte[] page = new byte[pageSize];
        using FileStream stream = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = pageStart;
        stream.ReadExactly(page);

        int header = (int)(headerOffset - pageStart);
        if (page[header] != LeafIndexPageType)
            throw new InvalidOperationException($"Fixture precondition: the root page of {indexName} is type {page[header]}, not a leaf index page.");

        int cellCount = BinaryPrimitives.ReadUInt16BigEndian(page.AsSpan(header + 3, 2));
        if (cellCount < 1)
            throw new InvalidOperationException($"Fixture precondition: {indexName} has no entries.");

        // A leaf page's header is 8 bytes; the cell pointer array follows it, in key order (§1.6).
        int cell = BinaryPrimitives.ReadUInt16BigEndian(page.AsSpan(header + 8 + (2 * (cellCount - 1)), 2));

        // An index leaf cell: the payload's length as a varint, then the payload, which is a record (§1.6).
        int position = cell;
        long payloadLength = ReadVarint(page, ref position);
        int recordStart = position;
        long recordHeaderLength = ReadVarint(page, ref position);
        long firstSerialType = ReadVarint(page, ref position);

        if (payloadLength > pageSize - 35)
            throw new InvalidOperationException($"Fixture precondition: the last entry of {indexName} overflows its page.");
        if (firstSerialType != SerialTypeInt8)
            throw new InvalidOperationException($"Fixture precondition: the last key of {indexName} has serial type {firstSerialType}, not an 8-bit integer.");

        int valueOffset = recordStart + (int)recordHeaderLength;
        sbyte current = (sbyte)page[valueOffset];
        if (newValue <= current)
            throw new InvalidOperationException($"Fixture precondition: {newValue} is not larger than the current key {current}, so key order would break.");

        stream.Position = pageStart + valueOffset;
        stream.WriteByte((byte)newValue);
    }

    /// <summary>
    /// Writes zero over the magic number of the <c>-wal</c> at <paramref name="walPath"/>, which then holds
    /// neither value the format allows (fileformat.html §4.1).
    /// </summary>
    /// <param name="walPath">The <c>-wal</c> to damage.</param>
    public static void CorruptWalMagic(string walPath)
    {
        RequireValidWalMagic(walPath);

        using FileStream stream = new(walPath, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Write(new byte[4]);
    }

    /// <summary>
    /// Inverts every bit of checksum-1 in the header of the <c>-wal</c> at <paramref name="walPath"/>, so it no
    /// longer matches the first 24 bytes of the header as fileformat.html §4.2 computes it.
    /// </summary>
    /// <param name="walPath">The <c>-wal</c> to damage.</param>
    public static void CorruptWalHeaderChecksum(string walPath)
    {
        RequireValidWalMagic(walPath);

        byte[] checksum = new byte[4];
        using FileStream stream = new(walPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = 24;
        stream.ReadExactly(checksum);
        for (int i = 0; i < checksum.Length; i++)
            checksum[i] = (byte)~checksum[i];

        stream.Position = 24;
        stream.Write(checksum);
    }

    /// <summary>
    /// Runs <paramref name="statements"/> against an existing database with foreign key enforcement turned off
    /// through the documented <c>PRAGMA foreign_keys</c>, so a row may reference one that does not exist.
    /// Turned off explicitly, because the bundled library is compiled with enforcement on by default.
    /// </summary>
    /// <param name="path">The database to write to.</param>
    /// <param name="statements">The statements to run.</param>
    public static void WriteWithoutForeignKeyEnforcement(string path, params IEnumerable<string> statements)
    {
        using SqliteConnection connection = Open(path);
        connection.Execute("PRAGMA foreign_keys = OFF;");
        if (connection.ExecuteScalar<long>("PRAGMA foreign_keys;") != 0)
            throw new InvalidOperationException("Fixture precondition: foreign key enforcement could not be turned off.");

        foreach (string statement in statements)
            connection.Execute(statement);
    }

    /// <summary>
    /// Whether the in-header database size is valid: non-zero, with the change counter exactly matching the
    /// version-valid-for number (fileformat.html §1.3.7).
    /// </summary>
    /// <param name="header">The first 100 bytes of the database.</param>
    public static bool InHeaderSizeIsValid(byte[] header) =>
        BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(28, 4)) != 0
        && BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(24, 4)) == BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(92, 4));

    /// <summary>Reads the 100-byte header of the database at <paramref name="path"/>.</summary>
    /// <param name="path">The database to read.</param>
    public static byte[] ReadHeader(string path)
    {
        byte[] header = new byte[HeaderLength];
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.ReadExactly(header);
        if (!header.AsSpan(0, HeaderMagic.Length).SequenceEqual(HeaderMagic))
            throw new InvalidOperationException($"Fixture precondition: {path} does not begin with the header magic.");

        return header;
    }

    private static SqliteConnection Open(string path)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    // Immutable, not only read-only: a read-only connection to a WAL database creates a -wal and -shm it
    // cannot remove when it closes, which would leave the fixture with sidecars no test asked for.
    private static long RootPage(string path, string name)
    {
        using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = new Uri(Path.GetFullPath(path)).AbsoluteUri + "?immutable=1",
            Mode       = SqliteOpenMode.ReadOnly,
            Pooling    = false,
        }.ToString());
        connection.Open();

        long? rootPage = connection.ExecuteScalar<long?>(Sql.SchemaMetadata.SelectRootPage, new { name });
        return rootPage is > 1
            ? rootPage.Value
            : throw new InvalidOperationException($"Fixture precondition: {name} has no root page of its own.");
    }

    // The page size is 2 bytes big-endian at offset 16; the value 1 means 65536 (§1.3.2).
    private static int PageSize(byte[] header)
    {
        int value = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(16, 2));
        return value == 1 ? 65536 : value;
    }

    // Page 1's b-tree header follows the 100-byte database header; every other page's starts the page (§1.6).
    private static long PageHeaderOffset(long pageNumber, int pageSize) =>
        ((pageNumber - 1) * pageSize) + (pageNumber == 1 ? HeaderLength : 0);

    // A big-endian varint of one to nine bytes: the high bit of each of the first eight says another follows,
    // and the ninth contributes all eight bits (§1.6).
    private static long ReadVarint(byte[] buffer, ref int position)
    {
        long value = 0;
        for (int i = 0; i < 8; i++)
        {
            byte b = buffer[position++];
            value = (value << 7) | (long)(b & 0x7f);
            if ((b & 0x80) == 0)
                return value;
        }

        return (value << 8) | buffer[position++];
    }

    private static void RequireValidWalMagic(string walPath)
    {
        byte[] header = new byte[WalHeaderLength];
        using (FileStream stream = new(walPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            stream.ReadExactly(header);

        uint magic = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        if (Array.IndexOf(WalMagics, magic) < 0)
            throw new InvalidOperationException($"Fixture precondition: {walPath} does not hold a valid WAL magic number.");
    }

    // SQLite holds its files open for shared reading and writing; File.Copy asks for exclusive write access
    // to the source and would fail while the connection is open.
    private static void CopyShared(string source, string destination)
    {
        using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }
}
