using System.Net;
using System.Net.Sockets;

namespace Quotinator.Data.Http;

/// <summary>
/// Establishes an outbound TCP connection by racing the two IP address families against each other, after
/// RFC 8305 ("Happy Eyeballs") — #325.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SocketsHttpHandler"/> walks a host's resolved addresses in OS-provided order and never races
/// families. Where one family is routed but unreachable — a default route exists, so SYNs are sent and
/// silently dropped rather than failing fast — the entire connect budget is spent on that family and the
/// working addresses of the other are never tried. Measured live 2026-08-17 against
/// <c>raw.githubusercontent.com</c>: the default dual-stack path failed after the full 10 s
/// <see cref="SocketsHttpHandler.ConnectTimeout"/> while a connection forced onto IPv4 succeeded in 0.49 s
/// at the same moment.
/// </para>
/// <para>
/// The preferred family starts immediately and the other follows after
/// <see cref="DefaultAttemptDelay"/>, so a healthy dual-stack host still prefers IPv6 and pays no
/// measurable penalty, while a degraded family costs only that delay instead of the whole budget.
/// </para>
/// <para>
/// This type never imposes an overall deadline of its own. It runs entirely under the caller's
/// <see cref="CancellationToken"/>, which <see cref="SocketsHttpHandler"/> has already bound to
/// <see cref="SocketsHttpHandler.ConnectTimeout"/> (#323) — one budget, owned in one place.
/// </para>
/// </remarks>
/// <param name="resolve">Resolves a host name to its candidate addresses.</param>
/// <param name="connect">Opens a stream to one resolved address.</param>
/// <param name="attemptDelay">How long the non-preferred family waits before starting its own attempts.</param>
public sealed class HappyEyeballsConnector(
    Func<string, CancellationToken, Task<IPAddress[]>> resolve,
    Func<IPAddress, int, CancellationToken, Task<Stream>> connect,
    TimeSpan attemptDelay)
{
    /// <summary>
    /// RFC 8305's "Connection Attempt Delay" — how long the second family waits before starting. The RFC
    /// recommends 250 ms and requires at least 10 ms; long enough that a healthy preferred family almost
    /// always wins outright, short enough that a degraded one is not worth waiting out.
    /// </summary>
    public static readonly TimeSpan DefaultAttemptDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The connector used in production: real DNS, real sockets, <see cref="DefaultAttemptDelay"/>.
    /// </summary>
    public static HappyEyeballsConnector Default { get; } = new(
        static (host, ct) => Dns.GetHostAddressesAsync(host, ct),
        ConnectToAddressAsync,
        DefaultAttemptDelay);

    /// <summary>
    /// Connects to <paramref name="host"/> on <paramref name="port"/>, racing IPv6 and IPv4 as described
    /// on the type. Returns the stream of whichever attempt connects first.
    /// </summary>
    /// <param name="host">Host name to resolve and connect to.</param>
    /// <param name="port">TCP port.</param>
    /// <param name="cancellationToken">Bounds the whole race — in production this carries the handler's connect timeout.</param>
    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        IPAddress[] addresses = await resolve(host, cancellationToken);

        List<IPAddress> preferred = [.. addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6)];
        List<IPAddress> secondary = [.. addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork)];

        if (preferred.Count == 0 && secondary.Count == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        // Only one family resolved: no race to run, and no attempt delay to pay.
        if (preferred.Count == 0 || secondary.Count == 0)
        {
            List<IPAddress> only = preferred.Count == 0 ? secondary : preferred;
            return await RunTrackAsync(only, port, TimeSpan.Zero, cancellationToken);
        }

        using CancellationTokenSource race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<Stream> preferredTrack = RunTrackAsync(preferred, port, TimeSpan.Zero, race.Token);
        Task<Stream> secondaryTrack = RunTrackAsync(secondary, port, attemptDelay, race.Token);

        List<Task<Stream>> running = [preferredTrack, secondaryTrack];
        List<Exception> failures = [];

        while (running.Count > 0)
        {
            Task<Stream> finished = await Task.WhenAny(running);
            running.Remove(finished);

            if (finished.IsCompletedSuccessfully)
            {
                // Cancel the loser and make sure anything it still produces is disposed rather than leaked.
                await race.CancelAsync();
                foreach (Task<Stream> loser in running) _ = DisposeWhenDoneAsync(loser);
                return finished.Result;
            }

            // The caller's own budget expiring is terminal for the whole race, not just this track.
            cancellationToken.ThrowIfCancellationRequested();

            if (finished.Exception is not null) failures.AddRange(finished.Exception.InnerExceptions);
        }

        throw failures.Count == 1
            ? failures[0]
            : new AggregateException($"No address for '{host}' could be connected to.", failures);
    }

    /// <summary>
    /// Walks one address family's addresses in order, returning the first that connects. An optional
    /// <paramref name="startDelay"/> gives the preferred family its RFC 8305 head start.
    /// </summary>
    private async Task<Stream> RunTrackAsync(
        List<IPAddress> addresses, int port, TimeSpan startDelay, CancellationToken cancellationToken)
    {
        if (startDelay > TimeSpan.Zero)
            await Task.Delay(startDelay, cancellationToken);

        List<Exception> failures = [];

        foreach (IPAddress address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await connect(address, port, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        throw failures.Count switch
        {
            0 => new SocketException((int)SocketError.HostNotFound),
            1 => failures[0],
            _ => new AggregateException(failures),
        };
    }

    private static async Task DisposeWhenDoneAsync(Task<Stream> track)
    {
        try
        {
            Stream orphan = await track;
            await orphan.DisposeAsync();
        }
        catch (Exception)
        {
            // A losing track normally ends cancelled or faulted — there is nothing to dispose and
            // nothing to report; the winner has already been returned to the caller.
        }
    }

    private static async Task<Stream> ConnectToAddressAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (Exception)
        {
            socket.Dispose();
            throw;
        }
    }
}
