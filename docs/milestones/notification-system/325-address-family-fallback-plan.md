# #325 — Source download: no address-family fallback — a black-holed IPv6 path fails the download even though IPv4 works

**Status:** Waiting for release
**GitHub issue:** #325
**Tiers required:** T1, T2
**Depends on:** #323

> **Next action: close the issue, recording that the fix was reverted as over-engineered.** The steps
> below describe a `ConnectCallback`-based address-family race that was built, shipped to this branch,
> and then removed again on 2026-08-20 without ever being released. **Read "Reverted" below before
> anything else in this document** — the Design, Steps and Verification sections are retained as the
> record of what was built and measured, not as a description of what the application does.

---

## Reverted (2026-08-20) — the fix was disproportionate to the fault

The `ConnectCallback` was removed and the default handler now resolves and downloads the file. What
remains of this issue is `ConnectTimeout` (#323's, raised here from 10 s to 60 s) and, when it lands,
retry (#329).

**The measurement that settled it.** A fresh database with forced downloads, 2026-08-20: both sources
failed to connect and each fell back to its local copy, and the application seeded from those copies
and served 799 quotes normally. Six minutes later the same two hosts, over the same default connect
path, answered in 348 ms and 202 ms. So the condition is intermittent, the failure is already handled,
and the handling is the designed behaviour rather than a gap — `SourceCacheUpdater` logs
*"could not reach … — using local …"* and moves on.

**What the race cost.** Every one of these was introduced by the fix, and none existed before it:

- A hardcoded IPv6 preference that overrode the operating system's own RFC 6724 address-selection
  policy, so a user who deprioritised IPv6 system-wide — the correct remedy for a black hole — got no
  benefit from having done so. Fixed before removal (see below) rather than left wrong.
- A dependency on resolver ordering that .NET's documentation does not describe in either direction.
- `SocketsHttpConnectionContext.DnsEndPoint.AddressFamily` accepted and ignored.
- Connect-cancellation `OperationCanceledException`s on every download, harmless and handled, but
  indistinguishable from a fault in a debugger — they cost a live session's attention before being
  explained.

**What the RFC actually says**, having been read properly the second time: RFC 8305 states an
*assumption* that the host's preference policy favours IPv6. It does not instruct an implementation to
hardcode family order — that preference is RFC 6724's, applied by the operating system and
reconfigurable by the user, and the resolver's returned order is how it speaks. The original reading of
"IPv6 first" as a prescription was wrong.

**What is kept, and why.** `HappyEyeballsConnector` and its seven tests remain in `Quotinator.Data`,
unused by `Quotinator.Api`. The concepts were learned expensively and should not have to be reinvented,
and a library offering a capability its consumer declines to use is exactly ADR 004's shape. The
address-family ordering bug above was fixed before the connector was set aside, so what is retained is
not quietly wrong. `SourceCacheHttpClientTests` keeps a test asserting the *absence* of a
`ConnectCallback`, because taking over connection establishment is an easy thing to reach for again the
next time a download misbehaves.

**Why the timeout moved instead.** #323 chose 10 s only as a safe finite value when the real defect was
an infinite budget; it was never measured as correct. A short budget converts an intermittent path into
a failure that did not have to happen, and lengthening it costs nothing user-visible because the
startup wait page (#280) already tells the user work is in progress. Connect is now 60 s and the
request budget 90 s — the latter must stay above the former, or the request cancels first and
`ConnectTimeout` never applies, which is the defect #323 fixed.

**Commits:** `c7aac63` (revert), `00c35dd` (timeouts), `1f44ea8` (ordering fix in the retained code).

---

## Background

Found while re-checking #323 against a live startup log the developer supplied. #323's fix was working
exactly as designed — the failure had moved from the 30 s request timeout to the 10 s `ConnectTimeout`,
thrown from `InjectNewHttp11ConnectionAsync` — but the sources were still reported unreachable. #323
bounded the *duration* of the symptom; this issue is its *cause*.

`SocketsHttpHandler` walks the addresses resolved for a host in OS-provided order and never races address
families. Where IPv6 is routed but unreachable — a default route exists, so SYNs go out and are silently
dropped instead of failing fast — the whole connect budget is spent on IPv6 and the working IPv4 addresses
are never tried.

## Measured root cause

Same host, same moment, `ConnectTimeout = 10s`:

```
DNS order as .NET sees it:
    InterNetworkV6  2606:50c0:8002::154   <- tried first
    InterNetworkV6  2606:50c0:8000::154
    InterNetworkV6  2606:50c0:8001::154
    InterNetworkV6  2606:50c0:8003::154
    InterNetwork    185.199.111.133       <- never reached
    InterNetwork    185.199.110.133
    InterNetwork    185.199.109.133
    InterNetwork    185.199.108.133

app default (dual-stack)   FAIL in 10.03s -> ConnectTimeout
forced IPv4                OK 200 in  0.49s
forced IPv6                FAIL in 10.00s -> ConnectTimeout
```

`MultiConnectSocketAsyncEventArgs` in the failure stack is the sequential multi-address walk: one family at
a time, in order, with no concurrent attempt on the other.

**The condition is intermittent on the machine where it was found.** A later run had all three variants
succeeding, including forced IPv6 at 0.06 s. That is not a reason to discount it — the defect is not that
IPv6 is down, it is that the download fails when it is, rather than using the family that works. An
earlier session note claiming "not the network path" was wrong: it generalised from a single raw-socket
probe that happened to land in a working window.

## Verified against the code before planning

- **`ConnectTimeout` is 10 s, not overridden anywhere.** No `appsettings*.json` sets
  `Quotinator:SourceRefreshConnectTimeoutSeconds` or `SourceRefreshTimeoutSeconds`, so #323's default
  applies as written.
- **An IPv6 default route exists** on the machine where this was found, which is precisely the black-hole
  shape: with no route at all the connect would fail fast with "network unreachable" instead of hanging.
- **No ADR governs HTTP connection behaviour** — `docs/architecture-decisions/` has nothing on
  `HttpClient`, `SocketsHttpHandler`, or connection pooling.
- **`Quotinator.Data` is the right home and needs no new package.** `Socket`/`SocketsHttpHandler` are BCL
  (`System.Net.Sockets`/`System.Net.Http`); connection racing carries no domain knowledge, so ADR 004's
  domain-agnostic boundary is unaffected.

## Design

`Quotinator.Data/Http/HappyEyeballsConnector.cs` (new folder, namespace `Quotinator.Data.Http`, per the
file-placement rule; `Import/` would be wrong — this is transport, not import).

RFC 8305, reduced to what this project needs:

1. Resolve the host to its addresses.
2. Split into an IPv6 list and an IPv4 list, each keeping DNS order.
3. Run both lists as two concurrent *tracks*. The preferred family (IPv6 when present) starts immediately;
   the other starts after a short **connection attempt delay** (250 ms). Each track walks its own addresses
   sequentially.
4. The first track to produce a connected socket wins. The losing track is cancelled and any socket it
   later produces is disposed.
5. If every address fails, throw an aggregate carrying all the failures.
6. Everything runs under the caller's `CancellationToken`, which `SocketsHttpHandler` has already bound to
   #323's `ConnectTimeout` — so the race inherits that budget and cannot outlive it.

### The testability seam

The connector takes its DNS resolver, its per-address connect function, and the attempt delay as
constructor parameters, with a static default wired to real `Dns`/`Socket`. Tests inject fakes: a
"black-holed" connect that never completes for one family and an immediate success for the other. This
makes every case deterministic and fast, with no reliance on real network conditions, unroutable
documentation prefixes, or a machine's live IPv6 state — none of which behave consistently enough to
assert against.

One live-wiring test stays at the registration level: that `Program.cs` actually installs the callback.
Without it the algorithm could be perfect and unreachable.

### Deliberately out of scope

- **The serialised import is untouched** — this changes how a single connection is established, nothing
  about how files are imported. The developer's constraint that one file's import can affect the next
  still holds and is not affected.
- **No "prefer IPv4" switch.** Demoting IPv6 wholesale would fix this symptom while degrading every
  correctly-configured IPv6 deployment. Racing is the fix; preference is not.
- **The machine's own flapping IPv6 is not a Quotinator concern.** This change makes the app resilient to
  it; it does not diagnose or repair it.

---

## Steps

### 1. Add the red tests

**Status:** ✅ Done

`tests/Quotinator.Data.Tests/Http/HappyEyeballsConnectTests.cs` for the algorithm (fakes injected via the
seam above) and one further case in `tests/Quotinator.Api.Tests/Startup/SourceCacheHttpClientTests.cs` for
the registration. Confirm every one red before any production change.

### 2. Implement `HappyEyeballsConnector`

**Status:** ✅ Done

Per the Design above, with XML `<summary>` on every public member (CS1591 is active in `Quotinator.Data`).

### 3. Wire it into `Program.cs`

**Status:** ✅ Done

Set `ConnectCallback` on the `SocketsHttpHandler` #323 added, leaving `ConnectTimeout` and
`PooledConnectionLifetime` exactly as they are.

### 4. Turn the tests green and re-run the full suite

**Status:** ✅ Done

`dotnet test --configuration Release --verbosity normal -m:1` — all green, and a clean
`--no-incremental` build at 0 warnings.

### 5. Housekeeping

**Status:** ✅ Done

`.editorconfig` `IDE0008` list, `Quotinator.slnx`, changelog entries in `en`/`nl`/`de` lockstep with all
three markdown files regenerated.

---

## Verification checklist

| # | Status | Requirement | Method | Verification |
|---|--------|-------------|--------|--------------|
| 1 | ✅ | A black-holed preferred family falls back to the working family promptly, well inside the connect budget | Unit test | `HappyEyeballsConnectTests.Connect_PreferredFamilyBlackHoled_FallsBackToWorkingFamilyPromptly` — 112 ms, i.e. the 100 ms attempt delay plus the connect |
| 2 | ✅ | When both families are reachable, the first to connect wins and the loser is cancelled and disposed | Unit test | `HappyEyeballsConnectTests.Connect_BothFamiliesReachable_UsesFirstToConnect` |
| 3 | ✅ | A host resolving only to IPv4 connects with no attempt-delay penalty | Unit test | `HappyEyeballsConnectTests.Connect_OnlyIPv4Resolved_ConnectsWithoutDelay` — 0.5 ms |
| 4 | ✅ | A host resolving only to IPv6 connects with no attempt-delay penalty | Unit test | `HappyEyeballsConnectTests.Connect_OnlyIPv6Resolved_ConnectsWithoutDelay` — 0.3 ms |
| 5 | ✅ | When every address is black-holed, the failure is bounded by the caller's token (#323's ConnectTimeout) | Unit test | `HappyEyeballsConnectTests.Connect_AllAddressesBlackHoled_FailsWithinConnectTimeout` — 611 ms against a 600 ms budget |
| 6 | ✅ | The registered client's primary handler actually installs the callback | Unit test | `SourceCacheHttpClientTests.SourceCacheClient_PrimaryHandler_HasHappyEyeballsConnectCallback` |
| 7 | ✅ | No regression across the solution | Unit test | Full suite `dotnet test --configuration Release -m:1` — 3,456 passed, 0 failed; clean `--no-incremental` build at 0 warnings |
| 8 | ✅ | T2 — container starts and refreshes both sources | Live | `docker build` + `docker run`; both sources logged `[Database - SourceRefresh] updated`, no timeout/ConnectTimeout lines, 30 s startup. Baseline smoke green: `/health` healthy, `/version` `1.8.3`, `love`/`Casablanca`/`Churchill` all return items, `Rick&field=character` `NoResults` as documented |
| 9 | ✅ | Live proof against the real URL with the preferred family black-holed | Live | Build a `HappyEyeballsConnector` whose resolver returns `2001:db8::1`, `2001:db8::2` (RFC 3849 documentation prefix — routed nowhere, so SYNs hang exactly as the degraded IPv6 path did) ahead of the real `raw.githubusercontent.com` IPv4 addresses, install it as `SocketsHttpHandler.ConnectCallback` with `ConnectTimeout = 10s`, and GET the vilaboim source URL. Expected: **HTTP 200 in ~1 s**. Measured 2026-08-18: `OK 200 in 1.16s`. Before this fix the same shape failed at the full 10 s `ConnectTimeout` |

---

## Finding — how many of these tests were genuinely red

Stated plainly, because "all expected tests start red" is a claim this project requires to be verified
rather than assumed. Of the six tests, **two were genuinely red against unmodified code**:

- `SourceCacheClient_PrimaryHandler_HasHappyEyeballsConnectCallback` — red immediately, no new type needed
  to make it compile.
- `Connect_PreferredFamilyBlackHoled_FallsBackToWorkingFamilyPromptly` — red against a deliberately naive
  first implementation (a sequential walk in resolved order, reproducing the `SocketsHttpHandler`
  behaviour this type replaces). It hit the guard token, never reaching IPv4. This is the bug.

The other three algorithm tests **passed against that naive implementation** — a sequential walk handles
single-family hosts and both-families-reachable correctly. They are regression guards, not demonstrations
of the defect, and are recorded as such rather than described as red.

The naive-first step exists because the algorithm tests cannot compile without the type, so a compile
error would otherwise stand in for a red test. A compile error proves nothing about behaviour; the naive
implementation makes the failure an assertion failure, which does.

**One test defect was found by this process**, which is the argument for it:
`Connect_AllAddressesBlackHoled_FailsWithinConnectTimeout` used `Assert.ThrowsExactlyAsync<OperationCanceledException>`
and failed on `TaskCanceledException` — a subclass. It would have failed against a *correct*
implementation too. Changed to `Assert.ThrowsAsync`, which accepts the derived type.

**A second test defect**: the algorithm tests originally took their token from `TestContext.CancellationToken`,
which never fires. Against a wrong implementation the run hung indefinitely instead of failing, and the
suite had to be killed. Every test now takes a 5-second `Guard()` token so a wrong implementation fails
its assertion rather than hanging the run.
