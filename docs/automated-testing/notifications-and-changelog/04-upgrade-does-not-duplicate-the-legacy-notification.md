# Upgrading a v1.8.3 database enriches its notification rather than duplicating it

**Smoke:** no
**Environment:** Upgraded
**Traces to:** #312, #413

## Preconditions

**Beyond the profile.** The Upgraded prior image is the **published
`ghcr.io/dutchjafo/quotinator:1.8.3` tag**: the row this test is about is one that release actually
shipped, so no other prior image reaches the state. Two app containers of this test's own share one
bind-mounted directory, each on its own port: `qt-notif-04-183` (the released image, publishing
`18504`) and `qt-notif-04-current` (the current build, publishing `19504`).

#312 moved a notification's identity out of message text into structured metadata. A row written before
that has no metadata, cannot be identified, and would be announced a second time. A migration backfills
v1.8.3's one shipped notification so the upgrade recognises it; this proves that.

#413 added the second half: that same row keeps the 30-day expiry v1.8.3 defaulted to, so a breaking
change that still applies reads as **Expired**, and its body is one unbroken paragraph. Migration 27
clears the expiry and restates the body over four lines, in every language.

**The v1.8.3 container must have actually written its announcement before the upgrade starts**: that
is the precondition this test confirms rather than assumes. It writes the #279 announcement *after*
first-boot seeding of ~800 quotes.

## Determinism

**This is a case where a fixed wait actively caused a defect to reach a T1 run.** A 45-second check saw
zero notifications and looked like proof that nothing had been written; it was not, seeding simply had
not finished. Upgrading at that point would have tested nothing at all, silently.

So the wait polls for **the row this scenario is about**, not for a duration and not for a total.

Gating on that specific announcement rather than a total matters for the same reason the assertion
does: a total changes whenever another producer is added.

**The gate matches the notification's body text, not a named field, and that is load-bearing.**
v1.8.3's API has no `title` field at all, returning `message` carrying the body, while the current
build returns `title` and `body`. A gate written against `title` can never become true against the
container it is waiting for, and it does not fail, it hangs: measured during #339's full run, where it
ran about ten minutes before being stopped. Counting the phrase across the serialized items is
indifferent to which field holds it, so the same command works on both versions.

**Count occurrences, not matching lines.** A line-counting match against single-line JSON reports `1`
however many copies exist, so a genuine duplicate would still read `1` and this test could never fail
in the direction it exists to catch. Found during #339's audit, 2026-08-22.

**Count only this announcement, never the total.** The running version may legitimately add its own
notifications; a total would then read `2` for an entirely correct reason, get "fixed" by editing the
digit, and hide a real duplicate the next time one occurs.

**The identity evidence is the row's `id`, because #413 retired the one this document used.** Until
migration 27, the discriminator between *enriched in place* and *rewritten to look the same* was the
retained `expiresAt`: a fresh write would carry none, since #312 made expiry opt-in. Migration 27
clears that expiry deliberately, so both readings now show an empty one and the old assertion can no
longer tell them apart. The row's `id` can: it is a GUID the v1.8.3 container generated, and a second
copy would carry a new one. Captured before the upgrade and compared after.

**The gate counts the statement's first line only.** #413 broke the body across four lines, and the
phrase this document waits on sits on the first, so the gate is unaffected by the layout; it would
also be unaffected by a future rewording of any line but that one.

## Steps

### 1. Seed a genuine v1.8.3 database and wait for its announcement to exist

```powershell
$dataDir = "$PWD\.claude\temp\qt-notif-04-data"
# A folder left by an earlier run still holds its database, and the released image would start against it.
if (Test-Path $dataDir) { Remove-Item -LiteralPath $dataDir -Recurse -Force }
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
"id before        = $($before.id)"
"expiresAt before = $($before.expiresAt)"
"bodyLines before = $((($before.message) -split "`n").Count)"

dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-04-183 --bind $dataDir
```

**Expected:** `1`, a non-empty `id`, a **non-empty** `expiresAt` (v1.8.3's always-on 30-day expiry),
and `bodyLines before = 1` (the unbroken paragraph #413 is about). The announcement is present, so
seeding has finished, and step 2 has the two values it compares against.

**On failure:** anything other than `1` for the count means the v1.8.3 database is not in the state this
test upgrades from, because seeding had not finished writing the announcement. Upgrading at that point
tests nothing at all, silently (see Determinism). Stop.

An empty `id` means v1.8.3's response does not carry one, and step 2's identity assertion has nothing to
compare; fall back to `createdAt`, which that release does return, and say so here.

### 2. Upgrade to the current build against the same data

```powershell
dotnet script scripts/testing/test-env.csx -- reenter --name qt-notif-04-current --port 19504 `
  --image quotinator:local --bind $dataDir

Count-Announcement 19504

$after = (Invoke-RestMethod "http://localhost:19504/api/v1/notifications?pageSize=0").items |
         Where-Object { $_.body -match 'operation IDs were renamed' }
"title=$($after.title) metadataKind=$($after.metadataKind)"
"sameRow=$($after.id -eq $before.id)"
"expiresAt after = '$($after.expiresAt)'  cleared=$([string]::IsNullOrWhiteSpace($after.expiresAt))"
"bodyLines after = $((($after.body) -split "`n").Count)"
```

**Expected:** still **`1`**, not `2`. The upgrade enriched the existing announcement rather than writing
a second copy.

That one row carries the backfilled `title` and `metadataKind` of `announcement`, `sameRow=True`,
`cleared=True`, and `bodyLines after = 4`.

`sameRow=True` is what proves the row was enriched in place rather than rewritten to look similar, now
that the cleared expiry can no longer distinguish the two (see Determinism). `cleared=True` and
`bodyLines after = 4` are #413's own two requirements.

**On failure:** `cleared=False` means migration 27 did not run or did not match the row.
`bodyLines after = 1` means the stored body was not restated, which is what a build from before #413
produces. `sameRow=False` with a count of `1` is the worst reading of the three: the original row is
gone and a replacement is standing in for it.

### 3. Confirm every language carries the layout

```powershell
foreach ($lang in 'en-GB', 'nl', 'de') {
  $items = (Invoke-RestMethod "http://localhost:19504/api/v1/notifications?pageSize=0" `
              -Headers @{ 'Accept-Language' = $lang }).items
  $row = $items | Where-Object { $_.metadataKind -eq 'announcement' }
  "{0,-6} lines={1} language={2} isTranslated={3}" -f $lang, (($row.body -split "`n").Count), $row.language, $row.isTranslated
}
```

**Expected:** `lines=4` for all three, and `isTranslated=True` for `nl` and `de`. The requirement is
that the layout reaches every language, not only the original, so asserting it on English alone would
pass while two of the three stayed unbroken.

`language` reads `en` for an `en-GB` request, not `en-GB`: the field carries the language the content is
in, and the announcement's original language is recorded as `en`. Asking for `en-GB` and being given
`en` with `isTranslated=False` is therefore the correct answer, not a near miss.

**On failure:** `lines=1` for `nl` or `de` with `4` for English means the migration rewrote
`System_Notification` but not `System_NotificationTranslation`.

## Observed effect

**Measured 2026-10-02**, red against `quotinator:canary413` (built from the commit before #413's first
change) and then green against `quotinator:local`.

Both runs seeded a genuine v1.8.3 database, which wrote the announcement with a 30-day expiry and a
one-line body, and both upgrades kept the count at `1` with `sameRow=True`. The two builds differ in
exactly the two assertions #413 adds:

| | canary (before) | local (after) |
|---|---|---|
| `cleared` | `False`, expiry still `2026-11-01T21:55:47Z` | `True`, empty |
| `bodyLines after` | `1` | `4` |
| step 3, each of `en-GB`/`nl`/`de` | `lines=1` | `lines=4` |

The upgrade logged `schema updated (data v27, app v9)` and no `[Runtime - Exception]` line.

`sameRow` is the load-bearing observation: it is the only thing distinguishing "enriched in place" from
"rewritten to look the same", and it took that role over from the retained `expiresAt` when #413 cleared
it. It read `True` on both builds, which is what makes it evidence of row identity rather than of the
fix.

## Cleanup

Read the log before the stop, per the index's *Read the log before the application stops*:

```powershell
docker logs qt-notif-04-current 2>&1 | Select-String -SimpleMatch '[Runtime - Exception]'
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-04-183 --bind $dataDir
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-04-current --bind $dataDir
Remove-Item $dataDir -Recurse -Force -ErrorAction SilentlyContinue
```

`qt-notif-04-183` is already removed mid-run; it is named again here so a run abandoned partway leaves nothing
behind. The data directory is a bind mount rather than a named volume, so removing the directory is
what removes its data.
