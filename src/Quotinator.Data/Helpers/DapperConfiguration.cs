namespace Quotinator.Data.Helpers;

/// <summary>
/// Default concrete <see cref="DatabaseConfiguration"/> that registers only the generic infrastructure handlers
/// (<see cref="GuidHandler"/>, <see cref="SafeDateHandler"/>). Domain-specific enum handlers are registered by
/// <c>QuotinatorDapperConfiguration</c> in <c>Quotinator.Engine</c>.
/// </summary>
public sealed class DapperConfiguration : DatabaseConfiguration
{
}
