using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Testing.Database;

/// <summary>
/// Builds a <see cref="NotificationReader"/> the same way <c>Program.cs</c> registers it — one
/// <see cref="JoinQueryRepository{TResult}"/> per query shape, per ADR 017.
/// <para>
/// One helper rather than the three-argument construction repeated at every call site: a test that
/// wired only some of the repositories would still compile and would fail for a reason unrelated to
/// what it was testing.
/// </para>
/// <para>
/// Lives here rather than inside <c>Quotinator.Data.Tests</c>, where #319 first put it: once a producer
/// outside that project needed a real reader (#304's seeding-path tests, in
/// <c>Quotinator.Core.Tests</c>), an internal helper in one test project could only be duplicated. This
/// project exists for exactly that — shared test helpers, referenced from test projects only.
/// </para>
/// </summary>
public static class TestNotificationReader
{
    /// <summary>Creates a reader over <paramref name="factory"/>'s database.</summary>
    /// <param name="factory">Opens connections to the database under test.</param>
    public static NotificationReader Create(IDbConnectionFactory factory)
        => new(factory,
            new JoinQueryRepository<NotificationEntity>(factory, new NotificationJoinStrategies.Active()),
            new JoinQueryRepository<NotificationEntity>(factory, new NotificationJoinStrategies.Page()));

    /// <summary>Creates a reader over the SQLite file at <paramref name="dbPath"/>.</summary>
    /// <param name="dbPath">Path to the SQLite file under test.</param>
    public static NotificationReader Create(string dbPath)
        => Create(new SqliteConnectionFactory(dbPath));
}
