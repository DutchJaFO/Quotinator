using Microsoft.VisualStudio.TestTools.UnitTesting;
using Quotinator.Api.Tests.Startup;

[assembly: DoNotParallelize]

namespace Quotinator.Api.Tests;

/// <summary>Process-wide setup, which testing-policy.md confines to <c>[AssemblyInitialize]</c>.</summary>
[TestClass]
public static class TestAssemblyConfig
{
    /// <summary>
    /// Installs the runtime layer of the #313 guard before any test builds a host, so every host this run
    /// builds is checked, then prepares the database the hosts that need one start from (#424).
    /// </summary>
    /// <param name="context">Supplied by MSTest; unused.</param>
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        _ = context;
        UnguardedFactoryRuntimeGuard.Install();
        PreparedDatabase.Prepare();
    }

    /// <summary>Deletes the prepared database once every test has run (#424).</summary>
    [AssemblyCleanup]
    public static void Cleanup() => PreparedDatabase.Remove();
}
