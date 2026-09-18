using System.Runtime.ExceptionServices;

namespace Quotinator.Data.Testing.Diagnostics;

/// <summary>
/// Lets a test see every exception thrown while it runs, including one the code under test threw and
/// caught itself — which no assertion on the call's result can observe. ADR 022 makes such an exception
/// a defect when the condition was already checked, so a test proving it is gone needs to see it.
/// </summary>
/// <remarks>
/// <see cref="AppDomain.FirstChanceException"/> is process-wide, so <see cref="Install"/> subscribes once,
/// from <c>[AssemblyInitialize]</c> — the one place <c>docs/testing-policy.md</c> allows global state to
/// be written. What each test sees is scoped by an <see cref="AsyncLocal{T}"/>: an exception is recorded
/// only into the scope open on the logical flow that threw it, so concurrent tests cannot see each
/// other's. Recording never logs, rethrows or swallows anything, so it changes nothing about the code
/// under test.
/// </remarks>
public static class ThrownExceptionRecorder
{
    /// <summary>Subscribes the recorder to the process. Safe to call more than once; only the first call subscribes.</summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1) return;
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    /// <summary>
    /// Opens a scope on the current logical flow. Every exception thrown on that flow — including in code it
    /// awaits — is recorded into the scope until it is disposed.
    /// </summary>
    /// <returns>The open scope; dispose it to stop recording.</returns>
    public static Scope Begin()
    {
        Scope scope = new([]);
        Current.Value = scope;
        return scope;
    }

    /// <summary>An open recording, returned by <see cref="Begin"/>.</summary>
    /// <param name="thrown">The list this scope records into.</param>
    public sealed class Scope(List<Exception> thrown) : IDisposable
    {
        /// <summary>Every exception thrown on this scope's flow while it has been open, in the order thrown.</summary>
        public IReadOnlyList<Exception> Thrown
        {
            get
            {
                lock (thrown) return [.. thrown];
            }
        }

        /// <summary>Stops recording into this scope.</summary>
        public void Dispose() => _closed = true;

        internal void Record(Exception exception)
        {
            if (_closed) return;
            lock (thrown) thrown.Add(exception);
        }

        private volatile bool _closed;
    }

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
    {
        // Recording can itself throw (out of memory), which would raise this event again on the same thread.
        if (_recording) return;
        _recording = true;
        try
        {
            Current.Value?.Record(args.Exception);
        }
        finally
        {
            _recording = false;
        }
    }

    private static readonly AsyncLocal<Scope?> Current = new();
    private static int _installed;

    [ThreadStatic]
    private static bool _recording;
}
