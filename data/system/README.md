# data/system/

Sibling to [`data/sources/`](../sources/). `data/sources/` holds **standard reseed** content — optional,
discardable bundled quote data plus user-imported files, loaded only on a fresh install or an explicit
reseed call, never automatically after a database Reset.

`data/system/` is reserved for **system reseed** content — vital, non-optional content the application
needs to function, loaded unconditionally after *any* database reset and after a genuinely fresh install.
See [#156's plan doc](../../docs/milestones/maintenance-milestone-v1.8.0/156-reset-baseline-and-system-reseed-plan.md)
for the full design.

This directory is currently empty. No real system/reference table exists yet in the application — the
`SeedSystemContentAsync` extension point this directory is the intended home for is proven today only
via test-only dummy fixtures (`SystemContent_`/`UserContent_`-prefixed tables in the test suites), not
by any file living here. It will start holding real content only once a genuine system/reference table
is introduced (see the pending genre-extensible-table idea, not yet scoped) and its file-driven loading
mechanism is built — deferred deliberately, matching the same "no such tables exist yet" pragmatism
#156's own originating issue applied to this same gap.
