# ADR 017 — Join-capable reads use JoinQueryRepository/IJoinStrategy, even without an immediate capability gain

**Status:** Accepted
**Date:** 2026-08-10
**GitHub issues:** #282, #281

---

## Context

`JoinQueryRepository<TResult>` + `IJoinStrategy<TResult>` (`docs/data-access.md`) was added by #74
(2026-06-28) as the documented, guard-tested pattern for "a read-only query that joins two or more
tables, or returns a projection." #74's own scope explicitly deferred building a second real
consumer: "Add at least one example / canonical implementation when the first real join query is
needed in a milestone." A canonical example (`WidgetWithOwnerStrategy`) shipped; a second, real
consumer never did.

Investigating #281 (masterdata CRUD endpoint duplication) found that every real join-needing read in
the domain hand-rolls its own raw `IDbConnectionFactory`/Dapper query instead of using the pattern
#74 built for exactly this:

- `ISourceSeriesReferenceReader` / `ICharacterSourceLinkReader` / `ISeriesUniverseReferenceReader` —
  masterdata FK-to-reference resolvers (#184–#189), each opening its own connection and running its
  own `connection.QueryAsync`/`QueryFirstOrDefaultAsync`.
- `ConversationLineCountReader` — a batched line-count aggregate read.
- `SqliteQuoteService.BuildConversationResponse`/`BuildLineResponse` — Conversation hydration
  (#69/#157), including per-line, language-dependent translation lookups.

This is not a case of the pattern not existing yet: #184 (Sources reader, 2026-07-18) came 20 days
after #74, and #69/#157 (Conversation hydration, 2026-07-11) came 13 days after — both confirmed via
`git log --follow --diff-filter=A`. Neither issue's body mentions `JoinQueryRepository` at all.
Confirmed genuine oversight, not a considered rejection: the two pieces of work were done without the
author reconnecting them.

Adopting the pattern for the 3 masterdata resolvers unlocks no new capability over what they already
do — `JoinQueryRepository.QueryAsync` is a 3-line wrapper around the exact `CreateConnection`/`Open`/
`QueryAsync` sequence these readers already write, and each still needs its own interface
(`ISourceSeriesReferenceReader`, etc.) to shape the flat result into a batched `Dictionary`/tuple
return — `JoinQueryRepository` doesn't do that shaping. Translation resolution — the one thing that
looked like it might be a genuine limit of the pattern — is not: `Quotes.SelectBase` and
`StageDirections`/`SoundCues.SelectByIdWithTranslation` already do single-query, parameterized,
`LEFT JOIN + COALESCE` translation resolution today, proving the shape already works in this
codebase.

One real consumer does not fit: `ConversationLineCountReader.GetLineCountsForManyAsync` deliberately
uses `QueryAsync<dynamic>`, not a typed POCO, documented inline as working around two independently
confirmed Dapper/SQLite bugs — Dapper's registered `Guid` `ITypeHandler` is skipped for a type with a
parameterized constructor matching the query's column count (dotnet/Dapper#461), and an
undeclared-type correlated-subquery column takes SQLite `BLOB` affinity on a zero-row result
(dotnet/efcore related, confirmed against Microsoft's own SQLite type-affinity docs).
`IJoinStrategy<TResult>` requires a concrete `TResult`; forcing this read into the pattern would
reintroduce the exact bug its own code comment documents fixing.

---

## Decision

**Any read that joins two or more tables, or returns a multi-table projection, uses
`JoinQueryRepository<TResult>`/`IJoinStrategy<TResult>` as its SQL-execution mechanism whenever the
result can be expressed as a concrete POCO — even when adopting it unlocks no new capability over a
hand-rolled `connection.QueryAsync` call.** Consistency and discoverability — one documented
mechanism a future contributor actually finds and follows, one place `IJoinStrategy` implementations
are collected — is itself sufficient justification. "No immediate capability gain" is not a reason to
keep reinventing the same connection-open boilerplate ad hoc; that reasoning is exactly how this
pattern ended up unused by every real consumer despite existing before all of them.

A domain-specific reader interface (e.g. `ISourceSeriesReferenceReader`) may still exist **above**
this mechanism whenever the caller needs a shaped or batched result (a `Dictionary<Guid, ...>`, a
tuple) — `JoinQueryRepository.QueryAsync` always returns a flat `IReadOnlyList<TResult>`; the reader
class does the shaping (`.ToDictionary()`, `.GroupBy()`), internally calling `JoinQueryRepository`/
`IJoinStrategy` instead of opening its own connection and writing its own SQL inline.

**Exemption:** a read whose correct result type cannot be a concrete POCO — today, only
`ConversationLineCountReader`'s `QueryAsync<dynamic>` — stays outside this pattern. Document the
reason inline at the exempted call site, the same way ADR 008 requires an enum `CHECK`-constraint
exemption to state its reason in the column's own doc comment. Do not treat this exemption as
precedent for a future reader unless it has the same genuine typed-POCO incompatibility, not just
"no gain."

---

## Consequences

- Two follow-up issues in v1.8.0:
  - Migrate the 3 masterdata reference readers to use `JoinQueryRepository`/`IJoinStrategy`
    internally, keeping their own domain-specific interface and dictionary/tuple shaping unchanged —
    and add the real-SQLite integration tests these readers are missing today (only fakes exist),
    matching `ConversationLineCountReaderTests.cs`'s pattern.
  - Wrap Conversation's per-line lookups (Quote/StageDirection/SoundCue, each already a single
    correct translation-aware query) as `IJoinStrategy` implementations via `JoinQueryRepository` —
    this also gives Conversation's `GetById` a real, documented generic pattern to point to instead
    of being permanently ad hoc, closing #281's open question about whether it can join the same
    category as the other 7 masterdata `GetById` handlers. The same pass fixes a redundant
    double-query found as a side-effect of this investigation (`BuildLineResponse`'s `"quote"` branch
    calling the already-translation-complete `Quotes.SelectById()` query twice whenever the requested
    language differs from the quote's original language) — folded into this issue rather than filed
    separately, since rewriting the branch as a single `JoinQueryRepository` call naturally eliminates
    it, and a standalone fix would just be rewritten again the moment this issue landed.
- `docs/data-access.md`'s "When to use which pattern" table gets a note pointing to this ADR for the
  "adopt even without an immediate capability gain" rule, so a future contributor facing the same
  "but the hand-rolled version already works fine" reasoning has an answer to check instead of
  re-deriving it.
- `ConversationLineCountReader`'s exemption is the only one today; any future dynamic-typed read
  needs its own genuine justification, checked against this ADR, not assumed by analogy.
