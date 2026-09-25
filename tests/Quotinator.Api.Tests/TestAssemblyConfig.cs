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
    /// builds is checked.
    /// </summary>
    /// <param name="context">Supplied by MSTest; unused.</param>
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        _ = context;
        UnguardedFactoryRuntimeGuard.Install();
    }
}
