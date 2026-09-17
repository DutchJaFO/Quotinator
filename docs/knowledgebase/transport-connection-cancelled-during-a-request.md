# The log reports a cancelled transport connection during a source download

**Kind:** diagnostic
**Entry code:** —
**Status code:** —
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** —

## Symptom

One or more `Error` lines, often several in a row carrying the same id:

```
[Runtime - Exception] 909eaa17 thrown: IOException
System.IO.IOException: Unable to read data from the transport connection: Operation canceled.
 ---> System.Net.Sockets.SocketException (125): Operation canceled
```

## Does it prevent the app or API from functioning?

**No.** The request in question did not complete; everything else is unaffected, and the application
stays healthy.

## Cause

**A source download whose connection was cancelled or timed out.** Source refresh
(`Quotinator:AutoUpdateSources`, on by default) fetches each manifest-declared file through the standard
HTTP handler, bounded by `Quotinator:SourceRefreshConnectTimeoutSeconds` (60 s) and the refresh's own
timeout (90 s). A connect that is dropped, stalls past its budget, or is cut short by shutdown surfaces
as this `IOException`. Intermittent by nature: the same host answers in ~300 ms on another run.

A refresh failing this way costs nothing — `SourceCacheUpdater` falls back to the local copy and the
refresh runs again next cycle.

An inbound client disconnecting mid-request produces a similar message, so the line alone does not
distinguish the two. What separates them is whether a refresh was running: the
`[Database - SourceRefresh]` lines around it say so.

**Several lines with one id are one exception, not several faults.** An id is assigned per exception
object, and a rethrow is notified again, so a single cancellation can produce four identical lines.
Count distinct ids, not lines.

## Remedy

Nothing — the download either succeeded on the winning attempt or is retried. Setting
`Quotinator:AutoUpdateSources=false` stops source refresh entirely, and with it this line.

## Notes

These lines only appear from 1.9.0-alpha, when every thrown exception started being logged
([#397](https://github.com/DutchJaFO/Quotinator/issues/397)). Earlier versions threw the same
exceptions and showed nothing, so an upgrade appearing to "introduce" them is the logging arriving, not
new faults.
