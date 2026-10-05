# Read-only presentation adapter audit

Version 0.1.1 is an independent diagnostic plugin for the inspected FFIV and
FFVI builds. It does not replace or extend the correction runtime. The purpose
is to check whether shared native position equations explain field rendering
before implementing a new correction.

## Capture

Install with the game closed:

```powershell
./scripts/Deploy-PresentationAudit.ps1 -Game FFIV
# Or -Game FFVI
```

The installer builds and checks the selected profile, backs up the audit DLL and
two configuration files, installs only the audit DLL, and disables automatic
corrections and normal playthrough recording. Existing correction DLLs are not
replaced. It verifies installed hashes/configuration and prints the backup
manifest. Changes can be restored with the game closed:

```powershell
./scripts/Restore-PresentationAudit.ps1 -Manifest '<printed manifest path>'
```

Restore refuses files changed since installation rather than overwriting newer
settings. In that case, retain those settings and review the saved configuration
differences. It removes only the exact audit DLL if none existed before.

Load a field area and press **Ctrl+F11** once. The bottom panel should show
`Audit RECORDING`, an increasing sample count and `corrections-disabled`.
The capture automatically stops/saves after 60 seconds; Ctrl+F11 also stops it
early. Quit saves any active capture. Recording does not persist between launches.

For the first check, walk, turn, stop, cross a place where the camera stops
following, and stand near a moving NPC if convenient. An ordinary scene change
is useful. A cinematic is not required for that first check; scroll-dummy pans
remain unverified until one is captured. Do not enable F9 corrections or F10
playthrough recording during this baseline. Ctrl+F11 is separate from the normal
F11 incident marker and leaves F12 alone. No visible smoothing is expected.

Files are saved under the game's
`BepInEx/diagnostics/PRStutter/presentation-audit/` directory. Analyze one with:

```powershell
./.tools/plot-python/Scripts/python.exe scripts/analyze_presentation_audit.py '<capture.json>'
```

An empty or baseline-excluded capture cannot pass. `observed-checks-match`
means only the checks actually evaluated agree; inspect exclusions, entity roles
and observed movement patterns before claiming coverage.

## Observation boundary

The audit adds one Harmony **void postfix** to the inspected
`Last.Map.FieldController.UpdateVisualInstancePosition` method. It observes after
native positioning. It neither skips the method nor changes its arguments or
return value. This is deliberately separate from Playthrough Diagnostics, whose
existing no-Harmony contract remains intact.

There are no gameplay/render setters, timing changes, GPU readbacks, native
imports, whole-scene searches or calls that advance a controller/entity. A
small MonoBehaviour handles the hotkey, timeout and status panel; removing the
plugin unpatches its own hook. The game binaries and save files are untouched.

No correction assembly is referenced. Optional runtime status is read through
cached reflection, supporting both the older FFVI and current FFIV status shape.
If that runtime is present but its status cannot be read, the sample is excluded.
If its master or any component is enabled, the sample is excluded. Runtime
absence is recorded separately; this is not a detector for every third-party mod.

Version 0.1.1 also optionally reads the independent unrounded experiment's status.
Test-on samples are explicitly labeled `unrounded-movement`; their mapping checks
remain evaluable, but the analyzer separates their counts from baseline samples.
Unknown/faulted experiment state is excluded. Fractional logical observations are
reported by condition and entity role. See [the test guide](unrounded-movement-test.md).

## Predictions and raw evidence

Each observation records immutable values for:

- Camera: actual target identity and world position, map-scroll and camera offsets,
  map bounds, loop/clamp flags, native aspect/size, actual camera transform and
  internal position. Predicted camera XY is checked against native output.
- Base map: its own dimensions/view/loop flags, cached scroll and root local
  position. Predicted clamped/offset scroll is checked against that cache.
- Entity visuals: logical world/local position, movement endpoints/timer,
  visual root world/local position and parent identity, role and shadow position.
  Predicted visual XY uses camera-relative mapping, inspected loop-wrap behavior,
  and FFVI's native per-layer scale.

The target may be a player, NPC or scroll dummy. Entity selection uses the
controller's existing location-update list, prioritizes player and camera target,
and rotates through the rest. At most eight entities are sampled and at most
16 list entries examined per observation. No gameplay controller/input permission
is required, so loss of manual control does not by itself stop observation.

Special/bird views and FFVI screen-space entities are explicitly excluded from
their affected checks, while raw data remains available. Missing inputs and
nonfinite values are reported. Depth, shadows, material position parameters,
parallax composition, final draw ordering and GPU output are not validated by an
XY match. In particular, a match is not permission to reuse these formulas for
mutation without establishing visual ownership and the final rendering boundary.

## Bounds and verification

The sampler runs at most 20 times/second for 60 seconds, with no catch-up after
a stall and a 1,200-row bound. It is spatial evidence, not per-frame jitter
telemetry. Observation cost is measured; allocation, callback and disk-worker
effects still need live measurement. All observer exceptions are contained and
stop/save the audit rather than escaping through the native callback.

The writer uses immutable snapshots, one background save, completed-file rename,
32 MiB/file and 512 MiB/128-file directory limits; no captures are auto-deleted.
These storage helpers are shared as source with the existing tested logger,
without taking a runtime dependency on it.

Both game profiles build and pass pure geometry/window checks and compiled
dependency/mutation audits. Tests cover map boundaries, small maps, wrapping,
offset order, layer scaling, exclusions, invalid values and timing bounds.
The analyzer independently recomputes XY errors from recorded expected/observed
values and never counts an excluded comparison as a match. Initial live results
for both titles are recorded below; FFVI includes one unresolved transition
disagreement.

## First FFIV live result: 2026-10-04

Capture `20261005-013702-692-722fc2ee.json` (UTC filename), produced by audit
0.1.0 from checkpoint `ac8a949`, covers 60.008 seconds and 951 observations.
The route starts in Dwarf Castle, visits the underground overworld and airship,
then returns through several castle rooms. Corrections were disabled throughout.
The analyzer reports `observed-checks-match`:

| Comparison | Matches | Excluded | Maximum evaluated XY error |
| --- | ---: | ---: | ---: |
| Camera position | 863 | 88 | 0 game units |
| Map scroll | 863 | 88 | 0 game units |
| Entity visual position | 5,219 | 626 | 0 game units |

Entity matches include 863 player, 3,741 NPC and 615 other-entity observations.
Four distinct NPCs have detected movement across eight sampled pairs, all with
matching visual positions. These are repeated observations, not entity counts.
There are also 105 consecutive target/camera pairs where the target moves while
the camera stays still. The shared mapping agrees across castle room dimensions
of 29x18, 34x34 and 20x17 cells and the 144x144-cell overworld. Dimensions come
from native state; no scene-ID conditions were added.

All 88 excluded camera/map observations occur in alternate view 1 during the
airship portion. The ordinary map is inactive there, so the earlier readiness
gate reports `unready-map`; this does not establish airship rendering support.
The same gate excludes 621 entity comparisons. The remaining five exclusions
are scroll-dummy targets without a drawable root. Their camera/map comparisons
match, but these are isolated transition samples, not a sustained cinematic pan.
No observed map has looping enabled.

The audit survives scene/target changes and resumes evaluated comparisons after
landing, with no recorded fault or ring overwrite. Mean sampled observer cost
is 0.073 ms; maximum is 14.706 ms. Only aggregate cost is recorded, so the peak
cannot be attributed to startup or a specific transition. These values do not
measure complete frame delivery or demonstrate imperceptible diagnostic cost.

This establishes native XY agreement for the observed FFIV ordinary field
behaviors. It does not validate fractional corrections, shadow/material ownership,
final render ordering, timing repair, looping maps, sustained cinematic pans or
FFVI. The subsequent cross-title check is recorded below. Ctrl+F11 can stop a
capture as soon as the route is complete. Raw captures and local
analysis remain in ignored `artifacts/ff4/presentation-audit-first/`.

## First FFVI live result: 2026-10-04

Capture `20261005-014344-411-4633dacc.json` uses the same audit 0.1.0 with
corrections disabled throughout. The user walked in the Narshe cave and continued
into the cinematic before the moogle battle. The 60.011-second recording has
860 field observations, ending at 48.956 seconds. The final interval without
field samples does not establish battle behavior or distinguish battle, menu,
loading and other reasons for absence of callbacks.

| Comparison | Matches | Mismatches | Excluded |
| --- | ---: | ---: | ---: |
| Camera position | 859 | 1 | 0 |
| Map scroll | 859 | 1 | 0 |
| Entity visual position | 5,704 | 0 | 753 |

All matches have zero recorded XY error. Visual matches comprise 110 player,
5,558 NPC and 36 other-entity observations. The 753 exclusions are scroll-dummy
targets with no drawable root, not failed actor mappings. Of those target
observations, 752 camera/map comparisons match and one disagrees. Unlike the
FFIV capture's isolated target switches, this run includes sustained scripted
camera motion: 91 consecutive moving-target pairs use the scroll dummy.
Six distinct NPCs move across sampled pairs, including movement while the
camera stays stationary. Native map dimensions change through 57x66, 29x47
and 15x26 cells. All recorded layer scales are (1,1), loop flags are false,
and ClampTileSubY is zero; other values remain live-unverified.

### Transition disagreement remains a failure

At frame 2375 (14.116 seconds), camera and map both remain at (-32,344),
matching their previous sampled positions. The same scroll-dummy target has
changed from (-32,328) to (-48,312); adding the unchanged (0,16) scroll predicts
(-48,328). Both checks therefore disagree by 16 units on each axis. The player
also relocates in this sample. Its visual, and all other sampled drawable
entities, still agree with mapping relative to the actual camera.

The next sample is 1.105 seconds later, with different map dimensions and
matching positions again. This is consistent with a transition temporarily
exposing newer target state alongside older camera/map state. It is not proof
of the exact callback sequence, a one-frame duration, or a visible glitch.
The analyzer correctly retains `disagreements-found`; no tolerance increase,
scene-specific exception or retrospective exclusion was added.

Existing FFVI native inspection confirms that CameraFollowing.UpdateController
(RVA 0x3487B0) reads the target transform directly. FieldController.UpdateController
(0x31B5E0) performs camera updates before visual positioning, with intervening
work; ChangeCameraTarget (0x2F94F0) also invokes camera updating. A visual-postfix
snapshot alone cannot establish which inputs produced a previous camera update.
The replacement must associate camera inputs/output with the same update and
validate them at the application boundary. On disagreement it must leave native
presentation in control, invalidate stale motion history and resume when coherent,
without rebuilding render resources or disabling unrelated timing/pacing.

No observer fault or ring overwrite was recorded. Mean sampled observer cost
is 0.098 ms, maximum 19.907 ms; aggregate timing cannot locate or explain the
peak. This is spatial evidence, not delivered-frame or diagnostic-overhead proof.
Raw capture, log, analyzer report and coverage breakdown remain in ignored
`artifacts/ff6/presentation-audit-first/`. No runtime was changed for this analysis.

The two captures support shared ordinary-field mapping and this FFVI scripted
camera path. Next work is coherent update snapshots and visual/render ownership,
with regression cases for target relocation and scene replacement. A further
user replay is not needed to identify those implementation requirements. Final
render ordering, shadows/materials, fractional correction and unsupported modes
still require verification before general smoothing can be claimed.
