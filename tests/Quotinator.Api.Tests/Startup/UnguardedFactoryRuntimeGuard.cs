using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Quotinator.Api.Startup;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// Layer C of the #313 guard: watches every host built in this test process and fails any host for
/// <c>Program</c> that did not come through <see cref="QuotinatorWebApplicationFactory"/>, including one
/// constructed from a <see cref="Type"/> known only at run time, which no static analysis can see.
/// <para>
/// Every in-process host build reports itself on the <c>Microsoft.Extensions.Hosting</c>
/// <see cref="DiagnosticListener"/>, the same channel <c>WebApplicationFactory</c> itself uses to capture
/// <c>Program</c>'s host. A host carrying <see cref="StartupPhaseState"/> is <c>Program</c>'s; one that also
/// carries <see cref="GuardedFactoryMarker"/> came through the guarded factory.
/// </para>
/// </summary>
internal static class UnguardedFactoryRuntimeGuard
{
    private const string HostingListenerName = "Microsoft.Extensions.Hosting";
    private const string HostBuiltEvent      = "HostBuilt";

    // Flows into the thread WebApplicationFactory starts Program on, since a new thread inherits the
    // starting thread's execution context. That is what lets a recording scope see that thread's build.
    private static readonly AsyncLocal<Recorder?> CurrentRecorder = new();

    private static IDisposable? _subscription;

    /// <summary>Whether the guard is watching host builds.</summary>
    internal static bool IsInstalled => _subscription is not null;

    /// <summary>Starts watching. Called once, from <c>[AssemblyInitialize]</c>, before any test runs.</summary>
    internal static void Install() =>
        _subscription ??= DiagnosticListener.AllListeners.Subscribe(new ListenerObserver());

    /// <summary>
    /// Runs <paramref name="action"/> with violations recorded rather than thrown. For the guard's own
    /// tests, which must provoke one to show it is caught.
    /// </summary>
    /// <param name="action">Code that builds hosts.</param>
    internal static UnguardedFactoryRuntimeResult RecordWhile(Action action)
    {
        Recorder recorder = new();
        Recorder? previous = CurrentRecorder.Value;
        CurrentRecorder.Value = recorder;
        try
        {
            action();
        }
        finally
        {
            CurrentRecorder.Value = previous;
        }

        return recorder.ToResult();
    }

    private static void OnHostBuilt(IHost host)
    {
        Recorder? recorder = CurrentRecorder.Value;

        if (host.Services.GetService(typeof(StartupPhaseState)) is null)
        {
            recorder?.RecordOther();
            return;
        }

        if (host.Services.GetService(typeof(GuardedFactoryMarker)) is not null)
        {
            recorder?.RecordGuarded();
            return;
        }

        string description = $"A host for Program was built without {nameof(QuotinatorWebApplicationFactory)}, " +
                             $"by:\n{new StackTrace(fNeedFileInfo: true)}";
        if (recorder is not null)
        {
            recorder.RecordUnguarded(description);
            return;
        }

        throw new InvalidOperationException(
            description + "\n\nUse QuotinatorWebApplicationFactory instead: the bare factory hands out a client " +
            "before startup completes, so requests can be answered by the startup wait page rather than the " +
            "endpoint under test (#313).");
    }

    private sealed class ListenerObserver : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == HostingListenerName)
            {
                value.Subscribe(new HostingObserver());
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class HostingObserver : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key == HostBuiltEvent && value.Value is IHost host)
            {
                OnHostBuilt(host);
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class Recorder
    {
        private readonly Lock _gate = new();
        private readonly List<string> _unguarded = [];
        private int _guarded;
        private int _other;

        internal void RecordUnguarded(string description)
        {
            lock (_gate)
            {
                _unguarded.Add(description);
            }
        }

        internal void RecordGuarded() => Interlocked.Increment(ref _guarded);

        internal void RecordOther() => Interlocked.Increment(ref _other);

        internal UnguardedFactoryRuntimeResult ToResult()
        {
            lock (_gate)
            {
                return new UnguardedFactoryRuntimeResult
                {
                    UnguardedHosts = [.. _unguarded],
                    GuardedHosts   = Volatile.Read(ref _guarded),
                    OtherHosts     = Volatile.Read(ref _other),
                };
            }
        }
    }
}
