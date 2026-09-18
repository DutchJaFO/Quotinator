# The log reports a cancelled socket or transport connection

**Kind:** diagnostic
**Entry code:** —
**Status code:** —
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** —

## Symptom

One or more `Error` lines, in either of two forms:

```
[Runtime - Exception] 909eaa17 thrown: IOException
System.IO.IOException: Unable to read data from the transport connection: Operation canceled.
 ---> System.Net.Sockets.SocketException (125): Operation canceled
```

```
[Runtime - Exception] b7d69199 thrown: SocketException
System.Net.Sockets.SocketException (125): Operation canceled
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
```

## Does it prevent the app or API from functioning?

**No.** The request in question did not complete; everything else is unaffected, and the application
stays healthy.

## Cause

Two causes are known, and the surrounding log lines tell them apart.

**1. An inbound connection closed while the server was reading it** — the bare `SocketException` form,
with no `[Database - SourceRefresh]` lines near it. A client disconnected mid-request, or the container
was stopped while requests were open (`docker stop` cancels every in-flight read). The frames are the
web server's own socket layer, not Quotinator's code. Observed 2026-09-17: four such lines, each with its
own id, at the moment a container was stopped for a database copy.

The same cause has a third form when the connection was a browser page's live connection: an
`OperationCanceledException` whose frames run through `Http1UpgradeMessageBody` and
`System.IO.Pipelines`. That is the web UI's WebSocket closing because the page was navigated away from
or closed — every page change produces one. Observed 2026-09-18 while driving the notifications page.

**2. A source download whose connection was cancelled or timed out** — the `IOException` form. Source refresh
(`Quotinator:AutoUpdateSources`, on by default) fetches each manifest-declared file through the standard
HTTP handler, bounded by `Quotinator:SourceRefreshConnectTimeoutSeconds` (60 s) and the refresh's own
timeout (90 s). A connect that is dropped, stalls past its budget, or is cut short by shutdown surfaces
as this `IOException`. Intermittent by nature: the same host answers in ~300 ms on another run.

A refresh failing this way costs nothing — `SourceCacheUpdater` falls back to the local copy and the
refresh runs again next cycle.

**Several lines with one id are one exception, not several faults.** An id is assigned per exception
object, and a rethrow is notified again, so a single cancellation can produce four identical lines.
Count distinct ids, not lines.

## Remedy

Nothing, for either cause. For cause 1, retry the request if its result was wanted. For cause 2, the
refresh falls back to the local copy and runs again next cycle; setting
`Quotinator:AutoUpdateSources=false` stops source refresh entirely, and with it that form of the line.

## Notes

These lines only appear from 1.9.0-alpha, when every thrown exception started being logged
([#397](https://github.com/DutchJaFO/Quotinator/issues/397)). Earlier versions threw the same
exceptions and showed nothing, so an upgrade appearing to "introduce" them is the logging arriving, not
new faults.
