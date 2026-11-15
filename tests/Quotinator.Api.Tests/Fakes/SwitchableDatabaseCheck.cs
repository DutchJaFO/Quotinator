using Quotinator.Data.Database.Checks;
using Quotinator.Data.Enums;

namespace Quotinator.Api.Tests.Fakes;

/// <summary>
/// #490: the real database check, until a test switches in a result of its own. Startup runs the real check and
/// reaches healthy; the test then makes a later on-demand check find a failure, which is the only way to show
/// in-process what an on-demand failure does to a healthy application.
/// </summary>
/// <param name="inner">The real check, run until <see cref="FailAt"/> is called.</param>
internal sealed class SwitchableDatabaseCheck(IDatabaseCheck inner) : IDatabaseCheck
{
    private DatabaseCheckResult? _override;

    /// <inheritdoc/>
    public string DatabaseName => inner.DatabaseName;

    /// <inheritdoc/>
    public Task<DatabaseCheckResult> CheckAsync(DatabaseCheckDepth depth) =>
        _override is { } result ? Task.FromResult(result) : inner.CheckAsync(depth);

    /// <summary>From now on, every check finds <paramref name="layer"/> failed, and every other layer passed.</summary>
    /// <param name="layer">The layer that fails.</param>
    /// <param name="finding">What it reports.</param>
    /// <param name="fault">What Storage found wrong, when <paramref name="layer"/> is Storage.</param>
    public void FailAt(DatabaseCheckLayer layer, string finding, DatabaseStorageFault? fault = null) =>
        _override = new DatabaseCheckResult
        {
            DatabaseName = inner.DatabaseName,
            Depth        = DatabaseCheckDepth.Full,
            Layers       =
            [
                .. Enum.GetValues<DatabaseCheckLayer>().Select(l => new DatabaseCheckLayerResult
                {
                    Layer   = l,
                    Status  = l == layer ? DatabaseCheckLayerStatus.Failed : DatabaseCheckLayerStatus.Passed,
                    Finding = l == layer ? finding : null,
                    StorageFault = l == layer ? fault : null,
                }),
            ],
        };
}
