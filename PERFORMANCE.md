# Performance baseline (Steam History Edition focus)

This repository includes an **opt-in synthetic performance harness**. It does not open or redistribute game files and does not change any patching logic.

## Run

```sh
git submodule update --init --recursive
dotnet run --project Tests/S6Patcher.Tests.csproj -c Release -- --benchmark
```

The normal C# regression suite is unchanged when `--benchmark` is omitted. No performance threshold is part of CI, because shared CI runners vary in storage, CPU load and hardware.

## Measurements

- `ResolveCaseInsensitivePath`: 400 deliberately case-mismatched paths in ten directories, compared with direct `File.Exists` on matching-case paths. The latter is only a contextual reference, **not** a drop-in semantic replacement.
- `P10 Begin + Commit`: snapshot and cleanup of a synthetic 4 MiB executable, `Options.ini` and 32 small mod files.
- `P10 Begin + rollback`: snapshot followed by full restoration of the same files.

Five timed repetitions follow a warm-up. The harness prints median and range in milliseconds and per-thread managed allocations. File contents and snapshots remain synthetic. It deliberately avoids modifying the real game, changes neither ModLoader concurrency nor recovery semantics, and is not a game-FPS benchmark.

## Interpreting the results

Repeat the command several times in a stable environment; record storage medium, CPU, OS, runtime and warm/cold cache conditions. Compare against the same synthetic fixtures and command on the baseline commit before calling an optimization faster. A cache warmer is not representative of first-run behavior. File-copy work includes filesystem cache effects, and thread allocations do not capture all native/filesystem costs.

Follow-up work may instrument the actual Steam History Edition mod generation after a legally owned **isolated installation** becomes available. Any path cache, streaming file restoration or parallelism tuning requires before/after numbers and the complete regression suite, including forced-crash recovery, to pass.
