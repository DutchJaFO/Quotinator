using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Quotinator.Data.Import;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// #323 — the named client used for source downloads must bound its own connection attempts.
/// <para>
/// Every test here drives the <em>real</em> registration from <c>Program.cs</c> through
/// <see cref="QuotinatorWebApplicationFactory"/>. A test that built its own
/// <see cref="SocketsHttpHandler"/> would assert .NET's behaviour rather than this project's wiring, and
/// would pass whether or not the registration was ever fixed.
/// </para>
/// </summary>
[TestClass]
public class SourceCacheHttpClientTests
{
    private const int ConnectTimeoutSecondsForTest = 2;
    private const int RequestTimeoutSecondsForTest = 8;

    #region Registration

    [TestMethod]
    public void SourceCacheClient_PrimaryHandler_HasFiniteConnectTimeout()
    {
        SocketsHttpHandler handler = ResolvePrimaryHandler();

        Assert.AreNotEqual(
            Timeout.InfiniteTimeSpan,
            handler.ConnectTimeout,
            "ConnectTimeout must be finite: an unbounded connect attempt outlives the request that "
            + "started it and decides the outcome of the next request to the same host.");
    }

    [TestMethod]
    public void SourceCacheClient_PrimaryHandler_HasFinitePooledConnectionLifetime()
    {
        SocketsHttpHandler handler = ResolvePrimaryHandler();

        Assert.AreNotEqual(
            Timeout.InfiniteTimeSpan,
            handler.PooledConnectionLifetime,
            "PooledConnectionLifetime must be finite: HandlerLifetime recycles the handler but never "
            + "its pooled connections, so a connection would never rotate and a DNS change would never "
            + "be observed.");
    }

    [TestMethod]
    public void SourceCacheClient_ConnectTimeoutOverride_IsApplied()
    {
        SocketsHttpHandler handler = ResolvePrimaryHandler(connectTimeoutSeconds: 3);

        Assert.AreEqual(TimeSpan.FromSeconds(3), handler.ConnectTimeout);
    }

    /// <summary>
    /// The inverse of what this test once asserted, and deliberately so. #325 wired a custom
    /// <see cref="SocketsHttpHandler.ConnectCallback"/> that raced the two address families; it was
    /// reverted as disproportionate to what it protected. A manifest entry is a plain download link —
    /// an ordinary URI or an IP-based one — and the default handler resolves and connects it.
    /// <para>
    /// Kept as a guard rather than deleted: taking over connection establishment is an easy thing to
    /// reach for again the next time a download misbehaves, and the answer is a retry (#329), not a
    /// custom connect path. <c>ConnectTimeout</c> is what bounds the failure, and its own tests are
    /// above.
    /// </para>
    /// </summary>
    [TestMethod]
    public void SourceCacheClient_PrimaryHandler_UsesTheDefaultConnectPath()
    {
        SocketsHttpHandler handler = ResolvePrimaryHandler();

        Assert.IsNull(
            handler.ConnectCallback,
            "a ConnectCallback makes DNS resolution and connection establishment this project's "
            + "responsibility. #325 did that and the cost outweighed it: a hardcoded address-family "
            + "preference that overrode the host's own policy, a dependency on resolver ordering .NET "
            + "does not document, and connect-cancellation noise that reads as a fault. A download that "
            + "fails is a retry's problem.");
    }

    #endregion

    #region Connection behaviour

    [TestMethod]
    public async Task StalledConnect_FailsWithinConnectTimeoutNotRequestTimeout()
    {
        using StallingListener listener = new();
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpClient client = CreateSourceCacheClient(factory);

        Stopwatch sw = Stopwatch.StartNew();
        await SwallowAsync(client.GetAsync(listener.UrlFor("a.json"), TestContext.CancellationToken));
        sw.Stop();

        Assert.IsLessThan(
            TimeSpan.FromSeconds(RequestTimeoutSecondsForTest - 1),
            sw.Elapsed,
            $"expected the connect budget ({ConnectTimeoutSecondsForTest}s) to end the attempt, but it "
            + $"ran for {sw.Elapsed.TotalSeconds:F2}s — i.e. it was bounded by the request timeout "
            + $"({RequestTimeoutSecondsForTest}s) instead.");
    }

    [TestMethod]
    public async Task TwoRequestsSameHost_FirstConnectStalls_SecondFailsIndependentlyNotByInheritance()
    {
        using StallingListener listener = new();
        using WebApplicationFactory<Program> factory = CreateFactory();
        HttpClient client = CreateSourceCacheClient(factory);

        await SwallowAsync(client.GetAsync(listener.UrlFor("a.json"), TestContext.CancellationToken));

        Stopwatch sw = Stopwatch.StartNew();
        await SwallowAsync(client.GetAsync(listener.UrlFor("b.json"), TestContext.CancellationToken));
        sw.Stop();

        // Inheriting an already-stalled attempt makes the second request fail almost immediately (the
        // shared attempt is already most of the way through its budget) or at the full request timeout.
        // An independent attempt spends its own connect budget, and only that.
        Assert.IsTrue(
            sw.Elapsed >= TimeSpan.FromSeconds(ConnectTimeoutSecondsForTest - 1)
                && sw.Elapsed < TimeSpan.FromSeconds(RequestTimeoutSecondsForTest - 1),
            $"the second request took {sw.Elapsed.TotalSeconds:F2}s, which is not its own "
            + $"{ConnectTimeoutSecondsForTest}s connect budget — its outcome was decided by the first "
            + "request's attempt rather than by its own.");
    }

    #endregion

    #region Helpers

    public TestContext TestContext { get; set; } = null!;

    private static WebApplicationFactory<Program> CreateFactory(int? connectTimeoutSeconds = null)
        => new QuotinatorWebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "Quotinator:SourceRefreshTimeoutSeconds",
                RequestTimeoutSecondsForTest.ToString());
            builder.UseSetting(
                "Quotinator:SourceRefreshConnectTimeoutSeconds",
                (connectTimeoutSeconds ?? ConnectTimeoutSecondsForTest).ToString());
        });

    private static HttpClient CreateSourceCacheClient(WebApplicationFactory<Program> factory)
        => factory.Services
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(SourceCacheUpdater.HttpClientName);

    /// <summary>
    /// Resolves the primary handler the named client is actually registered with, by replaying the
    /// registration's own builder actions — the only way to see what <c>Program.cs</c> configured.
    /// </summary>
    private static SocketsHttpHandler ResolvePrimaryHandler(int? connectTimeoutSeconds = null)
    {
        using WebApplicationFactory<Program> factory = CreateFactory(connectTimeoutSeconds);

        HttpClientFactoryOptions options = factory.Services
            .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
            .Get(SourceCacheUpdater.HttpClientName);

        RecordingHandlerBuilder recorder = new();
        foreach (Action<HttpMessageHandlerBuilder> configure in options.HttpMessageHandlerBuilderActions)
            configure(recorder);

        return recorder.PrimaryHandler as SocketsHttpHandler
            ?? throw new AssertFailedException(
                $"the '{SourceCacheUpdater.HttpClientName}' client has no SocketsHttpHandler configured "
                + "as its primary handler, so ConnectTimeout keeps its infinite default.");
    }

    private static async Task SwallowAsync(Task<HttpResponseMessage> request)
    {
        try
        {
            using HttpResponseMessage response = await request;
        }
        catch (Exception)
        {
            // Every request in these tests is expected to fail — the assertions are about how and how
            // long, not about the exception type, which differs between the broken and fixed wiring.
        }
    }

    private sealed class RecordingHandlerBuilder : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; }

        public override HttpMessageHandler PrimaryHandler { get; set; } = new SocketsHttpHandler();

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];

        public override HttpMessageHandler Build() => PrimaryHandler;
    }

    /// <summary>
    /// Accepts TCP connections on loopback and then does nothing — the TLS handshake never completes, so
    /// the client sits in connection establishment exactly as it did against the real upstream.
    /// </summary>
    private sealed class StallingListener : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly List<TcpClient> _held = [];
        private int _accepted;

        public StallingListener()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoopAsync);
        }

        public int Port { get; }

        public int AcceptedCount => Volatile.Read(ref _accepted);

        public string UrlFor(string file) => $"https://localhost:{Port}/{file}";

        public void Dispose()
        {
            _listener.Stop();
            foreach (TcpClient client in _held) client.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (true)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    Interlocked.Increment(ref _accepted);
                    lock (_held) _held.Add(client);
                }
            }
            catch (Exception)
            {
                // Stop() races the pending accept on dispose; nothing here needs to survive it.
            }
        }
    }

    #endregion
}
