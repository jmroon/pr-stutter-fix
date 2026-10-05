# Stock-resolution smooth movement

Stock Movement 0.5.0 preserves enabled intent across scene changes, with separate
lifetimes for timing, pacing and precision. This FFIV/FFVI preview needs live
transition/cinematic testing; offline checks do not establish full-game coverage.

## Controls

- Click **Enable smooth movement (stock resolution)** or press **Shift+F11**.
  It starts OFF on each launch, has no timeout, and can wait for a field to load.
- **Ctrl+F11** records a separate 60-second audit. Keep old F9/F10 controls off.
  Alt+F11 no longer changes resolution.
- The panel separates enabled intent from active/suspended components. Normal
  context loss suspends; eligible context return resumes. Actual startup,
  ownership or cleanup failures latch visibly. Restart the game after a fault.
- Native render targets, camera transforms and materials remain untouched by this
  mode. The 8x implementation is excluded from the stock movement assembly.

## Lifetimes

| Context | Precision | Pacing | Tile-time carry |
| --- | --- | --- | --- |
| Eligible ordinary manual field | Active | Active with live camera | Active |
| Eligible field Event/Message/Gimmick or scripted control | Active | Active with live camera | Suspended |
| Menu, battle, loading, transport, alternate view, focus loss | Suspended | Suspended | Suspended |
| Fault | Independent cleanup; no automatic restart | Same | Same |

Rules use scene state, the field that actually updated, active resources,
map/model/renderer identity, field view and transport state. There are no map-name
exceptions. World maps qualify only if they use the inspected ordinary field
path; transport remains native. Two distinct fresh field-update frames precede
activation in a new context. One void observer postfix supplies the field;
diagnostics are not required. Managed timing acquires the controller from this
field rather than searching unrelated scene objects.

Timing clears pending carry at ownership changes and never transfers time between
maps. It retains manual input/arrival/collision guards and does not step cinematic
task sequences. Precision changes only fingerprinted rounding calls with exact
ownership/restoration checks. Pacing retains the tested scoped VSync policy; this
is not proof of universal refresh-rate independence. Reconciliation runs in Unity
Update; intraframe timing checks also reject changed scene state/map identity.

## Independent diagnostics

PresentationAudit 0.2.0 reads a value-only status contract through reflection. Its
assembly has no correction dependency, mutators or native imports. Capture
failure cannot disable the fix. Recording is optional and off by default.

Each capture holds up to 1,200 spatial samples at 20 Hz and 240 lifecycle samples
at 4 Hz. Spatial rows observe player/NPC/scroll-target positions and camera/map
projection. Lifecycle rows continue without field updates, recording enabled
intent, actual component flags, faults, context, generation, timing session and
suspension reasons. Separate overhead totals/maxima and overwrite counts are
included. Storage and file retention remain bounded.

The analyzer independently validates component flags and fresh field/map/area
association. It excludes carry/motion comparisons across sessions, generations,
mode changes and gaps. Lifecycle sampling can miss brief transitions; its counts
are coverage, not exact durations. A menu-only capture is not a spatial pass.

```powershell
.tools/plot-python/Scripts/python.exe scripts/analyze_presentation_audit.py <capture.json>
```

Captures live in the game's `BepInEx/diagnostics/PRStutter/presentation-audit/`.
CPU evidence establishes code activity and sampled spatial agreement, not final
pixel quality or scanout cadence. Use video/PresentMon when judder persists with
component activity verified.

## Checkpoints and installation

Separate commits on main provide rollback checkpoints:

1. `8b965c8`: independent context and component lifetime policies.
2. `113d212`: persistent enabled intent and field reacquisition.
3. The next diagnostic/deployment checkpoint: lifecycle coverage, strict capture
   analysis and removal of the obsolete diagnostic bundle.

`checkpoint/stock-button` (`f28bdc6`) preserves the pre-lifecycle button build.
`checkpoint/pre-stock-mode` (`39477cf`) preserves the stock/8x comparison.
`checkpoint/pre-unrounded-offshoot` (`fa00647`) remains the requested older fallback.

```powershell
# Close the selected game first.
./scripts/Deploy-ComparisonExperiment.ps1 -Game FFVI
# Or -Game FFIV
./scripts/Restore-ComparisonExperiment.ps1 -Manifest <printed-manifest.json>
```

The installer builds/tests the profile and backs up four DLLs, two configs and
the obsolete PlaythroughDiagnostics DLL before removing that last DLL. This
resolves its dependency warning; Ctrl+F11 uses the independent audit. The new
seven-entry manifest verifies hashes or intentional absence. Restoration also
supports older six-entry manifests and refuses to overwrite changed files.
Backups remain local under `artifacts/`.

Bundle: Grid 0.10.0, Timing 0.7.0, Stock Movement 0.5.0, Audit 0.2.0.
Other PR games require separately inspected binary/metadata adapters.

## Tip playthrough test

Enable once, then capture walking, map entry/exit, menu and return to walking.
Revisit the moogle-cave fall and Vector pan, then return to manual control. Try
moving NPCs with a stationary camera, camera clamps, focus loss/return, battle
return and FFIV transport return. Separate captures are fine; leave the mode
enabled across transitions.

Supported field scripts should retain precision/pacing with timing suspended.
Unsupported scene families should suspend and eligible walking should resume.
Report visible judder, visual changes, faults or failures to resume. Live scene
classification, cinematic benefit, native CRT appearance, panel visibility and
sampling overhead remain unverified until these captures are reviewed.

## October 5 paired refresh measurement

The user reports good walking and graceful resumption, but slight recurring
judder at 165 Hz. Matched FFVI PresentMon/F8 runs used the existing 0.5.0 runtime,
ordinary field area 9, 2560x1440 output and the native 320x180 field target.
Timing logs confirm precision, pacing and manual timing stayed active throughout
the sampled walking in both runs. No runtime changes were made for this test.

| Evidence | 120 Hz run | 165 Hz run |
| --- | --- | --- |
| F8 samples / dropped rows | 1,803 / 0 | 2,474 / 0 |
| Reported display interval, median outside F8 window | 8.3337 ms | 6.0669 ms |
| Reported display interval, p99 outside F8 window | 8.3632 ms | 6.0921 ms |
| Intervals above 1.5x median outside F8 window | 0 | 0 |
| Eligible consecutive moving pairs | 1,546 | 2,111 |
| Max displacement error against speed times Unity delta | 0.0000181 game units | 0.0000240 game units |
| Tile crossings included in those pairs | 58 | 56 |

Pair selection excludes the first/last recording second, frame gaps, identity
changes, stopped/end-point frames, direction/speed changes and deltas >=25 ms.
All admitted pairs had nominal speed 80 units/second; camera displacement matched
player displacement within 0.002 units. This validates the sampled manual path,
not every movement state, cinematic task or final rendered frame.

Presentation spikes of about 100 ms (120 Hz) and 45 ms plus 9.76 ms (165 Hz)
coincide with F8 startup. The comparison excludes one second on either side of
F8 start/stop. Display intervals remain similarly steady inside the remaining
F8 window and outside it. Measured observer-body medians were 0.0119/0.0097 ms;
these exclude startup discovery, callback overhead and asynchronous saving.
The 165 Hz run lost focus/quit near its end; transition margins exclude that tail.
Both runs report composed GPU-GDI presentation: this is ETW evidence, not optical
panel timing or proof that G-Sync was engaged.

The remaining native raster grid is a hypothesis, not a measured final-pixel
result. At 80 units/second and one field texel per unit, ideal 120 Hz sampling
crosses pixel boundaries in alternating 1/2-frame holds. Ideal 165 Hz sampling
mostly needs two frames per pixel, with an extra frame about every 0.2 seconds.
Rounding the captured camera coordinates produces the corresponding change in
hold distribution, but does not prove the game's actual pixel output. Variable
Unity update intervals also remain visible despite steady display intervals;
time-correct logical movement is not proof of equally spaced displayed motion.
These results do not establish a mandatory multiple-of-60 simulation rate.
Final-pixel evidence or a controlled stock-versus-finer-rendering test at 165 Hz
would distinguish the remaining rendering/sampling hypotheses. The previous
stock-versus-8x visual comparison at 120 Hz does not settle the 165 Hz case.

Local evidence (ignored, contains game-derived recordings):

- `artifacts/measurements/20261005-170226-320/`: 120 Hz ETW, motion and logs.
- `artifacts/measurements/20261005-170547-571/`: 165 Hz ETW, motion and logs.
- `artifacts/measurements/analyze-refresh-pair.py`: reproducible paired analysis.
- `artifacts/measurements/refresh-comparison-20261005.json`: paired results.

## Temporary 165 Hz resolution comparison

`PRStutter.ResolutionComparison` 0.1.0 is an optional, independently removable
add-on. It links the previously inspected resolution-only session as source;
the stock movement DLL still excludes that implementation. No changes to
movement, timing, pacing, transforms or camera projection are made by the add-on.

Use FFVI at 165 Hz with CRT **off in both A and B**. Enable Smooth movement,
then press **Ctrl+F11** to record one minute. Walk the same ordinary route in
**A: stock** for about 15 seconds, press **Alt+F11** for **B: 8x**, walk about
20 seconds, then press **Alt+F11** again and repeat A. The add-on also has a
separate A/B button. B must show an increasing completed-frame counter; a refused
or suspended B is not a valid visual comparison. Avoid menus and scene changes.
Keep other settings unchanged and do not enable the historical F9/F10 renderer.

A uses native 320x180 field targets. B replaces only their camera/material
texture bindings with 2560x1440 targets for drawing, restoring bindings after
the compositor. Texture ownership, topology, CRT/effect and draw-order guards
remain from the earlier comparison. Binding cleanup precedes texture release.
It starts in A and returns to A on focus loss, changed field/controller context,
loss of any required movement component, or render validation failure. B does
not resume automatically and does not enable/disable the movement fix. Cleanup
failure is latched and requires restarting the game.

Audit 0.2.1 detects the optional add-on without an assembly dependency. It records
requested scale and completed render frames in spatial and lifecycle samples.
B is explicitly labeled `resolution-comparison-8x` and excluded from stock
spatial passes; its actual rendering must be checked using completed frames and
the movement flags. The separate timestamped `resolution-comparison.log` records
switches, returns to stock and frame counts. Core pacing logs remain unchanged.
The earlier F8 startup hitch makes Ctrl+F11 preferable for this visual A/B test.

```powershell
./scripts/Deploy-ResolutionComparison.ps1 -Game FFVI
./scripts/Restore-ResolutionComparison.ps1 -Manifest <printed-manifest.json>
```

Installation backs up/replaces only the add-on and audit DLLs, verifying that the
three core correction DLL hashes remain unchanged. Restoration removes the new
add-on if it was absent before installation and restores the previous audit.
`checkpoint/pre-165-resolution-comparison` (`40b1bc6`) preserves the preceding
source state. Both profile builds, movement-context checks, scoped ownership and
draw-order checks pass; visual A/B behavior and performance remain live tests.
