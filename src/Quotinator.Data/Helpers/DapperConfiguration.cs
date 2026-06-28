using Quotinator.Data.Entities;

namespace Quotinator.Data.Helpers;

/// <summary>
/// Concrete <see cref="DatabaseConfiguration"/> that registers all Quotinator type handlers.
/// This class is a temporary bridge — domain enum registrations will move to
/// <c>QuotinatorDapperConfiguration</c> in <c>Quotinator.Engine</c> once that project is wired up.
/// </summary>
public sealed class DapperConfiguration : DatabaseConfiguration
{
    /// <inheritdoc/>
    protected override void RegisterDomainHandlers()
    {
        RegisterEnumHandler<QuoteType>();
        RegisterEnumHandler<Genre>();
    }
}
