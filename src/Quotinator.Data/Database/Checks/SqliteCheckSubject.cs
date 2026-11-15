using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Quotinator.Logging;

namespace Quotinator.Data.Database.Checks;

/// <summary>
/// The database one run of the SQLite check examines (#490, ADR 028), and what its earlier layers found
/// about it. It owns the one connection the query layers share and, when the <c>-wal</c> holds data, the copy
/// that connection reads, and removes both when the check ends.
/// <para>
/// The originals are never opened for writing. Without a <c>-wal</c> holding data, the query layers read the
/// main file through an <c>immutable</c> read-only open, which skips all locking and change detection
/// (https://www.sqlite.org/uri.html). With one, nothing documents whether an <c>immutable</c> open reads
/// the <c>-wal</c>, so the worst case is assumed: it does not. The database and its <c>-wal</c> are then
/// copied into the work folder and the copy is opened normally, so SQLite recovers the copy's <c>-wal</c>
/// (https://www.sqlite.org/wal.html, sections 4 and 5).
/// </para>
/// </summary>
/// <param name="databasePath">The database file being checked.</param>
/// <param name="workFolder">The reserved folder a copy is made in.</param>
/// <param name="expectedSchemaScripts">The DDL that defines the expected schema.</param>
/// <param name="canCreateFileIn">Answers whether a file can be created in a directory.</param>
/// <param name="logger">Logs each handled exception (ADR 022).</param>
internal sealed class SqliteCheckSubject(
    string databasePath,
    string workFolder,
    IReadOnlyList<string> expectedSchemaScripts,
    Func<string, bool> canCreateFileIn,
    ILogger logger) : IDisposable
{
    private SqliteConnection? _connection;
    private string? _copyFolder;
    private string? _whyNoConnection;
    private bool _connectionAttempted;

    /// <summary>The database file being checked.</summary>
    public string DatabasePath { get; } = Path.GetFullPath(databasePath);

    /// <summary>The database's <c>-wal</c>, which may not exist.</summary>
    public string WalPath => DatabasePath + "-wal";

    /// <summary>The directory that holds the database.</summary>
    public string DataDirectory => Path.GetDirectoryName(DatabasePath)!;

    /// <summary>The DDL that defines the expected schema.</summary>
    public IReadOnlyList<string> ExpectedSchemaScripts { get; } = expectedSchemaScripts;

    /// <summary>Answers whether a file can be created in a directory.</summary>
    public Func<string, bool> CanCreateFileIn { get; } = canCreateFileIn;

    /// <summary>Logs each handled exception.</summary>
    public ILogger Logger { get; } = logger;

    /// <summary>Whether the database file exists. A first start, or a Reset, has none yet, and that is no fault.</summary>
    public bool FileExists => File.Exists(DatabasePath);

    /// <summary>Whether the Storage layer could read the file. Set by that layer.</summary>
    public bool FileReadable { get; set; }

    /// <summary>The database's page size, read from its header by the Format layer.</summary>
    public int PageSize { get; set; }

    /// <summary>Whether the <c>-wal</c> holds at least one frame, as the Journal layer found.</summary>
    public bool WalHoldsData { get; set; }

    /// <summary>
    /// The connection the query layers share, opened on first use, or the reason none could be opened soundly.
    /// A reason makes every query layer inconclusive (decision D): it is never a fault in the file. Opening a
    /// damaged file throws <see cref="SqliteException"/>, which the calling layer reports as its finding.
    /// </summary>
    public async Task<(SqliteConnection? Connection, string? WhyNot)> OpenAsync()
    {
        (SqliteConnection? connection, string? whyNot) = Connection();
        if (connection is not null && connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        return (connection, whyNot);
    }

    private (SqliteConnection? Connection, string? WhyNot) Connection()
    {
        if (_connectionAttempted)
            return (_connection, _whyNoConnection);
        _connectionAttempted = true;

        string source = DatabasePath;
        if (WalHoldsData)
        {
            try
            {
                _copyFolder = Path.Combine(workFolder, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_copyFolder);
                source = Path.Combine(_copyFolder, Path.GetFileName(DatabasePath));
                CopyShared(DatabasePath, source);
                CopyShared(WalPath, source + "-wal");
            }
            // ADR 022: whether the copy can be made is only known by making it, so the exception is the answer.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogExceptionHandled(ex, "no copy of a database whose -wal holds data could be made to check it");
                _whyNoConnection =
                    $"The -wal holds data, so the database can only be checked through a copy, and none could be made in {workFolder}: {ex.Message}";
                return (null, _whyNoConnection);
            }
        }

        _connection = new SqliteConnection(WalHoldsData ? CopyConnectionString(source) : ImmutableConnectionString(source));
        return (_connection, null);
    }

    /// <summary>Closes the connection and removes the copy, if one was made.</summary>
    public void Dispose()
    {
        if (_connection is not null)
        {
            _connection.Dispose();
            SqliteConnection.ClearPool(_connection);
        }

        if (_copyFolder is null || !Directory.Exists(_copyFolder))
            return;

        try
        {
            Directory.Delete(_copyFolder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogExceptionHandled(ex, "the copy a database check made could not be removed");
        }
    }

    private static string ImmutableConnectionString(string path) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = new Uri(path).AbsoluteUri + "?immutable=1",
            Mode       = SqliteOpenMode.ReadOnly,
            Pooling    = false,
        }.ToString();

    private static string CopyConnectionString(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

    // SQLite holds its files open for shared reading and writing; File.Copy asks for more than that.
    private static void CopyShared(string source, string destination)
    {
        using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }
}
