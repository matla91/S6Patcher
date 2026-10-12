# S6Patcher

An independent, community-minded fork of [Eisenmonoxid's S6Patcher](https://github.com/Eisenmonoxid/S6Patcher), focused on **The Settlers VI: Rise of an Empire – History Edition (Steam)**.

<p align="center">
	<img src="https://github.com/Eisenmonoxid/S6Patcher/blob/master/Features/Header.jpg?raw=true" width="60%" height="60%" alt="Header"/>
</p>

S6Patcher is an unofficial tool for fixing bugs and enabling additional features in *The Settlers VI: Rise of an Empire*. This fork builds on the original patcher to make its foundations safer and, over time, explore ways to make the game more beautiful, lively and immersive—without losing what makes it *Settlers VI*.

**Development status:** The fork's reliability changes are currently under review and automated testing. **There is no downloadable release of this fork yet.** See [Current status](#current-status) and [Downloads and usage](#downloads-and-usage) before patching your game.

## Why this fork exists

Settlers VI is at its best when you can simply watch your city live: settlers going about their work, warm medieval streets, changing seasons, distinctive buildings and landscapes worth exploring. The original campaign and single-player experience are at the heart of this project.

The goal is **not to redesign the strategy game**. Changes should feel at home in its world, respect its art direction, remain optional where practical, and never sacrifice stability for spectacle. The long-term plan is incremental: first make the existing patcher dependable, then investigate visual improvements and subtler ways to experience the world.

### Credit and independence

**Eisenmonoxid created the original S6Patcher**, including its existing feature set, patching tools and illustrated documentation. Thank you for building and sharing such a valuable foundation. The fixes and experiments developed in this fork are separate contributions; they should not be attributed to the original author.

This is an **independent, unofficial fan project**. It is not affiliated with or endorsed by Ubisoft, Blue Byte or the original game's developers. The game and its assets remain the property of their respective rights holders.

## Features inherited from S6Patcher

The original patcher already offers a wide range of improvements. Examples include:

- **Camera and presentation:** increased strategy-camera zoom, an existing first-person walking mode (toggle with **Z**) and an optional day/night cycle.
- **Interface and quality of life:** an in-game options menu, unit-control improvements and configurable settings.
- **Gameplay and campaign fixes:** corrections to existing missions and other long-standing game issues.
- **Modding tools:** the existing ModLoader, optional bugfix mod and archive utilities.
- **Edition-specific features:** further options for the original release, History Edition and map editor.

These features **come from the upstream project**; they are not new features invented by this fork. Availability and behavior depend on the edition and selected options. In particular, the first-person camera already exists, but its reliability in this fork still needs testing in the actual game.

**Explore the original illustrated documentation:**

- [Complete feature list and original screenshots](https://github.com/Eisenmonoxid/S6Patcher/blob/master/Features/Features.md)
- [ModLoader guide, folder layout and illustrated example](https://github.com/Eisenmonoxid/S6Patcher/blob/master/Features/Modloader.md)

The original images and guides remain maintained by their original author; this fork does not replace them.

## Current status

This fork currently concentrates on **reliability engineering**, especially for Steam History Edition. The following work is implemented in the development branch; automated test success does **not** mean every behavior has been confirmed inside the game.

| Area | Status | What has changed |
| --- | --- | --- |
| Existing S6Patcher features | **Inherited** | Original patching and gameplay options are retained. |
| Executable patching and backups | **Implemented; validation ongoing** | Working-copy patching, safer commit/abort handling, stronger executable checks and non-consuming backup restoration. |
| ModLoader and game resources | **Implemented; validation ongoing** | Safer staging, path/archive validation and recovery mechanisms for failed mod installation. |
| Lua, XML and first-person camera fixes | **Implemented; validation ongoing** | Improved camera exit/cleanup behavior, temporary Lua hook restoration and stricter XML transformations. |
| Diagnostics and recovery | **Implemented; validation ongoing** | More resilient logging, a recoverable patch-session journal for Steam History Edition and fault-injection tests. |
| Automated verification | **Available for developers** | C# regression tests, Lua tests and GitHub Actions on Windows and Linux, including a forced-process-termination recovery scenario. |
| Real-game compatibility and stability | **Still to verify** | History Edition Steam integration, campaign/camera scenarios and recovery after more severe interruptions need hands-on testing. |

For technical evidence and known limits, see [Audit](AUDIT.md), [Implemented fixes](FIXES.md), [Testing](TESTING.md), [Recovery](RECOVERY.md) and [Detailed roadmap](ROADMAP.md). The development work is tracked in [the draft reliability pull request](https://github.com/matla91/S6Patcher/pull/2).

## Project vision

These are **directions for future development, not features available today**. Work will be guided by what is technically possible in the game's engine, how it performs and whether it suits the original atmosphere.

| Phase | Direction | Status |
| --- | --- | --- |
| **1 — A dependable foundation** | Fix existing bugs; strengthen patching, backups, the ModLoader and diagnostics; stabilize first-person mode; test Steam History Edition and prevent regressions. | **Current focus** |
| **2 — Thoughtful visual improvements** | Investigate better textures and loading screens, graphic resources, distant landscape visibility, natural lighting, moving cloud shadows and subtle weather or atmospheric effects. | **Future research** |
| **3 — A more immersive world** | Explore a more natural exploration camera, smoother zoom transitions, distant haze and high clouds, height-aware soundscapes, and environmental audio such as wind, birds, gulls and village activity. | **Future concepts** |
| **4 — Community-friendly modding** | Consider optional graphical packs, community-created maps, compatible immersion features and additional content faithful to the game's world. | **Long-term ambition** |

An **independent remastering workshop** is being developed separately. Its work is **not integrated into S6Patcher** and should not be interpreted as a released graphics upgrade. Advanced rendering methods, including ray tracing, are only possible topics for long-term research—not commitments or claims of engine support.

Single-player and the original campaign come first. Multiplayer may be explored later alongside existing community efforts; it is **not a current development priority**. Compatibility with other mods is not promised without testing.

See [ROADMAP.md](ROADMAP.md) for the engineering priorities and their acceptance criteria.

## Downloads and usage

### Which version can I download?

- **Original S6Patcher:** [Eisenmonoxid's official upstream releases](https://github.com/Eisenmonoxid/S6Patcher/releases/latest). These are **upstream builds**, not releases of this fork, and do **not** contain the reliability changes described above.
- **This fork:** **No release published yet.** The code is available for review and testing, but the development branch is **not recommended as a drop-in stable replacement** for your only game installation.

### Using the original released patcher

The original project's workflow is summarized below. Follow its [feature documentation](https://github.com/Eisenmonoxid/S6Patcher/blob/master/Features/Features.md) for edition-specific details.

1. Launch the game successfully **at least once** so its profile and configuration exist.
2. Check the game's version. The original instructions specify official patch **1.71.4289** (shown in the main menu); do not assume that version number certifies every Steam History Edition executable.
3. Download **the upstream release** linked above and start `S6Patcher.exe`.
4. Select **Choose File** and choose the appropriate executable from **its original installation directory** (such as `Settlers6.exe` or `Settlers6R.exe`; editor builds use `S6MapEditor.exe` or `S6MapEditorR.exe`). **Do not move the executable elsewhere** before patching.
5. Choose the changes you want and select **Patch Game**. When the operation finishes, close the patcher and start the game as usual.
6. If necessary, select the patched executable again and use **Restore Backup**. Keep your own backup of the game and saves before experimenting.

**Steam note:** The original project's instructions state that Steam executables must first be processed with **Steamless**. That requirement comes from upstream and has **not been independently validated for every Steam History Edition build by this fork**. Only proceed with tools and game copies you are legally entitled to use; do not bypass platform rules or assume an untested build is supported.

### What the patcher changes

Depending on the selected features, S6Patcher can modify the game/editor executable, create a `Script` folder containing `UserScriptGlobal.lua`, `UserScriptLocal.lua` and `EMXBinData.s6patcher` under the game's Documents profile, add entries to `Config/Options.ini`, and create `modloader` directories beside the game installation. The exact profile path and ModLoader layout depend on the edition.

The original [ModLoader guide](https://github.com/Eisenmonoxid/S6Patcher/blob/master/Features/Modloader.md) explains these directories and includes an illustrated History Edition example. Avoid manually changing the patcher's own bugfix files.

For development testing of **this fork**, use a separate, disposable copy of the game and profile rather than your main installation. See [TESTING.md](TESTING.md) for the supported synthetic test procedure and the remaining real-game checks.

## Development and documentation

Build with the .NET SDK specified by [`global.json`](global.json), and initialize Git submodules first:

```sh
git submodule update --init --recursive
dotnet build S6Patcher.csproj -c Debug
dotnet run --project Tests/S6Patcher.Tests.csproj -c Debug
```

Lua 5.1 camera and hook regression tests are also available in `Tests/`; see [TESTING.md](TESTING.md) for their commands and CI results.

| Document | Purpose |
| --- | --- |
| [ARCHITECTURE.md](ARCHITECTURE.md) | How the patcher, resources and scripts fit together |
| [AUDIT.md](AUDIT.md) | Issues identified and the evidence behind them |
| [FIXES.md](FIXES.md) | Implemented reliability changes and their limits |
| [TESTING.md](TESTING.md) | Regression coverage and manual verification guidance |
| [RECOVERY.md](RECOVERY.md) | Crash-recovery design, guarantees and known gaps |
| [ROADMAP.md](ROADMAP.md) | Technical priorities and future validation work |

A successful CI run is not a substitute for proving a change works in the actual History Edition game.

## Contributing

This fork is an invitation to collaborate, **not a claim that an established development team or mod ecosystem already exists**. Issues, careful bug reports and pull requests are welcome—particularly for:

- Reproducible Steam History Edition bugs and compatibility testing, especially camera and campaign behavior.
- Small, well-tested fixes to patching, backups, scripts or ModLoader handling.
- Documentation, translations, and tools for inspecting graphics or building optional resources.
- Thoughtful, performance-conscious ideas for improving atmosphere and immersion.

When reporting a bug, include the game edition/build, the steps needed to reproduce it, expected and actual behavior, and relevant logs where safe to share. Please avoid uploading game executables, private paths, saves or licensed assets without permission.

Contributions must be original or properly licensed, respect the upstream authors and third-party creators, and **must not redistribute proprietary game resources without authorization**. Proposed changes should preserve the game's identity and clearly distinguish working functionality from experiments.

For questions about the **original project**, Eisenmonoxid's [community Discord](https://discord.gg/7SGkQtAAET) remains the upstream contact. This fork does not claim to operate that server.

---

*S6Patcher and this fork are unofficial community work. Thanks again to Eisenmonoxid and everyone who has contributed to preserving and improving Settlers VI.*
