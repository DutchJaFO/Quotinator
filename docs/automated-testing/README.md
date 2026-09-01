# Automated Testing — the T2 suite

These are the project's automated integration tests: everything that needs a real container, a real
SQLite file, a real volume, or the real bundled dataset to mean anything. Unit tests cover what can be
proven in-process; a test earns a place here by exercising something they cannot reach.

Run against a locally built Docker image. See [`docs/release-verification.md`](../release-verification.md)
for the tier definitions and [`CLAUDE.md`](../../CLAUDE.md)'s Pre-Push Checklist for when a pass is
required.

---

## When to run what

Three scopes, and they are not interchangeable.

| Scope | What runs |
|---|---|
| **End of an issue (T2)** | The designated smoke set below, plus whatever tests are relevant to that issue. Not everything |
| **End of a milestone** | Every test here. No exceptions |
| **Release** | Every test here — a release follows a milestone close |

An issue's "relevant tests" are the ones covering what it touched. Deciding that is the issue's own
job, recorded in its plan doc; the smoke set is the floor underneath it, never a substitute for it.

---

## The designated smoke set

Nine tests. The question this set answers is *does this container fundamentally work* — a test is in
it because its failure would invalidate most other results, not because it is important in its own
right.

| Test | Category |
|---|---|
| [Baseline — health/version/random/search](api-surface/01-baseline.md) | `api-surface/` |
| [Pagination contract](api-surface/02-pagination-contract.md) | `api-surface/` |
| [Import and staged-action review workflow](import-and-staged-actions/01-staged-action-review-workflow.md) | `import-and-staged-actions/` |
| [Fresh seed produces zero pending actions](import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions.md) | `import-and-staged-actions/` |
| [Per-file, per-entity-type import/seed report](import-and-staged-actions/19-per-file-import-report.md) | `import-and-staged-actions/` |
| [Reset is a full wipe with no reseed](database-lifecycle/03-reset-is-a-full-wipe.md) | `database-lifecycle/` |
| [Startup wait page during database initialisation](startup-and-degradation/03-startup-wait-page.md) | `startup-and-degradation/` |
| [Startup notification system](notifications-and-changelog/01-notification-system.md) | `notifications-and-changelog/` |
| [Changelog is served from its own on-disk database](notifications-and-changelog/07-changelog-served-from-its-own-database.md) | `notifications-and-changelog/` |

Every test document states its own membership in its `Smoke` field. This table is the index of that
field, not a second source of truth — if the two disagree, the document is right and this table needs
fixing. `RepositoryStructureTests.SmokeSetInTheIndex_MatchesTheDocumentsMarkedSmoke` enforces that they
never do, which is why each row links its document rather than naming it.

---

## Rules every test here follows

**A test must reproduce its condition reliably. An intermittent result is not a result.** If a test
passes or fails depending on something it does not pin, it is not testing what it claims to. This is
what the `Determinism` field exists for: name every variable the outcome depends on, and pin it.

Two measured cases are why this is a rule rather than advice. #326 found that WAL sidecar state — not
a pending migration — decides whether a read-only mount degrades, so a test that does not pin how the
seeding container was stopped passes or fails by luck. The source-download path has produced both
outright failures and ~300 ms successes minutes apart. A condition that cannot be pinned is reported
as such, not left in the suite as a coin flip.

**When the expected situation does not occur, there are three causes — decide which before changing
anything** (developer direction, 2026-08-23):

1. **The feature is broken.** Fix the code.
2. **The expectation was wrong.** Fix the test.
3. **It could not be observed at all.** Add the observability — a log line, a notification, a
   read-back — and only then decide between 1 and 2.

**The instinct is to assume cause 2, and that is what turns causes 1 and 3 into a passing test.** An
expectation edited to match whatever happened is no longer an expectation; the number gets adjusted,
the assertion gets loosened, and the suite reports coverage it stopped providing. Every "fixed by
editing the digit" warning elsewhere in this file is a specific instance of this general mistake.

All three have been found here, which is why it is written down:

- **Cause 2** — `import-and-staged-actions/10` told the reader to expect `NULL` dates for two films for
  three weeks after the underlying gap was fixed. The expectation was stale and nothing re-checked it,
  because it was prose rather than a command.
- **Cause 3, instrument broken** — `notifications-and-changelog/04` counted duplicate announcements with
  `grep -c`, which counts *lines*, against single-line JSON. A genuine duplicate still reported `1`. The
  feature was fine and the expectation was right; the observation could not distinguish them.
- **Cause 3, nothing observed at all** — four commands across the suite had no stated expectation of any
  kind, and two of those asked a human to read a log tail and judge whether it "looked finished".
- **Cause 3, the observation exists but cannot carry the weight** — `16` and `17` conclude from an
  empty list. The seed *does* log a per-file report rendering `stale=0`, so "the reseed ran" is
  observable; what is not is whether the staleness evaluation itself ran, since `stale=0` is produced
  identically by *compared the rules, none drifted* and by *never compared anything*. A count of zero
  is not evidence that something looked.

**Before concluding that something is unobservable, check all three channels.** This application
exposes what it did through **logs**, through the **audit trail** (`Audit_Entry` / `GET /admin/audit`),
and through **API responses**. Searching the log-message definitions alone and finding nothing is not
evidence that a decision is invisible — found twice in a row during #339: staleness counts turned out to
be rendered inside an existing report line rather than a message of their own, and the auto-purge turned
out to write an `Audit_Entry` with `Operation = Purge` rather than log anything. Both were called
unobservable on a keyword search that only covered logging.

**A test cannot distinguish causes the application does not expose.** Where the answer is cause 3 and
the missing observation is the application's own, the fix belongs in the application — normal logging
should not be swallowing a decision an operator would want to know about. A test may raise the log
level to see it (`Quotinator__LogLevel=debug`, as
[`11-batch-id-validation-and-request-log-status.md`](import-and-staged-actions/11-batch-id-validation-and-request-log-status.md)
does), but raising the level cannot conjure a line nobody writes.

### A test that cannot be confirmed has failed

**If a run cannot confirm the behaviour, the test's result is FAIL — not pass, and not "a known
limitation"** (developer direction, 2026-08-23). A test whose observation cannot distinguish working
from broken has not established anything, and recording that as anything other than a failure hands
back confidence the run did not earn.

**A failing test blocks the release.** It is cleared by fixing the problem or by fixing the
observability of the feature under test — never by restating the limitation more clearly, and never by
weakening the expectation until whatever happened counts as a pass.

**The order matters when the observation is the thing missing.** If a test says that doing X must
produce a `Stale` response somewhere, and that response genuinely should exist, and nothing can see it,
then the first work is making it visible — not building a fixture that forces the feature to fire, and
not softening the assertion. Only once the behaviour is observable can a run tell the three causes
apart, and only then does a green result mean anything.

**A document never records its own verdict.** A test states what is verified, how, and what a pass
looks like — instructions, not status. "This test currently fails" belongs to a run, not to the
specification, and writing it into the document creates exactly the staleness this suite keeps finding:
a status line is true on the day it is written and silently wrong afterwards, and nothing re-checks it.
The plan doc or the issue is where a known-failing state is tracked.

**So a document with a missing observation states the observation it needs, and the run fails.** Where
the application does not yet emit what a step must read, the step says what it must read anyway. That is
an ordinary expectation; the run fails against it until the application provides it, which is the
correct signal and the one that cannot go stale. Do not soften it to what is currently observable, and
do not annotate it with the fact that it fails — execute it and see.

**Never assert a specific migration number or schema version.** Not `Data v2 → v11`, not "migration 8
does X". Migration counts change whenever any milestone adds one, and they are consolidated before a
release, so a hardcoded number goes stale on its own and gets "fixed" by editing the number rather
than by anyone checking what actually happened. Assert the *behaviour*: that a `migration applied:`
line appears, that no SQLite error accompanies it, that the resulting state is healthy, that content
is present and correct.

**We only observe facts. We never claim to know how many.** A test cannot legitimately assert that an
import produces 799 quotes or 461 sources — that is a prediction about content, and content changes
when a bundled file is updated, a converter changes, or a user import lands. Asserting it means the
test fails for a correct reason, gets "fixed" by editing the digit, and absorbs a real regression the
next time one occurs.

What can be asserted are the facts the operation itself establishes:

- **Zero import failures.** This is the invariant, and it is the same rule as zero errors and zero
  warnings at compile time. Observably: the call succeeded, nothing threw, and the report shows zero in
  the buckets that mean something needs attention — `Pending`, `Blocked`, `Stale`.
- **Non-zero where content must exist.** An import into an empty database must produce at least one
  quote. That is a fact about the operation, not a prediction about the dataset.
- **Relationships that hold whatever the data is.** One seed batch per bundled file; the manifest linked
  to every batch it drove; a count unchanged across a restart; a read reporting the same number the
  write reported. These stay true when the dataset changes, because they are derived in the same run.

### A count is evidence only if the instrument counts the right thing

**A number that cannot be produced is not a failing assertion — it is a broken instrument, and it
fails or passes for reasons that have nothing to do with the application.** Every count a document
asserts must be checked against three questions before it is trusted.

**Does it count the right unit?** `Select-String` returns one result per *matching line*, not per match
— the same trap `grep -c` set before the suite was PowerShell, and piping it into `Measure-Object` does
not fix it. This suite's responses are single-line JSON, so counting that way returns `1` however many
times the string occurs, and `0` only when it occurs never. `notifications-and-changelog/01` required
`3` from exactly this shape and therefore failed on a correct setup every run.

Two forms that do count the right thing:

```powershell
([regex]::Matches($text, 'pattern')).Count                   # occurrences in a block of text
@(Invoke-RestMethod $url).items.Count                        # better still — count objects, not text
```

The second is the real answer wherever the response is JSON. A count taken from a parsed object cannot
disagree with the response about what a match is.

**Always wrap a filtered result in `@(…)` before taking `.Count`:**

```powershell
@($response.items | Where-Object { $_.source -eq $title }).Count
```

Windows PowerShell 5.1 gives a single `PSCustomObject` **no `Count` property at all**, so the same
expression written without the `@(…)` prints an empty string rather than `1` when exactly one row
matches — while being perfectly correct for zero rows and for two. Measured on this machine, and it
cost a false failure while converting `api-surface/03`, whose filter found the one row it was looking
for and reported nothing.

The one-row case is the *most likely* outcome of a well-targeted assertion, which is what makes this
worth a rule: the unwrapped form fails precisely when the test is working.

**Does it match what the application actually emits?** Two live cases. `import-and-staged-actions/16`
and `17` count `"operation":"Purge"` while the audit trail records `"operation":"Purged"` — the
trailing quote makes the pattern exclude the only value that exists, so both read `0` where 8 and 4
traces are present. `import-and-staged-actions/05` counts `"totalCount"` in a `/quotes/search`
response, and that endpoint returns `totalMatching`; the count is empty forever, so its resurrection
check silently asserts nothing.

**Does it match case the way the assertion needs?** `Select-String` is case-**in**sensitive by default,
and `-eq` on a string is too. A check for a case-variant duplicate written without thinking about it
matches the correctly-cased row and reports a duplicate that does not exist — found during #339's own
run, against `import-and-staged-actions/12`'s `AIRPLANE!` fixture. Where casing *is* the subject, use
`Select-String -CaseSensitive` or `-ceq`, and say at the command that the casing is the point.

**And the expected number itself must be derived in the same run, never predicted.** That rule is
stated above; these three are about the instrument rather than the expectation, and a document can get
the expectation right while the instrument makes it unreachable.

**Stable resources are the exception, not the default.** A fixture owned by a test — created from
scratch, or captured from a real database at the moment a bug is discovered — exists only where a
specific feature or issue **cannot be tested any other way**. Do not build one because a count looks
fragile: the bundled sources are what actually ships, and a test against them is testing the real
thing.

Two situations meet that bar. A feature whose condition cannot be reached through the application at
all — the state has to be constructed. And a test whose precondition comes from a path that can itself
break, where a fixture is what keeps the test runnable while that path is being fixed (see *Depending
on content is not the same as depending on another test* above).

### A test that needs a defective input must own that input

**Never let a test's ability to fail depend on shipped data happening to be wrong.** Shipped data gets
fixed — that is the point of shipping it — and when it is, the test stops being able to fail without
stopping being green. It keeps reporting coverage it no longer provides, and nothing announces the
change.

This is not hypothetical. Two documents were written against genuine defects found in the bundled
files: a conflict rule whose recorded snapshot used a straight apostrophe where the data had a curly
one, and source aliases flagged as stale. Both defects were then fixed, correctly. Both tests now
assert that a list comes back empty — which is equally true when the mechanism under test is entirely
broken, when the reseed silently failed, when the status filter regressed, and when the rows were
purged. The subject of each test is a *mechanism*; its input was *production data*; and the two have
different lifetimes.

**The tell is an assertion that something is absent, with nothing in the run proving the mechanism was
ever alive.** A test that can only observe "nothing happened" needs a positive control — a case where
something *does* happen, produced by the test itself, in the same run.

Three ways to own the input, best first:

1. **Drive the application into the state through its own mechanisms.** A registered rule-file override,
   an import, a recorded decision. Nothing outside the test changes, and restoring the profile clears
   it. Staleness, for instance, is reachable this way end to end: decide a batch, `generate` a rule
   recording that snapshot, change the underlying value, re-plan — the snapshot no longer matches.
2. **Generate the defective input at run time** — a fixture the test writes and deletes, as most import
   documents already do.
3. **Keep a captured copy** from the moment the bug was found, stored beside the document. Never in
   `data/sources/`, which ships.

**The anti-pattern: mutating a bundled file and rebuilding the image.** It does reproduce the state,
but it bakes the mutation into a shared image tag, and every sibling test running that tag is then
running data that does not exist in the repository. Found live in `import-and-staged-actions/15`, whose
cleanup now has to rebuild the image to undo it. Prefer option 1, which needs no rebuild at all.

### A removed or added feature needs its own proof, alongside the normal behaviour

**When a change removes something or adds something, the suite proves both that the change happened
and that ordinary behaviour still holds.** Those are two different claims, and a document asserting
only one of them reports coverage it does not have.

**A negative assertion needs a positive control in the same run, using the same instrument.** "The old
name is gone" and "my pattern never matches anything" produce identical output, and only a case where
the same instrument *does* match separates them. `api-surface/04` is the worked example: it checks that
two renamed `operationId`s are present and the two old ones absent, but every pattern in it misses a
space against the pretty-printed spec. The present-check fails loudly, so it would be noticed. The
absent-check passes silently, and would pass just as confidently with both old names still in the
spec — which is the half a breaking rename most needs proven.

`import-and-staged-actions/19` shows the honest shape of a removal check: it counts occurrences of the
three removed field names and requires `0`. What it still lacks is the control — nothing in that run
establishes the pattern could have matched, so a typo in it is indistinguishable from the fields being
gone. `import-and-staged-actions/01` gets it for free, because a `404` from the withdrawn
`/import/conflicts` route is a positive observation of an absence.

**An added feature is proven by exercising it, not by the absence of a complaint.** A startup that logs
no error is not evidence a new producer ran; assert the thing it produces.

**And in both directions, the regular behaviour is still asserted.** A removal test that only proves
the old path is gone says nothing about the path that replaced it — see
`notifications-and-changelog/02`, where proving the old text-matching suppression is dead requires
*also* showing that structural dedupe still suppresses a genuine duplicate. One without the other is
half the feature.

### A document that provokes a fault ends by proving the remedy works

The rule above applies to every document that sabotages something, and it is the one most easily
missed: a document that only ever asserts a refusal **passes against a build that refuses everything**.

So a document ends by removing its own sabotage and confirming success in the same environment. That
step is the positive control *and* the proof that the remedy the failure message names actually resolves
the condition — one step, two jobs. See `backup/01`–`04`, where it is delete the unreadable file, give
the volume room, and remount the directory writable, each followed by a `200`.

Found live in #348: all four of that category's documents were green, and all four would have stayed
green against a build that refused every reset, because not one contained a passing case. The general
form of the rule is in [`docs/testing-policy.md`](../testing-policy.md)'s *Every test proves the
positive result as well as the negative*.
### Import behaviour is proven at every origin, not just the convenient one

Content reaches the database by three routes, and `FileResourceOrigin` names them because they are not
the same code path:

| Origin | Where it comes from | How a test installs a file there |
|---|---|---|
| `System` | The bundled sources folder inside the image | Only by rebuilding the image, or by a registered override |
| `User` | `{dataDir}/imports/` — inside the volume | `docker cp` into the volume, or a bind mount, before the container starts |
| `Upload` | `POST /import` / `POST /import/preview` | One `http.csx --file` call — no file placement at all |

**Only one of the three needs an image step.** A defective user-import file goes into the volume with
no rebuild, and an upload needs nothing but a request. Reach for a rebuild only when the behaviour under
test is genuinely specific to the bundled folder.

**Proving a behaviour at one origin does not prove it at the others**, and the application already says
so: `AutoPurgeBundledImportActions` and `AutoPurgeUserImportActions` are deliberately separate settings.
Where the paths are meant to agree, that agreement is a claim needing its own test; where they are meant
to differ, the difference is intentional and the test states which it is. A parity test that treats every
divergence as a bug is as wrong as no parity test at all.

**Current state, recorded rather than assumed: `User` has no coverage anywhere in this suite.** Every
document reaches the database through `Upload` or through the bundled seed. The `{dataDir}/imports/`
folder — a real, documented, separately-configured path — has never been exercised here. Tracked as
[#346](https://github.com/DutchJaFO/Quotinator/issues/346); it is new test content, not a gap this
restructure could close by moving something.

**The id-casing guarantees are the sharpest instance.** Every `identity-and-casing/` document proves
capture-time canonicalization and either-casing lookup by `POST /import` — one origin of three. When
adding a test that turns on an id's or a natural key's casing, ask which origin it establishes the
behaviour for, and say so in the document rather than letting a single-origin result read as a
system-wide guarantee.

**Never assert a total count of notifications.** The same failure mode, and it has already produced
two wrong expectations. How many notifications exist depends on which producers exist and what the
bundled changelog flags for the running version, both of which move every milestone. Assert instead
that the notification a **known cause** produces is present — a successful import, a failed import, an
upgrade with notification-flagged highlights, a schema-version overshoot. The subject is the *cause*,
not the notification row as an object. Where a scenario genuinely is about counting, count occurrences
of that specific notification, never the total.

**A test must not depend on which other tests ran, or in what order.** The only order that matters is
the sequence of steps *inside* a test. Anything a test needs, it establishes itself — the rows it pages
through, the batch it applies, the state it corrupts. A test that inherits its preconditions from a
predecessor reports a false result the moment that predecessor is skipped, reordered, fails, or is
deleted, and it cannot be run alone to investigate anything.

This applies to what a test *leaves behind* as much as what it needs. Registered overrides, staged
batches and applied migrations are state the next test did not ask for. Clean up, or scope the test to
its own container and volume.

**Prose like "run the import tests first" or "the tests that follow" is the symptom, not the fix.**
Turning such a sentence into a link makes the dependency official rather than removing it — the fix is
always to give the test its own setup.

### Depending on content is not the same as depending on another test

Some tests need a database with content before they mean anything — the pagination contract needs rows
to page through, the masterdata reads need records to read. That is a legitimate precondition, and it
is not what the rule above forbids. What it forbids is *how* the precondition gets satisfied: by
whatever an earlier test happened to leave behind.

A precondition has exactly two honest resolutions, and a document states which one it uses:

1. **Guarantee it.** The test establishes the content itself — its own import, its own fixture, its own
   container. It then runs anywhere, in any order, including alone.
2. **Accept that it can be blocked.** If the content comes from the application's own import path, then
   a broken import blocks this test too, and it cannot run until that is fixed. Say so, and name what
   would block it, so a skipped run reads as a known consequence rather than an unexplained gap.

**Option 2 is why a prepared resource can be worth building.** A test whose precondition depends on a
feature that is currently broken is a test you lose exactly when you most want to run it — the
bundled-import path failing takes the endpoint tests down with it, even though the endpoints may be
perfectly fine. A fixture that does not go through that path keeps them runnable. That is a specific
feature this cannot otherwise be tested around, which is the bar the exception rule below sets.

**Prefer verification that needs no live environment.** A live test costs a container start, a human
to read the output, and a judgement call about whether the output was right. A test that runs in
`dotnet test` costs none of those and runs on every build, so anything provable in-process should be
proven there — leaving the live tier to cover only what genuinely cannot be reached without a real
container, volume or network.

This is not theoretical. Three checks have already made that move, and each got stronger for it:

- A `curl | grep` of the published OpenAPI spec became `OpenApiSpecEndpointTests`, which fetches
  `/openapi/v1.json` through the full pipeline and asserts the type via `JsonDocument`. The original
  command was wrong on its first outing — it assumed single-line JSON and matched nothing.
- A sixteen-minute wait proving the changelog database survived became
  `ChangelogDatabaseWiringTests`, asserting the DI registration is not an in-memory connection string.
  Instant, and it cannot pass for the wrong reason.
- #326's degraded-startup contract became `StartupResilienceTests`, which reaches the same states via
  `WebApplicationFactory` using deterministic sabotage.

**When a live test is written, ask what part of it needs to be live.** Often the answer is a smaller
part than the whole document, and the rest belongs in a unit or integration test that runs
continuously rather than once per release.

**Cover both the happy flow and the unhappy flow.** A test that only proves the good path proves half
a feature. What an operation does when its input is wrong, its precondition is absent, or its
dependency is unavailable is behaviour the application ships, and it is where the defects that reach
users actually live — a bare `400` where a `422` with a reason belongs, a write that silently matches
nothing, a page that 500s instead of degrading.

**A failure must be reproducible, or its consequences cannot be recorded.** This is why the unhappy
flow needs the same `Preconditions` and `Determinism` discipline as the happy one, and often more: an
error state reached by luck tells you nothing about what the application does in it. Pin how the
failure is provoked, then write down what it actually produces.

**The minimum expected consequence of any failure is a stable application.** Whatever else a wrong
input or a broken dependency causes, the process stays up, the surfaces stay reachable, and the
operator is told something they can act on. A test's unhappy flow states that outcome explicitly rather
than stopping at the error code — see the never-crash rule below, which is the same requirement stated
from the other side.

**The application must never crash.** The worst acceptable outcome of any startup problem is a
degraded UX plus an OpenAPI surface that still allows recovery. A test that provokes a startup problem
is therefore testing a *feature* — the degradation path — not merely reproducing a historical
incident. An unhandled exception, a container that exits, or a page returning 500 is a failure of that
feature regardless of what caused it.

**Wait for a condition, never for a duration.** A fixed `sleep` encodes a guess about how long a
machine takes, and it is wrong in both directions: too short and the test fails on a slower machine
for a reason that has nothing to do with what it verifies; too long and every run pays for the worst
case. Poll for the state the test actually depends on.

**`create` and `reenter` already wait**, so most documents never write a wait at all. These are for the
moments they cannot cover — after a `docker restart` or `docker start` a document issues itself.

Three canonical waits, because tests do not all wait for the same thing:

```powershell
# Wait until the app is healthy — the normal case.
dotnet script scripts/testing/http.csx -- --url "http://localhost:PORT/api/v1/health" --wait-for 200 --status
```

```powershell
# Wait until the app answers 503 — for a degraded scenario, where that IS the expected outcome and
# waiting for 200 would spend the whole timeout before failing for the wrong reason.
dotnet script scripts/testing/http.csx -- --url "http://localhost:PORT/api/v1/health" --wait-for 503 --status
```

```powershell
# Wait on the log — for a container with no published port, where neither HTTP form is available.
while (-not (docker logs qt-example 2>&1 | Select-String -SimpleMatch 'Quotinator ready')) { Start-Sleep 1 }
```

**Both HTTP forms give up.** `--wait-timeout` defaults to 300 seconds and a condition that never
arrives exits non-zero, so a wrong expectation fails the step instead of hanging the run. The log form
does not — bound it yourself if what it waits for might never appear.

A wait that is genuinely for elapsed time — a TTL expiring, a refresh interval passing, confirming a
container *stayed* dead rather than became ready — is not a readiness wait and keeps its `sleep`. It
must say so in `Determinism`, naming what the duration is measuring. An unexplained `sleep` is a
guess, and the rule above applies to it.

**This list only grows.** When a pass surfaces a new bug or edge case, add its verification here in
the same commit that fixes it.

**Refer to a test by what it verifies, never by its number.** Numbers are an index within a category
and shift when one is inserted. Cross-references between tests are links, not prose pointing at a
number.

**The same applies inside a document.** An `Expected output` bullet names the endpoint or the command it
describes — never "the first call", "the second one", or "the call above". A `Steps` section is several
code blocks holding many calls, so a positional reference makes the reader count them to
find out what is being claimed, and reads as a contradiction the moment neighbouring bullets state
different status codes for different endpoints. Found exactly that way: `200`, `404` and `202` listed
in consecutive bullets, correct for three separate endpoints, and unreadable as anything but a conflict
until each was named.

### Every test must be able to run unattended

**This folder is `automated-testing`, and a step that stops for a person is a defect in the test, not a
property of what it verifies** (developer direction, 2026-08-23). A run that needs someone watching
cannot be scheduled, cannot be repeated cheaply, and cannot be trusted to have been done the same way
twice.

Three shapes currently break this, and each has an answer:

- **"Visit this page and look at it."** Browser-driven checks are automatable — a driver can load the
  page, read the rendered DOM and capture the screenshot. `api-surface/04`'s Scalar spot-check,
  `notifications-and-changelog/01`'s rendered pages and Action-button flow, and
  `startup-and-degradation/05`'s three Blazor pages are all this shape.
- **"Read the response and see that X is there."** An assertion a human evaluates is one nothing
  records. Count it, match it, diff it — as `19` already does for its removed fields, precisely because
  reading an absence by eye cannot fail.
- **"Take a screenshot."** Worth keeping as evidence, but the *assertion* alongside it has to be
  machine-checkable, or the screenshot is the only record and nothing compares it to anything.

**A step that genuinely cannot be automated is a finding, not an exemption** — say what blocks it, in
the document, so it reads as known rather than as an oversight.

#### Every command is PowerShell

**This project's shell is PowerShell, and so is this suite.** Not a style preference — three reasons,
each of which cost something before the rule existed:

- **[ADR 010](../architecture-decisions/010-repository-is-csharp-only.md) already forbids the
  alternative.** *"No Python, Perl, Node.js, or Unix text-processing one-liners (`sed`, `awk`, etc.)
  anywhere in this repository or its tooling"*, and *"PowerShell remains the primary shell"*. A
  committed `grep -o '"batchId":"[^"]*"' | cut -d'"' -f4` is precisely that shape.
- **Bash produced two false defect reports during the 2026-08-25 full run.** Its path conversion mounted
  a directory inside the Docker VM where `dotnet script` mounted the Windows one, and an unprotected
  `-e Quotinator__DataDir=/data` was rewritten to `C:/Program Files/Git/data`.
- **String-matching a response is where this suite's instrument bugs come from.** `grep -c` counts
  lines, not matches; a pattern misses a space against a pretty-printed spec; a check for a case variant
  matches the correctly-cased row. Parse the response into an object and the whole class disappears —
  which is why the rule below is *assert on a property*, not *translate the `grep`*.

**Windows PowerShell 5.1 is the target.** `pwsh` is not installed on the development machine, and a test
suite may not impose a shell upgrade as a prerequisite. Two consequences are measured, not assumed:

| Written in PowerShell 5.1 | What a native exe actually receives |
|---|---|
| `'{"quoteText":{"choice":"keep"}}'` | `{quoteText:{choice:keep}}` — the JSON is destroyed |
| `'{\"quoteText\":\"keep\"}'` | `{"quoteText":"keep"}` — correct, and unreadable |

So **no JSON is ever passed to a native process as an argument**, and `Invoke-RestMethod` has no `-Form`
before PowerShell 7, so **multipart upload has no cmdlet path at all**. Both are why
[`scripts/testing/http.csx`](../../scripts/testing/http.csx) exists.

**A here-string does not escape this.** `@'…'@` is literal to PowerShell, but the stripping happens
when the value is handed to the process, not when it is parsed — so SQL carrying a JSON literal loses
its quotes the same way, and lands in the database as corrupt data rather than failing. Where a fixture's
SQL contains a double quote, write it to a file and use
[`execute-sql.csx`](../../scripts/testing/execute-sql.csx)'s `--sql-file`. SQL with no double quote in
it — every string single-quoted, as most fixtures are — passes safely through `--sql`.

**Five idioms cover the whole suite. Use these, and say why at the command if you depart from them.**

**Read JSON and assert on it** — a cmdlet, so nothing parses the URL on the way, and the result is an
object rather than a line of text:

```powershell
$page = Invoke-RestMethod "http://localhost:PORT/api/v1/quotes?pageSize=0"
$page.totalCount
$page.items | Where-Object { $_.type -eq 'movie' } | Measure-Object | Select-Object -ExpandProperty Count
```

**Send a JSON body** — `-Body` is a cmdlet parameter, so the JSON survives:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:PORT/api/v1/import/actions/$id/decide" `
  -Headers @{'X-Api-Key' = 'smoketest'} -ContentType 'application/json' `
  -Body '{"quoteText":{"choice":"keep"}}'
```

**Upload a file, expect a non-2xx status, or wait for one** — the helper. `--expect` exits non-zero on a
mismatch, which is what stops a run at the step that failed rather than three steps later:

```powershell
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:PORT/api/v1/import" `
  --file data/sources/quotinator-curated.json --duplicate-resolution review --expect 202
```

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:PORT/api/v1/quotes?page=0" --expect 422
```

The body goes to stdout and nothing else does, so it pipes straight into `ConvertFrom-Json`; the request
line and the status go to stderr, where a reader sees them and a pipeline does not.

**Wait for a condition, never a duration** — and unlike the `until … done` loop this replaces, it gives
up rather than hanging (one such loop ran ten minutes before being stopped by hand):

```powershell
dotnet script scripts/testing/http.csx -- --url "http://localhost:PORT/api/v1/health" --wait-for 200 --status
```

**Read the container log** — `Select-String`, and where a *count* is the assertion, count occurrences
rather than matching lines:

```powershell
docker logs qt-example 2>&1 | Select-String -SimpleMatch 'Quotinator ready'
([regex]::Matches((docker logs qt-example 2>&1 | Out-String), 'Seeded')).Count
```

`Select-String` is case-**in**sensitive by default, where `grep` was not. Where casing is the subject of
the assertion — a test about a case-variant duplicate, for instance — pass `-CaseSensitive` and say at
the command that the casing is the point.

#### Capture ids into variables, never into `<placeholders>`

A step reading `…/apply?batchId=<batchId>` cannot run: something has to read the previous response and
paste the value in, and that something is a person. Capture it instead, in the same block that produced
it — and note that none of this needs a text-extraction step, because the response is already an object:

```powershell
$batchId = (dotnet script scripts/testing/http.csx -- --method POST `
              --url "http://localhost:PORT/api/v1/import" `
              --file data/sources/quotinator-curated.json --duplicate-resolution review `
            | ConvertFrom-Json).batchId
$batchId
```

```powershell
# the first pending action in that batch
$actionId = (Invoke-RestMethod "http://localhost:PORT/api/v1/import/actions?status=pending&batchId=$batchId&pageSize=0").items[0].id
```

```powershell
# every pending action in that batch, decided in turn
foreach ($id in (Invoke-RestMethod "http://localhost:PORT/api/v1/import/actions?status=pending&batchId=$batchId&pageSize=0").items.id) {
  Invoke-RestMethod -Method Post -Uri "http://localhost:PORT/api/v1/import/actions/$id/decide" `
    -Headers @{'X-Api-Key' = 'smoketest'} -ContentType 'application/json' `
    -Body '{"quoteText":{"choice":"keep"}}' | Out-Null
}
```

**Echo the captured value** — the bare `$batchId` line above. An empty variable produces a request to
`…?batchId=` and a confusing error several steps later; echoing it turns that into an immediately
visible blank.

**`pageSize=0` on any listing a loop reads from**, or the default page of 20 silently truncates the
set — the curated file stages more than that.

A value a document genuinely cannot derive — one a *person* chooses, such as which of several rows to
corrupt — stays explicit, and the step says how to choose it.

---

## Environment profiles

**A test declares the environment it needs and establishes it. It never inherits one.**

This section exists because of a measured failure. The single file this folder replaces opened with a
*Baseline* section that started the container, published the port, set the admin key and let the
first boot seed — once, for every section after it. Read top to bottom that worked. Split into one
document per test it did not: 21 of 43 documents were left driving `localhost:8080` with nothing to
answer them, most sending an admin key nothing set.

A named profile a test *invokes* is not the same thing as a predecessor a test *follows*. It is the
first of the two honest resolutions above — guarantee the precondition — written once instead of
forty-three times.

### The three profiles

| Profile | What it establishes |
|---|---|
| **Fresh** | New volume, first boot, bundled seed, nothing else |
| **Constrained** | Fresh, then one deliberate defect — read-only root, dropped table, no writable path |
| **Upgraded** | A prior image ran against this volume first, then the current build |

**Content is a separate axis and stays the test's own responsibility.** A test needing a populated
database is Fresh plus an import step it already owns — not a fourth profile. Shared *setup* is what
these profiles provide; shared *content* is what the independence rule forbids, and the distinction is
the whole reason this is safe.

**Constrained is a layer, not a third base.** It is always applied *on top of* a base — usually Fresh,
sometimes Upgraded, when the defect is only reachable in a database that an older build wrote. A
document whose environment is both writes both, base first:

```markdown
**Environment:** Upgraded + Constrained
```

#### Fresh

**A profile is a recipe, not a shared instance.** Each test creates its own container and its own
volume, named after itself, and destroys both when it is done — two lines, with its own name and port:

```powershell
dotnet script scripts/testing/test-env.csx -- create --name <name> --port <port>
```

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name <name>
```

**The recipe lives in the script, not in forty-three copies of it.** Spelled out per document, changing
how a test environment is built — one more environment variable, a different readiness condition —
would mean editing every document that has one. The script is the single place that changes, and it
**echoes every `docker` command before running it**, so a reader following a document still sees
exactly what executed without opening it.

Options exist for the cases that genuinely differ, and a document passes only what makes it different:

| Option | For |
|---|---|
| `--env K=V` | An extra setting, repeatable — `--env Quotinator__LogRequests=true` |
| `--image <ref>` | A prior published release, for an Upgraded test |
| `--bind <dir>` | A directory instead of a named volume, where something outside the container must read or edit the database file |
| `--wait-listening` | A degraded scenario where `503` is the expected outcome, so waiting for healthy would hang |
| `--no-wait` | A container that should not be waited on before the next step |
| `--read-only` | A read-only root filesystem, for a test whose subject is what happens when the application cannot write |

**`--port` itself is optional.** A container nothing connects to over HTTP — one waited on by its own
log line — publishes none, and omitting the flag is how a document says so. Requiring one would force
it to invent a number it never uses, which then contradicts its own `Determinism`.

**`create` starts from an empty volume; `reenter` runs the same recipe against data that is already
there.** A second startup, or an upgrade to a different `--image` over a database a prior one wrote,
uses `reenter` — the container is replaced, the data is not:

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name <name> --port <port> --image quotinator:local
```

They are two commands rather than a flag on one because a step doing the second thing should say so.
That matters most with `--bind`, where the script cannot tell them apart anyway: a bind directory
belongs to the document, and neither command touches it.

**No document writes its own `docker run`.** Nine did, before `reenter` existed, and five of those nine
needed nothing more than `--bind` to begin with. Each carried a `MSYS_NO_PATHCONV=1` that existed only
because the block was bash — and that flag being *absent* once rewrote `-e Quotinator__DataDir=/data`
into `C:/Program Files/Git/data` and produced a false defect report.

**The admin key is `smoketest`**, set by the script. Documents use it literally rather than carrying a
`<your admin key>` placeholder for a reader to resolve.

**A bind directory is the document's to create and remove, and `--bind` is passed to `docker`
verbatim.** Three things resolve `/tmp/x` differently, measured on Docker Desktop for Windows
2026-08-23:

| Resolver | `/tmp/x` becomes |
|---|---|
| Docker Desktop `-v` | `C:\Users\<user>\AppData\Local\Temp\x` |
| .NET `Path.GetFullPath` | `C:\tmp\x` — a different directory, which does not exist |

Resolving the path inside the script would bind `C:\tmp\x`, and the test would then read an empty
database — which looks exactly like a passing check. That is also why a host-side `DbInspector` call
cannot read a bind-mounted database by its `/tmp/…` path: it is not that the path lives inside a VM, it
is that .NET roots it at the current drive.

**A PowerShell path is the unambiguous form**, and it is what a document should prefer: `--bind
"$env:TEMP\qt-example"` names one directory on one filesystem, with nothing translating it on the way.

**Building the image is the one genuinely shared step**, because it is the same image for every test
and rebuilding it per test would be absurd:

```powershell
docker build -f docker/Dockerfile -t quotinator:local .
```

**Owning its own container is what makes a test independent, and it is not optional.** A suite sharing
one container is sequential by construction: every test inherits whatever the last one left, and the
only way to keep that honest is a restore step between each pair — which is coupling wearing a cleanup
label. With per-test containers there is nothing to restore, because there is nothing shared.

**In principle every test could then run at the same time.** In practice the machine will not have the
resources for all of them at once, but nothing in the suite's design prevents it — and running several
at a time is what makes an end-of-issue T2 pass quick rather than a serial slog. That is only true
while no two tests can reach each other's state, which is why the rule is a necessity rather than a
preference.

**A test's own port is derived from where the document lives**, so two tests can never collide and no
central allocation table has to be maintained:

```
18 <category ordinal> <test number>
```

| Category | Ports |
|---|---|
| `api-surface/` | `1810x` |
| `identity-and-casing/` | `1820x` |
| `database-lifecycle/` | `1830x` |
| `startup-and-degradation/` | `1840x` |
| `notifications-and-changelog/` | `1850x` |
| `import-and-staged-actions/` | `186xx` |

So `api-surface/02` publishes `18102:8080` and `import-and-staged-actions/14` publishes `18614:8080`.

**A document needing more than one container raises the leading `18` to `19`, then `20`** — never
appends a digit. `api-surface/03` runs two containers on `18103` and `19103`; `database-lifecycle/02`
runs three, on `18302`, `19302` and `20302`. Appending would produce a six-digit number, and **the
maximum TCP port is 65535**, so `181031` is not a port at all — `docker run -p 181031:8080` simply
fails. The highest number this scheme can reach is `20619`.

**The container port stays `8080`** — only the host side varies. A document mapping the ingress port
instead says so and why, as `database-lifecycle/02` does with `8099`.

**Fresh always builds from the working tree, never a published tag** — that is the point of running it
at all, and a published tag would test something already shipped. The milestone base image is the
*prior* side of Upgraded, not a substitute for this build.

**The database lives at `/data/quotinatordata.db` inside the container**, because the profile sets
`Quotinator__DataDir=/data` and mounts the volume there — matching how the Home Assistant add-on runs,
and giving snapshot/restore a volume to work with. A `docker cp` must target `/data/`, not
`/app/data/`, which holds only the image's bundled source files. Copy the `-wal` and `-shm` sidecars
alongside it, from a stopped container.

**Every profile pins `Quotinator__AutoPurgeBundledImportActions` explicitly, and Fresh pins it to the
application's own default, `true`.** Pinned rather than left unset, because it is the variable three
documents silently depend on: with purging on, the bundled batches' `Import_Action` rows are removed
straight after a successful seed, so a test concluding anything from an empty action list cannot tell
its own result apart from the purge.

**A test needing those rows to survive declares `false` as its own delta** — it does not get it from
the profile. A profile's job is to be what a user actually runs; a test needing something else says so
where a reader can see it. `database-lifecycle/02` already works this way, running one container on the
default and a second on `false` precisely to compare them.

**`--name` is mandatory**, on this and on every `docker run` in the suite. Without it, every later
`docker cp` and `docker logs` is written against a `<container>` placeholder no reader can resolve.

**Never `docker run` in the foreground ahead of later steps.** `docker run --rm` without `-d` holds the
terminal, and every command after it in the block is unreachable — the previous suite's own baseline
had this defect, which is part of why nothing after it could run.

#### Invoking a profile

The `Environment:` field names which recipe a test uses; the test's own first step instantiates it,
with its own container name, volume and port written out. That is deliberately not a link back to this
section: a document has to be runnable on its own, and a step saying "go and read the index" is not a
command.

**What this section prevents is drift in the *shape*, not duplication of the lines.** Every Fresh
container mounts a volume at `/data`, sets `DataDir`, pins the auto-purge default, runs detached with
`--name`, and waits on a readiness poll. A document departing from any of that says why, in
`Preconditions`, as a delta.

**A document needing more than the profile states the difference in `Preconditions`, as a delta** — an
extra environment variable, a second container, a base image other than the default. Two rules follow:

- **State only the difference.** "A running container with an admin key" is the profile's job, and
  repeating it is how a document ends up looking self-sufficient while establishing nothing.
- **A required environment variable is confirmed, not assumed.** A document requiring, say,
  `Quotinator__LogRequests=true` and then reading the log for request lines has no way to tell "the
  behaviour is broken" from "the variable was never set" — absence looks identical either way. Assert
  something positive that proves the setting took effect before relying on it.

A document that runs several containers — an upgrade, a with-and-without comparison — names each after
itself and says which is which.

**Cleanup is the other half of owning your environment.** A test removes its own container and its own
volume, always. There is no "restore the profile for the next test", because no next test is looking at
it — that instruction only made sense while one container was shared, and it is the coupling this rule
removes.

#### Constrained

Fresh, then exactly one deliberate defect, named in `Determinism`. Two kinds, and they cost different
amounts:

- **A flag** — `--read-only`, an unwritable mount, a removed env var. Free and exactly reproducible;
  nothing to snapshot.
- **A state** — a dropped table, a corrupted file, a rolled-back version counter. Worth capturing as a
  database backup, because reconstructing it by hand is where irreproducibility creeps in.

#### Upgraded

Run the prior image against a fresh volume, let it finish its own first boot, stop it, then run the
current build against the same volume. Which prior image depends on what the test is about: the
milestone base image for "does this milestone's schema change upgrade cleanly", a published tag for
"does the upgrade our users will actually perform work".

The prior boot must be waited on by **the state the test is about**, never by a duration. A fixed wait
here has already let a defect through: a 45-second check read zero notifications and looked like proof
none were written, when seeding simply had not finished.

### Snapshot and restore

A group of tests sharing a profile should not pay for a rebuild and a reseed each. Capture the
environment once, restore it between tests.

**Image.** Tag the milestone's base image and save it once:

```powershell
docker tag quotinator:local quotinator:m<N>-base
docker save -o .claude/temp/test-environments/quotinator-m<N>-base.tar quotinator:m<N>-base
```

Tests run against the pinned tag. A test that must rebuild — proving a bundled file ships inside the
image, for instance — builds its own throwaway tag and never overwrites the base. This is not
hypothetical tidiness: one document edits a bundled rule file, rebuilds `quotinator:local`, and never
rebuilds after reverting, leaving three sibling tests running a mutated image.

**Database.** Captured from a **stopped** container, with the `-wal` and `-shm` sidecars *if they are
still there*, or via SQLite's own `VACUUM INTO`. A copy taken from a running container can be torn
mid-write, and a torn copy fails as "no rows" — indistinguishable from the assertion it was meant to
check.

**A clean `docker stop` usually leaves no sidecars to copy, and that is the healthy case.** SQLite
checkpoints the WAL back into the main file and removes both files when the last connection closes, so
`docker cp …db-wal` then fails with *"Could not find the file"* — which happened on every such copy
during #339's full run. The `.db` is complete precisely because the stop was clean. Two consequences:
the copy must not abort an unattended run, which is why every one of them in this suite ends `|| true`;
and **their absence is not evidence of a missing write**. Where they *do* exist — after a `docker rm
-f`, a crash, or a copy taken while the container runs — they are load-bearing and must be copied,
which is the case this rule was written for.

**Restore is unconditional between tests**, never "if the test dirtied something". The moment it is
conditional, inherited state is back with a better name.

**Ordering within a group is allowed. Ordering across groups is not**, and every group must be able to
start cold. Otherwise "run the Fresh group" quietly becomes the new Baseline section.

Backups live in `.claude/temp/test-environments/` — already gitignored, and deleted once the milestone
is published. Anything longer than a couple of commands becomes a `scripts/testing/` entry per
[ADR 010](../architecture-decisions/010-repository-is-csharp-only.md) rather than growing inside a
document.

### The milestone base image is also the migration fixture

Capture it at milestone start, and the upgrade tests stop depending on a published tag existing: the
base image *is* the version this milestone upgrades from, by definition. That also removes the staleness
— a hardcoded `ghcr.io/dutchjafo/quotinator:1.8.3` only ever tests one upgrade, and stops being the
interesting one at the next release.

**State the condition when the snapshot is taken.** It is the users' upgrade path only if the milestone
branched from the released tag. That is normally true here, and it is what makes the base image
legitimate rather than merely convenient.

### Users skip versions, so one prior version is not enough

**A milestone that adds or removes anything with a database dimension must leave behind a way to
produce a database exhibiting that state, so a later milestone can still prove the upgrade from it
works** (developer direction, 2026-08-25). Testing only *previous release to current* never exercises
a longer migration chain, and a user upgrading an add-on that has sat untouched for three releases is
running exactly that chain.

**Those databases are rebuilt from published image tags, not stored** (developer decision,
2026-08-25). The tag for a released version is already durable and already reproduces that version's
database exactly, so nothing is committed and nothing has to be kept in step with the schema by hand.
Committing fixtures was considered and rejected; for the record, a VACUUMed seeded database measures
about 4.0 MB, and about 416 KB with domain content stripped to schema and version rows.

So the Upgraded profile's prior image is chosen by what is being proven, and a document says which:

- **The milestone base image** — does this milestone's own schema change upgrade cleanly.
- **The previous published tag** — does the upgrade our users are about to perform work.
- **An older published tag** — does the chain still work for someone who skipped releases. A document
  covering a migration that transforms existing data needs this one, and names the versions it claims
  to cover rather than implying all of them.

**The boundary of this approach, stated now rather than discovered later.** A published tag can only
reproduce a version that was actually released and whose image is still pullable. Two states it cannot
reach: an unreleased intermediate — which is why
[`notifications-and-changelog/03`](notifications-and-changelog/03-upgrade-from-an-intermediate-schema-version.md)
hand-builds its own with SQL rather than pulling anything — and a database exhibiting a feature removed
before it ever shipped. **If a tag a test depends on ever stops being pullable, that is the trigger to
revisit this decision, not a reason to quietly drop the test.**

**Downgrade is deliberately not covered yet** (developer direction, 2026-08-25). Migrations are
append-only, and an older build meeting a newer database is already a handled degraded state rather
than a crash: `DatabaseInitializer` sets `SchemaVersionOvershootDetected`, logs it, and surfaces it as
a notification. What a rollback should actually guarantee becomes a real question the first time a
milestone removes a feature, and the process gets reviewed then, against that concrete case. Recorded
as a decision so a later reader does not read the absence as an oversight.

### Not adopted: reset-and-reseed as a cheaper restore

`POST /api/v1/admin/database/reset` followed by `POST /api/v1/admin/database/reseed` would avoid a
container restart entirely. It is not used yet, for a reason worth recording rather than rediscovering:
since #156 Reset is a full wipe that deliberately does **not** reseed, and whether the pair reproduces
a fresh first boot is unverified. Quote content should reproduce from deterministic ids, but audit
rows, notification rows, the two schema-version counters and `Import_Action` rows plausibly do not.
Prove that equivalence before the suite rests on it.

---

## Test outcomes feed the Knowledgebase

A test creates a specific circumstance on purpose and shows what it produces. That is exactly what a
Knowledgebase entry needs — with the cause established by construction rather than inferred from a log
line, and the remedy already exercised rather than reasoned about.

The `Observed effect` field is what captures it. #333 sweeps these documents alongside its sweep of
the application's own messages, and writes entries from what they record. No entry is written here.

**The unhappy flow is the richer source.** An entry answers *what happened, does it stop the app
working, and what do I do about it* — which is a description of a failure, not of a success. A test
that provokes a failure deliberately and records what an operator would see has already done the work
an entry needs; a happy-path-only test has nothing to contribute.

---

## Where things live

Test documents sit in a category subfolder, numbered per category with a stable slug —
`api-surface/02-pagination-contract.md`. Numbering restarts in each folder, so a new test appends to
its own category and disturbs nothing else.

Fixture files, seed data, and expected-output samples a test needs go in a subfolder beside its
document. Executable scripts go to `scripts/testing/`, per
[ADR 010](../architecture-decisions/010-repository-is-csharp-only.md) — never inline in the document,
never beside it.

The five this suite runs on:

| Script | What it is for |
|---|---|
| [`test-env.csx`](../../scripts/testing/test-env.csx) | Create, re-enter and destroy a test's own container and volume |
| [`http.csx`](../../scripts/testing/http.csx) | Upload a file, expect a non-2xx status, or wait for one — the three things Windows PowerShell 5.1 cannot do cleanly |
| [`execute-sql.csx`](../../scripts/testing/execute-sql.csx) | Run SQL against a database file, to break or repair it from the host side |
| [`sqlite-storage-probe.csx`](../../scripts/testing/sqlite-storage-probe.csx) | Measure what SQLite reports about a file's storage |
| [`corrupt-csv-cell.csx`](../../scripts/testing/corrupt-csv-cell.csx) | Damage one cell of an exported CSV, so a re-import has something to reject |

---

## The tests

### `api-surface/`

| # | Test | Smoke |
|---|---|---|
| 01 | [Baseline — health, version, random and search](api-surface/01-baseline.md) | yes |
| 02 | [The pagination contract holds live on every paginated endpoint](api-surface/02-pagination-contract.md) | yes |
| 03 | [The Unicode-aware search flag reaches the running app](api-surface/03-unicode-aware-search-toggle.md) | no |
| 04 | [Endpoint names and summaries follow the standard](api-surface/04-endpoint-naming-and-operation-ids.md) | no |

### `identity-and-casing/`

| # | Test | Smoke |
|---|---|---|
| 01 | [A file-authored explicit id is canonicalized at capture](identity-and-casing/01-canonicalize-explicit-ids-at-capture.md) | no |
| 02 | [A quote resolves by id in either casing](identity-and-casing/02-quote-id-case-insensitive-lookup.md) | no |
| 03 | [A conversation line in the wrong casing does not violate the foreign key](identity-and-casing/03-conversation-line-quote-id-fk-safety.md) | no |
| 04 | [String-typed id fields render canonically over HTTP](identity-and-casing/04-read-time-presentation-of-string-typed-ids.md) | no |
| 05 | [Generic-repository endpoints return correct data and lowercase ids](identity-and-casing/05-generic-repository-select-list-wrapping.md) | no |

### `database-lifecycle/`

| # | Test | Smoke |
|---|---|---|
| 01 | [Captured source files reconstruct byte-for-byte](database-lifecycle/01-file-resource-capture-and-reconstruction.md) | no |
| 02 | [Audit export, date-range discovery, and conflict-data auto-purge](database-lifecycle/02-audit-export-and-conflict-data-purge.md) | no |
| 03 | [Reset wipes the entire database and does not reseed](database-lifecycle/03-reset-is-a-full-wipe.md) | yes |

### `startup-and-degradation/`

| # | Test | Smoke |
|---|---|---|
| 01 | [Seeding backup, degraded startup, and Reset recovery](startup-and-degradation/01-seeding-backup-degraded-startup-and-reset-recovery.md) | no |
| 02 | [Startup backs up only when there is real work](startup-and-degradation/02-startup-backup-gating-and-storage-budget.md) | no |
| 03 | [Kestrel serves a wait page during initialisation](startup-and-degradation/03-startup-wait-page.md) | yes |
| 04 | [Migration replay under restricted write](startup-and-degradation/04-migration-replay-under-restricted-write.md) | no |
| 05 | [Degraded pages survive a migration failure](startup-and-degradation/05-degraded-pages-survive-a-migration-failure.md) | no |
| 06 | [A recorded schema version ahead of the build stays healthy](startup-and-degradation/06-schema-version-ahead-of-the-application.md) | no |

### `backup/`

The safety backup every migration, seed and Reset takes first. It has its own category rather than
sitting inside `startup-and-degradation/` because it is a feature in its own right (developer decision,
2026-08-28): startup is one caller among several, and the ways a backup can fail — five of them, with
five different remedies — are the subject here rather than a detail of somebody else's scenario. There
is deliberate overlap at the edges: a read-only data directory appears in both categories, answering a
different question in each.

| # | Test | Smoke |
|---|---|---|
| 01 | [Refuses a reset when the source cannot be read](backup/01-refuses-a-reset-when-the-source-cannot-be-read.md) | no |
| 02 | [Refuses a reset when the database is truncated](backup/02-refuses-a-reset-when-the-database-is-truncated.md) | no |
| 03 | [Refuses a reset when the disk fills during the backup](backup/03-refuses-a-reset-when-the-disk-fills-during-the-backup.md) | no |
| 04 | [Refuses a reset when the backup folder cannot be written](backup/04-refuses-a-reset-when-the-backup-folder-cannot-be-written.md) | no |
| 05 | [A full backup quota is resolvable from inside the application](backup/05-a-full-quota-is-resolvable-from-inside-the-application.md) | no |

### `notifications-and-changelog/`

| # | Test | Smoke |
|---|---|---|
| 01 | [Notifications list, dismiss, render, and drive their action](notifications-and-changelog/01-notification-system.md) | yes |
| 02 | [Notification metadata, provenance, and the released-database migration path](notifications-and-changelog/02-notification-metadata-and-provenance.md) | no |
| 03 | [Upgrading from an intermediate schema version](notifications-and-changelog/03-upgrade-from-an-intermediate-schema-version.md) | no |
| 04 | [Upgrading enriches the legacy notification rather than duplicating it](notifications-and-changelog/04-upgrade-does-not-duplicate-the-legacy-notification.md) | no |
| 05 | [The legacy notification gains provenance](notifications-and-changelog/05-legacy-notification-provenance.md) | no |
| 06 | [A what's-new row predating release state is backfilled](notifications-and-changelog/06-whats-new-row-predating-release-state.md) | no |
| 07 | [The changelog is served from its own on-disk database](notifications-and-changelog/07-changelog-served-from-its-own-database.md) | yes |
| 08 | [A notification's title and body resolve to the requested language](notifications-and-changelog/08-notification-text-resolves-per-language.md) | no |
| 09 | [Upgrading translates the notification a released build already wrote](notifications-and-changelog/09-upgrade-translates-the-shipped-announcement.md) | no |
| 10 | [A reset recommends a reseed, and running it resolves the condition](notifications-and-changelog/10-reseed-recommendation-and-action.md) | no |
| 11 | [A reseed confirms each file that applied cleanly, once per result](notifications-and-changelog/11-clean-reseed-confirmation.md) | no |
| 12 | [A running notification action says so, and cannot be started twice](notifications-and-changelog/12-running-action-state.md) | no |
| 13 | [A notification renders its title and body as separate things, and keeps its line breaks](notifications-and-changelog/13-notification-layout.md) | no |

### `import-and-staged-actions/`

| # | Test | Smoke |
|---|---|---|
| 01 | [The staged review → decide → apply workflow](import-and-staged-actions/01-staged-action-review-workflow.md) | yes |
| 02 | [A batch applied through the staged flow can be reversed](import-and-staged-actions/02-two-phase-decide-apply-reversal.md) | no |
| 03 | [`POST /import?batchId=` applies an already-staged batch](import-and-staged-actions/03-batch-id-mode-alias.md) | no |
| 04 | [Discarding a staged batch applies nothing](import-and-staged-actions/04-discard.md) | no |
| 05 | [Reversing an applied batch, and re-import resurrection](import-and-staged-actions/05-reverse-and-resurrection.md) | no |
| 06 | [A bodyless import request is rejected with an actionable message](import-and-staged-actions/06-bodyless-request-validation.md) | no |
| 07 | [StageDirection and SoundCue Modify, and Complete blocking](import-and-staged-actions/07-stagedirection-soundcue-modify.md) | no |
| 08 | [Person Modify, Complete blocking, and mixed-case id reversal](import-and-staged-actions/08-person-modify-and-lowercase-id-reversal.md) | no |
| 09 | [Character↔Source links stay per-Source](import-and-staged-actions/09-character-source-many-to-many-identity.md) | no |
| 10 | [A Source discovered from a quote carries that quote's date](import-and-staged-actions/10-source-date-from-resolving-quote.md) | no |
| 11 | [A missing `batchId` is rejected, and the log reports the real status](import-and-staged-actions/11-batch-id-validation-and-request-log-status.md) | no |
| 12 | [Character Modify, explicit ids on Add, case-insensitive Source matching](import-and-staged-actions/12-character-modify-and-explicit-id-on-add.md) | no |
| 13 | [Bulk-deciding a batch via file export and re-import](import-and-staged-actions/13-bulk-decide-via-file-export-import.md) | no |
| 14 | [A fresh seed resolves every bundled file with nothing left pending](import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions.md) | yes |
| 15 | [The rule lookup reads the file's live content](import-and-staged-actions/15-rule-file-live-read-proof.md) | no |
| 16 | [A stale conflict rule stages Stale, not Decided](import-and-staged-actions/16-conflict-rule-staleness.md) | no |
| 17 | [An alias is stale only on a genuine rename](import-and-staged-actions/17-source-alias-staleness.md) | no |
| 18 | [Rule-file override endpoints and alias candidates](import-and-staged-actions/18-rule-file-override-endpoints.md) | no |
| 19 | [Every seed and import surface reports per-file counts](import-and-staged-actions/19-per-file-import-report.md) | yes |
| 20 | [A file left awaiting review raises an alert, and resolving it retires the alert](import-and-staged-actions/20-pending-review-alert.md) | no |

---

## Where the previous suite's sections went

Until #339 this suite was one file, `docs/smoke-tests.md`, whose tests were numbered `1`–`44`. Plan
docs, issue comments and code comments written before the split refer to those numbers, and this table
is what resolves them. It is a permanent key, not a migration aid — those references are historical
records and are not going to stop existing.

| Old | Now |
|---|---|
| 1 | [api-surface/01-baseline.md](api-surface/01-baseline.md) |
| 2 | [import-and-staged-actions/01-staged-action-review-workflow.md](import-and-staged-actions/01-staged-action-review-workflow.md) |
| 3 | [import-and-staged-actions/02-two-phase-decide-apply-reversal.md](import-and-staged-actions/02-two-phase-decide-apply-reversal.md) |
| 4 | [import-and-staged-actions/03-batch-id-mode-alias.md](import-and-staged-actions/03-batch-id-mode-alias.md) |
| 5 | [import-and-staged-actions/04-discard.md](import-and-staged-actions/04-discard.md) |
| 6 | [import-and-staged-actions/05-reverse-and-resurrection.md](import-and-staged-actions/05-reverse-and-resurrection.md) |
| 7 | [import-and-staged-actions/06-bodyless-request-validation.md](import-and-staged-actions/06-bodyless-request-validation.md) |
| 8 | [import-and-staged-actions/07-stagedirection-soundcue-modify.md](import-and-staged-actions/07-stagedirection-soundcue-modify.md) |
| 9 | [import-and-staged-actions/08-person-modify-and-lowercase-id-reversal.md](import-and-staged-actions/08-person-modify-and-lowercase-id-reversal.md) |
| 10 | [import-and-staged-actions/09-character-source-many-to-many-identity.md](import-and-staged-actions/09-character-source-many-to-many-identity.md) |
| 11 | [import-and-staged-actions/10-source-date-from-resolving-quote.md](import-and-staged-actions/10-source-date-from-resolving-quote.md) |
| 12 | [identity-and-casing/01-canonicalize-explicit-ids-at-capture.md](identity-and-casing/01-canonicalize-explicit-ids-at-capture.md) |
| 13 | [api-surface/02-pagination-contract.md](api-surface/02-pagination-contract.md) |
| 14 | [identity-and-casing/02-quote-id-case-insensitive-lookup.md](identity-and-casing/02-quote-id-case-insensitive-lookup.md) |
| 15 | [identity-and-casing/03-conversation-line-quote-id-fk-safety.md](identity-and-casing/03-conversation-line-quote-id-fk-safety.md) |
| 16 | Folded into [identity-and-casing/02](identity-and-casing/02-quote-id-case-insensitive-lookup.md) — it was an explanatory note, not a test |
| 17 | [identity-and-casing/04-read-time-presentation-of-string-typed-ids.md](identity-and-casing/04-read-time-presentation-of-string-typed-ids.md) |
| 18 | [identity-and-casing/05-generic-repository-select-list-wrapping.md](identity-and-casing/05-generic-repository-select-list-wrapping.md) |
| 19 | [import-and-staged-actions/11-batch-id-validation-and-request-log-status.md](import-and-staged-actions/11-batch-id-validation-and-request-log-status.md) |
| 20 | [import-and-staged-actions/12-character-modify-and-explicit-id-on-add.md](import-and-staged-actions/12-character-modify-and-explicit-id-on-add.md) |
| 21 | [import-and-staged-actions/13-bulk-decide-via-file-export-import.md](import-and-staged-actions/13-bulk-decide-via-file-export-import.md) |
| 22 | [import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions.md](import-and-staged-actions/14-fresh-seed-produces-zero-pending-actions.md) |
| 23 | [import-and-staged-actions/15-rule-file-live-read-proof.md](import-and-staged-actions/15-rule-file-live-read-proof.md) |
| 24 | [import-and-staged-actions/16-conflict-rule-staleness.md](import-and-staged-actions/16-conflict-rule-staleness.md) |
| 25 | [import-and-staged-actions/17-source-alias-staleness.md](import-and-staged-actions/17-source-alias-staleness.md) |
| 26 | [import-and-staged-actions/18-rule-file-override-endpoints.md](import-and-staged-actions/18-rule-file-override-endpoints.md) |
| 27 | [import-and-staged-actions/19-per-file-import-report.md](import-and-staged-actions/19-per-file-import-report.md) |
| 28 | [api-surface/03-unicode-aware-search-toggle.md](api-surface/03-unicode-aware-search-toggle.md) |
| 29 | [startup-and-degradation/01-seeding-backup-degraded-startup-and-reset-recovery.md](startup-and-degradation/01-seeding-backup-degraded-startup-and-reset-recovery.md) |
| 30 | [database-lifecycle/01-file-resource-capture-and-reconstruction.md](database-lifecycle/01-file-resource-capture-and-reconstruction.md) |
| 31 | [database-lifecycle/02-audit-export-and-conflict-data-purge.md](database-lifecycle/02-audit-export-and-conflict-data-purge.md) |
| 32 | [database-lifecycle/03-reset-is-a-full-wipe.md](database-lifecycle/03-reset-is-a-full-wipe.md) |
| 33 | [notifications-and-changelog/01-notification-system.md](notifications-and-changelog/01-notification-system.md) |
| 34 | [api-surface/04-endpoint-naming-and-operation-ids.md](api-surface/04-endpoint-naming-and-operation-ids.md) |
| 35 | [startup-and-degradation/02-startup-backup-gating-and-storage-budget.md](startup-and-degradation/02-startup-backup-gating-and-storage-budget.md) |
| 36 | [startup-and-degradation/03-startup-wait-page.md](startup-and-degradation/03-startup-wait-page.md) |
| 37 | [startup-and-degradation/04-migration-replay-under-restricted-write.md](startup-and-degradation/04-migration-replay-under-restricted-write.md) |
| 38 | [startup-and-degradation/05-degraded-pages-survive-a-migration-failure.md](startup-and-degradation/05-degraded-pages-survive-a-migration-failure.md) |
| 39 | [notifications-and-changelog/02-notification-metadata-and-provenance.md](notifications-and-changelog/02-notification-metadata-and-provenance.md) |
| 40 | [notifications-and-changelog/03-upgrade-from-an-intermediate-schema-version.md](notifications-and-changelog/03-upgrade-from-an-intermediate-schema-version.md) |
| 41 | [notifications-and-changelog/04-upgrade-does-not-duplicate-the-legacy-notification.md](notifications-and-changelog/04-upgrade-does-not-duplicate-the-legacy-notification.md) |
| 42 | [notifications-and-changelog/05-legacy-notification-provenance.md](notifications-and-changelog/05-legacy-notification-provenance.md) |
| 43 | [notifications-and-changelog/06-whats-new-row-predating-release-state.md](notifications-and-changelog/06-whats-new-row-predating-release-state.md) |
| 44 | [notifications-and-changelog/07-changelog-served-from-its-own-database.md](notifications-and-changelog/07-changelog-served-from-its-own-database.md) |

**This table does not license numbering a test again.** Refer to a test by what it verifies; the
numbers here exist only to resolve references written before the split.

---

## Document template

Every test document follows this shape.

```markdown
# <What this verifies>

**Smoke:** yes | no
**Environment:** Fresh | Constrained | Upgraded
**Traces to:** #NNN[, #NNN]

## Preconditions

The exact state the setup must reach before any assertion below means anything, and how that state is
confirmed — not inferred from the recipe having been followed.

The named profile covers the environment. This field states only what is true *beyond* it — the
content this test imports for itself, the defect it introduces, the prior version it upgrades from.
A document that needs nothing beyond its profile says so.

## Determinism

Every variable the outcome depends on, and how each is pinned.

## Steps

### 1. <what this step does>

```powershell
<the command>
```

**Expected:** <what a pass looks like, precisely enough to check>.
**On failure:** <what a wrong result here means, and stop — do not run step 2>.

### 2. <the next step>

…and so on, the happy flow and the unhappy flow, not one of them.

## Observed effect

What this circumstance actually produces: the log lines, health response, UI state and messages an
operator would see. Distinct from the per-step expectations, which state only what must be true for a
pass.

## Cleanup

How to return the machine to a clean state.
```

**Each step carries its own expected result, and a failed step stops the run** (developer direction,
2026-08-23). A single `Expected output` section at the end has three faults this shape removes:

- **Feedback arrives too late.** A run that went wrong at step 2 is discovered at step 9, after seven
  more commands have written state on top of the failure — and the operator now has to work out which
  observations are still meaningful.
- **It invites finishing a test that has already failed.** Naming the consequence at the step is what
  makes stopping the obvious action rather than a judgement call.
- **It forces positional references.** With expectations pooled at the end, they get written as "the
  first call", "the second one" — and the reader has to count invocations across several code
  blocks to find out what is being claimed. Found live: `200`, `404` and `202` in consecutive bullets,
  each correct for a different endpoint, unreadable as anything but a contradiction. An expectation
  written beside its command cannot have this problem.

**`On failure:` is not required on every step** — only where a wrong result means something a reader
would otherwise misread: a precondition that did not take effect, a setup that silently did nothing, an
error that looks like the assertion it was meant to test.

**Cross-cutting commentary about an expectation's shape belongs in `Determinism`, not beside a step.**
"Never assert a total here", "this count moves with the dataset", "assert the relationship rather than
the figure" — these explain what the outcome depends on, which is exactly what `Determinism` is for.
Keep the per-step expectation to what a pass looks like.

**`Preconditions` and `Determinism` are the two fields that exist because of a specific failure.** Two
sections of the file this folder replaces carried identical setup and asserted opposite outcomes — one
that the app stayed healthy, one that it degraded — and neither confirmed it had reached its own
premise. The one asserting degradation could never have passed. It went unnoticed because each section
described its setup in prose, in a single file nobody read end to end.

A test whose `Preconditions` cannot be confirmed, or whose `Determinism` cannot be pinned, is a finding
about the test. Report it rather than writing the document anyway.
