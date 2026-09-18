using Microsoft.VisualStudio.TestTools.UnitTesting;
using Quotinator.Data.Testing.Diagnostics;

[assembly: DoNotParallelize]

namespace Quotinator.Data.Testing.Tests;

/// <summary>Assembly-level setup that runs once before any test in this project.</summary>
[TestClass]
public static class AssemblySetup
{
    /// <summary>Subscribes the thrown-exception recorder once for the entire test run.</summary>
    [AssemblyInitialize]
    public static void Initialize(TestContext _) => ThrownExceptionRecorder.Install();
}
