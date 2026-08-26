# Upgrading a v1.8.3 database enriches its notification rather than duplicating it

**Smoke:** no
**Environment:** Upgraded
**Traces to:** #312

## Preconditions

**Beyond the profile.** The Upgraded prior image is the **published
`ghcr.io/dutchjafo/quotinator:1.8.3` tag** — the row this test is about is one that release actually
shipped, so no other prior image reaches the state. Two app containers of this test's own share one
bind-mounted directory, each on its own port: `qt-notif-04-183` (the released image, publishing
`18504`) and `qt-notif-04-current` (the current build, publishing `19504`).

#312 moved a notification's identity out of message text into structured metadata. A row written before
that has no metadata, cannot be identified, and would be announced a second time. A migration backfills
v1.8.3's one shipped notification so the upgrade recognises it; this proves that.

**The v1.8.3 container must have actually written its announcement before the upgrade starts** — that
is the precondition this test confirms rather than assumes. It writes the #279 announcement *after*
first-boot seeding of ~800 quotes.

## Determinism

**This is a case where a fixed wait actively caused a defect to reach a T1 run.** A 45-second check saw
zero notifications and looked like proof that nothing had been written — it was not; seeding simply had
not finished. Upgrading at that point would have tested nothing at all, silently.

So the wait polls for **the row this scenario is about**, not for a duration and not for a total.

Gating on that specific announcement rather than a total matters for the same reason the assertion
does: a total changes whenever another producer is added.

**The gate matches the notification's body text, not a named field, and that is load-bearing.**
v1.8.3's API has no `title` field at all — it returns `message` carrying the body — while the current
build returns `title` and `body`. A gate written against `title` can never become true against the
container it is waiting for, and it does not fail, it hangs: measured during #339's full run, where it
ran about ten minutes before being stopped. Counting the phrase across the serialized items is
indifferent to which field holds it, so the same command works on both versions.

**Count occurrences, not matching lines.** A line-counting match against single-line JSON reports `1`
however many copies exist — so a genuine duplicate would still read `1` and this test could never fail
in the direction it exists to catch. Found during #339's audit, 2026-08-22.

**Count only this announcement, never the total.** The running version may legitimately add its own
notifications; a total would then read `2` for an entirely correct reason, get "fixed" by editing the
digit, and hide a real duplicate the next time one occurs.

## Steps

### 1. Seed a genuine v1.8.3 database and wait for its announcement to exist

```powershell
$dataDir = "$PWD\.claude\temp\qt-notif-04-data"
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

dotnet script scripts/testing/test-env.csx -- create --name qt-notif-04-183 --port 18504 `
  --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind $dataDir

function Count-Announcement($port) {
  $items = (Invoke-RestMethod "http://localhost:$port/api/v1/notifications?pageSize=0").items
  ([regex]::Matches(($items | ConvertTo-Json -Depth 5), 'Two REST API operation IDs were renamed')).Count
}

while ((Count-Announcement 18504) -lt 1) { Start-Sleep 2 }
Count-Announcement 18504

$before = (Invoke-RestMethod "http://localhost:18504/api/v1/notifications?pageSize=0").items |
          Where-Object { $_.message -match 'operation IDs were renamed' }
"expiresAt before = $($before.expiresAt)"

dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-04-183 --bind $dataDir
```

**Expected:** `1`, and a **non-empty** `expiresAt` — v1.8.3's always-on 30-day expiry. The announcement
is present, so seeding has finished, and step 2 has the value it needs to compare against.

**On failure:** anything other than `1` here means the v1.8.3 database is not in the state this test
upgrades from — seeding had not finished writing the announcement. Upgrading at that point tests
nothing at all, silently (see Determinism). Stop.

### 2. Upgrade to the current build against the same data

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-04-current --port 19504 `
  --image quotinator:local --bind $dataDir

Count-Announcement 19504

$after = (Invoke-RestMethod "http://localhost:19504/api/v1/notifications?pageSize=0").items |
         Where-Object { $_.body -match 'operation IDs were renamed' }
"title=$($after.title) metadataKind=$($after.metadataKind)"
"expiresAt after = $($after.expiresAt)  retained=$($after.expiresAt -eq $before.expiresAt)"
```

**Expected:** still **`1`**, not `2`. The upgrade enriched the existing announcement rather than writing
a second copy.

That one row must carry the backfilled `title` and `metadataKind` of `announcement`, and
`retained=True` — **v1.8.3's original `expiresAt`, unchanged**. That retained expiry is what proves
it is the original row enriched in place rather than a fresh write that happens to look similar: a new
row would have no expiry at all, since #312 made expiry opt-in.

## Observed effect

Not yet established as a captured record beyond the counts. The retained `expiresAt` is the load-bearing
observation — it is the only thing distinguishing "enriched in place" from "rewritten to look the same".

## Cleanup

```powershell
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-04-183 --bind $dataDir
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-04-current --bind $dataDir
Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
```

`qt-notif-04-183` is already removed mid-run; it is named again here so a run abandoned partway leaves nothing
behind. The data directory is a bind mount rather than a named volume, so removing the directory is
what removes its data.
