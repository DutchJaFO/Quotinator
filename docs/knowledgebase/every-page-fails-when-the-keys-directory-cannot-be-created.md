# Every page shows an error after startup, while /health says the data directory cannot be written

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** 1.9.0-alpha onwards
**GitHub issue:** #454, #455

## Symptom

`/api/v1/health` answers `503`:

```
{"status":"unhealthy","reason":"The data directory cannot be written. This usually means the volume is mounted read-only, or the container user lacks write permission on it. Restore write access to the data directory and restart. A database Reset cannot resolve this: it writes too."}
```

and every page, `/`, `/stats` and `/notifications`, answers `500` instead of the *Quotinator started
with a problem* page. The log carries, once per page request:

```
Microsoft.AspNetCore.DataProtection.KeyManagement.KeyRingProvider[{ Id: 48, Name: "ErrorOccurredWhileReadingKeyRing" }] An error occurred while reading the key ring.
```

## Does it prevent the app or API from functioning?

**Yes.** The application is degraded: every endpoint that reads the database answers `503`, and the
pages that would explain why cannot render. The API reference at `/scalar/v1` and the spec at
`/openapi/v1.json` still answer.

## Cause

The `keys/` folder inside the data directory, where the key ring the pages depend on is kept, could not
be created at startup. Two causes produce it:

- **A file named `keys` occupies the path** in the data directory, so no folder can be created there.
  The rest of the data directory may be perfectly writable, and the database may be healthy; the
  `/health` reason still names the data directory as a whole.
- **The whole data directory cannot be written or created**, for example a volume mounted read-only, or
  a configured data directory below a read-only mount.

## Remedy

Look at the data directory from outside the container.

- **If a file named `keys` is there**, remove it (or move it aside), then restart. The application
  creates the folder and its key ring itself.
- **Otherwise**, restore write access to the data directory: remount the volume writable, or correct
  the container user's permissions on it, then restart.

A database Reset does not help with either cause. With the first cause it runs, and can even turn
`/health` healthy, but the pages keep failing until the `keys` path is free.

## Notes

Found by #327's `startup-and-degradation/10` and `11` (2026-10-23). #454 tracks making the pages render
their degraded form in this state, as they do for every other startup problem; #455 tracks the reason
naming the keys folder rather than the data directory, and a Reset no longer clearing the fault.
