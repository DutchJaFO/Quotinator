# The log reports a cancelled transport connection during a request

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

The client went away mid-request — a browser tab closed, a command interrupted, a proxy timing out, a
script exiting before reading the response. The exception comes from the server's own socket read, not
from anything Quotinator decided.

**Several lines with one id are one exception, not several faults.** An id is assigned per exception
object, and a rethrow is notified again, so a single cancelled read can produce four identical lines.
Count distinct ids, not lines.

## Remedy

Nothing. Retry the request if its result was wanted.

## Notes

These lines only appear from 1.9.0-alpha, when every thrown exception started being logged
([#397](https://github.com/DutchJaFO/Quotinator/issues/397)). Earlier versions threw the same
exceptions and showed nothing, so an upgrade appearing to "introduce" them is the logging arriving, not
new faults.
