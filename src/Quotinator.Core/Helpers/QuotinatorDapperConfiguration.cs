using Quotinator.Core.Models;
using Quotinator.Data.Helpers;
using Quotinator.Core.Entities;

namespace Quotinator.Core.Helpers;

/// <summary>
/// Registers all Dapper type handlers required by the Quotinator domain, including generic
/// infrastructure handlers (base <see cref="DatabaseConfiguration"/>) and domain enum handlers.
/// </summary>
/// <remarks>
/// <see cref="Quotinator.Data.Import.DuplicateResolutionPolicy"/> and <c>ImportBatchType</c>/
/// <c>ImportBatchStatus</c> are registered by the base class, not here — they live in
/// <c>Quotinator.Data</c>, not this project's own domain, same as <c>ChangeAction</c>/<c>InitiatorType</c>.
/// </remarks>
public sealed class QuotinatorDapperConfiguration : DatabaseConfiguration
{
    /// <inheritdoc/>
    protected override void RegisterDomainHandlers()
    {
        RegisterEnumHandler<QuoteType>();
        RegisterEnumHandler<Genre>();
        RegisterEnumHandler<ConversationLineType>();
    }
}
