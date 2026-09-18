using Microsoft.VisualStudio.TestTools.UnitTesting;
using Quotinator.Core.Helpers;
using Quotinator.Data.Testing.Diagnostics;

[assembly: DoNotParallelize]

namespace Quotinator.Core.Tests;

/// <summary>Assembly-level setup that runs once before any test in this project.</summary>
[TestClass]
public static class AssemblySetup
{
    /// <summary>Registers Dapper type handlers and subscribes the thrown-exception recorder, once for the entire test run.</summary>
    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        new QuotinatorDapperConfiguration().Configure();
        ThrownExceptionRecorder.Install();
    }
}
