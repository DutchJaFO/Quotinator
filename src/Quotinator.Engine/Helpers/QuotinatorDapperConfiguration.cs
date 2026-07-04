using Quotinator.Core.Models;
using Quotinator.Data.Helpers;
using Quotinator.Data.Import;

namespace Quotinator.Engine.Helpers;

/// <summary>Registers all Dapper type handlers required by the Quotinator domain, including generic infrastructure handlers and domain enum handlers.</summary>
public sealed class QuotinatorDapperConfiguration : DatabaseConfiguration
{
    /// <inheritdoc/>
    protected override void RegisterDomainHandlers()
    {
        RegisterEnumHandler<QuoteType>();
        RegisterEnumHandler<Genre>();
        RegisterEnumHandler<DuplicateResolutionPolicy>();
    }
}
