# Automatic runtime and playthrough diagnostics

Timing 0.5.0 / Grid 0.8.0 replace the coordinated 15-second experiment with
independent, automatic field corrections. Playthrough Diagnostics 0.1.0 is a
separate optional DLL. The correction algorithms are the preceding tested
cardinal/diagonal algorithms; their lifecycle and observation have changed.

## Controls and configuration

| Control | Effect |
| --- | --- |
| F4 or top-left panel button | Enable/disable automatic corrections; persists in configuration |
| F2 | Enable/disable optional debug recording; persists in configuration |
| F3 | Mark a visible problem while debug recording is on |
| F8 | Original standalone 15-second passive logger, if still installed |

Corrections default to enabled. Debug recording and legacy timing CSV default
to disabled. The configuration files are `BepInEx/config/local.prstutter.timing.cfg`
and `BepInEx/config/local.prstutter.playthrough.cfg`. The timing configuration has
independent Timing, Pacing and Smoothing switches. Edit configuration while the
game is closed. F5/F6/F7/F9/F10 experiment controls are suppressed while the
automatic runtime is loaded, even when F4 has disabled corrections.

CRT must be off for smoothing. Timing and pacing do not require the smoothing
layout to be supported. The status panel shows each component's actual activation
state and suspension reason. Smoothing requires two observed intervals of freely
following movement (discovery is every half second), so it normally activates
after roughly 1–1.5 seconds of walking in a new context.

## Lifecycle and limits

Scene observations include follow/player/controller identities, area and map
renderer identity, display dimensions and ordinary manual-control availability.
Each component owns its existing restoration logic. Loss of control, focus,
camera assumptions or inspected movement conditions stops affected corrections.
The supervisor restarts eligible components after supported context returns.
A startup failure or runtime guard failure is latched for that component until
the relevant context changes; F4 off/on explicitly retries. Failed restoration
must complete before another start. Partial starts are restored independently.

The rendering algorithm still expects the inspected camera/texture/hierarchy
layout. A clamped or static camera suspends smoothing; freely following movement
can make it eligible again. Cinematic panning, scripted character movement,
battle animation and NPC movement are not newly corrected. They are observation
targets for future implementations. Unknown conditions must remain fallbacks,
not reasons to remove the guards wholesale.

## Optional diagnostics

The correction assembly does not reference the diagnostics assembly. It publishes
value-only movement and status records through bounded, exception-isolated
observer channels. A subscriber cannot grant movement permission or hold native
objects. Recording capacity, disabled recording, writer errors and missing
diagnostics do not stop the corrections. The debug DLL observes independently
while corrections are suspended; it does not require a field follow target.

The debug sampler discovers active cameras/field entities at most once per second
(also refreshes after a changed correction context), then samples cached references
in LateUpdate. It prioritizes the field camera and player, tracks at most two
cameras/eight field entities, and records camera motion even with no field entity.
It records logical world positions, camera projections, timer/endpoints, correction
records, QPC timestamps and component states. Pure battle actors that are not
FieldEntity objects are not tracked individually. Camera/entity discovery remains
main-thread work and must be profiled in larger scenes.

F3 requests approximately 15 seconds before and 3 seconds after the marker. Fixed
capacity can shorten this window at high FPS/entity counts: 65,536 motion records,
8,192 movement records, 8,192 frame-cost records and 256 state records. It saves
available evidence even when the buffer has only just started filling. Samples are
plain managed values; serialization and writing run on a worker without Unity calls.

Automatic incident candidates include unsupported/fallback states, long game
updates and a movement-conservation mismatch. They are deduplicated, limited to
one per 30 seconds and at most 128 distinct automatic keys per recording session.
F3 bypasses automatic deduplication/cooldown but never creates simultaneous captures.
Only one disk write can be in flight; extra incidents are dropped and counted.
Disabling diagnostics requests a final capture with the accumulated coverage.

Storage is bounded to 128 files / 512 MiB in the dedicated `playthrough` directory,
with a 32 MiB limit per capture. Reaching the budget stops saving; existing files
are never automatically deleted. Archive those files outside that directory to
make room. Failed/incomplete writes have a `.partial` suffix. The bottom status
line reports debug state and writer status. Buffers also report overwrite and
untracked-object counts; overwrite counts include ordinary rolling-buffer turnover.
Coverage totals have a 128-key limit and represent sampled wall time, including
focus loss/long pauses, not exact gameplay-active time.

```powershell
./.tools/plot-python/Scripts/python.exe scripts/analyze_playthrough.py 'PATH-TO-INCIDENT.json'
```

The analyzer reports correction persistence, reconstructed carried-frame error,
coverage, observer cost and per-camera/entity projected motion variability.
Intentional stops/easing and camera transitions can produce variability. These
records are not final rendered pixels and do not prove visible jitter. The sampling
phase is labelled; it does not claim to observe the temporary pre-raster transforms.
No GPU readback is performed. PresentMon remains useful for OS presentation timing;
video or a dedicated pixel capture is needed for final image artifacts.

Overhead counters separate startup allocation, callback time and LateUpdate wall
time (including discovery). Background serialization and cache/GC effects are not
fully represented by those counters. Compare debug on/off under PresentMon before
calling debug mode negligible-cost. F2 off releases the large ring buffers.

## Build, install and restore

Close the game, then run:

```powershell
./scripts/Deploy-Runtime.ps1
# Or skip installing the optional debug DLL:
./scripts/Deploy-Runtime.ps1 -WithoutDiagnostics
```

The script tests components, backs up existing DLLs with a manifest, copies the
matched build and verifies installed hashes. Keep the game closed throughout.
It does not alter the original game files, FFPR Fix state, saves or CRT settings.
`-WithoutDiagnostics` skips installation; it does not remove an already installed
debug DLL. To remove debug capability, close the game and move only
`BepInEx/plugins/PRStutter.PlaythroughDiagnostics` outside the plugins directory.

For a local binary rollback, close the game and restore the matching DLL set from
the backup directory recorded in `artifacts/runtime-deployment.json`. Move the new
optional diagnostics DLL outside the plugins directory if the old set predates it.
Do not mix timing/grid versions. Configuration can persist across binary rollback.

The Git checkpoints are deliberately separate:

1. `8de6aa7`: original timed experiment, with targeted live evidence.
2. `45b3ef1`: movement ownership separated from recording.
3. `71b42b0`: automatic independent activation and guarded recovery.
4. `18f5fe9`: optional bounded playthrough diagnostics and analyzer.
5. The following transition/deployment checkpoint adds integration checks,
   ownership audit fixes and these instructions.

To explore an older checkpoint without changing main, create a local worktree at
that commit and build there. A shared-main rollback should use new revert commits,
not a force push. Local proprietary tool/runtime artifacts remain ignored and
must be configured in another checkout before building.

## Verification status and next live check

Offline suites cover movement conservation at steady/jittered 30–360 FPS,
cardinal/diagonal guards, native task ownership simulation, render restoration,
observer failure/capacity isolation, 800 lifecycle transitions, fault latching,
cleanup retry, incident windows/deduplication, disk failures/quotas and compiled
mutation/dependency audits. Tests model menu/cutscene/battle/focus contexts;
they do not run those actual game scenes.

Automatic runtime behavior, its panel changes and debug overhead have **not yet
been verified in the game**. The earlier live evidence applies to the timed
algorithms, not this new lifecycle. The next live check is:

1. CRT off, load a safe field area. Walk until the three component states activate.
2. F2 on; walk cardinally and diagonally, change direction, then release input.
3. Open/close the menu, lose/regain focus, and return from an available cutscene or
   battle. Check appropriate suspension, original-state restoration and resumption.
4. Use F3 if anything looks wrong; allow three seconds of post-marker recording.
5. F4 off: confirm stock behavior while diagnostics stay on. F4 on: confirm recovery.
6. F2 off: confirm corrections continue, then inspect the saved incident/coverage files.

Static/cinematic camera smoothing may remain suspended. Do not treat this alone
as a failed playthrough: it is a recorded requirement for a later implementation.
