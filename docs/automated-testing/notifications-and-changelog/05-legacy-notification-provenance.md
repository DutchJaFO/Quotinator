# The legacy notification gains provenance, and only a real v1.8.3 database gets a 1.8.3 row

**Smoke:** no
**Environment:** Upgraded
**Traces to:** #312

## Preconditions

**Beyond the profile.** The Upgraded prior image is the **published
`ghcr.io/dutchjafo/quotinator:1.8.3` tag**. One container name (`qt-notif-05-upgraded`) is reused
across the two runs against one bind-mounted directory. The whole thing is then repeated a second time
with no v1.8.3 stage.

The migration that backfills legacy notification metadata restored the legacy notification's identity
but left its provenance null. A later migration fills that in and creates the `System_AppVersion` row it
points at — **conditionally**, because a database created fresh by an unreleased build also reaches this
migration and never ran v1.8.3.

**The current build must report a version other than `1.8.3`, or this proves nothing.** With both equal,
the row the migration inserts and the row the app records for itself are the same row, and the two
causes are indistinguishable.

**That is satisfied by the ordinary development version and needs no setup.** Development carries the
milestone's target version with an `-alpha` suffix from milestone start — see
`docs/workflow/checklist.md`'s *Version during development* — so `quotinator:local` already reports
something other than `1.8.3`. Run this document against it directly.

**Until #339's full run this test manufactured the difference**, temporarily rewriting
`Directory.Build.props` and rebuilding the image, then restoring both. That step is gone: the
difference is now real, and a test that has to edit the repository to express its own assertion was
always a signal that the version scheme was wrong rather than the test.

[`02-notification-metadata-and-provenance.md`](02-notification-metadata-and-provenance.md) reads the
same two rows from the same upgrade. The documents are not redundant — 02 proves replay from a released
database completes and that provenance attributes each notification to whichever build wrote it, while
this one proves the `1.8.3` row is inserted **conditionally**, which only its second half can show.

## Determinism

- **The version difference is the whole test.** See Preconditions — this is the one setup step that
  cannot be skipped without silently invalidating the result.
- **The seeding wait polls for the announcement**, not a duration: v1.8.3 writes it after seeding ~800
  quotes, so a fixed wait can see zero and read as proof that nothing was written.
- **That poll counts the announcement's *body text* across the serialized items.** v1.8.3 has no
  `title` field in its API response, so a gate on the title never becomes true and the loop hangs
  rather than fails — measured during #339's full run against
  [`04`](04-upgrade-does-not-duplicate-the-legacy-notification.md), which carried the same gate.
  Counting occurrences rather than matching lines matters for the separate reason that the response is
  single-line JSON.
- **The database is read from the host with `DbInspector`**, against an absolute Windows bind path
  built from `$PWD` so nothing translates it into a different directory. That replaces a throwaway
  `alpine` container that installed `sqlite3` over the network first.
- This scenario uses **its own database**, not one shared with a sibling test — the row counts below
  are exact and any prior state breaks them.

## Steps

### 1. Seed a v1.8.3 database and wait for its announcement

```powershell
$upgradedDir = "$PWD\.claude\temp\qt-notif-05-upgraded"
New-Item -ItemType Directory -Force -Path $upgradedDir | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-notif-05-upgraded --port 18505 `
  --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind $upgradedDir

function Count-Announcement($port) {
  $items = (Invoke-RestMethod "http://localhost:$port/api/v1/notifications?pageSize=0").items
  ([regex]::Matches(($items | ConvertTo-Json -Depth 5), 'Two REST API operation IDs were renamed')).Count
}

while ((Count-Announcement 18505) -lt 1) { Start-Sleep 5 }
Count-Announcement 18505
```

**Expected:** `1` — v1.8.3's announcement exists, so seeding has finished and the database is in the
state the upgrade is about.

**On failure:** a poll that never terminates means seeding did not complete and the announcement was
never written. Upgrading a database in that state proves nothing about provenance (see Determinism).
Stop.

### 2. Upgrade to the current build against the same database

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-05-upgraded --port 18505 `
  --image quotinator:local --bind $upgradedDir
```

**Expected:** the current build starts against the upgraded database and reports healthy — which is what
`reenter`'s own readiness poll establishes before it returns.

### 3. Read the version history

```powershell
dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$upgradedDir\quotinatordata.db" `
  --sql "SELECT Application, Version, SequenceNumber FROM System_AppVersion ORDER BY SequenceNumber"
(Select-Xml -Path Directory.Build.props -XPath '//Version').Node.InnerText
```

**Expected:** exactly two rows: `Quotinator.Api | 1.8.3 | 1`, then `Quotinator.Api | <the version
printed beneath> | 2` — measured as `1.9.0-alpha`, and read from the file rather than written here,
since it moves every milestone.

**The 1.8.3 row must sort first.** It predates every row this table can hold, and if it sorted last then
"the version that ran last" would answer 1.8.3 — and #81's catch-up would replay releases already
announced.

Joining the notifications back to that table attributes the v1.8.3-era announcement to **1.8.3**, and
anything written during this startup to the **current** version. Provenance records who wrote a row,
not who is running now.

### 4. Repeat the whole thing against a fresh database

Same build, no v1.8.3 stage. It needs its **own container name and its own directory**, distinct from
`qt-notif-05-upgraded` — run against the database the first half already upgraded, it would find the
1.8.3 row that half created and prove nothing:

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-05-upgraded --bind $upgradedDir

$freshDir = "$PWD\.claude\temp\qt-notif-05-fresh"
New-Item -ItemType Directory -Force -Path $freshDir | Out-Null
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-05-fresh --port 19505 --bind $freshDir

dotnet run --project tools/Quotinator.Tools.DbInspector -- --db "$freshDir\quotinatordata.db" `
  --sql "SELECT Application, Version, SequenceNumber FROM System_AppVersion ORDER BY SequenceNumber"
```

**The first container must be removed before this one starts**, which is why the `destroy` is the first
line of this step rather than being left to Cleanup.

**Expected:** exactly one row, the current build's own version, and **no 1.8.3 row at all**.

That guarantee is structural rather than guarded in SQL: an empty database takes the one-step baseline
path and never replays migrations, so the conditional insert never runs. Worth confirming rather than
assuming, which is why it is a step here.

**On failure:** a 1.8.3 row on a database that never ran 1.8.3 means the insert is unconditional — the
exact defect this half exists to catch, and invisible from the upgrade path alone, which produces that
row legitimately.

## Observed effect

Not yet established as a captured record. The ordering is the load-bearing observation — the two rows
existing is weaker evidence than the 1.8.3 row sorting first.

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-05-upgraded --bind $upgradedDir
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-05-fresh --bind $freshDir
Remove-Item $upgradedDir, $freshDir -Recurse -Force -ErrorAction SilentlyContinue
```

Both data directories are bind mounts rather than named volumes, so removing the directories is what
removes their data.

**This test no longer modifies the repository, so there is nothing else to restore.** It previously
edited `Directory.Build.props` and rebuilt `quotinator:local` to manufacture a version difference,
which left two things a profile restore could not fix — an edited file, and a shared image tag built
from it that every sibling test then ran against. Both are gone now that the development version
differs from the released one on its own.
