# ADR 022 — An exception is thrown only when nothing else can detect the condition, and every exception is logged

**Status:** Accepted
**Date:** 2026-09-18
**GitHub issues:** #370, #397, #398, #399

---

## Context

This codebase signals ordinary, expected outcomes by throwing. `FieldMergeResolver.ResolveWithDecisions`
throws when fields disagree and nobody has decided; seven call sites catch it and carry on, and the
review listing throws it once per conflicted row on every render. Lookups throw when a row is absent, and
state checks throw when a status is wrong — each immediately after a check whose result the code already
held.

An exception used this way costs the reader rather than the program. It stops a debugger set to break on
thrown exceptions, and it fills a log with lines indicating no fault, which trains a reader to skip
exception lines — including the one that matters.

Separately, a caught exception was visible nowhere outside a debugger. Nothing observed exceptions as
they were thrown, and since .NET 10 ASP.NET Core's exception-handler middleware writes nothing for an
exception an `IExceptionHandler` reports as handled. A rule about when to throw cannot be verified while
the throws themselves are invisible.

## Decision

**1. An exception is thrown only when there is no other way to detect the condition.** A condition the
code has already checked — a lookup returned nothing, a status is wrong, a set of conflicting fields is
non-empty — is returned as an outcome. What remains legitimate is what no API lets the code check first:
malformed input a parser rejects, file and network I/O, a database error.

**2. A legitimate exception is caught at the first point a response can be formed**, not rethrown as a
different type further up and not caught somewhere that says nothing.

**3. A guard against a programming error still throws.** An `ArgumentException`-family guard, or an
invariant the code constructed itself being violated, has no caller that could respond to it, so there
is no outcome to return.

**4. Every exception is logged, and where it ends decides its level.** All lines about one exception
carry the same 8-character id:

| Where it ends | Level |
|---|---|
| Thrown — every exception, when it is thrown | `Error` |
| Handled where a response is formed, through `LogExceptionHandled` | `Error` |
| Escaped a request, a thread, or an unobserved task | `Critical` |

A thrown line followed by a handled line is expected behaviour. A thrown line followed by a `Critical`
line is dangerous. A thrown line followed by neither was caught somewhere that never declared itself a
response point, which rule 2 forbids.

**5. These lines obey the operator's configured log level** like every other line. A configured level is
an instruction, not a gap to work around.

**6. The framework's own exception log line is suppressed only where our code already logged the
exception as handled** — `SuppressDiagnosticsCallback` reads the one list pairing each declared exception
type with the handler that owns it.

## Consequences

**Exception lines become a detector.** Once the application stops throwing for checked conditions, any
thrown line in a log is either a genuine fault or an environmental event worth knowing about — which is
what makes the rule enforceable by reading a log rather than by review.

**Converting the existing throws is a body of work**, tracked as #398 and its sub-issues, with #370
covering unresolved field conflicts and the decide path.

**Framework-internal exceptions appear in the log** — a client disconnecting, a socket read cancelled at
shutdown. They are triaged under `CLAUDE.md`'s triage rule and recorded in `docs/knowledgebase/`, never
filtered out of the log.

**A test that provokes exceptions and expects none is usually running on shipped data** rather than on
data built for the feature under test; see `docs/automated-testing/README.md`.
