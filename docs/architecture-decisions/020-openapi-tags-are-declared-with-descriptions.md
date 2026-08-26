# ADR 020 — Every OpenAPI tag an endpoint uses is declared with a description

**Status:** Accepted
**Date:** 2026-08-27
**GitHub issues:** #339

---

## Context

An endpoint declares its group with `.WithTags(ApiTags.X)`. That alone is enough for the operation to
appear under `X` in the published spec and in Scalar — nothing warns, nothing errors, and the group
renders.

What it does **not** do is give the group a description or a position. Those come from the document's
own top-level `tags` array, built in `Program.cs`'s `AddDocumentTransformer`, which is a separate
collection maintained by hand. A tag can therefore exist in three states, and only one of them is
correct:

| `.WithTags` | Declared in `document.Tags` | Result |
|---|---|---|
| yes | yes, with a description | The group renders with its description, in the intended order |
| yes | no | The group renders — bare, undescribed, and after the declared ones |
| yes | yes, description empty | Indistinguishable from the row above |

**The second row shipped and survived two releases.** `ApiTags.Notifications` was added with the
notification endpoints, the constant was used correctly at both call sites, and the group appeared in
Scalar — with no description and no ordering, while the other six tags had both. Nothing failed. It was
found by reading the live `/openapi/v1.json` during #339, not by any test, and not by anyone using the
API reference.

The two mechanisms are independent by construction: one is a call on an endpoint builder, the other is
a collection literal in `Program.cs`. Adding an endpoint group touches the first and there is nothing
in the compiler, the analyzers, or the request pipeline that connects it to the second.

**The API reference is a published surface.** `/scalar/v1` and `/openapi/v1.json` are served in every
environment including production, deliberately (see `CLAUDE.md`'s *Keeping API documentation in sync*),
and are English-only precisely because they are the whole of what a consumer reads. A group with no
description is a gap in that surface, not a cosmetic one.

---

## Decision

**Every tag used by an endpoint is declared in the document's top-level `tags` array with a non-empty
description.**

Concretely:

- The tag's name comes from a `Quotinator.Constants.Api.ApiTags` constant, referenced by both the
  `.WithTags(...)` call and the `document.Tags` entry — never spelled out a second time, the same
  reasoning `CLAUDE.md`'s *Endpoint naming convention* applies to `WithName`.
- A new endpoint group is not complete until its `document.Tags` entry exists. That entry is part of
  adding the group, in the same commit, exactly as a new UI string is not complete until it exists in
  all three `i18ntext` files.
- **A description is required, not merely a declaration.** An entry with an empty or whitespace
  description renders identically to a missing one, so the rule is stated in terms of what a reader
  sees rather than what the array contains.
- The description says what the group is for and names any authentication or rate-limiting the group
  carries, matching the six that already exist.

**This is enforced mechanically, not by review.**
`OpenApiSpecEndpointTests.EveryTagAnEndpointUses_IsDeclaredWithADescription` fetches the real
`/openapi/v1.json` through the full pipeline and checks every tag the operations actually carry against
the declared set.

**It derives the expected set from the operations themselves rather than from a list in the test.** A
maintained list would need updating whenever a group is added — which is the same manual step that
failed here, reproduced one layer down. Nothing has to be remembered for the guard to fire.

**It runs against the live document, not the transformer in isolation.** A unit test over the
transformer class would keep passing if the transformer were never registered; this is the same reason
`OpenApiSpecEndpointTests` exists alongside `NumericParameterSchemaTransformerTests` at all.

---

## Consequences

- Adding an endpoint group costs one extra edit — the `document.Tags` entry. Accepted deliberately as
  the price of the guarantee, on the same reasoning as ADR 019's central `<PackageVersion>` entry.
- The failure is now loud and immediate: a group added without its description fails a test in
  `dotnet test`, rather than rendering bare in Scalar until somebody happens to look.
- The guard covers the *presence* of a description, not its quality. Wording stays a review concern,
  as the `[Description]` attributes on endpoints and parameters already are.
- Tag **ordering** is not asserted. It follows declaration order in `document.Tags`, which is visible
  in one place and changes only when that literal is edited — there is no second mechanism for it to
  drift against, which is what made the description case worth guarding.
- This governs the tag set only. Nothing here extends to other spec metadata (`Info`, security
  schemes, servers), which have no equivalent split between two independently-maintained places.
