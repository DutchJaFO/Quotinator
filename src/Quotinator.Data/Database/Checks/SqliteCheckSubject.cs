using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Quotinator.Data.Enums;
using SQLitePCL;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// The database one run of the SQLite check examines (#490, ADR 028), and what its earlier layers found
/// about it. It owns the one connection the query layers share, and closes it when the check ends.
/// <para>
/// The originals are never opened for writing, and nothing is written to disk. Without a <c>-wal</c> holding
/// data, the query layers read the main file through an <c>immutable</c> read-only open, which skips all
/// locking and change detection (https://www.sqlite.org/uri.html). With one, nothing documents whether an
/// <c>immutable</c> open reads the <c>-wal</c>, so the worst case is assumed: it does not. The query layers then
/// read an in-memory image instead, the main file with the <c>-wal</c>'s committed frames applied by
/// <see cref="WalReplay"/> (decision D as superseded, developer, 2026-11-16).
/// </para>
/// </summary>
/// <param name="databasePath">The database file being checked.</param>
/// <param name="expectedSchemaScripts">The DDL that defines the expected schema.</param>
/// <param name="expectedSchemaVersions">The version each version table records once every known migration has run.</param>
/// <param name="canCreateFileIn">Answers whether a file can be created in a directory.</param>
/// <param name="logger">Logs each handled exception (ADR 022).</param>
internal sealed class SqliteCheckSubject(
    string databasePath,
    IReadOnlyList<string> expectedSchemaScripts,
    IReadOnlyDictionary<SchemaVersionTable, int> expectedSchemaVersions,
    Func<string, bool> canCreateFileIn,
    ILogger logger) : IDisposable
{
    // c3ref/deserialize.html: the image is the connection's to free once it closes, and is opened read-only.
    private const int DeserializeFlags = raw.SQLITE_DESERIALIZE_FREEONCLOSE | raw.SQLITE_DESERIALIZE_READONLY;

    // fileformat.html §1.3: offsets 18 and 19 are the file format write and read versions, 1 for rollback
    // journalling and 2 for WAL.
    private const int WriteVersionOffset = 18;
    private const int ReadVersionOffset = 19;
    private const byte RollbackJournalling = 1;

    private SqliteConnection? _connection;

    /// <summary>The database file being checked.</summary>
    public string DatabasePath { get; } = Path.GetFullPath(databasePath);

    /// <summary>The database's <c>-wal</c>, which may not exist.</summary>
    public string WalPath => DatabasePath + "-wal";

    /// <summary>The directory that holds the database.</summary>
    public string DataDirectory => Path.GetDirectoryName(DatabasePath)!;

    /// <summary>The DDL that defines the expected schema.</summary>
    public IReadOnlyList<string> ExpectedSchemaScripts { get; } = expectedSchemaScripts;

    /// <summary>The version each version table records once every migration this build knows has run.</summary>
    public IReadOnlyDictionary<SchemaVersionTable, int> ExpectedSchemaVersions { get; } = expectedSchemaVersions;

    /// <summary>Answers whether a file can be created in a directory.</summary>
    public Func<string, bool> CanCreateFileIn { get; } = canCreateFileIn;

    /// <summary>Logs each handled exception.</summary>
    public ILogger Logger { get; } = logger;

    /// <summary>Whether the database file exists. A first start, or a Reset, has none yet, and that is no fault.</summary>
    public bool FileExists => File.Exists(DatabasePath);

    /// <summary>Whether the Storage layer could read the file. Set by that layer, and what the reading layers need.</summary>
    public bool FileReadable { get; set; }

    /// <summary>The database's page size, read from its header by the Format layer.</summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Whether the <c>-wal</c> holds at least one frame beyond its header, so it is part of the database's state and
    /// an <c>immutable</c> open of the main file could miss it.
    /// </summary>
    public bool WalHoldsFrames => File.Exists(WalPath) && new FileInfo(WalPath).Length > JournalLayer.WalHeaderLength;

    /// <summary>
    /// The connection the query layers share, opened on first use: on the in-memory image when the <c>-wal</c>
    /// holds frames, otherwise an <c>immutable</c> open of the main file. A file SQLite rejects throws
    /// <see cref="SqliteException"/>, which the calling layer reports as its finding.
    /// </summary>
    public async Task<SqliteConnection> OpenAsync()
    {
        if (_connection is not null)
            return _connection;

        if (!WalHoldsFrames)
        {
            _connection = new SqliteConnection(ImmutableConnectionString(DatabasePath));
            await _connection.OpenAsync();
            return _connection;
        }

        byte[] mainFile = await ReadSharedAsync(DatabasePath);
        byte[] wal = await ReadSharedAsync(WalPath);
        byte[] image = WalReplay.Apply(mainFile, wal, PageSize);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = ":memory:", Pooling = false }.ToString());
        await _connection.OpenAsync();
        Deserialize(_connection, image);
        return _connection;
    }

    /// <summary>Closes the connection.</summary>
    public void Dispose() => _connection?.Dispose();

    // c3ref/deserialize.html: "The deserialized database should not be in WAL mode", and the application can set
    // bytes 18 and 19 "to 0x01 ... to force the database file into rollback mode". The buffer is SQLite's own,
    // allocated with sqlite3_malloc64, since FREEONCLOSE hands it to SQLite to free.
    private static void Deserialize(SqliteConnection connection, byte[] image)
    {
        if (image.Length > ReadVersionOffset)
        {
            image[WriteVersionOffset] = RollbackJournalling;
            image[ReadVersionOffset] = RollbackJournalling;
        }

        IntPtr buffer = raw.sqlite3_malloc64(image.Length);
        if (buffer == IntPtr.Zero)
            throw new OutOfMemoryException($"SQLite could not allocate {image.Length} bytes to check the database in memory.");

        Marshal.Copy(image, 0, buffer, image.Length);
        int rc = raw.sqlite3_deserialize(connection.Handle, "main", buffer, image.Length, image.Length, DeserializeFlags);
        SqliteException.ThrowExceptionForRC(rc, connection.Handle);
    }

    private static async Task<byte[]> ReadSharedAsync(string path)
    {
        // SQLite holds its files open for shared reading and writing; File.ReadAllBytes asks for more than that.
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using MemoryStream buffer = new();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static string ImmutableConnectionString(string path) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = new Uri(path).AbsoluteUri + "?immutable=1",
            Mode       = SqliteOpenMode.ReadOnly,
            Pooling    = false,
        }.ToString();
}
