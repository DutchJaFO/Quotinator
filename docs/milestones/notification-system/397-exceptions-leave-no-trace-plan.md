# #397 — Exceptions the application catches leave no trace outside Visual Studio

**Status:** In progress
**GitHub issue:** #397
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

Execute this plan, step by step. Re-planned against the current code on 2026-09-17, with every design
decision settled below.

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

**Status:** ✅ Done — `dotnet build --configuration Release`: 0 warnings, 0 errors

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

`ExceptionHandlerSuppressDiagnosticsContext`'s members, read by reflection from the 10.0.12 shared
framework because Microsoft's pages name none: `HttpContext`, `Exception`, and `ExceptionHandledBy`, an
`ExceptionHandledType` of `Unhandled`, `ExceptionHandlerService`, `ProblemDetailsService`,
`ExceptionHandlerDelegate` or `ExceptionHandlingPath`. "Handled by one of our `IExceptionHandler`s" is
`ExceptionHandlerService`.

### 2. Write every test and confirm each is red

**Status:** ✅ Done — 16 tests written, 15 red; the automated document red too

Run 2026-09-17 against the branch with only step 1's signatures in place: `ExceptionIdsTests` 2 of 2
failed, and the Api tests 12 of 13. The one pass is `AnotherException_IsDeclinedAndNotLogged`, a
control — declining an exception the handler does not own needs no logging, so it is correct that it
already holds.

The document needed no separate canary worktree: nothing is implemented yet, so this build *is* the
before-state. `POST /import` with malformed JSON returned `422` while the exception-line count stayed
at `0` across the upload — the document's own step 4 requires it to rise, and it did not. That is both
the document's red run and a live confirmation of the issue's premise: the `JsonException` was thrown,
caught, and left no trace in the container log.

Every row of the Verification checklist below. No test subscribes to an `AppDomain` or `TaskScheduler`
event: `docs/testing-policy.md` allows global state to be written only once, in `[AssemblyInitialize]`,
so each test calls the handler method directly with constructed event arguments
(`FirstChanceExceptionEventArgs`, `UnhandledExceptionEventArgs`, `UnobservedTaskExceptionEventArgs` all
have public constructors).

The automated document runs against a canary built from
the commit before this issue's first code commit, which must log no thrown line at all; then the
container, image and worktree are removed.

### 3. Log every thrown exception from the first line of `Program.cs`

**Status:** ✅ Done — 10 of the 15 Api tests green, both `ExceptionIds` tests green, build 0 warnings

The five still red are steps 4 to 6's: the suppression list, `BadRequestExceptionHandler`'s own line,
and the documentation.

Two things the implementation added beyond the step's own description. `LogOutputTemplates` holds the
two console templates, because the temporary logger and the configured one must render identically and
a second copy would drift. And `LogExceptionNotHandled` guards its call site with
`IsEnabled(LogLevel.Critical)`: CA1873 counts assigning an id and reading a type name as work not worth
doing when the level is off, which `docs/logging.md` already prescribes this exact remedy for.

- **The logger exists before the host, without Serilog's `CreateBootstrapLogger`.** That reloadable
  logger freezes when the first host is built and throws "The logger is already frozen" on the next
  (serilog/serilog-aspnetcore#312) — and `Quotinator.Api.Tests` builds a host per test in one process.
  Instead `Program.cs` assigns a plain logger to `Log.Logger` if none has been assigned yet, using the
  same output template as the configured one — the template moves to one constant so the two cannot
  drift — and `UseSerilog` replaces `Log.Logger` once the host is built.
- **The handlers read `Log.Logger` at the moment they log**, so they move to the host's logger the
  moment it exists, and wrap it in a `SerilogLoggerFactory` so they can call the `[LoggerMessage]`
  methods. That wrapper is created outside DI because it has to exist before the container does; the
  call site says so, per `CLAUDE.md`'s DI policy.
- **Subscription is the first statement of `Program.cs`**, ahead of `QuotinatorDapperConfiguration`,
  and happens once per process however many hosts are built.
- **The lines obey `Quotinator:LogLevel` like every other line** (developer decision, 2026-09-17): a
  configured level is the operator's instruction, not a gap to work around, so at `fatal` only the
  `Critical` lines appear. Nothing lowers the global minimum for these events.
- **The id** is 8 hex characters, held in a `ConditionalWeakTable<Exception, string>` so it lives exactly
  as long as the exception and the exception object itself is never modified.
- **Recursion:** a `[ThreadStatic]` flag makes the first-chance handler a no-op while it is already
  running on that thread, and the whole handler body sits in a `try`/`catch` that discards, as
  Microsoft's documentation requires.

### 4. Log the endings nobody handled at `Critical`

**Status:** ✅ Done — code complete; its verification row goes green with step 5

The thread and unobserved-task endings landed with step 3's handlers and are already green.
`UnhandledRequestExceptionHandler` now logs and declines, and is registered directly after
`BadRequestExceptionHandler` so it sees only what every handler above it passed on.

Its assertion sits inside `UndeclaredException_KeepsTheMiddlewareLineAndLogsCritical`, which reaches the
suppression callback first and therefore still fails on step 5's unimplemented method — confirmed by the
stack trace, not assumed. One test covering both was the price of matching the issue's own table.

- `UnhandledException` logs `Critical` and then calls `Log.CloseAndFlush()`, because the runtime
  terminates the process once the handler returns. Today's sinks (Console, Debug) are synchronous, so
  the line is written anyway; the flush keeps that true if an asynchronous sink is ever added.
- `UnobservedTaskException` logs `Critical` for each inner exception.
- `UnhandledRequestExceptionHandler` is registered last, logs `Critical`, and returns `false`, so the
  middleware still produces its `500` and its own log line.

### 5. Declare handled request exceptions and suppress only their duplicate line

**Status:** ✅ Done — every test but the documentation one is green

`DeclaredExceptionHandlers` maps `BadHttpRequestException` to `BadRequestExceptionHandler`, and
`Program.cs` configures `ExceptionHandlerOptions.SuppressDiagnosticsCallback` from it rather than
passing options to `UseExceptionHandler`, so the pipeline line is untouched.

The match is `IsInstanceOfType`, not an exact type comparison: the handler declines on
`is not BadHttpRequestException`, which also accepts subclasses, and a suppression check that
disagreed with its own handler would either hide a line or duplicate one.

**A defect in this issue's own test, found by running it:** `Activator.CreateInstance(declared)` cannot
build a `BadHttpRequestException`, which has no parameterless constructor — the test would have failed
for a reason unrelated to the behaviour, the broken-instrument shape `docs/automated-testing/README.md`
describes. It now builds through whichever constructor a declared type actually has.

- `BadRequestExceptionHandler` calls `LogExceptionHandled` before writing its `422`.
- `DeclaredExceptionHandlers` lists `BadHttpRequestException`. Both the handler registration and
  `SuppressDiagnosticsCallback` read that list. The callback suppresses only when `ExceptionHandledBy` is
  `ExceptionHandlerService` **and** the exception's type is on the list, so the middleware's own line
  disappears exactly for an exception one of our handlers declared and logged, and never for anything
  else.

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
