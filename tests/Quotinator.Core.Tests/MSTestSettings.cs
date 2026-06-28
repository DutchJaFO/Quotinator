using Quotinator.Data.Helpers;

namespace Quotinator.Core.Tests;

/// <summary>Assembly-level setup that runs once before any test in this project.</summary>
[TestClass]
public static class AssemblySetup
{
    /// <summary>Registers Dapper type handlers once for the entire test run.</summary>
    [AssemblyInitialize]
    public static void Initialize(TestContext _) => new DapperConfiguration().Configure();
}
