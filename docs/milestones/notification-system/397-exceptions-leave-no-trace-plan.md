# #397 — Exceptions the application catches leave no trace outside Visual Studio

**Status:** Waiting for release
**GitHub issue:** #397
**Tiers required:** T1, T2
**Depends on:** none

---

## Next action

Complete the *Waiting for release* checklist: the Definition of done ticked and the process-gap check.
Every step and verification row is green, T1 passed on 2026-09-18 (the developer's Visual Studio run: a
clean start through a Data v3 → v22 upgrade, zero exceptions before ready, every debugger `Exception
thrown:` line matched by a `[Runtime - Exception]` line), and the scope additions and changelog entry are
recorded.

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

**Status:** ✅ Done — every one of this issue's 17 unit tests is now green

`docs/logging.md` registers the `[Runtime - Exception]` prefix and gains an *Exception lines* section:
the three lines with their levels, the id that ties them together, how to read each combination, why
first-chance gives only the throwing frame, that the lines obey `Quotinator:LogLevel`, and that only a
line our own code already wrote suppresses the middleware's.

`docs/vocabulary.md` gains *exception ending* and *first-chance exception*, per `CLAUDE.md`'s rule that
a domain term used in a narrower sense is added in the same commit that introduces it.

`docs/logging.md`: register the `[Runtime - Exception]` prefix, document the three lines, their levels
and the id, and how to read their combinations, and state that the existing `LogWarning(ex, …)` catch
sites move to `Error` under #398.

### 7. Build and run the full suite

**Status:** ✅ Done — re-run 2026-09-18 after step 8's fixes: 4,113 tests passed across 10 projects, 0 warnings, 0 errors

`dotnet build --configuration Release --no-incremental` and
`dotnet test --configuration Release --verbosity normal -m:1`, 2026-09-17. Nothing regressed: the
handlers are subscribed in the test process too, since `Quotinator.Api.Tests` runs `Program.cs` per
test, and no test depends on an exception going unlogged.

`dotnet build --configuration Release` and `dotnet test --configuration Release --verbosity normal -m:1`,
both 0 warnings, 0 errors.

### 8. T2 pass

**Status:** ✅ Done — the full designated smoke set plus this issue's own document

Run 2026-09-17 against an image built from this branch.

| Document | Result |
|---|---|
| `api-surface/01-baseline` | Pass |
| `api-surface/02-pagination-contract` | Pass — 795 quotes, 1,507 actions, 35 audit rows; effective `pageSize` equalled `totalCount` on all three |
| `api-surface/05-a-thrown-exception-is-logged` | Steps 3 to 5 pass; step 2 fails — see below |
| `import-and-staged-actions/14-fresh-seed` | Steps 1 to 4 and 6 pass on the re-run below; step 5 cannot run until #400, and the document says so |
| `import-and-staged-actions/19-per-file-import-report` | Pass — five per-file reports, `removed=0` against `replacements=15`, `missingTypes=[]`, and zero exception lines across a reseed, reset, import and preview |
| `import-and-staged-actions/01-staged-action-review-workflow` | Failed on its own premise, now re-pointed and passing (below) |
| `database-lifecycle/03-reset-is-a-full-wipe` | Pass — 795 quotes and 40 audit rows before, `0` and the single self-trace row after, `NoResults` on the empty database, both schema counters unchanged at `1` |
| `startup-and-degradation/03-startup-wait-page` | Pass — `503`/`starting` during initialisation, a self-contained auto-refreshing page, then `healthy`/`ready`, Kestrel bound before the banner |
| `notifications-and-changelog/07-changelog-served-from-its-own-database` | Pass — both database files on disk, 126 entries imported and served from the database, no fallback, file intact after a restart |
| `notifications-and-changelog/01-notification-system` | Pass, driven in a browser; three expectations were stale and are corrected (below) |

**`notifications-and-changelog/01` had three stale expectations**, all cause 2: the action button is
labelled **Reset the database**, not **Run**; **All** lists ten rows, not "all five"; the spec declares
eight tags, not "seven". The assertions that matter all held — the expired row reads `Expired`, Cancel
leaves quotes at 795, Confirm takes them to `0` and empties the notification table.

**Every run's exceptions are accounted for, and none originates in Quotinator's code:**

| Run | Exceptions | What they are |
|---|---|---|
| `database-lifecycle/03` | 4 `SocketException` | Inbound reads cancelled by `docker stop` for the database copies |
| `notifications-and-changelog/07` | 2 `SocketException` | The same, from its `docker restart` |
| `notifications-and-changelog/01` | 5 `OperationCanceledException`, 2 `SocketException` | The web UI's WebSocket closing on each page change, and the `docker stop` in step 7 |
| `import-and-staged-actions/01` | 4 `UnresolvedFieldConflictException` | #370's defect, visible for the first time |
| every other run | 0 | — |

All three socket forms are one Knowledgebase entry with several causes, which is what an entry is for.

**These results stand on the Fresh profile as it seeds today — the full bundled corpus.** Every
document here inherits 795 quotes it did not ask for, where a fixture of its own would do; the developer
flagged that pattern (2026-09-18), and `Quotinator__IncludeDefaultSources=false` already exists to turn
it off. Moving the profile is #400's scope, not this issue's.

**`import-and-staged-actions/01` was stale, not broken.** Its step 4 expected `202` from re-importing
the curated file and got `200` with `pending=0`: since #373 an already-stored quote stages as an
`Unchanged` no-op, so the curated re-import leaves nothing to decide against. `04-discard.md` and
`20-pending-review-alert.md` had already been re-pointed at `scripts/testing/stage-import-conflict.csx`
for exactly this; this document had not. Re-pointed at the same fixture and measured green end to end:
`202` with one pending `Quote`, decide `204`, `movedToDecided=1`, undo `204`, `backToPending=1`, nothing
left pending, apply `200`, and a single `Applied` group of 2. Its `Observed effect` section is gone,
per the rule that a document holds the test and nothing else.

**That run logged eight exceptions, and both kinds are accounted for:** four
`UnresolvedFieldConflictException` with distinct ids — [#370](https://github.com/DutchJaFO/Quotinator/issues/370)
itself, visible in a container log for the first time, which is what both issues claimed would happen —
and four `IOException` lines sharing one id, the client disconnect recorded as a Knowledgebase entry.
Startup contributed none.

**What the thrown lines showed, which is the point of the issue.** One malformed upload produced
`JsonReaderException` then `QuoteImportValidationException`, both with ids. Four `IOException` lines
(*"Unable to read data from the transport connection: Operation canceled"*) shared a single id, which is
the id scheme working: one exception object, notified once per throw.

**A healthy startup threw six exceptions, and they are now fixed.** Each was *"A suitable constructor
for type `AdminApiKeyFilter` could not be located"* from inside `ActivatorUtilities`, one per
`AddEndpointFilter<AdminApiKeyFilter>` registration: the framework probes for a constructor taking
`EndpointFilterFactoryContext`, catches the failure and falls back to the parameterless one
(dotnet/runtime#67309 — confirmed against `EndpointFilterExtensions.cs`, which cannot be influenced by
container registration). The filter is now registered once and passed to each group as an instance,
with its configuration injected instead of service-located. Re-measured on a rebuilt image: `before=0`.

Under the developer rule that no exceptions should be seen at all (2026-09-17), `api-surface/05` step 2
now asserts that zero and passes.

**Boyscout, found by review of this issue's own diff:** the six endpoint files converted here passed
their route group prefix to `MapGroup` as a literal, one of them duplicating a string
`ApiRoutes.Import` already held — a breach of `CLAUDE.md`'s string-centralisation policy in files this
issue touched, left in place by a `var` conversion that changed the declaration beside it. The five
missing prefixes are now `ApiRoutes` constants, all six files use them, and
`RouteConstantUsageTests` guards the converted files with a positive control asserting each constant
names a path the API actually serves. The remaining endpoint files still hold literals and are
surfaced rather than swept.

**The upload's own lines, re-measured:** one `JsonReaderException` and four
`QuoteImportValidationException` lines sharing one id — a single exception object notified once per
throw and once per rethrow. The document now says to count distinct ids rather than lines.

**`import-and-staged-actions/14`'s step 5 was never runnable.** It calls
`dotnet run --project src/Quotinator.Api -- --convert`, and no `--convert` CLI exists anywhere in the
codebase — the step was authored in `7d1e9ff6` and never executed. The rest of the document stands: it
is the zero-pending test the bundled-content rule permits, and its other steps stay in its use case.

**Re-run 2026-09-18, after each document's last change**, on the current profile (no downloads),
every command as written:

| Document | Result |
|---|---|
| `api-surface/01` | Pass, 0 exceptions |
| `api-surface/02` | Pass — 795 / 1,507 / 35 rows, 0 exceptions |
| `api-surface/05` | Pass — `before=0`; the upload's lines share one id |
| `import-and-staged-actions/19` | Pass — every step, 0 exceptions |
| `import-and-staged-actions/01` | Pass — `202`, one pending action, decide / undo / apply, one `Applied` group of 2; 3 `UnresolvedFieldConflictException` (#370) |
| `import-and-staged-actions/14` | Steps 1 to 4 pass — nothing pending, no duplicate, casing or undeclared date rows; 2 `SocketException` from the step's own `docker stop`. Step 6 passes as corrected below, 0 exceptions |
| `notifications-and-changelog/01` | Pass, every step, driven in a browser |

**`import-and-staged-actions/14` step 6 could never fail.** It read `existingValue`/`incomingValue`,
which `GET /import/actions` has never returned — the response has carried `existingFields`/
`incomingFields` since #154. Every row errored inside the predicate, found no differences, and counted
as explained. Corrected, with a control row the predicate must flag: on the same data the old predicate
flags `0` and the new one `1`. The re-run gives 25 no-ops (24 `Quote`, 1 `Source`): one `Source` covered
by a declared rule, and 24 `Quote` rows whose incoming side leaves a stored field empty.

**`notifications-and-changelog/01` logged one exception pair nothing had recorded:**
`CryptographicException` with `AntiforgeryValidationException`, *"The antiforgery token could not be
decrypted"*, on the first page load. The browser still held a cookie from the previous container on the
same port, whose key ring went with its volume. The page rendered and every click worked afterwards, so
it is recorded as a new Knowledgebase entry. The other 7 lines are the WebSocket and socket forms
already recorded.

The designated smoke set plus `api-surface/05-a-thrown-exception-is-logged.md`, against a fresh build of
the branch. Every thrown line the smoke set produces is triaged in this step's record under `CLAUDE.md`'s
triage rule — never filtered out of the log.

---

## Scope changes

Recorded on the issue in a comment of its own. Each addition was found while executing this plan and
kept in scope because the issue could not otherwise verify what it claims.

**Added:**

1. **`AdminApiKeyFilter` is registered once and passed as an instance.** Activating it by type made the
   framework throw and swallow six exceptions on every startup. Under the developer rule that no
   exceptions should be seen at all (2026-09-17), `api-surface/05` asserts a zero baseline, which could
   not pass without it. Tests: `AdminApiKeyFilterRegistrationTests`.
2. **Route group prefixes come from `ApiRoutes`** in the six endpoint files this issue touched — a
   boyscout breach of the string-centralisation policy, found in review of this issue's own diff. Tests:
   `RouteConstantUsageTests`.
3. **`docs/knowledgebase/` exists**, with an entry template, a procedure section in `knowledgebase.md`,
   and four entries from this issue's T2 runs — developer direction to record what is learned before
   #333 builds the in-app form.
4. **The test profile downloads nothing** — `Quotinator__AutoUpdateSources=false` in
   `scripts/testing/test-env.csx`.
5. **Three documents corrected** while running the smoke set: `import-and-staged-actions/01` re-pointed
   at the conflict fixture, `notifications-and-changelog/01`'s stale expectations fixed, and
   `import-and-staged-actions/14` marked `**Fully green after:** #400`, with its step 6 predicate
   reading the fields the response actually carries.
6. **One extra test beyond the issue's table:** `BadRequestExceptionHandlerTests.AnotherException_IsDeclinedAndNotLogged`,
   the negative control beside the handler's own test.

**Moved out:** an exception ending a Blazor circuit (#399); tests owning their input instead of the
bundled corpus (#400); observations and unrelated prose across the suite (#401).

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | Every thrown exception is logged at `Error` with an id and the exception | Unit test | `FirstChanceExceptionLoggingTests.ThrownException_IsLoggedAtErrorWithIdAndException`, `FirstChanceExceptionLoggingTests.NoExceptionThrown_LogsNothing` |
| 2 | ✅ | The same exception keeps the same id on every line | Unit test | `ExceptionIdsTests.SameException_GetsTheSameId`, `ExceptionIdsTests.DifferentExceptions_GetDifferentIds`, `FirstChanceExceptionLoggingTests.LogExceptionHandled_CarriesTheSameIdAsTheThrownLine` |
| 3 | ✅ | Logging starts before the host is built | Unit test | `FirstChanceExceptionLoggingTests.ProgramCs_RegistersExceptionLoggingBeforeTheBuilderIsCreated` |
| 4 | ✅ | The handler never throws and never recurses | Unit test | `FirstChanceExceptionLoggingTests.LoggingItselfThrows_DoesNotRecurseOrPropagate` |
| 5 | ✅ | An exception nobody handled is logged at `Critical` with its id, and the log is flushed before the process ends | Unit test | `UnhandledExceptionLoggingTests.UnhandledException_IsLoggedAtCriticalWithItsId`, `UnhandledExceptionLoggingTests.UnhandledException_FlushesTheLogBeforeReturning`, `UnhandledExceptionLoggingTests.UnobservedTaskException_IsLoggedAtCriticalWithItsId`, `UnhandledExceptionLoggingTests.ProgramCs_SubscribesToUnhandledAndUnobservedTaskExceptions` |
| 6 | ✅ | A request exception our handler declared gets one handled line and no middleware line; any other keeps the middleware line and gets `Critical` | Unit test | `ExceptionHandlerDiagnosticsTests.DeclaredHandledException_LogsOneHandledLineAndNoMiddlewareLine`, `ExceptionHandlerDiagnosticsTests.UndeclaredException_KeepsTheMiddlewareLineAndLogsCritical`, `ExceptionHandlerDiagnosticsTests.SuppressionCallback_ReadsTheSameListTheHandlersAreRegisteredFrom` |
| 7 | ✅ | `BadRequestExceptionHandler` declares what it handles | Unit test | `BadRequestExceptionHandlerTests.BindingFailure_Returns422AndLogsHandledLineWithTheThrownId` |
| 8 | ✅ | A thrown exception is logged in a running container | Live (T2) | `automated-testing/api-surface/05-a-thrown-exception-is-logged.md` passes on this branch's build, and fails on the canary from step 2 |
| 9 | ✅ | `docs/logging.md` registers the prefix and documents every line and its level | Unit test | `LoggingDocumentationTests.RuntimeExceptionLines_AreRegisteredAndDocumentedWithTheirLevels` — an assertion over the document's own text, as #307 established |
| 10 | ✅ | No regression | Live | `dotnet test --configuration Release --verbosity normal -m:1` — all pass, 0 warnings, 0 errors |
