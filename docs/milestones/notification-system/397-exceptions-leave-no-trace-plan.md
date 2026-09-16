# #397 — Exceptions the application catches leave no trace outside Visual Studio

**Status:** Planning
**GitHub issue:** #397
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

This is a draft, written before #397 started. Re-plan it against the current code and issues when #397
starts.

---

## Description

Nothing in `src/` subscribes to `AppDomain.FirstChanceException`, so an exception the code catches itself
is visible only in Visual Studio's debugger output. Since .NET 10, ASP.NET Core's exception-handler
middleware also stops logging an exception an `IExceptionHandler` reports as handled, so
`BadRequestExceptionHandler`'s `422`s leave no line at all.

No notification can say at throw time whether an exception is expected — `FirstChanceException` fires
before the runtime searches for a handler. Where each exception *ends* decides it, so each ending gets its
own line, tied together by an id:

| Where it ends | Level |
|---|---|
| Thrown | `Error` |
| Handled where a response is formed | `Error` |
| Escaped a request, a thread, or an unobserved task | `Critical` |

Thrown plus handled is expected behaviour. Thrown plus `Critical` is dangerous. Thrown plus neither was
caught somewhere that never declared itself a response point — what #398 exists to remove.

### Measured: what the thrown line can show

At first-chance time an exception's `StackTrace` holds **only the frame that threw it**; the caller chain
fills in as the stack unwinds, and is complete by the time a `catch` runs. Measured 2026-09-16 with a
three-frame probe (`Inner` ← `Middle` ← caller): first-chance reported `at Inner() … line 4` alone, the
`catch` reported all three frames. So the thrown line identifies the throw site, and the handled and
`Critical` lines — which pass the exception after unwinding — carry the full trace.

### Endings not covered here

An exception escaping a Blazor Server circuit is handled inside the circuit, not by the exception-handler
middleware, so none of the three endings above sees it. That ending is #399's, kept separate so this
issue does not stall on circuit mechanics.

---

## Steps

### 1. Add the signatures only

**Status:** ⬜ Not started

Per `docs/testing-policy.md`'s *Red first means signatures first* — types and members, bodies throwing
`NotImplementedException`:

- **`Quotinator.Logging`** — where `LogExceptionHandled` must live, because #398's catch sites are in
  `Quotinator.Data` and `Quotinator.Core`, and every project already references `Quotinator.Logging`,
  which depends on nothing but `Microsoft.Extensions.Logging.Abstractions`:
  - `ExceptionIds` — `static string For(Exception)`, returning the same id for the same instance.
  - `LogExceptionThrown`, `LogExceptionHandled`, `LogExceptionNotHandled` in the shared `LogMessages`.
- **`Quotinator.Api/Startup/ExceptionLogging`** — the `FirstChanceException`, `UnhandledException` and
  `UnobservedTaskException` handlers, and the method that subscribes them.
- **`Quotinator.Api/Middleware/DeclaredExceptionHandlers`** — the one list of exception types our
  `IExceptionHandler`s handle, and the `SuppressDiagnosticsCallback` built from it.
- **`Quotinator.Api/Middleware/UnhandledRequestExceptionHandler`** — the last-resort `IExceptionHandler`.

Before writing the callback, confirm `ExceptionHandlerSuppressDiagnosticsContext`'s members against the
assembly itself. Microsoft's pages say it carries the exception, the request, and whether the exception
was handled, but name no properties.

### 2. Write every test and confirm each is red

**Status:** ⬜ Not started

Every row of the Verification checklist below. The automated document runs against a canary built from
the commit before this issue's first code commit, which must log no thrown line at all; then the
container, image and worktree are removed.

### 3. Log every thrown exception from the first line of `Program.cs`

**Status:** ⬜ Not started

- **The logger exists before the host.** `Program.cs` opens with Serilog's two-stage initialisation: a
  bootstrap logger assigned to `Log.Logger` using the same output template as the configured one — the
  template moves to one constant so the two cannot drift — then `UseSerilog` replaces its configuration
  once the host is built.
- **The handlers log through `Log.Logger`**, wrapped once in a `SerilogLoggerFactory` so they can call
  the `[LoggerMessage]` methods. That wrapper is created outside DI because it has to exist before the
  container does; the call site says so, per `CLAUDE.md`'s DI policy.
- **Subscription is the first statement** after the bootstrap logger is assigned.
- **The id** is 8 hex characters, held in a `ConditionalWeakTable<Exception, string>` so it lives exactly
  as long as the exception and the exception object itself is never modified.
- **Recursion:** a `[ThreadStatic]` flag makes the first-chance handler a no-op while it is already
  running on that thread, and the whole handler body sits in a `try`/`catch` that discards, as
  Microsoft's documentation requires.

### 4. Log the endings nobody handled at `Critical`

**Status:** ⬜ Not started

- `UnhandledException` logs `Critical` and then calls `Log.CloseAndFlush()`, because the runtime
  terminates the process once the handler returns. Today's sinks (Console, Debug) are synchronous, so
  the line is written anyway; the flush keeps that true if an asynchronous sink is ever added.
- `UnobservedTaskException` logs `Critical` for each inner exception.
- `UnhandledRequestExceptionHandler` is registered last, logs `Critical`, and returns `false`, so the
  middleware still produces its `500` and its own log line.

### 5. Declare handled request exceptions and suppress only their duplicate line

**Status:** ⬜ Not started

- `BadRequestExceptionHandler` calls `LogExceptionHandled` before writing its `422`.
- `DeclaredExceptionHandlers` lists `BadHttpRequestException`. Both the handler registration and
  `SuppressDiagnosticsCallback` read that list, so the middleware's own line is suppressed exactly for an
  exception one of our handlers declared and logged, and never for anything else.

### 6. Document the lines

**Status:** ⬜ Not started

`docs/logging.md`: register the `[Runtime - Exception]` prefix, document the three lines, their levels
and the id, and how to read their combinations, and state that the existing `LogWarning(ex, …)` catch
sites move to `Error` under #398.

### 7. Build and run the full suite

**Status:** ⬜ Not started

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both 0 warnings, 0 errors.

### 8. T2 pass

**Status:** ⬜ Not started

The designated smoke set plus `api-surface/05-a-thrown-exception-is-logged.md`, against a fresh build of
the branch. Every thrown line the smoke set produces is triaged in this step's record under `CLAUDE.md`'s
triage rule — never filtered out of the log.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | Every thrown exception is logged at `Error` with an id and the exception | Unit test | `FirstChanceExceptionLoggingTests.ThrownException_IsLoggedAtErrorWithIdAndException`, `FirstChanceExceptionLoggingTests.NoExceptionThrown_LogsNothing` |
| 2 | ❌ | The same exception keeps the same id on every line | Unit test | `ExceptionIdsTests.SameException_GetsTheSameId`, `ExceptionIdsTests.DifferentExceptions_GetDifferentIds`, `FirstChanceExceptionLoggingTests.LogExceptionHandled_CarriesTheSameIdAsTheThrownLine` |
| 3 | ❌ | Logging starts before the host is built | Unit test | `FirstChanceExceptionLoggingTests.ProgramCs_RegistersExceptionLoggingBeforeTheBuilderIsCreated` |
| 4 | ❌ | The handler never throws and never recurses | Unit test | `FirstChanceExceptionLoggingTests.LoggingItselfThrows_DoesNotRecurseOrPropagate` |
| 5 | ❌ | An exception nobody handled is logged at `Critical` with its id, and the log is flushed before the process ends | Unit test | `UnhandledExceptionLoggingTests.UnhandledException_IsLoggedAtCriticalWithItsId`, `UnhandledExceptionLoggingTests.UnhandledException_FlushesTheLogBeforeReturning`, `UnhandledExceptionLoggingTests.UnobservedTaskException_IsLoggedAtCriticalWithItsId`, `UnhandledExceptionLoggingTests.ProgramCs_SubscribesToUnhandledAndUnobservedTaskExceptions` |
| 6 | ❌ | A request exception our handler declared gets one handled line and no middleware line; any other keeps the middleware line and gets `Critical` | Unit test | `ExceptionHandlerDiagnosticsTests.DeclaredHandledException_LogsOneHandledLineAndNoMiddlewareLine`, `ExceptionHandlerDiagnosticsTests.UndeclaredException_KeepsTheMiddlewareLineAndLogsCritical`, `ExceptionHandlerDiagnosticsTests.SuppressionCallback_ReadsTheSameListTheHandlersAreRegisteredFrom` |
| 7 | ❌ | `BadRequestExceptionHandler` declares what it handles | Unit test | `BadRequestExceptionHandlerTests.BindingFailure_Returns422AndLogsHandledLineWithTheThrownId` |
| 8 | ❌ | A thrown exception is logged in a running container | Live (T2) | `automated-testing/api-surface/05-a-thrown-exception-is-logged.md` passes on this branch's build, and fails on the canary from step 2 |
| 9 | ❌ | `docs/logging.md` registers the prefix and documents every line and its level | Unit test | `LoggingDocumentationTests.RuntimeExceptionLines_AreRegisteredAndDocumentedWithTheirLevels` — an assertion over the document's own text, as #307 established |
| 10 | ❌ | No regression | Live | `dotnet test --configuration Release --verbosity normal -m:1` — all pass, 0 warnings, 0 errors |
