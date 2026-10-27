# A test environment reports it was removed, but docker could not remove it

**Kind:** diagnostic
**Entry code:** —
**Affected versions:** the repository's test tooling (`scripts/testing/test-env.csx`), not the application
**GitHub issue:** [#461](https://github.com/DutchJaFO/Quotinator/issues/461)

## Symptom

`test-env.csx destroy` prints a docker error and then reports success, and exits `0`:

```
$ docker rm -f qt-startup-04
failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine; check if the path is correct and if the daemon is running: open //./pipe/dockerDesktopLinuxEngine: The system cannot find the file specified.
$ docker volume rm qt-startup-04-data
Removed qt-startup-04.
```

## Does it prevent the app or API from functioning?

**No.** The application is not involved: this is the script that creates and removes the containers
`docs/automated-testing/` runs against. What it costs is a false report, so the container and volume a
test meant to remove may still be there, and the next run of that test starts from what was left.

## Cause

`destroy` ignores the exit code of `docker rm -f` and of `docker volume rm`, and prints `Removed <name>.`
regardless. `docker rm -f` already succeeds for a container that does not exist, so the only failures
ignored are genuine ones, most commonly the Docker daemon not running.

## Remedy

Read the lines above `Removed …`: any docker error there means nothing was removed. Start Docker, then
run `destroy` again, or remove what is left yourself with `docker rm -f <name>` and
`docker volume rm <name>-data`.

## Notes

First observed 2026-10-27 during #327's run of `startup-and-degradation/04`, with Docker Desktop not
running. #461 makes `destroy` exit non-zero when docker fails.
