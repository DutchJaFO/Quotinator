using Dapper;
using Quotinator.Core.Data.Enums;

namespace Quotinator.Core.Data.TypeHandlers;

public static class DapperConfiguration
{
    public static void Configure()
    {
        SqlMapper.AddTypeHandler(new SafeEnumHandler<QuoteType>());
        SqlMapper.AddTypeHandler(new SafeEnumHandler<Genre>());
        SqlMapper.AddTypeHandler(new SafeDateHandler());
    }
}
