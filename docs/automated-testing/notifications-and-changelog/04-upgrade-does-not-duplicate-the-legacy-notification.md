# Upgrading a v1.8.3 database enriches its notification rather than duplicating it

**Smoke:** no
**Traces to:** #312

## Preconditions

#312 moved a notification's identity out of message text into structured metadata. A row written before
that has no metadata, cannot be identified, and would be announced a second time. A migration backfills
v1.8.3's one shipped notification so the upgrade recognises it; this proves that.

**The v1.8.3 container must have actually written its announcement before the upgrade starts.** It
writes the #279 announcement *after* first-boot seeding of ~800 quotes.

## Determinism

**This is a case where a fixed wait actively caused a defect to reach a T1 run.** A 45-second check saw
zero notifications and looked like proof that nothing had been written — it was not; seeding simply had
not finished. Upgrading at that point would have tested nothing at all, silently.

So the wait polls for **the row this scenario is about**, not for a duration and not for a total:

```bash
until [ "$(curl -s "http://localhost:8080/api/v1/notifications?pageSize=0" \
  | grep -c 'Two API operation IDs were renamed')" -ge 1 ]; do sleep 2; done
```

Gating on that specific announcement rather than a total matters for the same reason the assertion
does: a total changes whenever another producer is added.

**Count occurrences, not matching lines.** `grep -c` counts *lines* that match, and the API returns
single-line JSON — so a genuine duplicate would still report `1` and this test could never fail in the
direction it exists to catch. Found during #339's audit, 2026-08-22. Use
`grep -o … | wc -l`, which counts occurrences.

## Steps

**Seed a genuine v1.8.3 database and wait for its announcement to exist:**

```bash
MSYS_NO_PATHCONV=1 docker run -d --name qA -e Quotinator__DataDir=/data \
  -v /tmp/qdup/data:/data -p 8080:8080 ghcr.io/dutchjafo/quotinator:1.8.3
until [ "$(curl -s "http://localhost:8080/api/v1/notifications?pageSize=0" \
  | grep -c 'Two API operation IDs were renamed')" -ge 1 ]; do sleep 2; done
curl -s "http://localhost:8080/api/v1/notifications?pageSize=0" | grep -o 'Two API operation IDs were renamed' | wc -l
docker rm -f qA
```

**Upgrade to the current build against the same data:**

```bash
MSYS_NO_PATHCONV=1 docker run -d --name qB -e Quotinator__DataDir=/data \
  -v /tmp/qdup/data:/data -p 8080:8080 quotinator:local
until curl -sf http://localhost:8080/api/v1/health > /dev/null; do sleep 1; done
curl -s "http://localhost:8080/api/v1/notifications?pageSize=0" | grep -o 'Two API operation IDs were renamed' | wc -l
```

## Expected output

**Before the upgrade** — `1`. The v1.8.3 announcement is present, so seeding has finished.

**After the upgrade** — still **`1`**, not `2`. The upgrade enriched the existing announcement rather
than writing a second copy.

**Count only this announcement, never the total.** The running version may legitimately add its own
notifications; a total would then read `2` for an entirely correct reason, get "fixed" by editing the
digit, and hide a real duplicate the next time one occurs.

That one row must carry the backfilled `title` and `metadataKind: announcement`, **and still hold
v1.8.3's original `expiresAt`** — the old always-on 30-day expiry. That retained expiry is what proves
it is the original row enriched in place rather than a fresh write that happens to look similar: a new
row would have no expiry at all, since #312 made expiry opt-in.

## Observed effect

Not yet established as a captured record beyond the counts. The retained `expiresAt` is the load-bearing
observation — it is the only thing distinguishing "enriched in place" from "rewritten to look the same".

## Cleanup

```bash
docker rm -f qB
rm -rf /tmp/qdup
```
