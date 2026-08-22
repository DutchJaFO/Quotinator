# #329 — Source refresh: no retry on a marginal connect, and sources download sequentially

**Status:** Planning
**GitHub issue:** [#329](https://github.com/DutchJaFO/Quotinator/issues/329)
**Tiers required:** T1, T2
**Depends on:** #323, #325

---

## Description

Source refresh has no retry and fetches sources one at a time. One `GetAsync` per file, awaited in
turn inside nested loops; when a single connect exceeds its budget the refresh is abandoned, a warning
is logged, and the local copy is used — after every other source has waited its turn.

**Measured live during #309's T1 run (2026-08-19).** Both bundled sources are paths on the same host.
One failed on an exhausted `ConnectTimeout`; the second succeeded 8927 ms later; the first succeeded
586 ms later still, ninety seconds on. On a 460 Mbps / 15 ms link, that is not network availability —
it is a single marginal connect treated as terminal. The 8927 ms success is the more alarming number:
it cleared the 10 s budget by 12%, and users on slower links sit the wrong side of that line
routinely.

The observed failure is *handled* correctly — warning, fall back to the local copy, startup continues
— so nothing is broken in the never-crash sense. What is missing is the capability to try again, to
not make every source wait on the slowest, and to report how hard we had to try.

**The dependency on #323 and #325 is a revision, not a build order.** This issue revisits the
`ConnectTimeout` #323 added and #325's revert raised to 60 s, which is now the entire resilience of the
download path.

---

## Steps

### 1. Prove the observed failure shape is actually retried

**Status:** ⬜ Not started — **red test first; blocks every configuration decision below**

The standard retry's `ShouldHandle` covers HTTP 5xx/408/429, `HttpRequestException` and Polly's
`TimeoutRejectedException`. The live failure surfaced as `TaskCanceledException ---> TimeoutException:
A connection could not be established within the configured ConnectTimeout`, which is **none of
those**.

Establish the real behaviour before writing any configuration. If the standard handler does not retry
this shape, either widen `ShouldHandle` or move the per-attempt budget off
`SocketsHttpHandler.ConnectTimeout` and onto the attempt timeout, whose `TimeoutRejectedException` the
retry does handle. Step 9 assumes the latter — confirm rather than assume.

### 2. Add `Microsoft.Extensions.Http.Resilience`

**Status:** ⬜ Not started

The first-party Polly v8 wrapper. Version declared in `Directory.Packages.props` per ADR 019, with the
`PackageReference` carrying no inline `Version`. This is the first NuGet dependency the milestone
takes.

### 3. Attach exactly one resilience handler to the named client

**Status:** ⬜ Not started

On `SourceCacheUpdater.HttpClientName` in `Program.cs`. Exactly one — the .NET documentation is
explicit that resilience handlers must not be stacked.

### 4. Configure the four values, each with its measured basis

**Status:** ⬜ Not started

Attempt timeout 30 s (10 s is only 12% above the observed 8927 ms success); 5 retries, 6 attempts
total; total request timeout 200 s (6 × 30 s + ~15 s backoff ≈ 195 s); flat backoff, ~1 s base, jitter
on (exponential at the 2 s default costs 62 s of pure waiting across 5 retries).

**These are deliberate best guesses to be revised after measurement, not derived constants.** Each
gets a named constant whose XML doc states the measurement it came from, matching how
`SourceCacheUpdater.DefaultConnectTimeoutSeconds` already documents its basis.

### 5. Run the per-file downloads in parallel

**Status:** ⬜ Not started

`ResolveAsync`'s nested loops currently await one file at a time; every source should be in flight at
once so a slow or failing source delays only itself. With step 4 this keeps the worst case at roughly
200 s for the whole refresh rather than 200 s per source.

### 6. Cap the concurrency

**Status:** ⬜ Not started

Two bundled entries carry a `downloadUrl` today, but a user manifest may declare many more, and
unbounded parallelism against one host is antisocial regardless. A named constant with a matching
`Quotinator:` config key, enforced by a `SemaphoreSlim` around `ResolveOneAsync` — no scheduler, no
partitioning. Small default (4 proposed), basis documented in its XML doc.

### 7. Keep parallelism from changing what the refresh produces

**Status:** ⬜ Not started

`results` stays ordered by batch index then file index, never completion order — the seeding report and
its log lines must be reproducible run to run. `effectivePaths` is populated safely from concurrent
tasks. The existing collision detection still runs first and still skips every member of a colliding
group, so no two parallel tasks can write the same target path.

### 8. Establish per-source download statistics

**Status:** ⬜ Not started

`SourceRefreshResult` gains Attempts (from the retry strategy's own `OnRetry` callback, never
inferred), Elapsed, and BytesDownloaded (none when nothing was transferred — `UpToDate`,
`SkippedCollision`). All three fall out of the existing code path; the byte count is already
materialised as a `byte[]`.

**Deliberately a foundation, not a finished telemetry design.** The point is to start generating
numbers that can fine-tune step 4's guesses. Emit them on one log line per source so they are usable
before any UI consumes them.

### 9. Remove `SocketsHttpHandler.ConnectTimeout` and revise #323's rationale

**Status:** ⬜ Not started

Letting the attempt timeout own the per-attempt budget supersedes #323's "one budget, owned in one
place" reasoning. Revise that comment in `Program.cs` **and** #323's plan doc to state the new
arrangement — do not leave two contradicting rationales in the tree.

Gated on step 1: if the retry does not handle the attempt-timeout shape either, this removal is wrong.

### 10. Leave `HappyEyeballsConnector` and the fallback contract alone

**Status:** ⬜ Not started

The connector (#325) sits below the handler as a `ConnectCallback`, so each retry re-runs the
IPv6/IPv4 race on its own. Once retries are exhausted the warning is logged and the local copy is
used, exactly as today — this issue changes how hard and how fast we try, never what happens when we
give up.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ❌ | The observed connect-timeout failure shape is retried, not treated as terminal | Unit test | `SourceRefreshResilienceTests.ConnectTimeoutShape_IsRetried_NotTreatedAsTerminal` |
| 2 | ❌ | Transient failures are retried up to five times, then succeed | Unit test | `SourceRefreshResilienceTests.TransientFailures_RetriedUpToFiveTimes_ThenSucceeds` |
| 3 | ❌ | A first-attempt success makes no retry | Unit test | `SourceRefreshResilienceTests.FirstAttemptSucceeds_NoRetryIsMade` |
| 4 | ❌ | All attempts failing still falls back to the local copy and logs a warning | Unit test | `SourceRefreshResilienceTests.AllAttemptsFail_FallsBackToLocalCopy_AndLogsWarning` |
| 5 | ❌ | A retried download reports how many attempts it took | Unit test | `SourceRefreshResilienceTests.RetriedDownload_ReportsTheNumberOfAttemptsItTook`, `...FirstAttemptSuccess_ReportsExactlyOneAttempt` |
| 6 | ❌ | Statistics carry elapsed time and bytes downloaded, and report no bytes when nothing transferred | Unit test | `SourceRefreshStatisticsTests.CompletedDownload_ReportsElapsedTimeAndBytesDownloaded`, `...UpToDateSource_ReportsNoBytesDownloaded` |
| 7 | ❌ | Statistics are emitted on one log line per source | Unit test | `SourceRefreshStatisticsTests.ResolvedSource_LogsItsStatisticsLine` |
| 8 | ❌ | Downloads run in parallel, and a slow source does not delay the others | Unit test | `SourceCacheUpdaterParallelTests.ResolveAsync_MultipleSources_DownloadsOverlapInTime`, `...ResolveAsync_SlowSource_DoesNotDelayTheOthers` |
| 9 | ❌ | Results keep batch and file order regardless of completion order | Unit test | `SourceCacheUpdaterParallelTests.ResolveAsync_ParallelDownloads_ResultsKeepBatchAndFileOrder` |
| 10 | ❌ | Concurrency never exceeds the configured cap | Unit test | `SourceCacheUpdaterParallelTests.ResolveAsync_MoreSourcesThanTheCap_NeverExceedsMaxConcurrency` |
| 11 | ❌ | The live container's named client carries the configured resilience values | Unit test | `SourceRefreshHttpClientWiringTests.NamedClient_ResolvedFromTheLiveContainer_CarriesTheConfiguredResilienceValues` — through the real DI container, per `ChangelogDatabaseWiringTests`' precedent |
| 12 | ❌ | Each configured value has a named constant whose XML doc states its measured basis | Live | Read the constants; each cites the measurement, not a bare number |
| 13 | ❌ | `SocketsHttpHandler.ConnectTimeout` removed, and #323's rationale revised in both `Program.cs` and its plan doc | Live | No contradicting rationale remains in the tree |
| 14 | ❌ | The package version is declared in `Directory.Packages.props` with no inline `Version` | Unit test | `RepositoryStructureTests.PackageReferences_DoNotCarryInlineVersions` |
| 15 | ❌ | A real refresh retries, parallelises, and reports statistics | Live | T1 + T2: startup log shows overlapping downloads and one statistics line per source |
