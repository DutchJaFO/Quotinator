using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Queries;
using Quotinator.Data.Repositories;

namespace Quotinator.Data.Tests.Repositories;

/// <summary>
/// Builds a <see cref="NotificationReader"/> the same way <c>Program.cs</c> registers it — one
/// <see cref="JoinQueryRepository{TResult}"/> per query shape, per ADR 017.
/// <para>
/// One helper rather than the three-argument construction repeated at every call site: a test that
/// wired only some of the repositories would still compile and would fail for a reason unrelated to
/// what it was testing.
/// </para>
/// </summary>
internal static class TestNotificationReader
{
    /// <summary>Creates a reader over <paramref name="factory"/>'s database.</summary>
    internal static NotificationReader Create(IDbConnectionFactory factory)
        => new(factory,
            new JoinQueryRepository<NotificationEntity>(factory, new NotificationJoinStrategies.Active()),
            new JoinQueryRepository<NotificationEntity>(factory, new NotificationJoinStrategies.Page()));

    /// <summary>Creates a reader over the SQLite file at <paramref name="dbPath"/>.</summary>
    internal static NotificationReader Create(string dbPath)
        => Create(new SqliteConnectionFactory(dbPath));
}
