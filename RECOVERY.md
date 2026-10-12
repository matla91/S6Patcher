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
