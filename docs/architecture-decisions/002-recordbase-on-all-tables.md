# ADR 002 — RecordBase applies to all tables without exception

**Status:** Accepted  
**Date:** 2026-06-20  
**GitHub issues:** #73, #74, #75, #76, #77

---

## Context

`RecordBase` is the abstract base class for all database-backed entities. It provides:

- `Id` — UUID v4 surrogate primary key
- `DateCreated` — set once at insert time
- `DateModified` — updated on every write
- `DateDeleted` — set when the record is soft-deleted, cleared on restore
- `IsDeleted` — soft-delete flag

When planning the many-to-many relationship pattern (#77), the question arose whether junction tables should be exempt from `RecordBase`. A junction table's natural identity is the pair of foreign keys — for example `(QuoteId, TagId)` — rather than a single `Guid`. Adding a synthetic `Guid Id` appeared to add noise. The same question applies, in principle, to any linking or associative table.

A parallel question arose for the audit trail (#73): whether a single global `audit_log` table becomes unmanageable at scale, and whether per-table audit tables or a richer audit payload would be needed instead.

---

## Decision

**RecordBase is applied to every table in the database without exception.**

Junction tables, link tables, and any other associative structure receive a synthetic `Guid Id` as their surrogate primary key. The natural uniqueness constraint (e.g. the pair of foreign keys) is enforced with a `UNIQUE` constraint alongside the surrogate key.

Example layout for a many-to-many junction table:

```sql
CREATE TABLE QuoteTag (
    Id           TEXT    NOT NULL PRIMARY KEY,
    QuoteId      TEXT    NOT NULL REFERENCES Quotes(Id),
    TagId        TEXT    NOT NULL REFERENCES Tags(Id),
    DateCreated  TEXT,
    DateModified TEXT,
    DateDeleted  TEXT,
    IsDeleted    INTEGER NOT NULL DEFAULT 0,
    UNIQUE (QuoteId, TagId)
);
```

---

## Reasoning

### Schema changes after the fact are costly

Adding `RecordBase` fields to a table that already exists requires an `ALTER TABLE` migration. In SQLite inside a Docker volume on a user's device, that migration must be written, tested, and shipped as a startup migration. The migration also cannot backfill `DateCreated` accurately — the original timestamps are gone. For a solo-maintained homelab project, every migration that touches existing data carries risk. Schema decisions made at creation time are free; schema decisions made after data exists are not.

### The exemption argument is only about aesthetics

The sole argument for exempting junction tables is that the synthetic `Guid Id` is semantically meaningless as a business key. That is true, but the `Guid` is harmless as a surrogate and the `UNIQUE` constraint preserves the natural key guarantee. No query or business rule depends on the surrogate having meaning — it exists only to give the row an identity in the repository layer.

### RecordBase makes junction rows full repository citizens

With `RecordBase` on a junction table:

- `IRepository<TJunction>` and `IRestorableRepository<TJunction>` work without any special-casing.
- Unlinking becomes `SoftDeleteAsync` and restoring a link becomes `RestoreAsync` — no new patterns needed.
- The `ILinkRepository<TLeft, TRight, TJunction>` planned in #77 is built on top of the existing repository infrastructure rather than against raw Dapper.

Without `RecordBase`, a junction table requires a bespoke, non-generic implementation that sits outside the established patterns and cannot benefit from future improvements to the repository layer.

---

## Consequences for the audit trail

Because `RecordBase` captures the current state of every row — `IsDeleted`, `DateDeleted`, `DateModified`, `DateCreated` — the planned `audit_log` table (#73) does **not** need to store state snapshots or change diffs. Its only remaining job is to record *who* triggered a given operation.

This narrows the audit log schema to a small, fixed set of columns regardless of which entity type is being audited:

| Column | Purpose |
|---|---|
| `Id` | RecordBase surrogate key |
| `TableName` | Which table was touched |
| `RecordId` | The `Guid` of the affected row |
| `Operation` | Enum: Insert, Update, SoftDelete, Restore, HardDelete, Purge, Link, Unlink |
| `PerformedBy` | Authenticated user (populated once auth is implemented) |
| `PerformedAt` | When the operation occurred |

A single `AuditEntries` table with indexes on `(TableName, RecordId)` is sufficient. Per-table audit tables were considered and rejected — the payload is identical for every entity type, so splitting into N tables adds schema complexity with no query or maintainability benefit.

### Why a global audit log is viable here but not in general

Global audit logs become unmanageable when they must store *what changed* — the payload then differs per entity, forcing serialised JSON or a long list of nullable columns. Because `RecordBase` eliminates that requirement, the log stores only an event reference, which is uniformly small and uniformly structured. The single-table design is a direct consequence of the RecordBase-everywhere decision.

---

## Follow-on

- #73 — audit trail implementation (deferred until auth milestone)
- #74 — read-model query pattern for joins
- #75 — master/detail (1:N) repository pattern
- #76 — 1:1 relationship pattern
- #77 — many-to-many relationship pattern with `ILinkRepository<TLeft, TRight, TJunction>`
