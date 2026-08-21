using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Quotinator.Data.Http;

namespace Quotinator.Data.Tests.Http;

/// <summary>
/// #325 — a black-holed address family must not consume the whole connect budget while a working family
/// goes untried.
/// <para>
/// DNS resolution and the per-address connect are injected, so every case is deterministic: a
/// "black-holed" address is one whose connect task never completes, which is exactly what a dropped SYN
/// looks like to the caller. Asserting against real unroutable prefixes or a machine's live IPv6 state
/// would be flaky — the condition that motivated this issue is itself intermittent.
/// </para>
/// </summary>
[TestClass]
public class HappyEyeballsConnectTests
{
    private static readonly IPAddress V6A = IPAddress.Parse("2606:50c0:8002::154");
    private static readonly IPAddress V6B = IPAddress.Parse("2606:50c0:8000::154");
    private static readonly IPAddress V4A = IPAddress.Parse("185.199.111.133");
    private static readonly IPAddress V4B = IPAddress.Parse("185.199.110.133");

    private static readonly TimeSpan AttemptDelay = TimeSpan.FromMilliseconds(100);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Connect_PreferredFamilyBlackHoled_FallsBackToWorkingFamilyPromptly()
    {
        using CancellationTokenSource guard = Guard();
        RecordingConnector attempts = new();
        HappyEyeballsConnector connector = Build([V6A, V6B, V4A, V4B], attempts.BlackHoleV6ElseSucceed);

        Stopwatch sw = Stopwatch.StartNew();
        using Stream stream = await connector.ConnectAsync("example.test", 443, guard.Token);
        sw.Stop();

        Assert.AreEqual(V4A, StreamMarker.AddressOf(stream), "the working IPv4 address should have won");
        Assert.IsLessThan(
            TimeSpan.FromSeconds(2),
            sw.Elapsed,
            $"fallback took {sw.Elapsed.TotalSeconds:F2}s — the IPv4 track must start after the attempt "
            + "delay rather than waiting for the IPv6 track to exhaust itself");
    }

    [TestMethod]
    public async Task Connect_BothFamiliesReachable_UsesFirstToConnect()
    {
        using CancellationTokenSource guard = Guard();
        RecordingConnector attempts = new();
        HappyEyeballsConnector connector = Build([V6A, V4A], attempts.SucceedImmediately);

        using Stream stream = await connector.ConnectAsync("example.test", 443, guard.Token);

        Assert.AreEqual(
            V6A,
            StreamMarker.AddressOf(stream),
            "IPv6 is the preferred family and starts first, so with both reachable it should win");

        // The loser must not be leaked: any socket the other track produced is disposed.
        await attempts.WaitForAllSettledAsync();
        Assert.AreEqual(
            0,
            attempts.UndisposedLosers,
            "a losing track's connection was returned to nobody and never disposed");
    }

    /// <summary>
    /// RFC 8305 states an <em>assumption</em> that the host's preference policy favours IPv6 — it does
    /// not instruct an implementation to hardcode family order. That preference is RFC 6724's address
    /// selection policy, which the operating system applies and the user can reconfigure, and it is
    /// what the resolver's returned order expresses. Overriding it means a user who deprioritises IPv6
    /// system-wide (the correct remedy for a black-holed IPv6 path) gets no benefit from having done so.
    /// </summary>
    [TestMethod]
    public async Task Connect_ResolverReturnsIPv4First_PrefersIPv4RatherThanForcingIPv6()
    {
        using CancellationTokenSource guard = Guard();
        RecordingConnector attempts = new();
        HappyEyeballsConnector connector = Build([V4A, V6A], attempts.SucceedImmediately);

        using Stream stream = await connector.ConnectAsync("example.test", 443, guard.Token);

        Assert.AreEqual(
            V4A,
            StreamMarker.AddressOf(stream),
            "the resolver returned IPv4 first, which is the host's own preference policy speaking — "
            + "the preferred track must follow it rather than assert IPv6");
    }

    [TestMethod]
    public async Task Connect_ResolverReturnsIPv4First_IPv6BlackHoled_StillFallsBackPromptly()
    {
        using CancellationTokenSource guard = Guard();
        RecordingConnector attempts = new();
        HappyEyeballsConnector connector = Build([V4A, V4B, V6A, V6B], attempts.BlackHoleV6ElseSucceed);

        Stopwatch sw = Stopwatch.StartNew();
        using Stream stream = await connector.ConnectAsync("example.test", 443, guard.Token);
        sw.Stop();

        Assert.AreEqual(V4A, StreamMarker.AddressOf(stream));
        Assert.IsLessThan(
            AttemptDelay,
            sw.Elapsed,
            "with IPv4 preferred by the host and reachable, the race should be won outright before the "
            + "IPv6 track is even started");
    }

    [TestMethod]
    public async Task Connect_OnlyIPv4Resolved_ConnectsWithoutDelay()
    {
        using CancellationTokenSource guard = Guard();
        RecordingConnector attempts = new();
        HappyEyeballsConnector connector = Build([V4A, V4B], attempts.SucceedImmediately);

        Stopwatch sw = Stopwatch.StartNew();
        using Stream stream = await connector.ConnectAsync("example.test", 443, guard.Token);
        sw.Stop();

        Assert.AreEqual(V4A, StreamMarker.AddressOf(stream));
        Assert.IsLessThan(
            AttemptDelay,
            sw.Elapsed,
            "with no IPv6 address resolved there is nothing to wait behind — the attempt delay must not apply");
    }

    [TestMethod]
    public async Task Connect_OnlyIPv6Resolved_ConnectsWithoutDelay()
    {
        using CancellationTokenSource guard = Guard();
        RecordingConnector attempts = new();
        HappyEyeballsConnector connector = Build([V6A, V6B], attempts.SucceedImmediately);

        Stopwatch sw = Stopwatch.StartNew();
        using Stream stream = await connector.ConnectAsync("example.test", 443, guard.Token);
        sw.Stop();

        Assert.AreEqual(V6A, StreamMarker.AddressOf(stream));
        Assert.IsLessThan(AttemptDelay, sw.Elapsed);
    }

    [TestMethod]
    public async Task Connect_AllAddressesBlackHoled_FailsWithinConnectTimeout()
    {
        using CancellationTokenSource guard = Guard();
        RecordingConnector attempts = new();
        HappyEyeballsConnector connector = Build([V6A, V6B, V4A, V4B], attempts.BlackHoleEverything);

        // Stands in for SocketsHttpHandler.ConnectTimeout, which is what actually bounds this in production.
        using CancellationTokenSource budget = new(TimeSpan.FromMilliseconds(600));

        Stopwatch sw = Stopwatch.StartNew();
        // Assignable, not exact: cancellation surfaces as TaskCanceledException, a subclass.
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await connector.ConnectAsync("example.test", 443, budget.Token));
        sw.Stop();

        Assert.IsLessThan(
            TimeSpan.FromSeconds(3),
            sw.Elapsed,
            "the race must honour the caller's token rather than outliving the connect budget");
    }

    /// <summary>
    /// Bounds every test so a wrong implementation fails on its assertion instead of hanging the run —
    /// a black-holed fake never completes on its own.
    /// </summary>
    private static CancellationTokenSource Guard() => new(TimeSpan.FromSeconds(5));

    private static HappyEyeballsConnector Build(
        IPAddress[] resolved,
        Func<IPAddress, int, CancellationToken, Task<Stream>> connect)
        => new(
            (_, _) => Task.FromResult(resolved),
            connect,
            AttemptDelay);

    /// <summary>Records what each track attempted so leaked/undisposed losers can be asserted on.</summary>
    private sealed class RecordingConnector
    {
        private readonly List<StreamMarker> _produced = [];
        private readonly List<Task> _pending = [];

        public int UndisposedLosers
        {
            get { lock (_produced) return _produced.Count(p => !p.Disposed && !p.HandedToCaller); }
        }

        // Exposed as delegates rather than methods so the parameters the connector's signature requires
        // but a given fake ignores can be written as discards.
        public Func<IPAddress, int, CancellationToken, Task<Stream>> SucceedImmediately
            => (address, _, _) => Track(Task.FromResult<Stream>(Produce(address)));

        public Func<IPAddress, int, CancellationToken, Task<Stream>> BlackHoleV6ElseSucceed
            => (address, _, ct) => address.AddressFamily == AddressFamily.InterNetworkV6
                ? Track(NeverCompletes(ct))
                : Track(Task.FromResult<Stream>(Produce(address)));

        public Func<IPAddress, int, CancellationToken, Task<Stream>> BlackHoleEverything
            => (_, _, ct) => Track(NeverCompletes(ct));

        public async Task WaitForAllSettledAsync()
        {
            Task[] snapshot;
            lock (_pending) snapshot = [.. _pending];
            try { await Task.WhenAll(snapshot).WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception) { /* losers are expected to end cancelled or faulted */ }
        }

        private Task<Stream> Track(Task<Stream> task)
        {
            lock (_pending) _pending.Add(task);
            return task;
        }

        private StreamMarker Produce(IPAddress address)
        {
            StreamMarker marker = new(address);
            lock (_produced) _produced.Add(marker);
            return marker;
        }

        private static async Task<Stream> NeverCompletes(CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        }
    }

    /// <summary>A stand-in for a connected socket's stream that remembers which address produced it.</summary>
    private sealed class StreamMarker(IPAddress address) : Stream
    {
        public IPAddress Address { get; } = address;

        public bool Disposed { get; private set; }

        public bool HandedToCaller { get; set; }

        public static IPAddress AddressOf(Stream stream)
        {
            StreamMarker marker = (StreamMarker)stream;
            marker.HandedToCaller = true;
            return marker.Address;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) { }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
