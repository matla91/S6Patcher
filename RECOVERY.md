# P10 — HE Steam patch-session recovery

The HE Steam GUI patching path now creates a disk-backed recovery journal beside the selected executable, named `Settlers6.exe.s6patcher-session`. Before any auxiliary changes, snapshots of the selected executable, the three bundled script targets and Options.ini for every selected profile, and the active `modloader/shr` tree are captured. The manifest is written **after** snapshots and **before** modification. If snapshot preparation fails, patching is aborted.

On a handled exception, S6Patcher closes its executable transaction and restores saved paths. On the next selection of that executable, the program checks for a pending session and restores it before opening the patcher. Successful executable commit is followed by a session committed marker and journal cleanup. The operation deliberately targets HE Steam only.

## Fault coverage

The synthetic C# tests cover restoration of executable, INI, existing mod and a previously absent script; recovery from a simulated restart; committed-session retention; and refusal of an incomplete manifest. The separate mod-directory tests exercise a rename fault, a pending recovery directory and ambiguous states. `Validate.yml` tests Windows/Linux and publishes a trimmed Windows build.

## Remaining failure modes

- **Not a database-grade atomic transaction:** a power cut, OS failure or media failure during snapshot creation, rollback, JSON manifest replacement, or filesystem metadata flushing may require manual recovery.
- **Committed marker ambiguity:** a crash after executable replacement but before recording the committed marker is resolved by rolling the executable and auxiliaries back to their pre-session snapshots on next selection. A crash while committing the marker can require manual inspection.
- **Partial rollback:** restoring directories involves deleting and copying from the durable snapshots. If it fails midway, retain the journal and retry only after investigating the failure; do not start the game.
- **Mod rename marker:** a separate `.s6patcher-recovery` directory may remain after an abrupt mod swap. Its presence must be reconciled before applying another mod; two competing active versions are deliberately rejected.
- **Other outputs:** HE scaffolding directories created outside `modloader/shr`, user-provided nonstandard scripts, third-party files and other editions are not included. The journal contains original local files and full paths: do not share it.
- **External modifications:** Steam or a second patcher instance must not write the selected files during patching or recovery. The mechanism does not enforce a global interprocess lock.
- **Validation level:** synthetic fault/restart simulation and CI are not evidence that abrupt process termination, power loss or game loading are safe. Verify using an isolated copy of the legally owned game.

Keep the feature in draft until an isolated Windows process-kill/relaunch matrix and actual History Edition Steam integration checks are completed.

## Follow-up safeguards

Before restoring any target, recovery now validates every named snapshot and rejects missing, duplicated or malformed entries without changing the installation. For Steam HE, the EXE snapshot is taken through the already-open transaction stream, because Windows refuses to reopen the exclusively locked executable. The GUI takes the canonical executable path from the transaction, not the mutable path textbox. A separate test exercises the exclusive-lock snapshot, and another verifies that a missing earlier backup causes no partial rollback.

These protections are not proof of durability under arbitrary power loss, concurrent Steam writes or directory symlink manipulation. A forced process-termination matrix on a disposable Windows game copy remains necessary before claiming P10 fully validated.

## Process-termination regression

The C# harness now starts a separate .NET process against an entirely synthetic HE-like tree, waits for its explicit marker after a persisted pending journal and modified EXE/INI/script/mod files, forcibly terminates the child process, then invokes the same recovery method and compares all pre-session contents. The regression runs in the existing Windows and Linux Actions matrix. It exercises abrupt process death after writes, **not** a power cut, mid-write crash, nor an actual installation of the commercial game. Timeout and child cleanup ensure CI does not hang indefinitely.
