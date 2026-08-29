# #353 — Upload a backup file

**Status:** Planning
**GitHub issue:** #353
**Tiers required:** T1, T2
**Depends on:** none — pairs with [#349](https://github.com/DutchJaFO/Quotinator/issues/349) and [#352](https://github.com/DutchJaFO/Quotinator/issues/352), any order

---

## Description

A backup that exists only inside the container is a restore point that cannot survive the container.
#349 adds download, which gets a copy out; this is the other half — bringing one back, after a volume is
recreated, a host is replaced, or every stored backup has been removed to clear the quota. It is also
the only path that recovers an installation whose stored backups are all gone or all unreadable.

---

## Next action

**Refine this plan: settle the stored-name rules, then write the verification checklist and the red
tests.**

The requirements are decided. What needs settling before tests can be written is the concrete naming
rule — what sanitisation actually strips, and whether an uploaded file keeps a recognisable relationship
to the `quotinatordata_v{N}_{timestamp}Z.db` convention the application's own backups follow, or is
stored under whatever the operator supplied once it is safe.

---

## Design

This endpoint accepts an arbitrary file that can later *become* the live database, which is what makes
it the security-relevant one in the group. Three things follow.

**Validation is by opening the file, not by inspecting its name.** The `SQLite format 3` header magic
plus a real open and read. Storing something that only reveals itself as unusable later, at restore
time, is the failure mode this designs out — a rejected upload is cheap, a restore point that turns out
to be worthless is not.

**Uniqueness is enforced, and case-insensitively** (developer direction, 2026-08-29). `Backup.db` and
`backup.db` are one file on Windows and two on Linux; without folding case, whether an upload destroys
an existing restore point would depend on the host filesystem. Consistent with `CLAUDE.md`'s
case-insensitive-by-default rule, applied to a file name rather than a column. A collision is refused
with a `409` naming the existing file; auto-renaming to a free name was rejected, since it makes
"unique" true by fiat and returns a file under a name the operator never chose.

**Overwriting requires explicit permission, and that flag is not the forbidden pattern.** Stated here
because it looks like one: the endpoint side-effect policy forbids an opt-in flag that bolts a *second,
independent action* onto an endpoint — which is why `restore=true` was rejected for this endpoint
(developer decision, 2026-08-29). An overwrite flag adds no second action; it authorises how this
endpoint's own single job resolves a collision, exactly as `allowNoBackup` does for a Reset that cannot
be backed up. Never a default, and the audit entry records *which file was replaced*, since overwriting
destroys a restore point.

---

## Steps

Not yet written — see *Next action*.

---

## Verification checklist

Not yet written — it is written once the steps above are, and before any implementation starts.
