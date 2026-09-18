using Microsoft.VisualStudio.TestTools.UnitTesting;
using Quotinator.Data.Testing.Diagnostics;

namespace Quotinator.Data.Testing.Tests.Diagnostics;

/// <summary>
/// Proves <see cref="ThrownExceptionRecorder"/> sees an exception the code threw and caught itself, and
/// sees only the ones thrown on its own flow while it is open — the two properties every "throws nothing"
/// test built on it depends on.
/// </summary>
[TestClass]
public class ThrownExceptionRecorderTests
{
    [TestMethod]
    public void Scope_ExceptionThrownAndCaughtInside_IsRecorded()
    {
        InvalidOperationException thrown = new("inside");

        using ThrownExceptionRecorder.Scope scope = ThrownExceptionRecorder.Begin();
        ThrowAndCatch(thrown);

        Assert.Contains(thrown, scope.Thrown);
    }

    [TestMethod]
    public void Scope_NothingThrown_RecordsNothing()
    {
        using ThrownExceptionRecorder.Scope scope = ThrownExceptionRecorder.Begin();

        Assert.IsEmpty(scope.Thrown);
    }

    [TestMethod]
    public void Scope_ExceptionThrownOutside_IsNotRecorded()
    {
        InvalidOperationException before = new("before");
        InvalidOperationException after  = new("after");

        ThrowAndCatch(before);
        ThrownExceptionRecorder.Scope scope = ThrownExceptionRecorder.Begin();
        scope.Dispose();
        ThrowAndCatch(after);

        Assert.IsEmpty(scope.Thrown);
    }

    [TestMethod]
    public async Task Scope_ExceptionThrownAfterAnAwait_IsRecorded()
    {
        InvalidOperationException thrown = new("after an await");

        using ThrownExceptionRecorder.Scope scope = ThrownExceptionRecorder.Begin();
        await Task.Yield();
        await Task.Run(() => ThrowAndCatch(thrown), TestContext.CancellationToken);

        Assert.Contains(thrown, scope.Thrown);
    }

    [TestMethod]
    public async Task Scopes_OnConcurrentFlows_RecordOnlyTheirOwn()
    {
        InvalidOperationException first  = new("first flow");
        InvalidOperationException second = new("second flow");
        using Barrier bothOpen = new(2);

        Task<IReadOnlyList<Exception>> RecordOn(InvalidOperationException own) => Task.Run(() =>
        {
            using ThrownExceptionRecorder.Scope scope = ThrownExceptionRecorder.Begin();
            bothOpen.SignalAndWait(TestContext.CancellationToken);
            ThrowAndCatch(own);
            bothOpen.SignalAndWait(TestContext.CancellationToken);
            return scope.Thrown;
        }, TestContext.CancellationToken);

        IReadOnlyList<Exception>[] recorded = await Task.WhenAll(RecordOn(first), RecordOn(second));

        Assert.AreSequenceEqual([first], recorded[0]);
        Assert.AreSequenceEqual([second], recorded[1]);
    }

    /// <summary>The MSTest context, for its cancellation token.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static void ThrowAndCatch(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught) when (ReferenceEquals(caught, exception))
        {
        }
    }
}
