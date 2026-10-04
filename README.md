# Pixel Remaster stutter investigation

Experimental Windows modding project investigating movement judder in **Final
Fantasy VI Pixel Remaster**. The current development build automatically manages
three independent corrections: preserving unused tile-movement time, rendering
field motion on a finer grid, and scoped display-paced VSync.

**This remains a prototype.** The underlying cardinal/diagonal walking algorithms
have targeted live evidence. The new automatic lifecycle and playthrough debug
mode pass offline checks but still require in-game transition and overhead checks.
Unknown cinematic/battle/camera layouts suspend unsupported corrections.

Current components: **Timing 0.5.1**, **Grid 0.8.1**, optional **Playthrough
Diagnostics 0.1.1**. F9 enables/disables automatic corrections; F10 toggles debug
recording; F11 marks an incident. Smoothing requires CRT off. Debug recording is
optional and cannot gate correction behavior.

**[Runtime controls, architecture, installation, rollback and verification](docs/runtime.md)**

The repository contains source, tests and investigation notes. Game assemblies,
assets, generated interop assemblies, native dumps, recordings, downloaded tools
and build outputs stay local. Building requires your own game installation with
BepInEx and generated interop assemblies. References to `artifacts/` describe local
evidence not included in this repository.

See [Quick start](#quick-start) for the local toolchain. The history below records
prior experiments and their old controls; use the runtime guide for current use.

## Investigation history

Local development and reverse-engineering workspace for FINAL FANTASY VI Pixel Remaster.
Setup builds the diagnostic plugin and creates analysis outputs. Installation
is a separate `Deploy-Diagnostics.ps1` step. The plugin observes movement and camera
updates. Version 0.2.0 caused camera instability and has been disabled. The replacement
0.3.0 introduced LateUpdate polling without native patches; its first capture succeeded
and the user confirmed normal camera behavior before and during recording. Version
0.3.1 adds a read-only render-layout snapshot; the user confirmed its capture kept
the camera normal. The separate 0.2.1 rendering experiment has been withdrawn after
the user reported CRT/background regressions, possible light judder and texture warble.
Its DLL and runtime evidence are preserved under `artifacts/disabled-plugins/`.
The passive logger remains installed; FFPR Fix remains disabled. The separate native
map-scroll diagnostic successfully applied fractional coordinates through the original
renderer, but the user observed no noticeable improvement. It has also been disabled
and archived. Neither experiment is a verified smoothing fix.

`PRStutter.GridExperiment` 0.1.0 established acceptable stationary appearance with
CRT off in the tested scene, but did not preserve the stock CRT appearance.
Version 0.2.1 successfully reduced judder in the user's test, with residual judder
remaining. The current 0.7.0 grid test requires CRT OFF: **F9 is 4x fractional
motion (1280x720), F10 is 8x fractional motion (2560x1440)**. Both use the same
parent-camera translation and player body/head/shadow offsets before drawing.
Gameplay positions, speed and the logical follow camera are unchanged. Either key
stops; maximum 15 seconds. Ordinary cardinal and diagonal walking with a freely following
camera are supported by the test build. The first diagonal capture confirms three
successful diagonal carries; the user reports smooth turning and normal stopping.
With Timing 0.4.0 installed, **the panel button or F4 starts/stops a coordinated
15-second timing + pacing + 8x test**, with visible states/countdowns and correction
count. F4 activation is verified in the preceding build; the panel button is also
available. The user could not distinguish 4x
from 8x; full-game visual/performance compatibility remains unverified.
Version 0.2.0 aborted every F10 attempt before rendering: it translated child cameras
separately from their parent. Version 0.2.1 moves only the parent, verifies inherited
child positions, and includes a hierarchy regression test. The reported lack of
improvement in 0.2.0 was not a completed fractional-motion comparison.

## Quick start

Use **PowerShell 7**, from this directory:

```powershell
# Repeat the full setup, using cached downloads when present.
./scripts/Setup.ps1

# Rebuild the plugin against the currently installed BepInEx assemblies.
./scripts/Build.ps1

# Verify the toolchain and compare recorded game files against the latest baseline.
./scripts/Verify-Setup.ps1 -CheckBaseline
```

Scripts which read the game accept `-GameDirectory` if the installation moves.
The default is `D:\SteamLibrary\steamapps\common\FINAL FANTASY VI PR`.
PowerShell 7 and a system .NET 6 runtime are prerequisites on another machine.
They already exist on this machine. No machine-wide PATH changes are required.
Pass `-BaselinePath` to `Verify-Setup.ps1` to select an older baseline directory.

### Verification

After toolchain setup, create a local Python environment for analysis tests and
optional plots (the current environment uses Python 3.13):

```powershell
python -m venv .tools/plot-python
./.tools/plot-python/Scripts/python.exe -m pip install -r scripts/analysis-requirements.txt
./scripts/Test.ps1 -PythonExe ./.tools/plot-python/Scripts/python.exe
./scripts/Test-TimingExperiment.ps1
./scripts/Test-GridExperiment.ps1
./scripts/Test-PlaythroughDiagnostics.ps1
```

Pass `-GameDirectory 'PATH-TO-GAME'` to the test scripts for a different installation.
The offline checks verify policies, conservation, restoration and recorded-data
analysis. They do not establish full-game compatibility or final display smoothness.

## Original investigation setup

FFPR Fix was disabled on October 2, 2026, at the user's request. Its DLL was moved
outside the game to `artifacts/disabled-plugins/FFPR-Fix-20261002-152825-653/FFPR_Fix.dll`.
`artifacts/ffpr-fix-state.json` records the exact original/disabled paths and SHA-256.
The original configuration remains unchanged in the game; BepInEx remains installed.
The baseline before installing diagnostics is `artifacts/baselines/20261002-152825-701` (119 recorded files).
The earlier 120-file baseline is retained as historical evidence from before disabling.

PR Stutter Diagnostics 0.2.0 was removed after the user reported an inverted/unstable
camera. The user confirmed normal camera behavior after removal. Its DLL, source,
log, and capture are preserved under `artifacts/disabled-plugins/PRStutter.Diagnostics-regression-20261002-205145/`.
After rollback, no plugin DLLs remained and the user confirmed normal camera behavior.
All 119 files recorded in the pre-diagnostics baseline still matched. Version 0.3.0
passed a staged idle/recording test. Version 0.3.1 completed a render-layout capture
with 898 samples, zero drops, and normal camera behavior confirmed by the user.
`artifacts/diagnostics-deployment.json` records the current deployment hash
and runtime verification status. FFPR Fix remains disabled.

To restore later, close the game and move that preserved DLL back to
`BepInEx/plugins/FFPR_Fix.dll`, checking that no replacement DLL already exists there.
Record a new baseline after an intentional plugin change.

## Pinned tools

| Tool | Version | Purpose |
| --- | --- | --- |
| .NET SDK | 10.0.401 | Compile the .NET 6 plugin against the installed BepInEx host |
| ILSpy CLI | 11.1.0.9782 | Inspect managed assembly types and interop wrappers |
| Il2CppDumper | 6.7.46, .NET 6 build | Recover metadata and native method addresses |
| Eclipse Temurin JDK | 25.0.4.1+1 | Run Ghidra |
| Ghidra | 12.1.4 | Inspect and decompile native code |
| PresentMon CLI | 2.6.0 | Capture frame presentation timing |

Tools live under `.tools`; package caches and preferences stay under `.cache` and
`.state`. `tools.local.json` records download URLs and hashes. Setup verifies the
publisher's SHA-256/SHA-512 digest where published. The older Il2CppDumper asset has
no publisher digest; its locally calculated hash is recorded, not presented as an
independent authenticity check. ILSpy's version is pinned in `.config/dotnet-tools.json`.

Official sources: [Ghidra](https://github.com/NationalSecurityAgency/ghidra),
[Il2CppDumper](https://github.com/Perfare/Il2CppDumper),
[ILSpy](https://github.com/icsharpcode/ILSpy),
[PresentMon](https://github.com/GameTechDev/PresentMon),
[Temurin](https://adoptium.net/temurin/releases),
[.NET release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json).

## Analysis commands and outputs

```powershell
./scripts/Snapshot-Game.ps1
./scripts/Dump-Game.ps1
./scripts/Inspect-Type.ps1 -TypeName Last.Entity.Field.FieldEntity
./scripts/Inspect-Type.ps1 -TypeName Last.Entity.Field.FieldSpriteEntity -Interop
./scripts/Import-Ghidra.ps1

# Later, with the game running at a repeatable test location:
./scripts/Capture-FrameTimes.ps1 -Seconds 15
```

- `artifacts/baselines/`: SHA-256 fingerprints of the game DLL, Unity player,
  metadata, BepInEx core/interop/plugin/config files, plus copies of configuration
  and the existing startup log. This is an evidence snapshot, not a full game or save backup.
- `artifacts/latest-dump.json`: location and full input hashes of the latest dump.
- `artifacts/il2cpp/`: type listings, method addresses, `il2cpp.h`, and dummy assemblies.
- `artifacts/ghidra/`: imported native DLL project and verification logs. Whole-program
  analysis and application of IL2CPP method/type mappings remain investigation work.
- `artifacts/setup-verification.json`: checks completed by `Verify-Setup.ps1`.
- `artifacts/captures/`: later PresentMon CSV captures. Live ETW capture may need an
  elevated terminal; setup verifies the CLI only. Input tracking is disabled.

Dummy assemblies contain type signatures and address annotations, **not recovered
method implementations**. Interop assemblies contain managed-to-native wrappers.
Use native disassembly/decompilation to understand actual movement behavior.

The development plugin is `src/PRStutter.Diagnostics/Plugin.cs`. Its Release DLL is
under that project's `bin/Release/net6.0` directory. See
[the investigation and capture guide](docs/investigation.md) for native-code findings,
measurement stages, F8 capture instructions, and interpretation limits. Compilation
does not establish live compatibility or visual improvement. Both rendering experiments
have now been compared in-game and withdrawn; see the recorded outcomes in the guide.

```powershell
./scripts/Test.ps1
./scripts/Deploy-Diagnostics.ps1 # game must be closed
./scripts/Test-RenderExperiment.ps1 -CapturePath 'PATH-TO-CAPTURE.csv'
./scripts/Deploy-RenderExperiment.ps1 # game must be closed; OFF until F9/F10

# Withdrawn upstream diagnostic, retained for reproducibility:
./scripts/Test-NativeScroll.ps1 -CapturePath 'PATH-TO-CAPTURE.csv'
./scripts/Deploy-NativeScroll.ps1 # game must be closed; OFF until F10

# Current CRT-off rendering-grid/movement comparison; previous experiments stay disabled:
./scripts/Test-GridExperiment.ps1
./scripts/Deploy-GridExperiment.ps1 # game closed; CRT off, F9 4x / F10 8x fractional motion, maximum 15s

# Optional read-only shader analysis (local Python environment, pinned dependencies):
./scripts/Setup-AssetReader.ps1
./.tools/asset-python/Scripts/python.exe ./scripts/Inspect-Compositor.py
```

## Initial installation observations

Recorded on October 2, 2026:

- BepInEx log reports `6.0.0-be.697`, Unity `2019.4.26f1`, and embedded .NET `6.0.7`.
- Il2CppDumper reads metadata version 24.4 and selects native IL2CPP layout 24.5.
- FFPR Fix `1.3.1` is installed. Its current configuration sets `PlayerWalkspeed = 0.75`
  and `DisableDiagonalMovements = true`; `Uncap` and `Vsync` are false. These values
  must be accounted for when designing a baseline experiment. Setup preserves them.
- The current type names include `Last.Entity.Field.FieldEntity` and
  `Last.Entity.Field.FieldSpriteEntity`.
- Ghidra's import can emit PE export and Windows resource warnings. Successful
  import and the unique `PRSTUTTER_SETUP_OK_...` marker establish the setup check;
  these do not establish that every function has been analyzed correctly.

Prior investigation: [Stutter fix, FFPR-Fix issue #4](https://github.com/d3xMachina/FFPR-Fix/issues/4).
The maintainer reported that removing movement rounding did not solve judder and
caused softlocks. Treat that as a historical lead to verify on this build.

## Next investigation

Native tracing found per-frame rounded interpolation in ordinary player movement,
before camera following. The diagnostic plugin records logical position, visual
transforms, camera transforms, and timing by polling. Live gameplay and capture
validation of diagnostics 0.3.0 and 0.3.1 succeeded. Compositor compensation caused
visual regressions, while fractional native map scrolling gave no noticeable benefit.
The investigation guide records both results. The user chose to investigate a consistent
higher-resolution rendering pipeline; the existing mod already covers speed alignment.
The stationary grid comparison passed with CRT off in the tested scene, and fractional
visual motion reduced judder. The user could not distinguish 4x from 8x. A subsequent
PresentMon capture found similarly uneven display cadence in both modes at the
roughly 165 Hz desktop refresh rate. Display synchronization helped, but timing
variation remained at both 165 Hz and 120 Hz. Pixel tracking subsequently verified
the smoothing on the tested scenery and exposed discarded movement time at tile
boundaries, corroborated by native code and earlier passive logs. Preserving that
remainder during continuous ordinary walking is the next targeted experiment;
see the investigation guide for evidence and limits.
RenderDoc can be added if the evidence points to render-target quantization; it is
not installed by this initial setup.

Generated game-derived outputs, tool binaries, local paths/configuration snapshots,
and build outputs are ignored by Git. Do not distribute the game's DLL or metadata.

## Historical presentation measurement

**Tile Timing Test 0.4.0 + Grid 0.7.0** provide a **Start all three / Stop all three**
panel button and **F4** for a coordinated 15-second
comparison. It starts timing correction, display-paced VSync and 8x smoothing with
one deadline. The top-left status panel shows the actual on/off states, countdown,
correction count and stop reason. F4 again stops all three and saves the timing CSV.
A component safety stop ends the combined test on the next safe Update/LateUpdate.
Partial startup is rolled back. F6 readback is disabled during the combined test.

F5 still offers a separate 30-second timing experiment. F7/F9/F10 retain their
separate controls outside a combined test; pressing any of them during the combined
test stops all three instead of creating an ambiguous partial combination. Timing
now declares a dependency on Grid 0.7.0, and deployment checks their build hashes.
During cardinal 0.2-second or diagonal 0.2*sqrt(2)-second manual walks, timing lets the original tile completion
finish, advances only the newly queued arrival task once, and requests the next tile
through the ordinary input controller only after the native arrival checks approve it
before the native field/camera update,
and advances only an approved same-direction next tile by the unused time. Fresh
input from the current frame is required. Turns, stops, blocked paths, dash, scripted
movement and frame deltas over 50 ms receive no carry. Loss of control/focus or timeout
stops the session. F5 again stops immediately; committed movement is not rewound.

This is a gameplay experiment with five temporary Harmony hooks, not the passive
logger or a release-ready fix. No hooks are installed before activation. The old
camera/ref-Vector3 hooks are not reused. Offline conservation/guard/mutation checks
pass; the task hook and narrow carry behavior have live evidence below. F4 and the
panel have successful live verification in Timing 0.3.1 / Grid 0.6.0: the user reports the combined result
looks perfect on the tested route at 120 Hz, CRT off. The second run correctly
stopped on diagonal movement. The new 0.4.0 / 0.7.0 build admits adjacent diagonal
tiles with independent rounding correction on each axis. Native movement duration,
input and collision processing are retained. The first 0.4.0 capture verifies 29
corrections (three diagonal), all preserved into the next update, with normal
turning/stopping reported by the user. The panel button and broader gameplay
compatibility remain unverified. Cutscene/battle attempts were refused by manual
control or camera-layout guards; use passive F8 inspection where a field follow
target exists. Arrival/event processing can happen one frame earlier.
In particular, zero `carriedTiles` means no successful
intervention, even if walking appears normal. The first live 0.1.0 test recorded
seven rejected continuation requests and zero carries: native tile-arrival checks
were still pending at the original intervention point. Version 0.1.1 waits for
approved checks and a safe same-frame window before the camera update. If either
is unavailable it records a refusal; it never forces the game's operation gate open.

**Previous live result:** all four 0.1.1 captures applied zero carry. Across 96 tile
completions, arrival monitoring finished on the following frame, after the current
frame's camera opportunity. Two captures kept smoothing active throughout. The
0.1.1 experiment was therefore ineffective on these routes.

**Timing mechanism, introduced in 0.2.0:** identifies the exact arrival iterator created during the
qualifying player update. Before the camera update, it admits that task to the
native scheduler's running list and steps it once. It refuses if another task is
queued, the iterator has already started, or its player/monitor identity differs.
It never drains the whole queue or forces the arrival gate open. A requested wait
receives no carry; the game resumes and cleans up the admitted task normally,
including after F5 stops. The first eleven live captures verified 588 successful
carries, all preserved at the next update, with carried-frame movement error below
0.0000014 game units. Only four captures kept F5/F7/F10 active throughout; differing
15/30/90-second timeouts mixed the other comparisons. This verifies the narrow
movement correction, not perfectly smooth presentation or full-game event compatibility.
Version 0.3.0 adds the coordinated control and status panel to remove this ambiguity.

For the next comparison: relaunch, turn CRT off and load the same safe field area.
Confirm the status panel appears. Walk briefly with the panel showing all OFF,
press **F4**, and walk diagonally for several tiles. All three rows should show ON; the correction
count should increase across tiles. Try another diagonal direction, briefly walk on one
axis, then release movement to check stopping. Press F4 or
the **Stop all three** button to stop/save, or let the shared 15-second countdown finish. Use the route that
kept smoothing active: its camera guard can fire at a scroll clamp within one screen.
The lightweight CSV is saved under `BepInEx/diagnostics/PRStutter/timing/`.

```powershell
./scripts/Test-TimingExperiment.ps1
./scripts/Deploy-GridExperiment.ps1 # Matching dependency first; game closed.
./scripts/Deploy-TimingExperiment.ps1 # Game must be closed.
./.tools/plot-python/Scripts/python.exe scripts/analyze_timing.py 'PATH-TO-TIMING-CSV'
```

Grid 0.5.0 adds **F6**, a bounded pixel-motion diagnostic. At 120 Hz, CRT off,
enable F7, press F6 and walk on one axis for seven seconds. After the first capture
is saved, enable F10, press F6 and repeat; then stop F10 and F7. Avoid menus, map
transitions and diagonal movement. F6 warms up for half a second, records six seconds
(at most 1,024 frames), then saves automatically under
`BepInEx/diagnostics/PRStutter/pixels/`. It reads two 128x64 field-target patches before
final composition, using synchronous ReadPixels because usable async readback was
stripped from the interop API. Readback may disturb pacing; its cost is recorded.
These image measurements must not replace the uninstrumented PresentMon timing runs.

```powershell
./.tools/plot-python/Scripts/python.exe scripts/analyze_pixels.py 'PATH-TO-PIXEL-CAPTURE'
./.tools/plot-python/Scripts/python.exe -m unittest discover -s tests -p test_analyze_pixels.py
```

The analysis matches each patch independently and accepts displacement only when
both confident matches agree. Blank/repeated textures, mismatched images and missing
frames are rejected. It produces a patch preview, motion chart and JSON evidence.

Grid experiment 0.4.0 adds **F7**, an independent 90-second VSync-paced field test.
It sets `QualitySettings.vSyncCount=1`, which bypasses Unity's software cap on desktop.
It retains `Application.targetFrameRate=60` for game code that reads it; it is not a
fully unrestricted/VSync-off mode. F7 again, loss of focus/control, timeout, or quit
releases the override. Start in the same ordinary field-walking scene. This does not
establish high-FPS menu/battle/revive compatibility or prove G-SYNC engagement.
F9/F10 still enable smoothing for 15 seconds independently.

With the game running, CRT off, and grid tests initially off:

```powershell
# Prompts for Windows administrator access for this capture only.
./scripts/Start-JudderMeasurement.ps1 -Elevated -Seconds 75 -DelaySeconds 10
# Keep the game focused. Walk 20s off, press F9 and walk 20s, then F10 and walk 20s.
# After the capture exits:
./scripts/Complete-JudderMeasurement.ps1
python -m unittest discover -s tests -p test_analyze_presentmon.py
```

For a pacing comparison, add `-PacingComparison`: walk stock 15 seconds, press F7
and walk 15 seconds, F9 and walk 20 seconds, then F10 and walk 20 seconds, then F7
off. Start F7 only after returning from UAC; focus loss restores stock pacing.
The analyzer separates both pacing and rendering modes from timestamped log events.

The analyzer aligns PresentMon QPC timestamps with grid-mode log events, excludes
transition margins, and keeps swap chains/runs separate. Raw CSV, logs, metadata and
analysis are in `artifacts/measurements/`. This measures presentation timing, not
on-screen pixel displacement. Optional plots use `scripts/plot_presentation.py`
with a repository-local Python environment containing matplotlib 3.10.7.
