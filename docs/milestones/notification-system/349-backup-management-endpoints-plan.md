# #349 — Admin endpoints to list, delete and report status for database backups

**Status:** Planning
**GitHub issue:** #349
**Tiers required:** T1, T2
**Depends on:** none — pairs with [#348](https://github.com/DutchJaFO/Quotinator/issues/348), either order

---

## Description

Quotinator writes a safety backup before every migration, seed and Reset, into `{dataDir}/backups/`.
Nothing can see them, and nothing can remove one — the only route is filesystem access to the
container's volume, which a Home Assistant add-on user does not meaningfully have.

That matters rather than being untidy: the folder carries a hard size budget, and once #348 lands a full
folder becomes a *refusal* — a startup degrades, or a Reset declines. The remedy is to remove old
backups, and there is no in-app way to do it.

Endpoints are preferred over telling an operator to delete files by hand because an endpoint carries
authorization and writes an audit trail — useful precisely when diagnosing a problem that arose from the
action (developer direction, 2026-08-27).

---

## Next action

**Refine this plan into a verification checklist, then write the red tests.**

The endpoint shapes are settled in the issue; what is not yet written is the checklist mapping each
requirement to its proof, and the decision on where the backup file enumeration lives — the endpoint
handlers must not reach into the filesystem directly, and `DatabaseInitializer` currently owns
`BackupsPath` without exposing anything that lists it.

---

## Design

Three endpoints, all under a new `Backup` OpenAPI category (developer decision, 2026-08-27) rather than
the `Admin` tag every `/admin/**` route carries today — backup management is a distinct operator task,
and burying it among reseed/reset/audit hides it from the operator who came looking because a backup
failed. Their own `MapGroup` still chains the admin API key and the concurrency-1 rate-limit policy: the
category is a documentation grouping, never relaxed access.

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/admin/backups` | List what exists — name, size, when taken. Paginated per the standard contract |
| `DELETE /api/v1/admin/backups/{name}` | Remove one, by the name the list returns |
| `GET /api/v1/admin/backups/status` | Can a backup be made right now, and are we within quota |

**The status endpoint answers the question an operator actually has**, before they act rather than when
a Reset declines. It surfaces #348's pre-flight check (a yes, or a no naming which obstacle) alongside
the quota picture (used, operating quota, absolute ceiling, what remains, whether the reserve is in
use), plus real free disk space — an independent constraint from the self-imposed quota, so showing only
one would mislead exactly when the other binds.

**All three must answer while the database is degraded**, which is the state they exist for.

---

## Steps

### 1. Decide where backup enumeration lives

**Status:** ⬜ Not started

The handlers must not touch the filesystem directly. `DatabaseInitializer` owns `BackupsPath` but
exposes nothing that lists or removes a backup, so this needs a small reader/writer pair in
`Quotinator.Data` with the endpoints consuming it through DI.

### 2. Write the verification checklist

**Status:** ⬜ Not started

Every requirement in the issue gets a row, including all eight pagination cases the standard contract
requires of a new paginated GET.

### 3. Write the red tests

**Status:** ⬜ Not started

Including both halves of the path-traversal case — rejected *and* nothing deleted, since a status check
alone would pass even if the file had already gone.

### 4. Implement the three endpoints

**Status:** ⬜ Not started

### 5. Declare the `Backup` tag with a description

**Status:** ⬜ Not started

Per [ADR 020](../../architecture-decisions/020-openapi-tags-are-declared-with-descriptions.md), in the
same commit that first uses the tag. This is that rule's first live application since #339 added it.

### 6. Update the API documentation

**Status:** ⬜ Not started

`docs/api-endpoints.md` and the `[Description]` attributes, in the same commit.

---

## Verification checklist

**Not yet written — this is step 2, and implementation does not start until it exists.** Recorded as an
explicit gap rather than an empty table, so the doc's own state says what the next action is.
