using Microsoft.AspNetCore.Components;
using Quotinator.Api.Startup;

namespace Quotinator.Api.Components.Controls;

/// <summary>
/// The degraded-state banner a page shows while the database is unhealthy (#350): the recorded failure
/// reason, and <see cref="DatabaseRepairActions"/> beneath it.
/// <para>
/// It exists because the repair used to live only in <see cref="StartupErrorModal"/>, which an operator
/// dismisses once: after that the degraded pages named a remedy with no way to run it. Every page that
/// can carry an interactive control includes this, so the repair follows the operator rather than
/// belonging to one screen.
/// </para>
/// <para>
/// Pages rendered as static SSR (<c>/about</c>, <c>/stats</c>) cannot carry it without a render mode of
/// their own, and do not: the nav reaches <c>/rest-api</c> from anywhere, which is also where the
/// modal's Continue lands.
/// </para>
/// </summary>
public partial class DegradedDatabaseBanner
{
    #region Private

    [Inject] private DatabaseHealthState DatabaseHealth { get; set; } = default!;

    private string? FailureReason => DatabaseHealth.FailureReason;

    #endregion
}
