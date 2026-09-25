using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quotinator.Api.Tests.Fakes;
using Quotinator.Core.Services;
using Quotinator.Data.Database;
using Quotinator.Data.Testing.NoOps;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// Layer C of the #313 guard, held to a positive and a negative result. The bare factory here is reached
/// through a <see cref="Type"/> obtained at run time, deliberately invisible to both static layers, which
/// is the case this layer exists for, and why this file does not trip them.
/// </summary>
[TestClass]
public class UnguardedFactoryRuntimeGuardTests
{
    [TestMethod]
    public void GuardIsInstalledBeforeAnyTest()
    {
        Assert.IsTrue(UnguardedFactoryRuntimeGuard.IsInstalled,
            "The runtime guard must be installed in [AssemblyInitialize], or no test run is watched at all.");
    }

    /// <summary>
    /// The positive control, and the proof that a factory-built host reaches the guard at all: without it,
    /// every "nothing recorded" below could mean the listener never fired.
    /// </summary>
    [TestMethod]
    public void BareFactoryFromRuntimeType_IsRecorded()
    {
        UnguardedFactoryRuntimeResult result = UnguardedFactoryRuntimeGuard.RecordWhile(BuildHostWithBareFactory);

        Assert.HasCount(1, result.UnguardedHosts, "A Program host built by the bare factory must be recorded once.");
        Assert.AreEqual(0, result.GuardedHosts);
    }

    [TestMethod]
    public void GuardedFactory_IsNotRecorded()
    {
        UnguardedFactoryRuntimeResult result = UnguardedFactoryRuntimeGuard.RecordWhile(() =>
        {
            using WebApplicationFactory<Program> factory = Configure(new QuotinatorWebApplicationFactory());
            using HttpClient client = factory.CreateClient();
        });

        Assert.IsEmpty(result.UnguardedHosts);
        Assert.AreEqual(1, result.GuardedHosts, "The guarded host must be seen, or its absence from UnguardedHosts proves nothing.");
    }

    [TestMethod]
    public void HostThatIsNotPrograms_IsNotRecorded()
    {
        UnguardedFactoryRuntimeResult result = UnguardedFactoryRuntimeGuard.RecordWhile(() =>
        {
            using IHost host = Host.CreateApplicationBuilder().Build();
        });

        Assert.IsEmpty(result.UnguardedHosts);
        Assert.AreEqual(0, result.GuardedHosts);
        Assert.AreEqual(1, result.OtherHosts, "The unrelated host must be seen, or its absence from UnguardedHosts proves nothing.");
    }

    /// <summary>Outside a recording scope the guard does its real job: the offending test fails, naming why.</summary>
    [TestMethod]
    public void BareFactoryOutsideAnExpectation_FailsItsHostBuild()
    {
        Exception thrown = Assert.Throws<Exception>(BuildHostWithBareFactory);

        string messages = string.Join("\n", Chain(thrown).Select(e => e.Message));
        Assert.Contains("#313", messages, "The failure must say which guard stopped the host and why.");
    }

    private static void BuildHostWithBareFactory()
    {
        Type bare = typeof(QuotinatorWebApplicationFactory).BaseType!;
        using WebApplicationFactory<Program> factory = Configure((WebApplicationFactory<Program>)Activator.CreateInstance(bare)!);
        using HttpClient client = factory.CreateClient();
    }

    private static WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton<IQuoteService>(new FakeQuoteService());
            services.AddSingleton<IDatabaseInitializer>(NoOpDatabaseInitializer.Instance);
        }));

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
