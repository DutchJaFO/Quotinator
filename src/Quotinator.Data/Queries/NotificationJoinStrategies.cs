using Quotinator.Data.Entities;

namespace Quotinator.Data.Queries;

/// <summary>
/// #319's three notification reads, each a two-table projection over
/// <c>System_Notification</c> and <c>System_NotificationTranslation</c>.
/// <para>
/// These exist because ADR 017 requires a join-capable read to execute through
/// <see cref="IJoinStrategy{TResult}"/>/<c>JoinQueryRepository</c> rather than a hand-rolled
/// connection, whenever the result is a concrete POCO — which <see cref="NotificationEntity"/> is. The
/// reads were single-table before this issue, which is why the reader was previously compliant while
/// opening its own connection.
/// </para>
/// <para>
/// Each strategy only returns its <c>Sql.Notifications</c> constant. The SQL itself stays in
/// <c>Sql.cs</c>, per this project's string-centralisation rule, which is also what keeps
/// <c>SqlIdCaseGuard</c>/<c>SqlSelectPresentationGuard</c> scanning it.
/// </para>
/// <para>
/// <c>Sql.Notifications.CountAll</c> has no strategy of its own: it is a bare <c>COUNT(*)</c> with no
/// join and no projection, so ADR 017 does not reach it.
/// </para>
/// </summary>
public static class NotificationJoinStrategies
{
    /// <summary>Undismissed, unexpired, non-deleted notifications, newest first. Binds <c>@now</c> and <c>@lang</c>.</summary>
    public sealed class Active : IJoinStrategy<NotificationEntity>
    {
        /// <inheritdoc/>
        public string BuildSql() => Sql.Notifications.SelectActive;
    }

    /// <summary>One page of the full history, newest first. Binds <c>@pageSize</c>, <c>@offset</c> and <c>@lang</c>.</summary>
    public sealed class Page : IJoinStrategy<NotificationEntity>
    {
        /// <inheritdoc/>
        public string BuildSql() => Sql.Notifications.SelectPage;
    }

    /// <summary>A single notification by id. Binds <c>@id</c> and <c>@lang</c>.</summary>
    public sealed class ById : IJoinStrategy<NotificationEntity>
    {
        /// <inheritdoc/>
        public string BuildSql() => Sql.Notifications.SelectById;
    }
}
