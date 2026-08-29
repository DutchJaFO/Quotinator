using Dapper;
using Microsoft.Data.Sqlite;
using Quotinator.Data.Connections;
using Quotinator.Data.Entities;
using Quotinator.Data.Models;
using Quotinator.Data.Queries;

namespace Quotinator.Data.Repositories;

/// <summary>
/// SQLite implementation of <see cref="INotificationReader"/>.
/// <para>
/// SQL execution goes through <see cref="JoinQueryRepository{TResult}"/>/<see cref="IJoinStrategy{TResult}"/>
/// per ADR 017 — since #319 every read here is a two-table projection over
/// <c>System_NotificationTranslation</c>, and <see cref="NotificationEntity"/> is a concrete POCO, so
/// the ADR's one exemption does not apply.
/// </para>
/// <para>
/// The domain reader still sits above that mechanism, which ADR 017 explicitly allows: it owns the
/// paging arithmetic, the total count, and the missing-table fallback below —
/// <see cref="JoinQueryRepository{TResult}"/> deliberately does none of those.
/// </para>
/// </summary>
/// <param name="factory">Opens the connection for the total-row count, which is a bare <c>COUNT(*)</c> with no join and therefore outside ADR 017.</param>
/// <param name="activeRepository">Executes the active-notifications join.</param>
/// <param name="pageRepository">Executes the paged-history join.</param>
public sealed class NotificationReader(
    IDbConnectionFactory factory,
    JoinQueryRepository<NotificationEntity> activeRepository,
    JoinQueryRepository<NotificationEntity> pageRepository) : INotificationReader
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<NotificationEntity>> GetActiveNotificationsAsync(string? language = null)
    {
        string now = DateTime.UtcNow.ToString(SafeDateValue.TimestampFormat);
        try
        {
            return await activeRepository.QueryAsync(new { now, lang = language });
        }
        catch (SqliteException ex) when (IsMissingNotificationTable(ex))
        {
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<PagedItems<NotificationEntity>> GetPagedAsync(int page, int pageSize, string? language = null)
    {
        int limit  = pageSize == 0 ? -1 : pageSize;
        int offset = pageSize == 0 ? 0  : (page - 1) * pageSize;

        try
        {
            using System.Data.IDbConnection conn = factory.CreateConnection();
            conn.Open();
            int total = await conn.ExecuteScalarAsync<int>(Sql.Notifications.CountAll);
            IReadOnlyList<NotificationEntity> items =
                await pageRepository.QueryAsync(new { pageSize = limit, offset, lang = language });

            int effectivePageSize = pageSize == 0 ? items.Count : pageSize;
            return new PagedItems<NotificationEntity>(items, page, effectivePageSize, total);
        }
        catch (SqliteException ex) when (IsMissingNotificationTable(ex))
        {
            return new PagedItems<NotificationEntity>([], page, pageSize, 0);
        }
    }

    // Not exception-based migration recovery (CLAUDE.md's "No exception-based migration recovery"
    // policy governs InitialiseAsync's own version-vs-schema mismatch handling, which stays a hard
    // failure) — this is a read-only, display-only query reached from Home's degraded-state modal and
    // the /notifications page, both of which stay reachable while the database is genuinely mid- or
    // pre-migration (#263/#280's whole point). System_Notification not existing yet in that state is
    // an expected, already-logged condition, not a structural mismatch to interpret; "no active
    // notifications" is the only correct response, not a 500 that defeats the degraded-state UI's
    // purpose. SqliteErrorCode 1 (SQLITE_ERROR) covers many message shapes, so the message text is
    // also checked to stay narrowly scoped to this one table.
    //
    // #319 widened the match to the translation table: the same degraded state can now be reached with
    // System_Notification present but System_NotificationTranslation not yet created, since the two
    // arrive in separate migrations.
    internal static bool IsMissingNotificationTable(SqliteException ex)
        => ex.SqliteErrorCode == 1
           && (ex.Message.Contains("no such table: System_Notification", StringComparison.Ordinal)
            || ex.Message.Contains("no such table: System_NotificationTranslation", StringComparison.Ordinal));
}
