# The source refresh is off unless it is turned on, and still works when it is

**Smoke:** no
**Environment:** Fresh
**Deterministic:** no
**Traces to:** #424

## Preconditions

**Beyond the profile.** Two containers of this test's own, each on a bind directory so the host can see
whether anything was downloaded:

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

**What varies:** whether GitHub answers during step 2. When it does not, the step fails, because a refresh
that could not download cannot show that it would have. That is the index's *A test that cannot be
confirmed has failed*: wait and run it again, and do not read a `failed` outcome as a pass.

**Step 1 needs the setting absent, not `false`.** The Fresh profile pins it to `false`, which would pass
step 1 whatever the default was. `--unset` leaves the pin out, and the step confirms from inside the
container that the variable is not set before it reads anything else.

**Absence alone proves nothing**, so step 2 is step 1's positive control: the same directory check that
finds nothing there finds the downloaded files here, so the check can see a refresh when there is one.

**The downloaded files are in `sources/download`.** A fresh directory holds no cached copy, so with the
refresh on there is always something to fetch.

**The response tells the two states apart.** `POST /api/v1/admin/sources/refresh` asks the same updater
startup uses, and names an outcome per source: `uptodate` with the network not allowed, `updated` when it
downloaded, `failed` when it could not. The default itself is also proven in-process by
`SourceRefreshDefaultTests.AutoUpdateSources_KeyAbsent_IsOff`, and the wiring by
`SourceRefreshDefaultTests.Startup_AutoUpdateSourcesExplicitlyTrue_AllowsNetwork`.

## Steps

### 1. With the setting absent, startup fetches nothing, and neither does a refresh

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-07 --delete C:\repos\Quotinator\.claude\temp\qt-startup-07b --delete C:\repos\Quotinator\.claude\temp\qt-startup-07-out --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-07 --create-directory C:\repos\Quotinator\.claude\temp\qt-startup-07b
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-07 --port 18407 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-07 --unset Quotinator__AutoUpdateSources
docker inspect -f "{{.Config.Env}}" qt-startup-07
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-absent C:\repos\Quotinator\.claude\temp\qt-startup-07\sources\download
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:18407/api/v1/admin/sources/refresh?force=true" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-07-out\refresh-off.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-07-out\refresh-off.json --contains results=uptodate
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-absent C:\repos\Quotinator\.claude\temp\qt-startup-07\sources\download
```

**Expected:** the environment list printed by `docker inspect` has no `Quotinator__AutoUpdateSources`, nothing
is at `sources/download` either time, and the forced refresh reads `uptodate`: even when asked, it does not
reach out.

**On failure:** the variable in the list means `--unset` did not remove the profile's pin, and the rest of
the step tests the pin rather than the default. A `sources/download` directory, or an outcome other than
`uptodate`, with the setting confirmed absent means the default is still on. Stop either way.

### 2. With the setting turned on, startup downloads every downloadable source

```
dotnet script scripts/testing/test-env.csx -- create --name qt-startup-07b --port 19407 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-startup-07b --env Quotinator__AutoUpdateSources=true
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --expect-matches C:\repos\Quotinator\.claude\temp\qt-startup-07b\sources\download "*.json" 2
dotnet script scripts/testing/http.csx -- --method POST --url "http://localhost:19407/api/v1/admin/sources/refresh?force=true" --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-startup-07-out\refresh-on.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-startup-07-out\refresh-on.json --contains results=updated
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-07b --pattern "thrown: (?!SocketException|TaskCanceledException|OperationCanceledException|HttpRequestException|IOException)" --exactly 0
dotnet script scripts/testing/http.csx -- --method POST --url http://localhost:19407/api/v1/import --file docs/automated-testing/startup-and-degradation/fixtures/malformed-import.json --duplicate-resolution review --expect 422 --status
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-07b --pattern "thrown: JsonReaderException" --at-least 1
```

**Expected:** at least two files at `sources/download`, written by startup itself before anything asked, and
the forced refresh reads `updated`. No exception of a type a network call does not explain was thrown; the
malformed upload answers `422` and its `JsonReaderException` is logged, which is the positive control that
makes that zero mean something.

**A download cut short or timed out can throw the five types excluded there**, intermittently, at no cost
to the result: see the Knowledgebase entry *A cancelled socket or transport connection*. Any other type is
an issue.

**On failure:** a `failed` outcome, or no files with a `failed` outcome, means GitHub did not answer (see
Determinism). `uptodate` with the setting explicitly on means the setting no longer reaches the refresh,
which `SourceRefreshDefaultTests.Startup_AutoUpdateSourcesExplicitlyTrue_AllowsNetwork` should have caught
first.

## Observed effect

**Measured 2026-10-31** against `quotinator:local` built from `00a9dfe2`.

With the setting absent, the container's environment did not carry it, nothing appeared at `sources/download`,
and a forced refresh read `uptodate` for both downloadable sources with still nothing downloaded. With it
turned on, startup wrote both files to `sources/download` and a forced refresh read `updated` for both, with
a `lastRefreshedAtUtc`. The refresh ran within seconds of the application starting to listen.

**Exceptions with the setting on were intermittent.** One run logged four `[Runtime - Exception]` lines and
the next, on the same steps, none. The four were not identified before that container was removed, which is
why step 2 now counts by type.

## Canary

Built from `29b55805`, the commit before #424's first code change, as `quotinator:canary424`, via
`git worktree add` and `docker build`. Step 1 must find `sources/download` populated: on that build the
refresh is on by default.

**A document that is ever run against more than one build names its image in every `create`.** The first
attempt at this canary named none, so `test-env.csx` used its default, `quotinator:local`, and the "canary"
run was the current build twice.

## Cleanup

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- log --container qt-startup-07 --pattern "\[Runtime - Exception\]" --exactly 0
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-07 --bind C:\repos\Quotinator\.claude\temp\qt-startup-07
dotnet script scripts/testing/test-env.csx -- destroy --name qt-startup-07b --bind C:\repos\Quotinator\.claude\temp\qt-startup-07b
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-startup-07 --delete C:\repos\Quotinator\.claude\temp\qt-startup-07b --delete C:\repos\Quotinator\.claude\temp\qt-startup-07-out
```

**Expected:** the `log` command exits `0`: with the setting absent nothing reaches the network, so nothing
has a reason to throw, which step 2's positive control lets this zero mean.
