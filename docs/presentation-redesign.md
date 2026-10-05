# Movement and presentation investigation, 2026-10-04

## Decision

Update: before implementing the presentation replacement, test the narrower
[unrounded shared movement experiment](unrounded-movement-test.md). The design
below remains a fallback under investigation. Logical-position rounding has not
been proven necessary for gameplay; changing it before native collider updates
is materially different from replacing positions afterward.

Keep the existing implementation as an experimental checkpoint. Do not extend
the player-equals-camera residual assumption or deploy another guard-only patch.
The next implementation should calculate each supported entity's presentation
position independently and pass the actual camera target through the native
camera/map mapping. Gameplay movement remains a separate correction domain.

This is an investigated design direction, not a supported runtime. No plugin or
game configuration was changed in this investigation. The offline model is not
referenced by any plugin. A universal hook covering every cinematic, vehicle,
layer and title has not been found.

## What the third FFIV run actually established

Evidence is preserved locally in `artifacts/ff4/third-live/`; recordings overlap
and their counts must not be summed.

- Final capture `20261004-191825-425-54ab435b.json`: 569 recorded frames over
  approximately 9.57 seconds. Timing and pacing remained off, smoothing on
  until quit. There are no movement-correction events in this capture.
- Across the game log: 14 smoothing starts, 10 camera-follow-offset faults and
  two manual-control faults. The first-preparation fix worked: sessions now
  complete render frames, including 1,048 frames in the final session.
- Earlier capture `20261004-191745-373-0ef336ad.json`: 10 tile carries, all
  preserved next frame; 12 midpoint-loss candidates, estimated 51.56 ms lost.
  This supports the narrow carry mechanism, not continuous runtime availability.
- Final recording observer mean cost is about 0.061 ms; its maximum is 16.358 ms.
  The single discovery scan accounts for about 16.319 ms. Removing periodic scans
  helped steady observation, but starting diagnostics still has a measurable cost.

The lifecycle defect is independently visible in source. Per-frame feature guards
can stop on a brief loss of control. `AutomaticRuntime` probes every half second,
so it can miss the unavailable interval. `AutomaticFeature` treats an unexpected
stop as a latched failure even if the current probe finds normal control again.
Smoothing also changes eligibility when the follow-offset assumption fails,
causing repeated allocation/discovery/start/teardown. The resulting performance
impact has not been measured separately from the game.

These are runtime defects as well as algorithmic limits. Pure policy tests did
not cover the real timing relationship between guards and probes.

## Native evidence

Inspection used the exact FFIV/FFVI hashes in `Get-GameProfile.ps1`. Reproduce
the targeted exports with:

```powershell
./scripts/Inspect-MotionPipeline.ps1 -Game FFIV
./scripts/Inspect-MotionPipeline.ps1 -Game FFVI
```

The runs exported 30 and 33 distinct method addresses respectively. Native
decompilation and assembly remain ignored local artifacts; the table records
method RVAs for reproducibility. Decompiled types, aliases and apparent integer
casts are not reliable on their own. Relevant field offsets and clamp operations
were cross-checked against metadata and assembly.

| Stage | FFIV RVA | FFVI RVA | Finding |
| --- | --- | --- | --- |
| `FieldEntity.UpdateEntity` | `0xBDCBA0` | `0xFE9E20` | Linear segment timer/endpoints exist below player-specific code. IV clamps at half duration; both clear time at completion. |
| `FieldSpriteEntity.UpdateEntity` | `0xBDFDB0` | `0xE10200` | IV assembly tail-jumps to the shared base update; earlier inferred C misleadingly expanded that call. VI dispatches the position helper virtually. |
| `FieldCharaEntity.UpdateEntity` | `0x860600` | `0xFE1510` | IV updates collider offset after movement to destination minus current position. |
| `CameraFollowing.UpdateController` | `0x2D1F30` | `0x3487B0` | Reads its target's position, adds map scroll, clamps, adds camera offset, then invokes rendering callbacks. |
| `BaseMapRenderer.UpdateMapScrollIfNeed` | `0x4AD4E0` | `0xB2A9C0` | Separate render mapping clamps input before adding offset; fractional input reaches map-root positioning. |
| `FieldController.UpdateVisualInstancePosition` | `0x2923E0` | `0x31C2A0` | Iterates entity visuals and positions them relative to the camera; contains wrapping, depth and title-specific mapping. |
| `FieldController.ChangeCameraTarget` | `0x276450` | `0x2F94F0` | Can select an entity other than the player, including the scroll dummy. |
| `FieldScrollDummyEntity.GetEntityPosition` | `0xBE6F70` | `0xE0DE60` | Scroll dummy inherits `FieldEntity`; position includes a target-relative offset. |
| `FieldController.UpdateMapScrollOffset` | `0x291ED0` | `0x31BE20` | Separate timed/eased camera offset path. IV also inlines it in UpdateController. |

### Spatial rounding is upstream of presentation

Ordinary movement retains start, destination, duration and timer, then rounds
interpolated XY before assigning the logical transform. A fractional position
can therefore be reconstructed for a matching linear segment without moving
the logical entity. Do not infer matching semantics merely from field names:
native subtype overrides, custom interpolation, attachments and jumps require
admission checks or separate adapters.

IV's sprite update uses the timer for linked animation before tail-calling the
shared movement step. Character update subsequently adjusts the collider. Therefore,
changing logical position after FieldPlayer.UpdateEntity is not an isolated
visual correction. Removing the midpoint clamp globally is also unjustified:
its gameplay purpose remains unknown. Timing repair must account for animation,
colliders and native arrival/event ordering at its own verified boundary.

### The camera is a mapping, not the player's residual

For the inspected base-map scroll operation, each non-looping axis clamps input
to `[-extent, extent]`, then adds camera offset, where
`extent = max(0, mapCellCount * 16 - viewSize) / 2`.
Looping or the explicit bypass flag skips that clamp. The cached map position
retains fractions. Tile-window floor/ceil operations select cells and must not
be removed indiscriminately as if every integer operation were a visual snap.

For a source position P, a reconstructed fractional residual R and mapping C:

```text
camera residual = C(P + R) - C(P)
entity screen correction = entity residual - camera residual
```

The current algorithm effectively substitutes `camera residual = player
residual`. That is only correct in the freely following case. At a boundary the
camera residual can be zero or partial; an actor may still need its full own
correction. A stationary NPC also moves across the screen when the camera moves.

The formula is a unit-scale XY model. It does not license subtracting arbitrary
local-space vectors. World/local transforms, layer scale, wrapping and rotated
views require the game's actual mapping. FFVI has additional `ClampTileSubY`
behavior and layer scaling; FFIV has a bird-view rotation path. Neither is
represented by the simple base-map clamp alone.

### Some cinematic paths share useful machinery

Both games have `FieldScrollDummyEntity : FieldEntity`, and camera target switching
can select it. This supports a path to shared presentation for some scripted
pans. Separately, IV's map-scroll-offset update uses a timed easing function and
Vector2.Lerp; preserve that already-fractional input and its easing.

This does not prove all cinematics use those paths. The controller also invokes
bird-view rendering and event updates; late entity updates write material
position parameters. Re-running the whole controller with temporary gameplay
positions would risk repeating native side effects. Do not use that as a shortcut.

### Sampling remains a separate problem

The native map path already accepting fractions explains why the earlier native
scroll test could read back fractional input without visibly resolving judder.
A 320x180 target still samples a coarse grid. At 80 units/second and 60 Hz, snapping
to that grid necessarily produces alternating one- and two-unit steps.

Retain an explicit presentation-resolution/filtering policy. Higher resolution
is one available mechanism, not a magic speed multiplier. CRT still needs a
separate treatment; moving corrections upstream does not automatically repair it.
Likewise, positional reconstruction does not restore discarded movement time.

## Proposed boundaries and next acceptance gate

1. **Movement timing:** retain native ownership of collision, arrival and script
   scheduling. Repair time consumption separately, with title-specific handling
   of IV's midpoint boundary. No extrapolation through an unapproved next segment.
2. **Presentation math:** consume immutable movement segments and native camera
   inputs. Produce independent desired entity and camera poses. No Unity objects,
   logging dependency, allocations of render targets, or gameplay writes here.
3. **Game adapters:** identify supported entities, their visual roots/shadows,
   camera target, native clamp/offset inputs and layer mapping. Verify current
   native output against the model before applying a correction. Unknown mappings
   suspend locally with a reason; they do not repeatedly rebuild the whole scene.
4. **Render ownership:** apply presentation changes after native visual positioning
   and required material updates, before culling. Restore only owned values.
   The exact live ordering and full texture/layer consumer set still need proof.
5. **Lifecycle:** distinguish temporary ineligibility, unsupported topology and
   execution/cleanup failure. Brief control loss must recover even between probes.
   Render resources belong to a scene/layout lifetime, not each clamp transition.
6. **Diagnostics:** observe availability, prediction disagreement and bounded
   timing-loss events through optional value records. No recorder dependency in
   correction policy. Use PresentMon/video separately for delivered frames/pixels.

Before another installed build, demonstrate native-output agreement for each
adapter and add lifecycle regressions for short control loss, camera clamps,
scene replacement and cleanup failure. Existing captures lack native clamp
inputs, complete visual-root poses and layer mappings, so they cannot prove that
agreement. A future bounded read-only probe can gather those fields without
asking the user to find another guard failure through repeated walking tests.

First live scope should include an ordinary player, one moving NPC under a
stationary camera, crossing a clamp and a scroll-dummy pan. This is a behavior
matrix, not a claim that every map or cinematic needs individual patching.

## Offline verification completed

`scripts/model_presentation.py` and ten design regressions check independently
computed ideal screen geometry against corrected rounded geometry. Cases cover
free follow, static/clamped/partially clamped cameras, independent actors, a
scroll-dummy source, native offset ordering and variable intervals at 30–360 Hz.
They also demonstrate that this math does not fix midpoint time loss or coarse
raster sampling. Arbitrary segment lengths and durations are exercised; this
model is not restricted to 16 units in 0.2 seconds.

As an additional local replay, all 813 active player-segment samples in the
19:17:45 capture match the rounded linear-position model. Among 1,247 consecutive
player/CameraFieldMain pairs with matching dimensions and identities, 64 have
unequal player/camera steps; in 57 the player moves while the camera stays still.
This supports separating their motion. It does not identify the camera mode or
prove the clamp mapping: this replay assumes the recorded world XY and movement
endpoints are aligned. Its report is `artifacts/ff4/third-live/presentation-replay.json`.

All 49 Python tests pass, including existing capture-analysis tests. Both native
inspection commands succeed. These are offline research checks; no claim of
native hook compatibility, GPU behavior, display smoothness or completed general
runtime follows from them.

## Next implementation checkpoint

The separate [presentation adapter audit](presentation-audit.md) now observes
the native visual-positioning boundary and records camera/map/entity predictions
without applying corrections. It replaces neither the old smoothing algorithm
nor its lifecycle. The first FFIV capture now demonstrates exact native XY
agreement for evaluated ordinary field camera/map/entity observations, including
moving NPCs and intervals with a moving target and stationary camera. See the
audit report for counts and exclusions. The subsequent FFVI capture includes
sustained scroll-dummy pans and independent NPC motion: all 5,704 evaluated
visual positions match, while camera/map comparisons each have one disagreement
near a map transition. This remains an unresolved failure, consistent with
different update stages exposing different target/camera state. Do not infer a
visible defect or one-frame duration from the sampled recording.

Before mutation, associate native camera inputs and outputs with the same update,
validate coherence at application, and reset stale motion history on relocation
or scene replacement. Add regressions for mismatched generations, transient
disagreement/recovery and replacement without scene-ID exceptions. Visual
ownership and final rendering order remain separate gates. Looping maps,
non-unit layer scales and alternate airship views remain live-unverified.
