# #424: Bundled sources are not auto-downloaded by default, and are refreshed by hand instead

**Status:** In progress
**GitHub issue:** #424
**Tiers required:** T1, T2
**Depends on:** none

---

## Description

`Quotinator:AutoUpdateSources` is on by default, and its only real target, `raw.githubusercontent.com`,
is intermittently unreachable while the refresh sits in front of the application being able to serve. The
issue inverts the default: the refresh is opted into, and bundled source files are refreshed by hand.

The test suite has the same dependency. Every host `QuotinatorWebApplicationFactory` builds ran the real
startup with the refresh on, against the build output's own `data` folder, which also keeps each run's
database, backups and key ring for the next. Found 2026-09-27 during #348's verification, when two
downloads 69 seconds apart held a startup past the factory's 30-second wait. Moved into this milestone
from v1.9.0 (developer, 2026-09-27) so the test side is fixed on this branch.

## Next action

**T1 by the developer (row 14).** Every other step is done and every other row is ✅.

---

## Design

### Every test host is self-contained by default

`QuotinatorWebApplicationFactory.ConfigureWebHost` gives every host it builds three settings before any
test's own configuration, which runs after it and can override each one:

| Setting | Why |
|---|---|
| `Quotinator:AutoUpdateSources=false` | No test reaches the network unless the refresh is its subject |
| `Quotinator:IncludeDefaultSources=false` | No test reads the bundled sources at run time (`docs/testing-policy.md`) |
| `Quotinator:DataDir` = a new temporary directory | No test reads or writes the build output's `data` folder, or state an earlier run left there |

The bundled sources themselves are not in the data directory: the application reads them from its own
folder (`AppContext.BaseDirectory/data/sources`), so the build output keeps them and nothing about them
changes. Only what the application writes at run time moves.

**Nothing in the build output's data folder is needed** (developer, 2026-09-27). Whatever a real-startup
test reads is created by that startup and observable through the API; the Core and Data tests already
build their own databases. Unlike the T2 suite's shared key ring, which exists because the browser pane
carries cookies from one container to the next, no in-process host shares anything with another, so
nothing here is shared.

### The directory goes with its host

A test usually disposes the factory `WithWebHostBuilder` returned, not the one it constructed, so tying
the cleanup to the factory would leak. The directory is removed when its host stops instead: the factory
registers a callback on the host's `ApplicationStopped`, which clears SQLite's connection pools and
deletes the directory.

### The application waits for its own background work when it stops

Removing the directory is only possible once every connection into it is closed, and a connection is
closed by the work that opened it, when that work finishes (developer, 2026-09-27: close the connections
before clearing the pool, not after). Startup ran two pieces of work fire-and-forget (the changelog import
and the what's-new notification), so a host stopped while they ran reported itself stopped with a query
still open, and the work carried on against a container already disposed. Both now start through
`StartupBackgroundWork`, a hosted service whose `StopAsync` waits for them, so by the time the host
reports stopped they have finished and closed their own connections. This is a change to the application,
not to the tests: it applies to every real shutdown.

### A test that needs a schema or content starts from a prepared database

A fresh data directory holds an empty database only when nothing is placed there first (developer,
2026-09-27). `PreparedDatabase` is built once per test run in `[AssemblyInitialize]`, by a real startup
with the bundled sources and the refresh off, and copied into a host's directory before startup when the
factory is created with `preparedDatabase: true`. It holds the current schema and nothing the outside world
supplies; a test that needs content as well adds its own.

### ADRs

ADR 006 (sequential execution, global state written only in `[AssemblyInitialize]`) governs where the
prepared database is built. ADR 007 holds the new test class to CS1591. ADR 009 concerns migrations
against a released schema and does not apply: the prepared database is a fixture at the current build's
schema.

---

## Steps

### 1. Write the verification checklist
**Status:** ✅ Done

### 2. The factory's tests, red first
**Status:** ✅ Done

`QuotinatorWebApplicationFactoryTests`, one statement each, run 2026-09-27 against the factory before step
3: `EveryHost_HasSourceAutoUpdateTurnedOff` (the setting absent, so `null`), `EveryHost_HasBundledSourcesTurnedOff`,
`EveryHost_HasItsOwnDataDirectory`, `EveryHost_DataDirectoryIsNotTheBuildOutput` and
`EveryHost_DataDirectoryIsRemovedWithTheHost`, each failing on its assertion.

### 3. The factory's defaults
**Status:** ✅ Done

The three settings and the removal callback, in `QuotinatorWebApplicationFactory.ConfigureWebHost`.
`StartupResilienceTests` and `StartupBackupRefusalTests` lost their own copies of the auto-update setting,
added earlier in each class alone when the same timeout hit it; their own data directories stay, since
they need a specific one. Four of the five tests green; the removal stayed red, for the reason step 4
fixes.

### 4. The application waits for its own background work when it stops
**Status:** ✅ Done

The removal failed with the changelog database still in use. Every connection the changelog code opens is
disposed where it is opened (checked: the changelog initializer, the importer's unit of work, the join
repository the reader uses), so an open file after the host stopped meant a query still running: the
what's-new producer, started fire-and-forget, reads the changelog and had no completion anything waited
on. `StartupBackgroundWork` now starts both background tasks and is awaited by the host's shutdown.

Tests, red first: `StartupBackgroundWorkTests.StopAsync_WaitsForWorkStillRunning` (against a
`StopAsync` that returned at once, today's behaviour) and
`RepositoryStructureTests.Program_StartsNoWorkItDoesNotWaitFor` (against `Program.cs`'s two bare
`Task.Run` calls), each failing on its assertion. `EveryHost_DataDirectoryIsRemovedWithTheHost` then
went green with no wait of its own on the test side.

### 5. Find every test that relied on bundled content or an earlier run
**Status:** ✅ Done

The full Api suite with the defaults in place, 2026-09-27: 1,131 passed, 8 failed. All eight were
`NotificationEndpointsTests.GetNotifications_*`, answering `500`: their host replaces the database
initializer with a no-op, yet the real action executor still reads the live import batches, from tables
that existed only because another test's startup had created them in the shared build-output folder.
Fixed by step 6. No other test depended on bundled content or an earlier run.

### 6. The prepared database
**Status:** ✅ Done

`PreparedDatabase`, built in `[AssemblyInitialize]` and deleted in `[AssemblyCleanup]` (ADR 006), and the
factory's `preparedDatabase` option. `PreparedDatabase_IsWhatTheHostStartsFrom`: red against the option
present but copying nothing (the host had no `System_AppVersion` table), then green.
`NotificationEndpointsTests` asks for it. Full Api suite: 1,140 passed, 0 failed, 0 warnings; no test
data directory and no prepared file left afterwards, and nothing written to the build output's `data`
folder.

### 7. The default inverted
**Status:** ✅ Done

Requirements 1 to 3. The default is stated once, `SourceCacheUpdater.DefaultAutoUpdateSources`, now
`false`, and read through `SourceRefreshSettings.AutoUpdateSources`, which `Program.cs` calls. Both add-on
configurations default `auto_update_sources` to `false`, and the option's description in all six
translation files says it is off by default and what turning it on does.

**Deviation from the issue's test description, recorded here.** The issue asks for
`Startup_AutoUpdateSourcesKeyAbsent_NeverAllowsNetwork` to boot the real startup with the key unset. The
test factory pins the key for every host (requirement 10), `UseSetting(key, null)` stores an empty string,
which does not convert to a boolean, and a value added with `ConfigureAppConfiguration` does not reach
`Program.cs`'s read at all (measured: an explicit `true` added that way arrived as the factory's `false`).
So the default is proven where it is decided, `SourceRefreshDefaultTests.AutoUpdateSources_KeyAbsent_IsOff`
against a configuration that genuinely lacks the key, and the wiring through the real startup by
`Startup_AutoUpdateSourcesExplicitlyTrue_AllowsNetwork` with a spy updater.

Red runs, each on its assertion: `AutoUpdateSources_KeyAbsent_IsOff` against the default still `true`;
`AddOnAutoUpdateSourcesDefault_MatchesTheApplicationDefault` against the default flipped and both
`config.yaml` files still `true`; `Startup_AutoUpdateSourcesExplicitlyTrue_AllowsNetwork` against a
resolution that ignores configuration. `SourceCacheUpdater.cs` lost every dash, its log messages included;
nothing quotes them but one Knowledgebase entry, updated in step 8. Full Api suite: 1,143 passed, 0
warnings.
### 8. Documentation
**Status:** ✅ Done

Requirements 4 to 9. `RepositoryStructureTests.SourceRefreshDocuments_SayTheRefreshIsOffByDefault` holds
six documents to a paragraph that names the setting and says it is off by default: red for all six first,
then `docs/api-endpoints.md`, the `transport-connection-cancelled-during-a-request` Knowledgebase entry,
the T2 README's profile rule, `import-and-staged-actions/14`'s refresh reasoning, `CLAUDE.md`'s Data
Sources section and `scripts/SOURCES.md` were each restated. The changelog carries a highlight, two
`changed` entries and a `fixed` entry in all three languages, and `CHANGELOG.md` is regenerated.

Every touched file is dash-free except two left for the developer: `CLAUDE.md` (340) and the T2 README
(162) carry only this issue's own dash-free edits, since cleaning either completely rewrites text well
beyond this issue. `import-and-staged-actions/14` keeps one dash, in its quote of a Core log message this
issue does not change.
### 9. Full verification
**Status:** ✅ Done, except T1 (row 14), which is the developer's

Build clean at 0 warnings; the full suite green across three consecutive `-m:1` runs, 4,393 tests passed
across 11 projects each time, 0 failed, 0 warnings.

**T2.** No document had the refresh as its subject, so row 13 named something that did not exist; this
step wrote `startup-and-degradation/07`. It needs the setting genuinely absent in one container, which the
Fresh profile's pin made impossible, so `test-env.csx` gained `--unset <K>`. Run red against `29b55805`
(the build before #424's first code change): with the setting absent it refreshed both sources, 2 refresh
lines and 6 log lines naming GitHub. Green against this build: seeded from the five bundled files with no
refresh line and no request, and with the setting on, both sources `updated`. The first canary attempt
tested nothing, since the document named no `--image`, and one earlier run found `test-env.csx` unable to
compile from a damaged dotnet-script cache: the document's own positive check (seeding ran) is what
exposed that the container never existed.

The smoke set against this build: `api-surface/01` and `02`, `import-and-staged-actions/01` and `19`,
`database-lifecycle/03`, `startup-and-degradation/03`, `notifications-and-changelog/01` (its browser steps
5, 6 and 8 driven, 6 for the first time) and `07` all green. `import-and-staged-actions/14` fails only
where it failed before #424, confirmed identical: step 4's twelve undeclared date variants, and step 5's
PowerShell 7 `??` operator, which Windows PowerShell 5.1 cannot parse.
---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | `Quotinator:AutoUpdateSources` resolves to `false` when absent | Unit test | `SourceRefreshDefaultTests.AutoUpdateSources_KeyAbsent_IsOff` (see step 7 for why not through startup) |
| 2 | ✅ | An explicit `true` still enables the refresh | Unit test | `SourceRefreshDefaultTests.Startup_AutoUpdateSourcesExplicitlyTrue_AllowsNetwork` |
| 3 | ✅ | Both add-on configurations default the option to `false`, as the code does | Unit test | `RepositoryStructureTests.AddOnAutoUpdateSourcesDefault_MatchesTheApplicationDefault` |
| 4 | ✅ | The add-on translations and every document name the new default | Unit test | `RepositoryStructureTests.SourceRefreshDocuments_SayTheRefreshIsOffByDefault`; the translations by step 7 |
| 5 | ✅ | Every test host has the refresh off | Unit test | `QuotinatorWebApplicationFactoryTests.EveryHost_HasSourceAutoUpdateTurnedOff` |
| 6 | ✅ | Every test host has the bundled sources off | Unit test | `QuotinatorWebApplicationFactoryTests.EveryHost_HasBundledSourcesTurnedOff` |
| 7 | ✅ | Every test host has its own data directory, outside the build output | Unit test | `QuotinatorWebApplicationFactoryTests.EveryHost_HasItsOwnDataDirectory` and `..._DataDirectoryIsNotTheBuildOutput` |
| 8 | ✅ | A test host's data directory is removed with it | Unit test | `QuotinatorWebApplicationFactoryTests.EveryHost_DataDirectoryIsRemovedWithTheHost` |
| 9 | ✅ | The application waits for its own background work before it reports stopped | Unit test | `StartupBackgroundWorkTests.StopAsync_WaitsForWorkStillRunning` and `RepositoryStructureTests.Program_StartsNoWorkItDoesNotWaitFor` |
| 10 | ✅ | A test that needs the schema starts from a prepared database | Unit test | `QuotinatorWebApplicationFactoryTests.PreparedDatabase_IsWhatTheHostStartsFrom` |
| 11 | ✅ | No test relies on bundled content or an earlier run's state | Unit test | Step 5: the full Api suite green with the defaults in place, each failure it found recorded and fixed |
| 12 | ✅ | Build clean and the full suite green across three `-m:1` runs | Build | Step 9: 4,393 passed in each of three runs |
| 13 | ✅ | The refresh is off by default, and turned on explicitly still works end to end | Automated (T2) | `startup-and-degradation/07`, red against `29b55805`, then green |
| 14 | ❌ | The application still starts | Live (T1) | The developer starts `Quotinator.Api` in Visual Studio after step 9 |
