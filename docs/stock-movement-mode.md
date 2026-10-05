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
