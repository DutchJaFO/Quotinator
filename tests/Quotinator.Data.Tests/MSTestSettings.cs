using Dapper;
using Quotinator.Data.Helpers;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Quotinator.Data.Tests;

/// <summary>Assembly-level setup that runs once before any test in this project.</summary>
[TestClass]
public static class AssemblySetup
{
    /// <summary>Registers Dapper type handlers once for the entire test run.</summary>
    [AssemblyInitialize]
    public static void RegisterTypeHandlers(TestContext _)
    {
        SqlMapper.AddTypeHandler(new GuidHandler());
        SqlMapper.AddTypeHandler(new SafeDateHandler());
    }
}
