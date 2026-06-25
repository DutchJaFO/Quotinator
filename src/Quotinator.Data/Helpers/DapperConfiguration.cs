using Dapper;
using Quotinator.Data.Entities;

namespace Quotinator.Data.Helpers;

/// <summary>Registers Dapper type handlers for Quotinator's domain types. Call <see cref="Configure"/> once at application startup.</summary>
public static class DapperConfiguration
{
    /// <summary>Registers all <see cref="Quotinator.Data.Models.SafeValue{T}"/> type handlers with the global Dapper SqlMapper.</summary>
    public static void Configure()
    {
        SqlMapper.AddTypeHandler(new GuidHandler());
        SqlMapper.AddTypeHandler(new SafeEnumHandler<QuoteType>());
        SqlMapper.AddTypeHandler(new SafeEnumHandler<Genre>());
        SqlMapper.AddTypeHandler(new SafeDateHandler());
    }
}
