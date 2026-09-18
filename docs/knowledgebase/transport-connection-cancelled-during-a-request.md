# The log reports a cancelled socket or transport connection

**Kind:** diagnostic
**Entry code:** —
**Status code:** —
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** [#402](https://github.com/DutchJaFO/Quotinator/issues/402) — automated tests that stop their container

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

**No.** At shutdown the application finishes stopping. Otherwise, the request in question did not
complete; everything else is unaffected, and the application stays healthy.

## Cause

Three causes are known, and the surrounding log lines tell them apart.

**1. The application is stopping** — the bare `SocketException` form, immediately after
`[Server] Quotinator … stopping`. On shutdown the web server closes each port it listens on, which
cancels its wait for the next connection; the web server throws this exception and catches it itself
(`SocketConnectionListener.AcceptAsync`, ASP.NET Core release/10.0). Exactly one per listening port:
two in the container, which listens on 8080 and 8099. It appears on every stop and restart, with no
client connected. Measured 2026-09-18: two lines on stopping a container that had served only requests
from exited processes. The application follows the documented shutdown sequence, and the documentation
neither mentions this exception nor offers a way to stop listening without it — so it is recorded
here as unresolved, not as expected.

**2. A client connection closed while the server was reading it.** A client disconnected mid-request,
or the application stopped while a connection was open. On Windows, and over HTTPS, this reads as
`IOException: Unable to read data from the transport connection: The I/O operation has been aborted
because of either a thread exit or an application request` over `SocketException (995)`, with
`SslStream` frames. Error 995 is Windows' equivalent of Linux's 125, and the `SslStream` frames only
mean the connection was TLS. Observed 2026-09-18 in a Visual Studio run, the same id repeated as it was
rethrown.

When the connection was a browser page's live connection, it reads as an `OperationCanceledException`
whose frames run through `Http1UpgradeMessageBody` and `System.IO.Pipelines`: the web UI's WebSocket
closing because the page was navigated away from or closed, or because the application stopped while
the page was open. Observed 2026-09-18 while driving the notifications page.

**3. A source download whose connection was cancelled or timed out** — the `IOException` form. Source refresh
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

None is known for cause 1; the application stops completely regardless. For cause 2, retry the request
if its result was wanted. For cause 3, the refresh falls back to the local copy and runs again next cycle; setting
`Quotinator:AutoUpdateSources=false` stops source refresh entirely, and with it that form of the line.

## Notes

These lines only appear from 1.9.0-alpha, when every thrown exception started being logged
([#397](https://github.com/DutchJaFO/Quotinator/issues/397)). Earlier versions threw the same
exceptions and showed nothing, so an upgrade appearing to "introduce" them is the logging arriving, not
new faults.
