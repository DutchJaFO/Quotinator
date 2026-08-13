using System.Data;

namespace Quotinator.Data.Connections;

/// <summary>
/// Holds one open connection to the changelog database's shared-cache in-memory SQLite instance for
/// the app's lifetime. A shared-cache in-memory database (<c>mode=memory&amp;cache=shared</c>) is
/// destroyed the moment its last open connection closes — <see cref="IDbConnectionFactory.CreateConnection"/>
/// otherwise returns a new, closed connection per call, so without something holding one open
/// continuously, the database would vanish between requests. Resolved eagerly at startup (see
/// <c>Program.cs</c>), before anything else tries to use the changelog database, and disposed at
/// application shutdown via normal singleton disposal.
/// </summary>
/// <remarks>Opens the keep-alive connection immediately.</remarks>
/// <param name="factory">The keyed <see cref="IDbConnectionFactory"/> for the changelog database (see <see cref="DatabaseConnectionKeys.Changelog"/>).</param>
public sealed class ChangelogConnectionKeepAlive(IDbConnectionFactory factory) : IDisposable
{
    private readonly IDbConnection _connection = Open(factory);

    private static IDbConnection Open(IDbConnectionFactory factory)
    {
        var connection = factory.CreateConnection();
        connection.Open();
        return connection;
    }

    /// <inheritdoc/>
    public void Dispose() => _connection.Dispose();
}
