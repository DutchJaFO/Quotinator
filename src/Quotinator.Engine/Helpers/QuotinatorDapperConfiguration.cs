using Quotinator.Core.Models;
using Quotinator.Data.Helpers;
using Quotinator.Engine.Entities;

namespace Quotinator.Engine.Helpers;

/// <summary>
/// Registers all Dapper type handlers required by the Quotinator domain, including generic
/// infrastructure handlers (base <see cref="DatabaseConfiguration"/>) and domain enum handlers.
/// </summary>
/// <remarks>
/// <see cref="Quotinator.Data.Import.DuplicateResolutionPolicy"/> is registered by the base class, not
/// here — it lives in <c>Quotinator.Data.Import</c>, not this project's own domain, same as
/// <c>ChangeAction</c>/<c>InitiatorType</c>.
/// </remarks>
public sealed class QuotinatorDapperConfiguration : DatabaseConfiguration
{
    /// <inheritdoc/>
    protected override void RegisterDomainHandlers()
    {
        RegisterEnumHandler<QuoteType>();
        RegisterEnumHandler<Genre>();
        RegisterEnumHandler<ImportBatchType>();
        RegisterEnumHandler<ImportBatchStatus>();
    }
}
