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

So the wait polls for **the row this scenario is about**, not for a duration and not for a total:

```bash
until [ "$(curl -s "http://localhost:18504/api/v1/notifications?pageSize=0" \
  | grep -c 'Two API operation IDs were renamed')" -ge 1 ]; do sleep 2; done
```

Gating on that specific announcement rather than a total matters for the same reason the assertion
does: a total changes whenever another producer is added.

**Count occurrences, not matching lines.** `grep -c` counts *lines* that match, and the API returns
single-line JSON — so a genuine duplicate would still report `1` and this test could never fail in the
direction it exists to catch. Found during #339's audit, 2026-08-22. Use
`grep -o … | wc -l`, which counts occurrences.

**Count only this announcement, never the total.** The running version may legitimately add its own
notifications; a total would then read `2` for an entirely correct reason, get "fixed" by editing the
digit, and hide a real duplicate the next time one occurs.

## Steps

### 1. Seed a genuine v1.8.3 database and wait for its announcement to exist

```bash
dotnet script scripts/testing/test-env.csx -- create --name qt-notif-04-183 --port 18504 \
  --image ghcr.io/dutchjafo/quotinator:1.8.3 --bind /tmp/qt-notif-04/data
until [ "$(curl -s "http://localhost:18504/api/v1/notifications?pageSize=0" \
  | grep -c 'Two API operation IDs were renamed')" -ge 1 ]; do sleep 2; done
curl -s "http://localhost:18504/api/v1/notifications?pageSize=0" | grep -o 'Two API operation IDs were renamed' | wc -l
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-04-183
```

**Expected:** `1`. The v1.8.3 announcement is present, so seeding has finished.

**On failure:** anything other than `1` here means the v1.8.3 database is not in the state this test
upgrades from — seeding had not finished writing the announcement. Upgrading at that point tests
nothing at all, silently (see Determinism). Stop.

### 2. Upgrade to the current build against the same data

```bash
MSYS_NO_PATHCONV=1 docker run -d --name qt-notif-04-current -e Quotinator__DataDir=/data \
  -v /tmp/qt-notif-04/data:/data -p 19504:8080 quotinator:local
until curl -sf http://localhost:19504/api/v1/health > /dev/null; do sleep 1; done
curl -s "http://localhost:19504/api/v1/notifications?pageSize=0" | grep -o 'Two API operation IDs were renamed' | wc -l
```

**Expected:** still **`1`**, not `2`. The upgrade enriched the existing announcement rather than writing
a second copy.

That one row must carry the backfilled `title` and `metadataKind: announcement`, **and still hold
v1.8.3's original `expiresAt`** — the old always-on 30-day expiry. That retained expiry is what proves
it is the original row enriched in place rather than a fresh write that happens to look similar: a new
row would have no expiry at all, since #312 made expiry opt-in.

## Observed effect

Not yet established as a captured record beyond the counts. The retained `expiresAt` is the load-bearing
observation — it is the only thing distinguishing "enriched in place" from "rewritten to look the same".

## Cleanup

```bash
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-04-183
dotnet script scripts/testing/test-env.csx -- destroy --name qt-notif-04-current \
  --bind /tmp/qt-notif-04/data
```

`qt-notif-04-183` is already removed mid-run; it is named again here so a run abandoned partway leaves nothing
behind. The data directory is a bind mount rather than a named volume, so removing the directory is
what removes its data.
