using System.Data;

namespace Quotinator.Data.Connections;

/// <summary>
/// Holds one open connection to the changelog database's shared-cache in-memory SQLite instance for
/// the app's lifetime. A shared-cache in-memory database (<c>mode=memory&amp;cache=shared</c>) is
/// destroyed the moment its last open connection closes — <see cref="IDbConnectionFactory.CreateConnection"/>
/// otherwise returns a new, closed connection per call, so without something holding one open
/// continuously, the database would vanish between requests.
/// <para>
/// <b>No longer used in production as of #309 step 14.</b> The changelog database is a file
/// (<see cref="Paths.DataPaths.ChangelogDatabaseFile"/>) rather than an in-memory instance, because
/// holding a connection open turned out not to keep a shared-cache in-memory database alive for the
/// life of a real process — found live thirteen minutes into a container run, after which every read
/// fell back to JSON permanently. This class is retained as reusable infrastructure (developer
/// decision, 2026-08-18) and is exercised by tests that deliberately use in-memory SQLite for speed
/// and isolation; it is not wired into <c>Program.cs</c>.
/// </para>
/// </summary>
/// <remarks>Opens the keep-alive connection immediately.</remarks>
/// <param name="factory">The keyed <see cref="IDbConnectionFactory"/> for the changelog database (see <see cref="DatabaseConnectionKeys.Changelog"/>).</param>
public sealed class ChangelogConnectionKeepAlive(IDbConnectionFactory factory) : IDisposable
{
    private readonly IDbConnection _connection = Open(factory);

    private static IDbConnection Open(IDbConnectionFactory factory)
    {
        IDbConnection connection = factory.CreateConnection();
        connection.Open();
        return connection;
    }

    /// <inheritdoc/>
    public void Dispose() => _connection.Dispose();
}
