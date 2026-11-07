# A database is complete for its schema version, fresh or upgraded, and an extra object is reported

**Smoke:** no
**Environment:** Fresh + Upgraded
**Deterministic:** yes
**Traces to:** #305

## Preconditions

**Beyond the profiles.** Two databases, each in its own bind-mounted directory, are checked through
`GET /api/v1/admin/database/integrity`:

- **Fresh**: the one this build creates on an empty directory.
- **Upgraded**: one the **published `ghcr.io/dutchjafo/quotinator:1.8.3` tag** creates, which this build
  then upgrades. That is the upgrade a user performs, and the database is rebuilt from the published image
  rather than stored (index: *Users skip versions*).

The Fresh database is then given one column no migration defines, `System_Notification.Stray`, while its
container is stopped, so the check has something extra to find.

## Determinism

- **Both directories are new and absolute**, deleted first if an earlier run left them.
- **Both sides are waited on by state, never by a duration.** The released container publishes a port so
  its first boot is judged by its own `/health` reading `healthy` through `wait-field`; this build by
  `/health` reading `200`. The released container is then stopped cleanly before it is removed, so its
  database holds no journal for the upgrade to replay.
- **Databases are read by position**: the main database is always reported first, the changelog second.
- **The extra column is added while the container is stopped, and stopped cleanly**, so no running
  connection or leftover journal sits beside the change.
- **Never assert a migration number or schema version.** The upgrade's version numbers move whenever a
  migration is added or condensed; the check's verdict is what is asserted.

## Explicitly not covered here

- **A missing or different object, and the degraded start it causes**:
  [`startup-and-degradation/14`](../startup-and-degradation/14-a-schema-that-does-not-match-its-version-degrades-and-names-it.md).
- **That the endpoint checks when asked rather than returning the startup result**: proven in-process by
  `AdminDatabaseIntegrityEndpointTests.ExtraColumn_ReportedButIntact`, which adds the column while the
  application runs. Here the column is added while the container is stopped, by the index's rule on host
  writes, so startup's own check also sees it.
- **Older published tags than the last release**: the chain from a release a user skipped is proven per
  milestone by ADR 009's last-release verification, which runs this document's Upgraded half after the
  migrations are condensed.

## Steps

### 1. A fresh database is complete

```
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-fresh --delete C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-upgraded --delete C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-out --create-directory C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-fresh --create-directory C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-upgraded --create-directory C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-out
dotnet script scripts/testing/test-env.csx -- create --name qt-lifecycle-04-fresh --port 18434 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-fresh
dotnet script scripts/testing/http.csx -- --url http://localhost:18434/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-out\fresh.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-out\fresh.json --length-equals databases=2 --equals databases.0.database=main --equals databases.0.isIntact=true --length-equals databases.0.differences=0 --equals databases.1.database=changelog --equals databases.1.isIntact=true --length-equals databases.1.differences=0
```

**Expected:** the environment reports healthy, and the `json` command exits `0`: two databases, main and
changelog, each intact with no difference and no extra object.

**On failure:** a difference here means this build's own fresh install disagrees with its own migration
path, which the baseline-versus-replay unit tests should already have caught. Stop.

### 2. An extra column is reported, and does not degrade

```
docker stop -t 15 qt-lifecycle-04-fresh
dotnet script scripts/testing/execute-sql.csx -- --db C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-fresh\quotinatordata.db --sql "ALTER TABLE System_Notification ADD COLUMN Stray TEXT;"
dotnet script scripts/testing/test-env.csx -- reenter --name qt-lifecycle-04-fresh --port 18434 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-fresh
dotnet script scripts/testing/http.csx -- --url http://localhost:18434/api/v1/health --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18434/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-out\extra.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-out\extra.json --equals databases.0.isIntact=true --length-equals databases.0.differences=1 --equals databases.0.differences.0.kind=extra --equals databases.0.differences.0.objectKind=column --equals databases.0.differences.0.objectName=System_Notification.Stray
```

**Expected:** the restart reaches healthy, and the `json` command exits `0`: the main database is still
intact, with one difference, the column reported as extra.

**This is the positive control for step 1 and for step 3:** an instrument that reports no difference has
to be shown able to report one, or "no difference" is what a broken instrument would say too. And an extra
object means a migration may have been missed, which is worth knowing but is not a broken schema, so it
must not degrade.

**On failure:** `/health` not `200` means an extra object degraded the application. No difference means
the check does not see columns it was not told about.

### 3. A database the last release created, upgraded by this build, is complete

```
dotnet script scripts/testing/test-env.csx -- create --name qt-lifecycle-04-183 --port 18436 --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-upgraded --wait-listening
dotnet run --project tools/Quotinator.Tools.TestProbe -- wait-field --url http://localhost:18436/api/v1/health --field status --in healthy,unhealthy --out C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-out\released-health.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-out\released-health.json --equals status=healthy
docker stop -t 15 qt-lifecycle-04-183
dotnet script scripts/testing/test-env.csx -- destroy --name qt-lifecycle-04-183 --bind C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-upgraded
dotnet script scripts/testing/test-env.csx -- reenter --name qt-lifecycle-04-current --port 18435 --image quotinator:local --bind C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-upgraded
dotnet script scripts/testing/http.csx -- --url http://localhost:18435/api/v1/health --expect 200 --status
dotnet script scripts/testing/http.csx -- --url http://localhost:18435/api/v1/admin/database/integrity --expect 200 --body-out C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-out\upgraded.json
dotnet run --project tools/Quotinator.Tools.TestProbe -- json --file C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-out\upgraded.json --equals databases.0.isIntact=true --length-equals databases.0.differences=0 --equals databases.1.isIntact=true --length-equals databases.1.differences=0
```

**Expected:** the released image reads `healthy`; this build upgrades its database and reaches
healthy; and the `json` command exits `0`: both databases intact, with no difference and no extra object.

**This is ADR 009's last-release verification, measured.** A fresh install and a full replay are compared
by unit tests on every run; only this proves the path a real installation takes.

**On failure:** a difference means an installed database ends up with a different schema than a fresh
one. That cannot be fixed by editing a baseline: it needs a migration, and it is the developer's decision.

## Observed effect

Not yet measured as a whole. The Upgraded half was read by #305's step 6 on 2026-11-07, against this
build at `88795817` before this document existed: both databases intact with no difference, after 25 Data
and 4 App migrations. Listed under the index's *Observed effects awaiting measurement* until a full run
records it here.

## Canary

Build the commit before [#305](https://github.com/DutchJaFO/Quotinator/issues/305)'s first code change,
under a distinct tag, and run step 1 against it.

The integrity call must answer `404`: the endpoint does not exist there, so the document cannot pass on a
build without the check. Steps 2 and 3 depend on the same endpoint and fail the same way.

Remove the container, image and worktree afterwards.

**Where the canary has been run, 2026-11-08.** Against `95093abc`, the commit before #305's first code
change, as `quotinator:canary-305`: the fresh container reached healthy and step 1's integrity call
answered `404`.

## Cleanup

```
dotnet script scripts/testing/test-env.csx -- destroy --name qt-lifecycle-04-fresh --bind C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-fresh
dotnet script scripts/testing/test-env.csx -- destroy --name qt-lifecycle-04-current --bind C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-upgraded
dotnet run --project tools/Quotinator.Tools.TestProbe -- file --delete C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-fresh --delete C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-upgraded --delete C:\repos\Quotinator\.claude\temp\qt-lifecycle-04-out
```

**Expected:** both containers and the three directories are removed.
