# The source refresh is off unless it is turned on, and still works when it is

**Smoke:** no
**Environment:** Fresh
**Traces to:** #424

## Preconditions

**Beyond the profile.** Two containers of this test's own:

- `qt-startup-07`, publishing `18407`, created with `--unset Quotinator__AutoUpdateSources`, so the
  setting is absent and the application's own default decides;
- `qt-startup-07b`, publishing `19407`, created with `--env Quotinator__AutoUpdateSources=true`.

Since #424 the refresh is opted into rather than out of: its only upstream host is intermittently
unreachable, and it runs in front of the application being able to serve. This proves the default is
off, and that turning it on still reaches the network.

## Determinism

**This is the one document allowed to depend on the network, and only in step 2.** A test depends on
nothing outside the project unless that is what it tests (`docs/testing-policy.md`); the refresh reaching
GitHub is what step 2 tests. Step 1 depends on nothing outside: its point is that nothing is fetched.

**Step 1 needs the setting absent, not `false`.** The Fresh profile pins it to `false`, which would pass
step 1 whatever the default was. `--unset` leaves the pin out, and the step confirms from inside the
container that the variable is not set before it reads anything else.

**Step 2 accepts either outcome for each source.** Whether GitHub answers decides whether a source logs
`updated … from …` or `could not reach …; using local …`, and both prove the refresh ran. What it may not
do is log nothing: a fresh volume holds no cached copy, so the refresh always has something to fetch.

**Absence alone proves nothing**, so step 1 asserts a positive line first: the seed importing the
bundled files, which shows startup reached the point where a refresh would have run.

## Steps

### 1. With the setting absent, startup seeds the bundled files and fetches nothing

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-07 --port 18407 --image quotinator:local `
  --unset Quotinator__AutoUpdateSources
"setting inside the container: [$(docker exec qt-startup-07 printenv Quotinator__AutoUpdateSources)]"
$log = docker logs qt-startup-07 2>&1
"seed imports: $(@($log | Select-String -SimpleMatch '[Database - Seed] importing').Count)"
"refresh lines: $(@($log | Select-String -SimpleMatch '[Database - SourceRefresh]').Count)"
"requests to GitHub: $(@($log | Select-String -SimpleMatch 'raw.githubusercontent.com').Count)"
```

**Expected:** `setting inside the container: []`, `seed imports` at least `1`, `refresh lines: 0` and
`requests to GitHub: 0`.

**On failure:** a non-empty setting means `--unset` did not remove the profile's pin, and the rest of the
step tests the pin rather than the default. A refresh line or a request with the setting confirmed absent
means the default is still on. Stop either way.

### 2. With the setting turned on, startup refreshes every downloadable source

```powershell
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-07b --port 19407 --image quotinator:local `
  --env Quotinator__AutoUpdateSources=true
$log = docker logs qt-startup-07b 2>&1
$log | Select-String -SimpleMatch '[Database - SourceRefresh]' |
  ForEach-Object { ($_.Line -split '\[Database - SourceRefresh\] ')[1] }
```

**Expected:** one line for each manifest entry that declares a download URL, two for the bundled
sources, each reading `updated <file> from <url>` or `could not reach <url> (…); using local <file>`.

**On failure:** no line at all with the setting explicitly on means the setting no longer reaches the
refresh, which `SourceRefreshDefaultTests.Startup_AutoUpdateSourcesExplicitlyTrue_AllowsNetwork` should
have caught first.

## Observed effect

**Measured 2026-09-27** against `quotinator:local`. With the setting absent, the container reported it
unset, seeded from the five bundled files, and logged no refresh line and no request to GitHub. With it
turned on, both downloadable sources logged `updated "<file>" from "<url>"`. Neither container logged an
exception.

## Canary: run red against the build before #424

Built from `29b55805` (the commit before #424's first code change) as `quotinator:canary424`, via
`git worktree add` and `docker build`, 2026-09-27:

| Step | Assertion | Pre-work result |
|---|---|---|
| 1 | the setting is absent inside the container | passes: `--unset` is independent of the build |
| 1 | seeding ran | passes: `5` imports |
| 1 | no refresh line | **fails**: `2`, both sources `updated` |
| 1 | no request to GitHub | **fails**: `6` log lines naming it |
| 2 | every downloadable source logs a refresh outcome | passes: the control, unchanged by #424 |

**The first attempt at this canary tested nothing, and is recorded because the trap is general.** Both
`create` commands named no `--image`, so `test-env.csx` used its default, `quotinator:local`, and
`run-doc.ps1`'s image swap, which rewrites that literal, had nothing to rewrite: the "canary" run was the
current build twice. A document that is ever run against more than one build names its image in every
`create`.

## Cleanup

Read what each container logged, then remove both:

```powershell
foreach ($name in 'qt-startup-07', 'qt-startup-07b') {
  "${name}: thrown=$(@(docker logs $name 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]').Count)"
  dotnet script scripts/testing/test-env.csx -- destroy --name $name
}
```

**Expected:** `qt-startup-07: thrown=0`. `qt-startup-07b` may log exceptions when a download is cut
short or times out; see the Knowledgebase entry *A cancelled socket or transport connection* for why that
costs nothing.
