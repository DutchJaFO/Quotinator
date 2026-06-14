using Dapper;
using Quotinator.Core.Data.Enums;
using Quotinator.Data.Helpers;

namespace Quotinator.Core.Data.TypeHandlers;

/// <summary>Registers Dapper type handlers for Quotinator's domain types. Call <see cref="Configure"/> once at application startup.</summary>
public static class DapperConfiguration
{
    /// <summary>Registers all <see cref="Quotinator.Data.Models.SafeValue{T}"/> type handlers with the global Dapper SqlMapper.</summary>
    public static void Configure()
    {
        SqlMapper.AddTypeHandler(new SafeEnumHandler<QuoteType>());
        SqlMapper.AddTypeHandler(new SafeEnumHandler<Genre>());
        SqlMapper.AddTypeHandler(new SafeDateHandler());
    }
}
