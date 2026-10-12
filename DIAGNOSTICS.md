# Logging reliability (D05)

The logger is intentionally best-effort. Logging must not alter patching results.

## Storage and lifecycle

- Preferred location: `S6Patcher.log` next to the application, preserving historical location.
- If opening or writing there fails: `S6Patcher/S6Patcher.log` beneath the operating system's `LocalApplicationData` directory.
- Files are **appended**, not truncated; the application does not delete, rotate or upload logs.
- Every successful entry is flushed. Access to writes, recovery and closing is serialized.
- On the first opening failure, fallback is attempted immediately. On an application-log write failure, the same line is retried once at the fallback path. The previous path is not automatically reselected during that process.
- If fallback is unavailable, entries are dropped rather than blocking unrelated patching. Later calls retry opening the fallback. This does not guarantee recording when no filesystem destination is available.
- First failure and every 32nd consecutive error are reported directly to stderr/Trace, not through Logger. Reporting errors are swallowed to avoid recursion.
- Repeated Dispose is safe, and calls after Dispose are ignored. Disposal errors are reported best-effort.

**Limitations:** append and locking protect only one logger instance within a process; two processes using the same file may conflict. A power failure may lose the most recent buffered/disk-cached bytes. Malformed filesystem paths and exhausted disks can defeat both destinations. Logs are local and never transmitted. A failed write may have partially persisted bytes, so retry can duplicate a fragment.

## Regression cases

`Tests/LoggerTests.cs` injects opening, writing, closing and diagnostic-reporter faults. It also checks prior-session preservation, recovery after temporary failure and 128 concurrent entries. These are synthetic test doubles, **not** a measured OS disk-fault or game integration test.

Run using the existing test runner:

```shell
dotnet run --project Tests/S6Patcher.Tests.csproj -c Debug
```

GitHub Actions `Validate.yml` runs the same tests on `ubuntu-latest` and `windows-latest`, builds with analyzers, and publishes a Windows release-build smoke check. Review the workflow run attached to the PR for actual results; neither a simulated write failure nor green CI validates game installation behavior.

## Scope and privacy

The logging API `Logger.Instance.Log(message)` and `Dispose()` remain available. `LogDirectory` provides the active location for troubleshooting, or null while unavailable. Registry path diagnostics no longer repeat absolute paths. Existing callers can still pass arbitrary text; secrets in other caller-provided messages are not automatically redacted. Patching and binary/script operations have not been altered.
