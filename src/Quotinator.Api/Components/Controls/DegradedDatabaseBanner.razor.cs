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
/// Pages rendered as static SSR (<c>/about</c>) cannot carry it without a render mode of their own, and
/// do not: the nav reaches <c>/rest-api</c> from anywhere, which is also where the modal's Continue
/// lands.
/// </para>
/// </summary>
public partial class DegradedDatabaseBanner : IDisposable
{
    #region Public

    /// <summary>Stops listening to <see cref="DatabaseHealthState"/>, which outlives this component.</summary>
    public void Dispose() => DatabaseHealth.Changed -= OnHealthChanged;

    #endregion

    #region Protected

    /// <inheritdoc/>
    protected override void OnInitialized() => DatabaseHealth.Changed += OnHealthChanged;

    #endregion

    #region Private

    [Inject] private DatabaseHealthState DatabaseHealth { get; set; } = default!;

    private string? FailureReason => DatabaseHealth.FailureReason;

    // A repair runs on this circuit, but the state is a singleton: another tab's repair reaches this
    // one too, which is why the re-render is marshalled rather than called directly.
    private void OnHealthChanged() => _ = InvokeAsync(StateHasChanged);

    #endregion
}
