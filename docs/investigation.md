# Native movement findings and first capture

Examined October 2, 2026. Evidence applies to GameAssembly.dll SHA-256
`0029a22ed933aa3b6ea3b1290060502267f514d61ba6e6619308d844557f2ffd` and metadata SHA-256
`f50d9d1ff84f8033b8acbdc6845ab3a0f2793dd253c36a4a7d212303980844dd`.
All addresses below are RVAs, not file offsets. The imported image base is `0x180000000`.

## Confirmed static evidence

| Method/helper | RVA | Observed behavior |
| --- | --- | --- |
| `FieldEntity.UpdateEntity` | `0xFE9E20` | Advances `moveTimer` using delta time, calls the virtual position-update method, then writes transform local position |
| `FieldEntity.UpdateMovingSetPosition` | `0xFEA100` | Interpolates X/Y using `moveTimer / moveTime`, then calls the helper at `0xE86D0` on each component |
| Float rounding helper | `0xE86D0` | Rounds to whole-number values with ties to even; assembly checks fractional ±0.5 and integer parity |
| `FieldEntity.UpdateMoveFinishedSetPosition` | `0xFEA0F0` | Copies destination X/Y |
| `FieldPlayer.UpdateMovingSetPosition` | `0xFF3E60` | Normal path repeats the rounded interpolation; `MoveState.Unique` (7) instead uses fractional interpolation |
| `FieldPlayer.UpdateMoveFinishedSetPosition` | `0xFF3E00` | Normal path copies destination; `Unique` instead computes fractional interpolation |
| `FieldController.UpdateCamera` | `0x31B110` | Passes map-scroll offset to `CameraFollowing.UpdateController` |
| `CameraFollowing.UpdateController` | `0x3487B0` | Reads target transform position, applies offsets and map bounds, updates camera transform and invokes camera/map callbacks |
| `CameraController.SetPosition` | `0xB9FAF0` | Assigns the supplied X/Y/Z values without additional rounding |

The interpolation path is equivalent for X/Y to:

```text
ratio = moveTimer / moveTime
candidate = start + (destination - start) * ratio
localPosition.xy = round_to_even(candidate.xy)
```

The float/double constants used by the helper were checked in the PE image:
`0x1CBFCB8` is float 0.5, `0x1B14B54` is float 1,
`0x1CBFD88` is double 0.5, and `0x1D99068` is double -0.5.
These observations establish a per-frame quantization point before camera following.
They do not yet prove that it fully explains the displayed judder, that one coordinate
unit equals one source-art pixel, or that no later rendering quantization exists.
Camera boundary calculations also use clamp/ceil operations, which should not be
confused with unconditional movement rounding.

## Reproduce the native exports

Run from the development directory in PowerShell 7:

```powershell
./scripts/Export-NativeMethods.ps1
./scripts/Export-NativeMethods.ps1 -MethodPattern '^Last\.Entity\.Field\.FieldPlayer\$\$(UpdateMovingSetPosition|UpdateMoveFinishedSetPosition)$'
./scripts/Export-NativeMethods.ps1 -MethodPattern '^(Last\.Map\.FieldController\$\$UpdateCamera|Last\.Systems\.Camera\.CameraController\$\$SetPosition|UnityEngine\.Mathf\$\$Round)$' -ExtraRva @('0xE86D0', '0x182330')
```

Artifacts are `.c` and `.asm` files under `artifacts/native/`. Decompiler signatures
are inferred and can be inaccurate for structs, floating-point parameters and shared
native implementations. Inspect the instruction listings and metadata together.
No source game DLL is modified by this workflow.

## Logger behavior

Version 0.2.0 caused an inverted/unstable camera in the first live test. Removing its
DLL restored normal behavior, confirmed by the user. The exact failing hook has not
been isolated. A native detour/argument-marshalling problem is a hypothesis, not a
confirmed root cause. The lack of explicit game-state writes did not make those
hooks behaviorally safe. Offline tests had covered storage and analysis, not native
hook compatibility. The affected capture is invalid as a normal-gameplay baseline.

Version 0.3.0 removes all three Harmony patches and the global pre-cull delegate.
It has no Harmony reference. An injected MonoBehaviour reads existing state in
LateUpdate while recording. Camera-follow controllers are discovered once at capture
start using FindObjectsOfType; an ambiguous selection refuses to record. An inactive
or destroyed field camera stops the capture, including ordinary transitions to combat.
It still refuses an unrecognized game DLL/metadata pair. This build is prepared
and installed. Its first staged live test completed, and the user confirmed normal
camera behavior before and during capture.

## First successful polling capture

On October 2, 2026 at 21:07 Toronto time, capture
`20261003-010739-221-748ec159` recorded 897 LateUpdate rows over 15 seconds with
zero dropped samples and no logged exceptions. The user explicitly confirmed that
the camera stayed normal before and during recording. Raw CSV/JSON, startup log,
and a motion-analysis summary are preserved under `artifacts/captures/20261003-010739-221-748ec159/`.

After excluding one second of warmup:

- Unity frame duration: median 16.6668 ms, p95 17.04372 ms, maximum 17.4492 ms.
  These are game-update timings, not measured display presentation intervals.
- All 497 moving samples were walking state 0, with 16-unit destinations and
  approximately 0.2-second move duration. Each sampled local X/Y matched rounded
  interpolation reconstructed from the fields; maximum distance from the unrounded
  candidate was 0.49824 units. This is a LateUpdate correlation, not a native-output probe.
- Camera position matched entity world position plus (0, 16) throughout all 837
  post-warmup samples. All 451 consecutive moving-frame pairs had equal camera and
  entity deltas. Vertical camera steps were 1 unit (271 pairs) or 2 units (180 pairs).
- CameraFieldMain reported a 320x180 render target and orthographic size 90, consistent
  with one vertical game unit per target pixel at that camera. The screen was 2560x1440.
  Its culling mask was zero, so the downstream rendering cameras and composition still
  need tracing before equating these coordinates with displayed pixel displacement.
- Both visualParent and spriteRenderer stayed at world Y=-16 while the entity moved
  from Y=-200 to Y=-8. Those references therefore cannot yet identify the visible
  player sprite. Do not infer player sprite jitter from their projected coordinates.
- Measured observer body cost: median 0.0116 ms/frame, p95 0.02334 ms, max 0.0587 ms.
  This excludes some callback/interop overhead and is not a plugin-free comparison.

This strengthens the case for movement quantization propagating into the camera.
It does not rule out presentation-timing or later render-target effects. The next
implementation decision requires tracing the actual sprite instances and map-render
callbacks/compositing, so a visual correction can be evaluated without altering logical
movement, collision, or event timing. The original rounding-removal softlock warning
still applies.

## Capture operation

Version 0.3.1 adds a `.render.json` companion file: a capture-start snapshot of active
scene camera objects (including disabled Camera components), render-target identities,
dimensions and filtering, projection matrices, transform ancestry, compositor texture
bindings/UV bounds, and body/head sprite renderer ancestry. It uses `sharedMaterial`
instead of `material`, avoiding implicit material instantiation. Snapshot collection
and JSON formatting happen before the measured interval. There are still no game
method patches or direct game/Unity property setters. Its runtime test is pending.

Press **F8** to start a capture (15 seconds by default), or press it again to stop.
It needs an active camera-follow target: first load a field map and walk briefly.
The BepInEx log reports `Diagnostics ready`, `CAPTURE START`, `CAPTURE STOP` and
`CAPTURE SAVED`. If the legacy keyboard API is unavailable, use
`scripts/Start-DiagnosticCapture.ps1` while the game is running.

Captured stages:

- `LateUpdate`: a later Unity callback; ordering relative to other scripts is not guaranteed.

Schema 2 identifies `ObservationMode: LateUpdatePolling`. It samples only the selected
field camera, not every render pass. The native method-output, interpolation-candidate,
and scroll-offset columns remain NaN. Direct rounding comparisons and final pre-render
camera measurements are unavailable. Schema 1's MovementResult, CameraAfter and PreCull
stages are supported for historical analysis; version 0.2.0 always receives an explicit
invalid-baseline warning.

Each row includes entity/controller/camera IDs, movement state, timing, logical and
visual/sprite transforms, camera coordinates, projected entity/sprite coordinates,
and screen/render-target dimensions. Missing values are `NaN`, not zero. A zero
render-target size means that camera renders to the default target.

Samples are held in a bounded 65,536-row buffer. CSV formatting and disk writes
happen after capture on a background task. A completed CSV is published only after
its JSON metadata. An abrupt process exit during recording can lose that active
buffer. Capacity overflow stops the capture and is reported, rather than silently
overwriting old samples. Captures may span map/target changes, but the analyzer does
not combine camera steps across changed entity/controller/movement-state IDs.

Instrumentation has overhead. The measured observer-body time is recorded, but it
does not include every callback/interop cost. The analyzer excludes the first second
by default to reduce start/allocation effects. Unity delta times are not a substitute
for PresentMon presentation times. Camera coordinates and WorldToScreenPoint do not
prove the final sampled pixel displacement; a frame capture may still be needed.

## First in-game test

1. Start FFVI normally and load a field map with a clear straight walking route.
2. Walk briefly so the camera-follow target is established.
3. Press F8 once. Walk steadily in one direction for about 10 seconds, then stop.
   Avoid sprinting, menus, transitions and vehicle changes for this first test.
4. Allow at least 15 seconds total, then wait a few seconds for `CAPTURE SAVED`.

Outputs go to the game's `BepInEx/diagnostics/PRStutter/` directory. Keep the CSV
and its matching JSON together. Run:

```powershell
python ./scripts/analyze_capture.py 'PATH-TO-CAPTURE.csv' --output './artifacts/capture-summary.json'
```

State IDs: 0 Walk, 1 Dush (game spelling), 2 AirShip, 3 Ship, 4 LowFlying,
5 Chocobo, 6 Gimmick, 7 Unique; -1 means the followed entity is not a FieldPlayer.

Build/storage/analyzer checks can run without the game through `scripts/Test.ps1`.
They do not establish live callback coverage or the visual result by themselves.
The first replacement-plugin capture is recorded above. Do not remove rounding as a fix on the basis
of the static findings alone: the earlier modder reported softlocks from doing so.

To disable this logger with the game closed, move its DLL out of
`BepInEx/plugins/PRStutter.Diagnostics/`. The existing FFPR Fix DLL remains disabled
and preserved under `artifacts/disabled-plugins/`.

## Rendering path traced after the successful capture

Additional native exports and the installed shader establish:

- `FieldController.UpdateVisualInstancePosition` (`0x31C2A0`) reads each logical
  entity's transform, combines it with a camera-relative offset, handles looping
  and per-layer scale, and writes the entity's separate visualParent transform.
  It then stores that rendered position in VisualParentPosition. There is additional
  rounding in depth placement; do not confuse those Z calculations with XY snapping.
  The followed sprite can therefore remain fixed in render space while logical
  entity/camera positions move. The earlier stationary sprite observation is compatible
  with this design and is not proof that the renderer is an unused template.
- `FieldCharaEntity.LateUpdatePositionToMaterial` (`0xFDFF50`) supplies logical
  coordinates/offsets to body, head, shadow and ride-on materials. Merely changing
  one transform can leave shader effects or depth-related behavior inconsistent.
- `BaseMapRenderer.UpdateMapScrollIfNeed` (`0xB2A9C0`) combines the followed position
  with camera offset and calls the layer-offset calculation when that position changes.
  `UpdateLayerOffsetBase` (`0xB29E00`) selects map cells using floor/ceil but retains
  the remainder in the map root transform; those floor/ceil calls alone do not prove
  a second whole-pixel quantization of base scrolling. `UpdateLayerOffset` (`0xB2A2E0`)
  rounds the additional per-layer offset before adding the base position.
- The installed `Last/PostProcessLite` shader has separate `_MainGameTex` and
  `_OverlayTex` inputs and independent U/V lower/upper sampling bounds. Extracted
  Direct3D vertex bytecode applies affine UV ranges independently, and the default
  pixel program samples the two textures separately before combining them.
  These shader names alone would not establish behavior; the compiled instructions
  were disassembled with Windows D3DDisassemble to confirm it.
- `FrontRenderTarget.UpdateMainGameUV` (`0x51FD00`) updates the horizontal range via
  PostProcessLite. The similarly named ConvertScreenPos/ConvertMainGamePos methods
  are not the compositor's vertex sampling implementation.

The local shader inspection is repeatable with `scripts/Setup-AssetReader.ps1` and
`scripts/Inspect-Compositor.py`. UnityPy 1.23.0 and its resolved dependencies are pinned
in `scripts/asset-reader-requirements.txt`. The output manifest identifies source
`sharedassets0.assets`, SHA-256
`8d69cb7d1d17c7959f4678c287895542c134ac827a0795404d633e41127d1b29`.
No source game assets were changed; extracted shader code stays in ignored artifacts.

### Candidate experiment and limits

For ordinary walking, reconstruct the continuous visual candidate from the original
start/destination/timer. The difference from the rounded logical position is bounded
by approximately half a game unit per axis. A rendering-only experiment could apply
that residual to the map and character presentation while preserving logical movement.
At the observed projection scale, a 320x180 target only samples one pixel per game unit;
fractional transforms alone may still rasterize as coarse steps. A larger field target
is a candidate to test, with matching correction of all contributing field layers.

A compositor-only image translation is insufficient as a complete fix: it would also
shift the followed sprite that was previously screen-stationary. A camera-only edit
could miss the camera-relative map/sprite conversion. Shared render-target identity,
actual render-camera projection/rotation, and the sprite render layer were confirmed
by the 0.3.1 runtime snapshot described below. General support would also need boundaries, event cameras,
vehicles, layer scrolling, transitions, rendering cost and image sharpness validation.

### Render snapshot and opt-in experiment

Capture `20261003-011906-617-1a524ab6` completed 898 samples with zero drops;
the user confirmed normal camera behavior throughout. CSV, metadata, render snapshot,
startup log and analysis are preserved in the matching `artifacts/captures/` folder.
After a one-second warmup, 239 moving frame pairs showed 144 one-unit steps and
95 two-unit steps. Median Unity frame time was 16.6667 ms, p95 17.03756 ms.
These remain polling measurements, not proof of final presentation timing.

The snapshot identified a shared 320x180 field target, an independent 2560x1440
overlay, and CameraFrontFilter as the depth-99 output camera. Field projection is
orthographic with size 90 and identity rotation. The followed sprite is camera-relative
and shares the field image with the map. CRT filtering is enabled; it is preserved.
Shader inspection also exports all CRT/blur/fade variants; the CRT vertex program
derives its extra main-image taps from the same main UV range, independently of overlay UVs.

`PRStutter.RenderExperiment` 0.1.0 is a separate plugin, **OFF on every launch**.
F9 requests at most 15 seconds of field-image translation; F9 again stops early.
Its own MonoBehaviour on CameraFrontFilter applies main-image UV bounds in OnPreCull
and restores them in OnPostRender, with a next-Update recovery path. There are no native
game-method patches, camera/transform setters, movement changes or speed changes.
Only the four `_MainGameU/VL/H` material values are temporarily overridden.

The correction reconstructs ordinary straight 16-unit walking interpolation and
accepts only positions matching the game's rounded result. Each world-axis correction
is bounded by about half a unit (half a field texel), normalized by 320/180 for UVs.
The experiment stops on unsupported movement, changed target/texture, camera-follow
offset changes, blur, nonstandard timing, or changed UV ranges. It refuses unrecognized
game/metadata hashes and render layouts. These checks support this narrow test scene;
they do not establish general event, map-edge, vehicle or transition compatibility.

Offline checks passed: both axes/directions/endpoints and rejected invalid movement;
replay of all 528 moving samples from the earlier capture and 297 from the new capture;
UV restoration, independent later writes, partial write failures, restore retries and
nonfinite inputs. Compiled-assembly checks found no Harmony/MonoMod imports, P/Invoke,
or game/Unity property setters. Builds had zero warnings/errors. Unity callback execution,
actual sampling direction and visual improvement still require in-game verification.

For the first comparison, load the same field area, keep encounters off, and walk
normally along a straight route. Observe the background before pressing F9, during
the next 15 seconds, and after automatic restoration. Avoid sprinting, menus and
transitions. Watch the background: this whole-field translation also shifts the
followed character, so character wobble is an expected limitation of this diagnostic.
Report whether the background looks smoother, unchanged or worse and whether the
camera stays normal. F8 remains the separate passive capture key.

`artifacts/render-experiment-deployment.json` records the installed hash and pending
runtime verification. To disable completely with the game closed, move only
`BepInEx/plugins/PRStutter.RenderExperiment/PRStutter.RenderExperiment.dll` outside
the game plugin directory. FFPR Fix remains disabled and preserved.

First visual feedback: the user reported no noticeable background stutter, with rapid
character jitter as expected. Video analysis has not yet been performed. The latest
inspected log confirms both plugins loaded without logged errors, but contains no
experiment ON/OFF entries, so it does not independently establish callback counts.
Preserve this as user-reported visual success rather than quantitative validation.

The residual calculation uses `moveTimer / moveTime` on each render callback, with
no 60 Hz constant or fixed per-frame increment. Its mathematics does not require a
60 fps lock. Other frame rates and irregular presentation remain untested; the
experiment follows the game's movement timer and does not interpolate separate
render frames between simulation updates. It also currently stops when a frame's
unscaled delta exceeds 100 ms. Neither plugin changes the game's frame-rate cap.

### Player stabilization experiment 0.2.0

F9 preserves the background-only comparison. F10 now requests player stabilization
for the same maximum 15 seconds. Either key stops an active test; changing modes
requires stopping first. Both modes are OFF on launch. A separate append-only
`BepInEx/diagnostics/PRStutter/render-experiment.log` records startup, mode activation,
stop reason, rendered frame count and faults without per-frame disk writes.

The new mode creates an owned 2560x1440 field RenderTexture with settings copied from
the original 320x180 resource. LateUpdate temporarily redirects the two inspected
field cameras. At CameraTileMap.OnPreCull it reads the final movement residual and
offsets only the player's separate body/head/shadow renderer transforms; OnPostRender
restores those transforms. The final compositor samples the new field image with the
matching UV offset, then restores all target/material overrides. Update is a recovery
path if a render callback is skipped. Owned resources are released after stopping,
outside render callbacks. Original textures are never resized, released or destroyed.

Corrections use increments of 1/8 game unit, making both sides of the cancellation
integer texel shifts in the larger target. The modeled residual background-position
error is at most 1/16 game unit. Body/head/shadow stay in the existing field draw order;
this does not redraw the player over foreground scenery or counter-shift NPCs. Entity
position, movement timing, camera pose, physics, render layers and sorting are unchanged.

The character shader was found in `chara_field_assets_all_8479fd6d778d09a0a00f3a89fdf195aa.bundle`,
SHA-256 `b7d16c38a1caec177b189281d32add6c3e6c62a4a383b2742944a94b94acced3`.
Its default vertex bytecode uses normal object/world/projection transforms without
pixel rounding. Shader effects also use separate logical-position inputs; those remain
unchanged. This is one reason water/effect-heavy scenes still need later validation.
The CRT vertex program uses `_MainGameTex_TexelSize.x` for both main and overlay color
spread. Both diffuse-bias values are temporarily multiplied by eight to preserve that
spread with the larger target; their originals are restored after compositing.

F10 additionally requires the observed camera layout, ordinary walking, an active
input-enabled FieldPlayerController targeting this player, no automatic movement or
riding, and the expected separate player visual hierarchy. Target changes, loss of
control, hierarchy changes, display-size changes or incomplete callback ordering stop
the test. These guards narrow this experiment; they are not full cinematic support.
NPC animation smoothing, map boundaries, parallax, transparency, water, scripted events,
vehicles and performance across hardware are not validated. Auxiliary transparency
targets remain at their original resolution. Image sharpness may differ, and the
main field target now has 64 times as many pixels, so rendering cost must be measured.

Offline checks cover interrupted overrides before and after writes, restore failure
retries, preserving independent later writes, movement replay, and a raster model
showing matching character/image cancellation at 30/60/120/144 fps and scales 2/4/8.
Those frame-rate cases are mathematical tests, not game compatibility claims. The
assembly audit forbids native patches/PInvoke/game setters and explicitly allowlists
the reviewed Unity render-resource/visual-transform setters. Actual Unity callback
order, image quality, occlusion and player stability require the first F10 live test.

Unity API references used for the resource/callback design:
[Camera.targetTexture](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/Camera-targetTexture.html)
and [RenderTexture.Create](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/RenderTexture.Create.html).

### 0.2.1: correct the shadow layer check

The first 0.2.0 F10 test failed in AddVisual before resource allocation or render
overrides. The guard incorrectly required all three player visuals to use layer 0.
Reading the shipped FieldPlayer prefab in the character bundle confirms its shadow
SpriteRenderer uses layer 17, beneath ShadowParent / SerialOffset / VisualParent /
FieldPlayer, with unit scale. CameraTileMap's inspected mask 58867457 includes both
layers 0 and 17. `artifacts/player-prefab-layout.json` preserves that asset evidence.

Version 0.2.1 requires layer 0 for body/head and 17 for the directly referenced player
shadow, both at setup and before each offset. It retains player ancestry, scale,
non-overlap and camera-mask checks; it does not broadly allow other scene renderers.
Accepted visuals and rejected conditions now identify their role, layer and hierarchy
details. F10 still requires live verification after this guard correction.

The same runtime log independently confirms F9 ran twice (160 and 153 frames) and
restored successfully on manual stop, with maximum corrections 0.4820 and 0.4171 units.
Failed-build logs and source are archived under `artifacts/render-tests/hierarchy-rejection-*`.

### 0.2.1 visual result: withdrawn

After the shadow guard correction, F10 did execute: the log records multiple
successful render sequences (including 246, 394, 176, 212 and 848 frames) and cleanup
on manual stop/quit. The user reports that background CRT rendering broke while the
character retained it, possible light judder remained, and textures warbled with CRT
off. Callback execution and restoration are therefore established for these runs;
visual correctness is not. The enlarged-target approach did not preserve the original
image pipeline. Resampling is a plausible source of warble, but its precise cause has
not been isolated from the user's report alone. Do not label this experiment a fix.

With the game closed, the experiment DLL was moved to
`artifacts/disabled-plugins/PRStutter.RenderExperiment-0.2.1-*` with the log and deployment
record. The logger remains installed and all 119 baseline files still match.

Reassessment: native ordinary movement explicitly rounds both interpolated XY
coordinates before FieldEntity.UpdateEntity writes the entity's localPosition.
FieldPlayer's Unique-state branch instead retains fractions; changing the movement
state is not an appropriate fix because it selects other behavior. The map path also
rounds additional per-layer offsets, while the field target itself samples only
320x180 pixels. Removing the first rounding call alone is not established to remove
visible quantization, and it changes gameplay transforms rather than just presentation.
The earlier FFPR-Fix maintainer's report of softlocks remains a warning to investigate,
not a demonstrated impossibility of every upstream approach.

For the captured ordinary walk, 16 units per approximately 0.2 seconds is 80 units/s.
At 60 evenly presented frames/s, ideal displacement is 4/3 source pixels per frame;
whole-source-pixel alignment requires alternating step sizes. At 60 or 120 units/s,
it can instead advance one or two source pixels per frame. This does not guarantee
smoothness with missed frames, diagonal motion, parallax or arbitrary scripted speeds.
Original speed, strict source-pixel alignment and identical displacement every 60 Hz
frame cannot all be retained simultaneously. Subpixel presentation can preserve speed
but must be designed around sampling/filtering/CRT behavior consistently across layers.

### Native map-scroll diagnostic 0.1.0

The user authorized trying the original map path with fractional coordinates. The
new, separate `PRStutter.NativeScrollExperiment` uses F10 for at most 15 seconds;
the old render experiment stays disabled. This is a background-only diagnostic,
not a new claim that smooth original-speed pixel-perfect rendering has been solved.

Native `CameraFollowing.UpdateController` reads the rounded target position, adds
scroll/offset and supplies it to renderer callbacks. `BaseMapRenderer.UpdateMapScrollIfNeed`
(`0xB2A9C0`) clamps the input, adds offset, caches the combined position, and updates
the map's render root and layer offsets. `UpdateLayerOffsetBase` (`0xB29E00`) retains
fractional scroll when positioning that root; its integer cell calculations select
the visible tile window. Neither inspected method writes player movement or physics.
The test calls that generated interop wrapper directly, with no native detours.

At the earliest of the four inspected field-camera OnPreCull callbacks, the plugin
reads the renderer's already-clamped `preCameraPosition` and requires it to agree in
XY with the followed camera. It adds the bounded walking residual to that native
input, with zero additional offset. The method's `isScreenshot=true` argument bypasses
ONLY its input-clamping branch in this build; this avoids clamping/offsetting a cached
result twice. It does not take a screenshot. Ordinary manual control and constant
camera-follow offset are required; camera-boundary/control/target changes stop the test.

After the last field camera renders, it restores the original cached scroll through
the same native routine and verifies the map root returned to its prior XY position.
The next Update is a recovery path if the render callback is skipped. The wrapper's
native call updates its own render caches, visible cells and map shader coordinates;
the test does not claim that native rendering has no state changes.

No gameplay movement, sprite transforms, camera transforms, texture sizes, materials,
compositor UV bounds or CRT settings are directly set by this plugin. It verifies
player and camera positions are unchanged across each call and records fractional
input/readback, sample root positions, frame counts and native-call CPU duration in
`BepInEx/diagnostics/PRStutter/native-scroll.log`. Timing is observer CPU timing,
not presentation/GPU timing. Samples flush on stop/fault/quit, not per frame.

The original 320x180 rasterization and per-layer rounding remain. Therefore residual
judder or warble may remain even when readback proves the native path accepts fractions.
NPC positions are intentionally left alone in this diagnostic and may misalign with
the fractionally shifted scenery. Test the same clear walking route first and judge
background motion plus CRT/texture appearance; do not treat NPC alignment as supported.

Offline verification uses the established scoped-override failure/retry tests and
motion capture replay, plus a stricter compiled-assembly audit: no native patches,
PInvoke, Unity/game property setters, new render targets, compositor setters or gameplay
movement calls. The audited native map-scroll call is the intended write path.
The F10 comparison is now complete; its outcome follows.

### Native map-scroll result: no noticeable visual change

The user reported no noticeable change. Two completed runs recorded 717 and 354
render frames, including 614 and 310 fractional-input frames. Maximum native cache
readback error was 0.000008 units in each run. Samples show the map root actually
moving by fractional units, and both runs report restoration on manual stop. An
earlier run stopped on a camera/target layout change; a subsequent start attempt
found no active field-follow target. Those guarded failures do not explain the
negative result in the two completed runs.

Read-only shader inspection selected the color pass (pass index 1), rather than
the first pass, which is ShadowCaster, in the installed `Last/MapOpaqueTileShader`.
Both inspected color vertex variants use ordinary object/world/projection transforms
without explicit XY rounding. In the default color fragment variant, rounding
operates on sampled color/alpha values used for palette selection and blending,
not on the geometry position or incoming main-texture UV. This covers the inspected
opaque tile shader, not every layer, shader, sampler state or GPU draw in the scene.
The shader inspection script now accepts `--pass-index` and records it in its manifest.
Evidence is preserved as `artifacts/shaders/map-opaque-color-*`.

Fractional positions therefore survive the tested CPU map path and the inspected
vertex shader. Rasterization/sampling on the original 320x180 target is a stronger
remaining explanation, but the logs are not a frame capture and cannot establish
the sole cause. This experiment did not remove logical movement rounding or test a
complete replacement rendering pipeline. It provides no evidence that changing
gameplay positions would be sufficient, or that all original-speed fixes are impossible.

With the game closed, the native-scroll DLL, deployment record and runtime logs were
preserved under `artifacts/disabled-plugins/PRStutter.NativeScrollExperiment-0.1.0-*`.
The installed DLL's hash matched its deployment and archived copy. Only passive
diagnostics remains installed. FFPR Fix remains disabled and no movement speed was
changed. The deployment record distinguishes verified callback execution/restoration
from the user's negative visual result.

The practical next candidate is speed alignment for the chosen presentation rate:
60 or 120 source units/s gives one or two pixels per evenly presented 60 Hz frame,
versus the captured 80 units/s. Choosing slower or faster movement changes game feel
and remains a user decision. This does not solve missed frames, every scripted speed,
diagonal motion or parallax automatically.

### Stationary rendering-grid experiment 0.1.0

The user chose further rendering investigation, setting aside speed alignment because
FFPR Fix already offers that approach. The first new stage isolates resolution from
movement: no fractional map input, visual-transform offsets, UV shifts or speed changes.
It is not expected to improve walking in this stage, and moving ends the test.

`PRStutter.GridExperiment` is OFF at launch. F10 starts/stops a maximum 15-second test
while the player and field cameras are stationary. All three inspected 320x180 targets
(main field, upper transparency and ceiling transparency) receive owned 1280x720 copies
with the original format, depth, sampling and wrapping settings. Four field cameras
are redirected together. Existing loaded material texture properties referencing those
targets are discovered once and redirected as well, including the final compositor.
Original textures are neither resized nor destroyed. The UI target stays unchanged.

Native exports `FieldCharaEntity.AttachTransparentTexture` (0xFDEE10),
`FieldRideOnEntity.AttachTransparentTexture` (0xE08F50) and
`FieldController.AttachTransparentTexture` (0x2F8270) establish local-material binding
of transparency buffers. The character shader declares `_OneUpTex` and `_TwoUpTex`,
which are included in material texture-property discovery. Logs enumerate actual
consumers in the tested scene; zero consumers means that buffer's sampling may not
be exercised there. Discovery is not a proof covering arbitrary dynamic materials,
property blocks, command buffers or every game scene.

The inspected CRT vertex shader multiplies main texel width by color-spread biases
for both field and overlay samples. Multiplying those two biases by four offsets
the fourfold decrease in main texel width. The CRT fragment shader's scanline spacing
uses final-screen coordinates/resolution, so those settings remain unchanged. This
preserves the inspected offset arithmetic; final appearance still needs comparison
because higher-resolution sampling can change edge softness and other effects.

LateUpdate applies the temporary camera/material bindings. All five camera callbacks
verify that the game has not overwritten them. Each of the four field cameras must
complete before the final compositor; its OnPostRender restores the originals, with
Update as a recovery path. Movement, control loss, camera changes, display/CRT mode
changes, transitions or unexpected callback ordering end the test. Exceptions retain
restoration ownership; cleanup attempts every binding and does not release resources
while restoration has failed. Owned resources/components are disposed outside render
callbacks. The game build is checked against the two known hashes before activation.

`scripts/Test-GridExperiment.ps1` builds the plugin and runs the existing scoped-state
rollback/failure/retry tests plus a specific compiled-assembly audit forbidding native
patches, PInvoke, game mutations/calls, transform setters, native scroll, blits and UV
reprojection. Shared motion/raster tests also run but are not evidence of this stage's
rendering quality. `Deploy-GridExperiment.ps1` refuses a running game or conflicting
experiments, backs up a prior DLL, and verifies the installed hash.

Live procedure: load the same field area, release movement and wait for the camera
to settle. With CRT enabled, press F10, compare stationary scenery, character, edges
and any visible transparency, then F10 again to return to baseline (or wait 15 seconds).
With the test OFF, disable CRT in the game and repeat. Report whether either mode
changes CRT appearance, creates seams/halos, changes sprite/scenery alignment or
noticeably changes sharpness. `BepInEx/diagnostics/PRStutter/grid-test.log` records
target allocation, discovered consumers, CRT biases, faults and completed frame counts.
No live visual success is claimed until this comparison is performed.

### Grid 0.1.0 live result: CRT-off stationary appearance accepted

The user reports that the test removes the CRT appearance everywhere except the
character, but notices no unwanted stationary-image changes with CRT disabled in
the game. This is a limited visual pass for the tested scene with CRT off, not a
motion, performance or whole-game compatibility result. The user is willing to
consider replacing CRT later if the underlying rendering approach succeeds.

The preserved log contains ten manually stopped runs totaling 990 completed
camera sequences, each with three replacement targets and 19 material texture
bindings: one compositor field binding and 18 character-head `_OneUpTex` bindings.
One transparency buffer had no discovered material consumers, so its sampling
coverage remains unverified. These recorded completed runs report CRT=1. The
CRT-off assessment is user-reported rather than an instrumented CRT=0 run in this
saved log. Other attempts stopped on movement/control or display/transition changes.
No texture-binding or callback-order failure was reported in the completed runs.
Logs and the prior deployment record are under `artifacts/grid-tests/20261003-041522`.

Additional native inspection confirms `PostProcessLite.ApplyTextureFilterModeByFakeCRT`
(0x86F8B0) sets a supplied texture's filter mode to the CRT boolean: Point (0) when
off, Bilinear (1) when on. The resolved native API string at RVA 0x1E44A68 is
`UnityEngine.Texture::set_filterMode(UnityEngine.FilterMode)`. `SetFakeCRT` also
invokes registered callbacks after updating compositor settings. Which texture
callbacks contribute to the reported background/character difference has not yet
been traced. The inspected character shader has no explicit CRT variant; the user
report does not establish that a separate character-only CRT shader exists, or
that the final compositor shader literally stopped running.

The next diagnostic should keep CRT off and test fractional visual movement on
the coordinated higher-resolution pipeline. Replacement CRT can follow if motion
is acceptable; it should use an explicit intended pixel scale rather than derive
all effect spacing from the high-resolution target. This remains an unimplemented
next stage. Grid 0.1.0 stays installed OFF by default for repeatable comparisons.

### Grid 0.2.0: fractional visual camera/player motion, CRT OFF

The user authorized the next movement test. Version 0.1.0 source, deployment record
and log were archived under `artifacts/grid-tests/0.1.0-source-*`; deployment also
backs up its DLL. Version 0.2.0 keeps the coordinated 1280x720 targets and consumers.
F9 is now a grid-only walking control; F10 enables fractional visual motion. Either
key stops the active mode, and switching modes requires stopping first. Both require
CRT off and expire after 15 seconds. No compositor scalar/CRT/UV writes remain.

At the first field-camera OnPreCull, the existing captured-motion guard reconstructs
the player's unrounded ordinary-walk position from start, destination, timer and
duration. The residual is quantized to quarter-game-unit increments: one texel on
the 4x field grid. It offsets the three rendering camera positions by that amount,
so all their scenery/sprites move together before rasterization. The logical
CameraFieldMain follow camera has no draw layers and is left untouched. The player's
separate body/head/shadow visual transforms receive the same world-space offset,
converted through their parents, cancelling camera movement for the followed sprite.
Gameplay entity positions, movement timers, speed, events and physics are not written.
There are no native hooks, native map-scroll calls, camera projection overrides,
final-image translations, blits or separately redrawn player layers.

All rendering offsets persist through the four field passes and are restored after
the final compositor, with Update recovery and scoped partial-write/retry handling.
Every pre-render pass checks camera/visual readback and unchanged logical player/follow
camera positions. Logs distinguish completed frames and applied/corrected motion frames,
maximum original residual and quantization error. Rendering camera hierarchy, expected
player visuals/layers, original target bindings, control, CRT state and consistent
camera-follow offset are guarded. Boundary clamps, diagonal/nonstandard motion, control
loss, transitions, display changes or replaced bindings end the test.

The numerical model verifies player cancellation before rasterization and static NPC
alignment with scenery. For the modeled 80-units/s walk at evenly spaced 60 Hz frames,
the worst step error falls from 0.6667 to 0.1667 units; position error is bounded by
0.125 unit at 4x. This still has finite-grid quantization and does not promise perfectly
uniform output steps. Replay accepts all 297 moving samples in the preserved good
capture. Shared rollback/failure/retry tests pass; the compiled audit allows only the
reviewed rendering transforms/resources and forbids native patches, game mutation
calls/setters, compositor scalar writes and image reprojection. These are offline
checks, not live visual/performance proof.

Moving NPC interpolation, parallax, water shader coordinates, map boundaries, vehicles
and cutscenes remain unsupported/unverified. Start with the same unobstructed walking
route away from camera boundaries. Compare F9 against F10 with CRT off, stopping between
modes. Judge background cadence, player jitter, texture warble and alignment separately.
The live result is pending; this is not yet a general-game fix.

### Grid 0.2.0 failed activation; 0.2.1 camera-hierarchy correction

The user reported no odd visual glitches but no judder improvement. Logs show that
F9 completed two runs (241 and 342 frames), while all six F10 attempts failed inside
VisualMotion.Apply/ValidateApplied with `Render camera overwritten before draw`, zero
completed frames and zero corrected frames. This is an invalid fractional-motion
comparison, not evidence that the approach failed to improve motion.

The saved render-layout capture already contains the cause: CameraUpperTransparentRT
and CameraCeilTransparentRT are direct children of CameraTileMap. Version 0.2.0
incorrectly treated their world positions as independently writable. Writing child
world offsets before translating their parent adds the translation twice. Worse,
individual scoped restoration can interpret inherited position changes as an
independent writer, leaving child-local offsets behind until the scene is recreated.
The game was closed before repair/deployment; testing the correction starts with a
fresh launch. Failed source and logs are under `artifacts/grid-tests/0.2.0-hierarchy-fault-*`.

Version 0.2.1 verifies the expected parent-child hierarchy and moves only CameraTileMap.
The two transparency cameras inherit its displacement. They supply readback getters
only to the new InheritedTranslation helper: no child setters exist in that operation.
All three world positions remain checked, and restoring the parent does not modify
child-local positions. The logical player and follow camera must remain outside this
render hierarchy. Player body/head/shadow offsets and the 4x target setup are unchanged.

A regression test exercises the exact generic hierarchy helper used by the plugin
against parent/child world-position getters, both displacement signs, repeated
restore, a setter that writes then throws, and independent child/root edits. This
adds the missing hierarchy coverage; the previous independent-transform math tests
could not detect the implementation error. Live verification of 0.2.1 is still required.

### Grid 0.2.1 live result: reduced judder; 0.3.0 resolution comparison

The user reports that judder is minimized but still present. The 0.2.1 log confirms
four clean fractional-motion runs: 891, 741, 891 and 892 completed frames (3,415 total),
with 519, 473, 637 and 616 corrected frames (2,245 total). All report successful
restoration. Maximum residual quantization error was 0.12483 game unit, consistent
with the quarter-unit correction grid. There were no faults in these recorded 0.2.1
runs. A separate grid-only run completed 262 frames. Source, logs and the deployment
record are preserved under `artifacts/grid-tests/0.2.1-improved-*`.

This establishes a useful visual improvement in the tested route, not its completeness
or whole-game compatibility. At the observed 2560x1440 output and unchanged framing,
one game unit spans eight screen pixels. The 4x target thus permits motion in two
screen-pixel increments, with up to one screen pixel of rounding error. This is a
plausible contributor to remaining judder, not a demonstrated sole cause. Completed
callback frame counts are not frame-presentation measurements.

Version 0.3.0 isolates grid density: both modes now perform fractional visual motion.
F9 selects 4x (1280x720, the previous successful F10 behavior), and F10 selects 8x
(2560x1440). Either key stops an active test; stop before changing modes. CRT stays
required off, maximum duration stays 15 seconds, and hierarchy/scene/restoration guards
remain unchanged. The requested target size is checked against SystemInfo.maxTextureSize.
8x halves positional quantization to eighth-unit steps with a 0.0625-unit error bound,
matching one-screen-pixel steps at the observed output. It renders four times as many
target pixels as 4x, so performance cannot be assumed equivalent.

Offline tests cover both scales, including parent/child restoration, player cancellation,
static NPC/scenery alignment, motion guard replay (297 captured moving samples), and
the compiled mutation audit. At an ideal evenly spaced 60 Hz cadence, modeled maximum
step error drops from 0.1667 unit at 4x to 0.0833 unit at 8x. Finite output-pixel steps
remain; these tests do not prove visual improvement or presentation smoothness.
If the denser grid does not help, measure presentation timing with the existing
PresentMon workflow before adding more rendering corrections. Live 0.3.0 results
were subsequently completed; see the measurement below.

### Grid 0.3.0 and presentation timing, October 3

The user could not reliably distinguish the 4x and 8x modes; both looked substantially
smoother than stock. A 75-second PresentMon 2.6.0 capture of game PID 28320 recorded
4,464 rows on one swap chain. Raw evidence and the plot are under
`artifacts/measurements/20261003-045038-163`. QPC/UTC anchoring and the grid log identify
the two 15-second modes; transition and boundary trimming retain 651 rows per mode.

| Mode | Retained rows | Mean submission interval | Display p99 |
| --- | ---: | ---: | ---: |
| First unmodified interval | 1,057 | 16.724 ms | 24.295 ms |
| 4x fractional motion | 651 | 16.862 ms | 24.302 ms |
| 8x fractional motion | 651 | 16.849 ms | 24.303 ms |

Display-change intervals cluster around 12.1, 18.2 and 24.3 ms, consistent with
multiples of a roughly 6.065 ms refresh cycle. WMI reports the active RTX 5080 output
at 2560x1440 and integer refresh rate 164 (roughly 165 Hz). Both corrected modes have
similar display timing. This is objective uneven presentation cadence, not proof
that timing explains every perceived motion artifact. The off intervals have no
walking markers and may include idle time. No image-displacement measurement was made.

All rows report Composed: Copy with GPU GDI, SyncInterval 0, AllowsTearing 0, and
Dropped 0. These fields do not establish whether a driver override or VRR is enabled.
PresentMon documents MsBetweenDisplayChange as the previous frame's displayed duration,
and warns that the application's SyncInterval can be modified later by driver settings:
https://github.com/GameTechDev/PresentMon/blob/main/README-ConsoleApplication.md

Next isolate synchronization at a 60 Hz-compatible refresh rate (for example 120 Hz),
then repeat the same capture and verify the actual cadence. A refresh-rate change alone
does not guarantee an exactly paced 60 fps game. Avoid additional grid changes until
this timing variable is understood. CRT compatibility and broader scene coverage remain
separate unresolved requirements.

### Grid 0.4.0: VSync-paced field experiment

At the user's request, F7 now scopes vSyncCount=1 for up to 90 seconds, independently
of the 15-second F9/F10 grid modes. Unity 2019.4 documents that nonzero vSyncCount
overrides its targetFrameRate limiter on desktop. Keep targetFrameRate=60 untouched:
the upstream FFPR Fix source shows that gameplay code also reads that property for
revive animation, and its full uncapping patch adjusts menu-repeat acceleration.
This experiment therefore stays limited to ordinary manual field walking, restoring
on focus/control loss, setting changes, timeout, quit or unload. It uses no native
hooks and makes no claim about high-FPS compatibility outside that field test.

F7 requires the expected baseline targetFrameRate=60 and vSyncCount=0 and logs
five-second update-rate samples plus settings readback. A successful setting write
does not prove higher FPS, VSync/VRR behavior, or smooth display cadence: measure
those live with PresentMon. Capture/analyzer scripts now distinguish pacing changes
from grid changes, including focus-loss restoration while a grid test stays active.
Restoration uses the existing tested ownership helper, preserving independent game
writes. Offline raster checks now also cover 165, 240 and 360 FPS. Live results pending.

Sources: https://docs.unity3d.com/2019.4/Documentation/ScriptReference/Application-targetFrameRate.html
and https://github.com/d3xMachina/FFPR-Fix/blob/master/Patches/FrameRate.cs

### Grid 0.4.0 live result: VSync and smoothing both help

The user completed the comparison and reports that F7 helped, with an additional
improvement from F9/F10 smoothing. The 90-second PresentMon capture at
`artifacts/measurements/20261003-141255-471` contains 10,375 rows on one swap chain.
Transitions are excluded, and grid and pacing events are independently labeled.

| Mode | Retained frames | Mean submission FPS | Display median | Display p99 | Display standard deviation |
| --- | ---: | ---: | ---: | ---: | ---: |
| Initial stock pacing, smoothing off | 287 | 59.82 | 18.145 ms | 24.288 ms | 4.109 ms |
| VSync-paced, smoothing off | 1,379 | 164.82 | 6.067 ms | 9.875 ms | 1.427 ms |
| VSync-paced, 4x smoothing | 1,813 | 164.84 | 6.067 ms | 9.867 ms | 1.419 ms |
| VSync-paced, 8x smoothing | 1,813 | 164.84 | 6.067 ms | 9.916 ms | 1.439 ms |

The initial retained baseline lasts 4.78 seconds; the VSync-only interval lasts 8.36
seconds and each smoothing interval about 10.99 seconds. The post-test stock interval
also returns to about 59.86 FPS with a 24.346 ms display p99, but may include idle time.
F7 changed recorded SyncInterval from 0 to 1; the composed-copy presentation mode
remained unchanged. No dropped frames were reported. The changed timing distribution
supports an improvement in frame delivery, but does not establish G-SYNC engagement,
perfect cadence, physical panel behavior or the relative contributions of higher FPS
and the different pacing mechanism. Display timing still varies around the 6.067 ms
median; a lower p99 alone is not a refresh-rate-normalized smoothness score.

Grid logs show 2,471 completed frames / 1,843 corrected frames at 4x, and 2,473 / 2,168
at 8x. Both expired normally after 15 seconds and restored render bindings without
faults. F7 was manually stopped, restoring vSyncCount=0 while targetFrameRate stayed 60.
This verifies both grid modes on the tested walking route near 165 FPS. It does not
validate other scenes or arbitrary FPS. The user has not established a preference
between 4x and 8x. Retain the combined approach for further work; broader high-FPS
compatibility, CRT rendering and pixel-displacement measurement remain unresolved.

### 120 Hz comparison, October 3

The user changed the display to 120 Hz and repeated the same pacing/grid comparison.
Capture `artifacts/measurements/20261003-142237-674` contains 8,198 rows. WMI reports
119 Hz as an integer; Unity reports 120 Hz, and measured application submissions
average 119.99-120.00 FPS with F7 on. The initial stock interval is too short for a
useful baseline (38 retained frames / 0.62 seconds); use the independently labeled
post-test stock interval cautiously because it may contain standing still.

| VSync-paced mode | Retained frames | Mean submission FPS | Display median | Display p99 | Display standard deviation |
| --- | ---: | ---: | ---: | ---: | ---: |
| Smoothing off | 1,227 | 119.993 | 8.334 ms | 11.264 ms | 2.168 ms |
| 4x smoothing | 1,320 | 119.999 | 8.333 ms | 11.264 ms | 2.155 ms |
| 8x smoothing | 1,319 | 119.998 | 8.333 ms | 11.264 ms | 2.120 ms |

Both smoothing runs completed and restored without faults: 1,800 completed / 1,172
corrected frames at 4x, and 1,801 / 1,209 at 8x. F7 was manually stopped and vSyncCount
returned to 0. The composed-copy presentation mode remained unchanged; all retained
VSync rows report SyncInterval=1 and no dropped frames.

The measured interval variation did not disappear at a multiple of 60 Hz. A repeated
sequence in the 120 FPS display-event trace is approximately 5.42, 11.25, 8.33, 8.33 ms:
four frames / about 33.33 ms (30 cycles per second). This is not a claim of a pure
30 Hz oscillator throughout the capture. Lag-four display-interval correlation is
0.59-0.70 and lag-eight is 0.76-0.81 across the three modes; submission-interval lag-four
correlation is weaker, 0.28-0.32. Descriptive values are saved in cadence-periodicity.json.
Application interval standard deviation is only 0.47-0.48 ms. Periodicity appears with
smoothing off as well as on, so the grid override is not necessary for it to occur.

Intervals shorter than a full refresh must not be interpreted as direct physical
full-frame holds. These measurements alone cannot assign the pattern to game timing,
Windows/driver composition, or event timestamp reconstruction. A lower or higher
absolute p99 at different refresh rates is not by itself a smoothness comparison.
This result does not prove or exclude every possible 60 Hz dependency; it specifically
fails to show that moving to 120 Hz removes the recorded timing variation. Visual
comparison of the user's recurring judder remains separate from this ETW result.

### Grid 0.5.0: cropped field-pixel diagnostic prepared

To separate spatial motion from presentation-event timing, F6 now captures two
128x64 RGBA scenery patches at CameraTileMap.OnPostRender, alongside the logical
player, reconstructed continuous position, logical camera, actually rendered camera,
frame number, QPC, move timer/duration/endpoints, and readback/observer cost. The
tile-camera callback observes the fractional offset while it is applied; the final
compositor callback restores it later. This reads the field target, not final screen
composition, upper/ceiling passes or physical scanout. Crop centers are at 20% and
80% width, 65% height in Unity's bottom-origin coordinates, avoiding the centered player.

AsyncGPUReadback.Request and AsyncGPUReadbackRequest are absent from the installed
stripped interop API (only ValidateFormat and WaitAllRequests remain). The diagnostic
therefore uses synchronous ReadPixels into an owned CPU texture, then managed array
copy. RenderTexture.active is scoped and restored in finally via the existing tested
ownership helper. No native patches, memory-layout assumptions, targetFrameRate writes,
gameplay setters or CRT/compositor scalar writes are introduced. This is an intrusive
image-content diagnostic: its frame timing must not be treated as an unperturbed run.

Capture is bounded to six seconds after a half-second warmup, 1,024 frames, and a
64 MiB preallocated managed pixel buffer. Formatting/disk writes occur afterward on
a background task. Control/focus/pacing/target-size/transition changes stop recording.
Unexpected images or callbacks fail closed; partial capture metadata records the reason.

The offline tracker searches both axes without feeding the predicted camera shift
into its matching decision. Equal-size overlaps, texture contrast, normalized image
error, separation from the second-best match, boundary checks, two-patch agreement,
and consecutive frame IDs determine acceptance. Low confidence remains unmeasured.
Five tests cover signed shifts/idle, blank and repetitive images, unrelated frames,
boundary/diagonal rejection, packed-image layout, scaling/sign, overhead conversion,
and truncated-file rejection. Build/restoration/mutation checks pass. Live readback
compatibility, overhead and tracking confidence remain unverified until the user runs F6.

### Pixel captures: tile-boundary time loss, October 3 local / October 4 UTC

Both user captures are usable. Under the game's diagnostic `pixels/` directory,
`20261004-035101-578` recorded 719 frames at the original 320x180 target; all 718
consecutive image pairs were accepted. `20261004-035119-864` recorded 358 frames
at 2560x1440 (8x smoothing); all 357 pairs were accepted. The second capture ended
after 2.975 seconds because the camera-follow boundary guard stopped smoothing,
which changed the target size. No pixel-capture fault occurred. Both used VSync
at approximately 120 FPS, with a 2560x1440 output. A repeated capture is unnecessary.

Independent matching of both scenery patches gives the following horizontal steps:

| Capture | Moving pairs | Measured displacement, screen pixels |
| --- | ---: | --- |
| Original grid | 576 | 379 steps of -8; 197 steps of 0 |
| 8x smoothing | 357 | 180 steps of -5; 104 of -6; remaining steps range from 0 to -10 |

The measured image displacement exactly equals the sum of native logical-camera
scroll and the tile-camera rendering offset in every accepted pair, in both runs.
The analyzer originally considered only the tile-camera transform, omitting native
scroll; its prediction has been corrected and tested. Reconstructed game movement
and smoothed raster motion differ by at most 0.932 screen pixels per step, consistent
with differencing two quantized positions. This validates the correction on these
patches, not every render layer or final display composition.

The smoothed capture contains 14 tile completions, separated by 24 or 25 frames
(roughly 0.2 seconds). The movement timer reaches a 0.2-second duration, the entity
snaps to the destination, and duration/timer reset to zero. Any excess frame time
is discarded instead of advancing the immediately following tile. For example,
one completion has only 0.20111 ms left in its tile but receives an 8.4549 ms update:
the reconstructed advance is 0.12872 output pixels instead of the full-frame
5.411136 pixels, and the rendered advance is zero. The next tile starts afresh.
Across the 14 completions, unused time totals 46.52062 ms (median 3.30352 ms).
Between completions, reconstructed movement agrees with 640 output pixels/second
times deltaTime to within 0.000145 pixels per update.

Native evidence independently supports this mechanism. In
`artifacts/native/00FE9E20_Last_Entity_Field_FieldEntity__UpdateEntity.asm`, the
completion branch calls the endpoint setter, then at 0x180fe9f8e zeros EAX and at
0x180fe9f95 writes zero to the eight bytes at entity+0x70. The interop dump identifies
these fields as float moveTime (0x70) and moveTimer (0x74). The corresponding C export
shows the same reset. `00FE85D0_Last_Entity_Field_FieldEntity__MoveTo.c` initializes
the next move's timer to zero. No remainder is retained by this path.

Earlier passive captures corroborate the loss without GPU readback. Restricting
to consecutive LateUpdate frames, unchanged endpoints on completion, and immediate
same-direction next tiles whose start equals the preceding destination:

| Passive capture | Continuous completions | Median unused time | Maximum unused time |
| --- | ---: | ---: | ---: |
| 20261003-010739-221-748ec159 | 43 | 1.11830 ms | 2.51240 ms |
| 20261003-011906-617-1a524ab6 | 24 | 0.42515 ms | 1.05171 ms |

In all 67 cases, the next tile's timer equals only that next frame's deltaTime,
with no remainder carried over. Median observer cost at these completions is
0.0117/0.0118 ms. Extracted events are retained in
`artifacts/measurements/passive-tile-boundaries.json`. The older native-hook capture
is excluded because it has no qualifying passive LateUpdate sequence.

This is a concrete upstream timing defect and a candidate for the user's regular
remaining judder. It does not establish that all perceived judder has this cause,
or explain the separately measured approximately 30 Hz ETW interval pattern.
The pixel recorder is intrusive: median readback cost is 3.459 ms on the original
grid and 4.978 ms at 8x; 8x p99 is 9.001 ms. Its exact frame-time variation and
tile-end hitch severity must not be generalized to uninstrumented play. The passive
captures and native code establish that timer loss itself predates pixel recording.

The output is already an exact 8x integer scale of 320x180. This timing reset is
not evidence of a requirement for refresh rates divisible by 60. The next targeted
experiment should preserve unused time only when ordinary walking actually continues
into the next tile, while respecting stops, turns, collisions, event callbacks and
scene transitions. Avoid globally accelerating every MoveTo call or bypassing
completion callbacks. No gameplay patch was installed during this analysis.

Seven pixel-analyzer tests now cover matching, layout/scaling, native-scroll plus
render-offset prediction, and the tile-completion shortfall calculation. Both live
captures were reanalyzed with the corrected model, and their charts were inspected.

### Tile Timing Test 0.1.0 prepared, October 4

Implemented as a separate plugin so the verified grid/pacing plugin and its mutation
audit remain unchanged. F5 activates a bounded 30-second session, off by default.
The timing plugin installs exactly two Harmony hooks at activation and removes its
own hooks on stop: the parameterless `FieldPlayer.UpdateEntity` (0xFF3D20) and the
value-Vector2 input method `FieldPlayerKeyController.OnTouchPadCallback` (0x5F55B0).
It does not reuse the withdrawn camera or ref-Vector3 hooks. This reduces the hook
surface but does not prove native-hook compatibility; a live test remains required.

Additional native inspection found that `FieldPlayer.UpdateEntity` calls its base
update chain before player-specific sprite/flight handling. `FieldEntity.UpdateEntity`
performs the endpoint snap, resets timers, invokes SquareMoveFinished and
OnMoveCharacter. The timing postfix therefore runs after those original callbacks.
The normal key-controller input handler computes the next destination, checks player
permission, selects walk/dash and calls the virtual MoveTo path. FieldPlayer.MoveTo
only starts when moveTime <= 0; FieldEntity.MoveTo runs its raycast and MoveToCallback
and leaves moveTime zero if the destination is blocked. These exports are retained
under `artifacts/native/`; inferred decompiler signatures are checked against the
generated type metadata.

Simply adding the remainder on the next frame would retain the endpoint dip and
then produce a catch-up step. Instead this prototype observes the genuine input
axis for the selected controller in the same frame, snapshots the player's timer
before its ordinary update, and lets the entire original update run. When that
update completes an ordinary tile with excess time, it requires unchanged endpoints,
an exact endpoint position, zero timers, continued manual control, no queued automatic
path, no pause/riding, and fresh input in the same direction. It then calls the normal
controller once to request continuation. Only if that succeeds with an adjacent
same-direction 16-unit tile, unchanged 0.2-second duration and zero new timer does it
set moveTimer to the remainder and advance XY using the native round-to-even formula.
Z is retained. No second UpdateEntity, direct MoveTo bypass or duplicate completion
callback is invoked. There is no saved remainder to leak into a subsequent stop/turn.

The only direct game setters in the compiled plugin are FieldEntity.moveTimer and
Transform.localPosition. The new-tile request itself can invoke normal game callbacks;
this is therefore a genuine gameplay change with unverified broader event/follower
compatibility, not a purely visual fix. The fractional start of the next tile occurs
after the previous update's OnMoveCharacter callback; downstream behavior must be
checked live. If the actual input/update order supplies no current-frame axis before
the player update, the experiment performs no carry and records that refusal rather
than reusing old input. Loss of control, repeated player updates within one frame,
focus loss, timeout or exceptions disable intervention; hook removal occurs outside
the native callback. Pending hook-removal failures remain disabled and retry.

Diagnostics buffer up to 16,384 rows with pre-update, original post-update and final
timers/endpoints/positions, axis/frame identity, budget/applied time and action.
CSV formatting and disk writes happen after stop on a worker task. No GPU readback
is used. Postfix cost is recorded but excludes prefix/original-native-update cost.
The analyzer verifies reconstructed frame-distance conservation; it does not measure
final raster pixels, presentation events or physical display delivery.

Verification: Release build with no warnings; 89,520 simulated frames across both
axes/directions at steady and jittered 30/60/120/144/165/240/360 FPS conserve motion
within 0.001 game units. Policy checks reject stale/released/turning input, blocked
or mismatched next tiles, teleports, changed speed, nonfinite values and >50 ms
updates. A compiled metadata audit restricts game setters and rejects explicit
update replay/MoveTo calls and native imports. Three analyzer tests distinguish
successful conservation, an endpoint shortfall and an intervention that never ran.
Simulation callback counts do not verify actual event callbacks or native hooks.

First live test: same safe ordinary-walking scene, CRT off, F7 and F10 active, two
seconds baseline then F5 during three to four seconds of straight walking, release
to check stopping, F5 to stop/save. No F6 readback. Review `carriedTiles`, fresh input,
per-frame conservation, faults and subjective motion before expanding to turn/wall
tests or changing any broader gameplay behavior. Existing rendering/pacing limits
and CRT restrictions remain unchanged.

### Timing 0.1.0 live result and 0.1.1 deferred continuation

Capture `timing/20261004-041402-759.csv` recorded 544 player updates, including
191 with fresh same-frame input, during F7 VSync at 120 Hz and F10 smoothing.
All seven qualifying tile completions attempted continuation but retained zero
duration/timer and unchanged endpoints after the input request. No carry was applied.
The session stopped manually and removed its hooks without logged faults. The grid
later timed out and restored normally; pacing stopped on focus loss. This establishes
capture/hook execution on the route, not a successful timing fix or subjective safety.
No reported visual result is inferred from the user's completion message.

Additional exports show that `FieldController.OnPlayerMoveFinished` (0x310D20)
sets isPlayerFootProcessing (offset 0x228) and schedules arrival monitoring via
EventProcedure.EventUpdateFootMonitor. `IPlayerAccessor.IsCanPlayerOperation`
(0x30A9D0) rejects input while that flag or isActivateGimmick (0x208) is set.
`OnPlayerFootMonitoringFinished` (0x310180) clears the flag only after its event,
hidden-passage and landing-state work. The seven no-op continuation requests are
consistent with this still-pending arrival gate. Version 0.1.0 did not record these
flags directly, so the attribution is supported by static control flow rather than
a captured flag read. Its generic `blocked_or_changed_next_tile` label does not
establish a wall collision.

Version 0.1.1 retains each qualifying budget only within its completion frame.
Two additional hooks observe the original foot-monitor completion's `toNext` result
and enter `FieldController.UpdateController` before its camera update. A budget can
be consumed once only when the same frame has an approving foot completion, the
native player-operation accessor returns true, current input still matches, and
the camera phase has not already begun. Endpoints, control and collision-checked
next-tile guards remain in force. The original field update then handles camera
following and visual instances once in the normal path. There are no extra camera
updates and no setter for the operation flags. If arrival monitoring finishes too
late, the budget is discarded and the reason recorded; next-frame catch-up is not
silently substituted. This ordering is guarded but not yet established by a live run.

Added evidence columns record the foot-busy flag at completion, latest foot-finished
frame/result and prior camera phase. The analyzer also totals unapplied completion
time, preventing zero error on interior-only frames from being mistaken for success.
Policy tests cover stale budgets/approvals, rejected foot results, a closed operation
gate and an already-started camera phase. Release build, mutation audit and all
89,520 simulated frames pass; the three analyzer tests pass. After the user closed
the game, 0.1.1 was installed with a backup and matching source/destination hashes.
The grid plugin remains the same verified 0.5.0 DLL. Live 0.1.1 verification is pending.

### Timing 0.1.1 live result: approval occurs in the following frame

Three captures completed on October 4. All recorded zero continuation attempts and
zero carries; the new guards correctly declined to intervene:

| Capture (UTC stem) | Player updates | Completion budgets | Refused before camera | Endpoints without matching fresh input |
| --- | ---: | ---: | ---: | ---: |
| 20261004-042144-614 | 1,640 | 34 | 33 | 1 |
| 20261004-042211-032 | 1,055 | 7 | 6 | 1 |
| 20261004-042248-458 | 526 | 9 | 8 | 1 |

Every completion recorded isPlayerFootProcessing=true. At the field-update prefix,
the most recent approval still belonged to an earlier tile. Subsequent rows show
that all 50 completion callbacks finished at frame N+1: 49 with toNext=true, one
with toNext=false. Therefore the current same-frame policy cannot run on these
routes. The two later captures retained F7 pacing and F10 smoothing throughout
their F5 interval, so their results do not depend on the camera guard. Hooks were
removed on stop and there were no timing faults. No visual effectiveness or normal
stop/camera behavior is inferred solely from the user's report of usable captures.

The first grid run began at 04:21:42.732Z and stopped at 04:21:46.450Z because
the logical camera-to-player offset changed. Its timing recording continued until
04:21:58.978Z, stopping on control change before the 30-second timeout. This is
usable for scheduling evidence, not a full smoothed visual comparison. The user
changed locations and obtained two subsequent clean smoothing runs. The guard
does not detect a map transition: it checks that camera.position-player.position
stays at the value captured at activation. A fixed camera, reaching a scroll clamp,
or scripted camera motion can violate that condition within one screen. These logs
establish an offset change, not which of those causes occurred. Boundary-aware
render smoothing remains unimplemented; weakening this guard is not part of the
timing experiment.

Native scheduling exports corroborate the measured frame boundary:
`MainGame.Update` (0x363C10) runs residentMultiTask.UpdateProcess before the subscene
controller update. `EventProcedure.EventUpdateFootMonitor` constructs the arrival
task and passes it to ISceneParentAccessor.RequestTask. `MainGame.RequestTask`
(0x363AF0) enqueues that task rather than executing it immediately. A movement
completion during the field update therefore schedules work after that frame's
task-processing opportunity. This explains why moving the carry attempt to the
field/camera prefix was insufficient. It is not a hidden 60 Hz timer.

The design must now address task scheduling or explicitly accommodate delayed
approval. Forcing the operation gate open would bypass meaningful arrival/event
checks. Merely adding the budget to the following frame would conserve elapsed
movement but retain a small step followed by a larger catch-up step; that alone is
not established as a smooth-motion fix. Running the entire task scheduler twice
could advance unrelated scripts/timers and is not an acceptable narrow experiment.
Investigate whether only the newly queued arrival task can safely take an initial
step at the normal completion point, preserving its yield behavior and callbacks,
before preparing another gameplay build. No new DLL was installed during this
analysis; leave F5 off and do not request another identical capture.

Evidence is preserved in `artifacts/timing-tests/0.1.1-20261004/` and the extracted
50-event timeline in `artifacts/measurements/timing-arrival-order.json`. The analyzer
now reports arrival-completion delay in frames; five tests cover same-frame,
next-frame and unobserved approvals plus prior conservation/refusal cases.

An additional capture completed while analysis was underway:
`20261004-042447-908` has 626 updates, 46 completion budgets (44 arrival-gate
refusals, two without matching fresh input), and again zero carries. All 46
observed arrival completions occurred at N+1, bringing the four-capture total to
96. This extra run was outside F7/F10 coverage and ended on control change, so it
is not added to the clean smoothed comparison. Its maximum ordinary-frame model
shortfall is 4.496 units; the ordinary label includes unsupported updates for which
the carry guard declined, so this metric alone is not a conservation validation.
The extra raw CSV, metadata and analysis are preserved alongside the first three.

### Timing 0.2.0: one early arrival-task step, October 4

Native inspection identifies a bounded scheduling intervention rather than another
guess at a later hook. `FootMonitoring.UpdateMoveAction` (0x88C2A0) returns a new
`EnumeratorTaskProcess` containing `<UpdateMonitor>d__41` in state 0. Its MoveNext
(0x8935D0) retains transport/event/floor-damage/encounter checks and can yield while
waiting. Even the ordinary approval path invokes monitoringFinished and then yields
in state 3; the next MoveNext returns false. Therefore it is incorrect to treat an
approval callback as task completion or to discard the iterator after approval.

`EnumeratorTaskProcess.Update` (0xB52BC0) advances its iterator once when Running,
clears the handler and marks End when MoveNext returns false. Its inherited
`TaskBase.Start` (0x573260, shared native body) only sets Running. MainGame's
CreateInstance (0x360A30) constructs residentMultiTask with type 2 (DynamicParallel).
TaskMachine.UpdateRequestProcess (0x573A30) starts queued tasks and moves them to
the running list; UpdateParallelProcess (0x573780) steps running tasks and sends
completed ones to normal termination. Leaving an ended wrapper on the request
queue would restart an empty wrapper and potentially strand it, so 0.2.0 does
not use that shortcut.

The fifth temporary Harmony hook observes FootMonitoring.UpdateMoveAction's
result only inside a qualifying selected-player update with fresh straight input
and a positive time remainder. No global RequestTask hook is used: MainGame's
native body is shared by other scene classes. The existing field-update prefix
then requires the same frame before the camera, the unchanged completed endpoint,
fresh matching input, exactly one captured arrival, Request status, iterator state
0, matching player/FootMonitoring pointers and the original nonnull callback. The
resident request queue must contain only that task, with no pending trash entries
and no duplicate on the running list.

The task is started, appended to running, removed from the request queue and updated
once. Appending before removal preserves ownership if allocation fails; a failed
queue removal rolls back only that appended running entry. There is no rollback
after executing game code. Both yielded and ended tasks remain game-owned for
normal resumption/termination, including after the experiment stops. The entire
scheduler is never replayed. Arrival processing occurs after the original player
update has fully returned, not recursively inside tile-completion callbacks.

Continuation still requires the native same-frame toNext approval, open operation
gate, unchanged endpoint and fresh direction. Native input/collision/dash handling
selects the next tile; only an ordinary adjacent 0.2-second tile receives its
remainder. A wait, rejection, changed control or changed input receives no carry.
F5 remains off by default with a 30-second limit. The grid/pacing plugins are
unchanged. This moves native event processing earlier by up to one frame, so
offline tests do not establish full-game event/cutscene compatibility.

CSV evidence adds arrival-task action, iterator state after the step and the queue/
running counts before admission. The analyzer distinguishes an early step from a
successful carry and retains support for old recordings. Verification passes:
zero build warnings/errors; 89,520 simulated movement frames; stop/turn/collision/
speed/freshness guards; same-frame arrival gates; single-task lifecycle tests for
ordinary approval, multiple waits, immediate end, callback-created tasks and
start/add/remove/step faults; compiled checks against whole-scheduler replay,
extra game setters, entity update replay and native imports; six analyzer tests.
Live hook behavior, successful carries, visuals and stopping remain unverified
until the first 0.2.0 capture. The next test is the same short F7/F10 straight walk
with F5 toggled once and then off; F6 remains off to avoid GPU-readback overhead.

### Timing 0.2.0 first live results: carry works; test states were mixed

The October 4 session from 04:37:04Z to 04:41:14Z produced eleven saved F5
captures (17,212 player updates). All eleven applied corrections. Of 681 positive
completion budgets, 588 were carried; 65 were declined after a direction change,
18 lacked input from the current frame, and 10 ordinary continuation requests
did not start another tile. All ten ended with duration/timer zero and start,
destination and position equal to the prior endpoint, consistent with a blocked
next tile; the CSV alone does not identify the particular collision object.

All 598 task steps reached iterator state 3 and yielded after native approval
in the same frame, with toNext=true. 588 of those approved next moves conserved
the remainder; the other ten respected the native refusal to start a tile.
Maximum reconstructed movement error on a carried frame was 0.00000136 game
units. All 588 exact final movement states were still present at the next
consecutive player-update prefix: none were immediately overwritten. Total
carried time was 2306.295 ms across the recordings. No experiment fault or
detach/restore failure was logged. These are local movement/task results, not
measurements of final pixels, GPU delivery, or complete event/cutscene safety.

The user's impression that the combination sometimes worked and sometimes did
not has a concrete confounder: activation-log correlation shows four complete
F5 captures with both F7 and F10 active throughout, four with no three-way overlap,
and three with partial overlap. The first three and last F5 captures had the full
combination throughout. For example, F10 stopped at 04:40:23.875Z while F5 remained
on until 04:40:40.008Z; F7 also timed out during two other F5 recordings. There were
no camera-boundary faults in this session. F10's 15-second, F5's 30-second and
F7's 90-second independent lifetimes, plus manual/focus changes, explain the mixed
states without establishing which exact change the user perceived.

The user reports that the combination may be working but cannot identify its
state reliably. Do not request more identical multi-key comparisons. The next
usability change should expose all three actual states/countdowns and a coordinated
bounded test control; keep the existing scene/control guards. Broader gameplay
coverage and objective presentation measurement remain separate follow-ups.

Raw CSV/metadata and log snapshots are preserved in
`artifacts/timing-tests/0.2.0-20261004/`, with `session-analysis.json` and
`carry-followthrough.json`. `scripts/analyze_timing_session.py` reproduces the
per-capture F7/F10 overlap and refusal totals, while `analyze_timing.py` now checks
next-frame carry persistence. Eleven analyzer tests cover prior conservation cases,
overwrites/missing next frames, staggered timeouts, faults, reactivation and archived
log subsets. This analysis changes no game DLL or experiment behavior.

### Coordinated comparison and visible status: Timing 0.3.0 / Grid 0.6.0

The user authorized a combined toggle/status panel after the mixed-state recordings.
F4 now replaces any separate experiments with timing carry, display-paced VSync
and 8x smoothing using the same absolute Stopwatch deadline, fifteen seconds after
the start request. F4 again stops all three. A component guard/fault stops the
remaining features on the next safe Update/LateUpdate; no cross-plugin cleanup is
performed recursively from a native movement hook or render callback. Existing
camera/control/CRT/collision/arrival guards and the movement correction are retained.
Game-owned arrival tasks continue normally after the timing hooks are removed.

Grid exposes a narrow coordination API rather than duplicating its render/pacing
ownership logic in Timing. Timing declares a BepInDependency on Grid >=0.6.0 and a
non-copying build reference. Deployment requires the installed grid DLL hash to
match the dependency build. No additional plugin or external tool is installed.
Separate F5/F7/F9/F10 controls remain available when the combined test is off.
While combined, those keys stop the whole comparison. A same-frame suppression
flag, combined ownership flag and F4 check in the grid driver prevent Unity script
update order from turning one feature back on after the shared stop. F6 is blocked
and any pre-existing pixel capture is stopped before the combined comparison.

The top-left IMGUI panel displays actual timing/pacing/smoothing state, remaining
time, 4x/8x mode, applied-correction count, save progress and the combined stop
reason. Its text refreshes at 10 Hz or immediately on state changes; GUIContent
and GUIStyle are reused. Only Repaint draws the panel; OnGUI does not mutate test
state. Drawing errors are handled in Update. A combined test requires a recent
successful panel draw and stops if the panel fails or has not drawn for a second.
The runtime logs a single STATUS PANEL READY once its first draw succeeds.

The installed game's stripped GUIStyle lacks the copy constructor; the panel uses
its available default constructor, a private style and the native Box/Label/CalcHeight
entry points instead. The compiled mutation audit explicitly allows only that local
GUI style/content in addition to the prior timing mutation surface. No global GUI
color/skin/font or new rendering/gameplay setter is introduced by the panel.

Builds pass without warnings. Existing motion/conservation/render-state audits pass,
and tests exercise the actual CombinedRun coordinator with shared deadlines,
replacement of separate tests, component stops, partial and silently declined
startup, cleanup failures and retries. UI appearance, OnGUI dispatch, keyboard
interaction and actual coordinated runtime behavior still need an in-game check.
Next user action: launch with CRT off, confirm the panel, then use F4 alone for
a short straight walk and stop/release comparison. No new movement mechanism is
being introduced in this revision.

### Timing 0.3.1: direct panel control and input evidence

The user reported that F4 enabled only timing. The 04:56:37Z launch loaded the
expected Grid 0.6.0 and Timing 0.3.0 DLLs, and STATUS PANEL READY confirmed a
successful draw. At 04:58:10Z and 04:58:34Z, logs show only TIMING ON (30.0s),
Coordinated=False, with no COMBINED ON, pacing/grid activation or control fault.
The installed DLL hash matches the deployed build. Decompiling that DLL confirms
Driver.Update calls the coordinator before Timing.Tick; the latter's independent
F5 branch is the only source path producing these standalone activations. Unity's
installed KeyCode values distinguish F4=285 from F5=286. This identifies the
executed path, not which physical key the user pressed or why that path was selected.
There is no evidence here of a partial combined startup failing after timing starts.

Version 0.3.1 adds a clickable Start all three / Stop all three control invoking the
same tested coordinator, bypassing function-key interpretation. Clicks only queue
a command for the next Update. IMGUI controls are now submitted in the same order
for every event so mouse/control IDs remain stable; text still refreshes at 10 Hz
or on state changes, and GUIContent/GUIStyle remain cached. A single TEST INPUT
entry per activation records the observed F4/F5/F7/F9/F10 values and panel-command
flag. The F4 and standalone F5 bindings are retained. This is a direct-control
workaround plus diagnostic evidence, not a demonstrated fix for an unknown key
mapping problem. Existing build, movement, lifecycle, coordination and mutation
checks pass; button interaction and coordinated live startup remain unverified.

### Combined F4 test confirmed visually and in telemetry

The user subsequently suggested they might have pressed F5 instead of F4, then
completed the next test and reported: "looks perfect to my eyes." The new input
logs explicitly show F4=True, F5=False and panel=False on both starts, plus F4
on the first manual stop. STATUS PANEL READY confirms successful drawing. Thus
the keyboard/coordinator path is now demonstrated; the button was not exercised.

Captures `20261004-050204-751` and `20261004-050213-916` contain 507 and 1,244
player updates respectively. Timing, VSync pacing and 8x smoothing all activate
with Coordinated=True and the same countdown. The first run's 507 grid frames
match its 507 player updates; F4 stops and restores all three. The second records
1,243 grid frames before a guard stops it at the next player update. It reports
119.46/119.99 update FPS with a 120 Hz display at 2560x1440, CRT off. Update FPS
does not independently establish physical presentation cadence.

All 54 eligible arrival steps approve in-frame and apply carry (10 in the first
run, 44 in the second). All 54 corrected states survive exactly into the next
consecutive player update; maximum reconstructed carried-frame movement error
is 0.000000956 game units. Eight other completion budgets are intentionally
declined for changed direction or non-current input. No timing fault occurs.
Log-overlap analysis reports 4.27/4.36 and 10.37/10.48 seconds because the timing
logger begins before synchronous pacing/grid initialization finishes; this is
startup ordering, not the previous mid-walk independent timeout problem.

The second stop is explained by the final CSV row: frame 3810 begins a diagonal
tile from (64,-184) to (80,-200), duration 0.2828427, with input (1,-1). This fails
the smoothing model's deliberate single-axis restriction. The coordinator then
stops timing/pacing and removes hooks on the next safe update, as intended. It
does not demonstrate support for diagonals or a random dropout during supported
straight walking. User testimony establishes the visual success on this route;
there is no final-output video/pixel-cadence measurement in these two captures.

Evidence is preserved in `artifacts/timing-tests/0.3.1-20261004/`. Installed DLLs,
current source and deployment records are saved as a scoped working baseline in
`artifacts/experiment-snapshots/combined-0.3.1-0.6.0-20261004/`. Keep this mechanism
as the comparison baseline. Next work should address unsupported movement and
camera states with explicit handling and targeted tests before extending normal
gameplay coverage; CRT support remains a separate unfinished item. No plugin
behavior was changed during this verification.

### Diagonal walking extension: Timing 0.4.0 / Grid 0.7.0

The preceding 0.3.1 / 0.6.0 straight-walking baseline remains preserved under
`artifacts/experiment-snapshots/combined-0.3.1-0.6.0-20261004/`.
Capture `20261004-050213-916`, frame 3810, ended the grid experiment on a legitimate
(16,-16) diagonal: start (64,-184), destination (80,-200), duration 0.2828427,
timer 0.0084416, rounded position (64,-184). This was the single-axis guard,
not evidence that the timing correction or renderer had failed.

Native `FieldEntity.MoveTo` at RVA 0xFE85D0 multiplies the base duration by a
square-root factor when both displacement axes are nonzero; the captured duration
matches 0.2 * sqrt(2). Timing now recognizes exactly adjacent 16-unit cardinal or
diagonal tiles at their corresponding normal durations. It still requires fresh
matching input, the normal arrival approval, an identical next direction/duration,
and the same frame before camera update. Direction changes, collision slides,
blocked tiles, dash, scripts and old input do not receive carry. No new hooks,
native calls or game setters were introduced, and moveTime is never written.

The render residual accepts a 16-unit displacement on both axes and verifies each
against native rounded interpolation. Existing VisualMotion already applies and
quantizes independent X/Y corrections and restores both together, so its transform
implementation did not change. Each component remains bounded by half a source
pixel before quantization, and half a high-resolution texel of quantization error.
Camera-follow/clamp, hierarchy, display, focus and transition guards are retained.

Both builds and full timing/grid checks pass with zero warnings: 89,520 cardinal
and 89,520 diagonal simulated frames, steady/jittered 30-360 FPS; cumulative
position error under 0.001 units per axis; callback counts preserved. Regression
checks cover the observed diagonal, all four diagonal directions at 4x/8x, endpoint
restoration, collision slides, turns, stale/analog input, changed speed, teleports,
and the existing task ownership, rollback and compiled mutation audits.

Live verification of this extension is pending. Next capture: CRT off, same open
area away from camera clamps, F4, several diagonal tiles, another diagonal direction,
brief cardinal movement and a stop. Check all three panel rows remain on and the
correction count increases on sustained diagonals. Let 15 seconds finish or stop
with F4. Camera boundaries and CRT remain separate follow-up work.

### Diagonal live result and unsupported scene attempts (2026-10-04)

Evidence is preserved in `artifacts/timing-tests/0.4.0-20261004/`, including all
three CSV/metadata pairs, timestamped logs, session-analysis.json and diagonal-detail.json.
The user reports smooth diagonals, normal direction changes and normal stopping.

Capture `20261004-051357-392` recorded 1,344 updates, with the grid rendering all
1,344 frames. Moving samples include 340 diagonal frames across (-16,16),
(-16,-16), and (16,16). There were 29 carried tile completions: 26 cardinal and
three diagonal, all three in direction (-16,16). All 29 final movement states
persisted exactly into the next update. Maximum reconstructed carried-frame
error was 0.00000088 game units. This is live evidence for diagonal carry in one
direction and rendering through three directions, not an all-directions or
full-game compatibility claim.

Of 34 early arrival steps, 29 carried, four native continuation requests changed
from diagonal to cardinal (consistent with collision sliding) and correctly
received zero carry, and one native arrival callback refused continuation. That
last callback reported foot_allows_next=false at frame 2975. Manual-control guards
then stopped timing and smoothing; the pacing override was released and hooks
removed. Sixteen input direction changes and one stale-input completion also
received zero carry. All three features overlapped for about 11.32 seconds; the
strict analyzer full-duration flag is false because startup is sequential.

The user also tried the Kefka/Terra soldier cutscene and the moogle/scripted battle
sequence. Logs do not name scenes, so exact scene-to-attempt mapping is unknown.
Three F4 attempts (frames 3761, 4901, 7969) were rejected before starting because
there was no active ordinary manual-walking controller. Two later attempts
(frames 12570, 13250; CSV stems 051651-329 and 051702-883) started timing/pacing,
but grid construction rejected the camera/target/compositor layout. Both rolled
back with zero sampled frames, zero arrival steps and zero movement corrections;
the header-only CSVs are expected results of partial-start cleanup. Logs confirm
hooks removed and VSync restored. This does not demonstrate cutscene/battle support.

F4 combines interventions and is intentionally unavailable outside their inspected
scope. The existing F8 logger is separate and does not require manual control:
it can inspect such a scene if exactly one active field camera-follow target is
present. It can still refuse pure battle scenes without that target. A passive
render-layout snapshot is the next evidence needed before broadening camera
support; do not simply remove the guards. No plugin behavior changed in this
verification turn.

### Automatic lifecycle and optional playthrough diagnostics

The working timed algorithms are now wrapped in independent automatic lifetimes:
Timing 0.5.0 and Grid 0.8.0. Movement carry policy, arrival admission, motion residual
and VisualMotion algorithms are unchanged from public baseline 8de6aa7. Corrections
no longer depend on a recording buffer or its capacity. F4 toggles automatic mode;
unsupported components suspend independently and recover when their observed
context changes. Camera-follow readiness requires observed matching movement.

Playthrough Diagnostics 0.1.0 is a separate optional plugin depending on value-only
observation/status APIs; the fix has no diagnostics assembly dependency. F2 toggles
bounded debug recording, F3 marks an incident. Cached camera/entity sampling works
while corrections are suspended, including camera-only evidence outside field
movement. Automatic incident candidates, deduplication, recording/file budgets,
coverage summaries and measured observer overhead are described in docs/runtime.md.
No GPU readback or new native diagnostic hooks were introduced.

Four implementation checkpoints separate recording ownership, automatic lifecycle,
optional diagnostics, and transition/deployment verification. Offline validation
includes 37 Python tests, cardinal/diagonal conservation checks, restoration and
mutation audits, 800 simulated lifecycle transitions, recorder fault isolation,
rolling window/dedup policies and writer failure/quota tests. The unchanged timed
algorithms have the earlier live evidence. The new automatic lifecycle, debug UI,
actual menu/cutscene/battle resumption and debug overhead still need live checks.
A successful model test is not a claim that those game scenes have been exercised.

Deploy-Runtime.ps1 builds/tests the matching set, preserves previous plugin DLLs,
verifies installed hashes and writes a local deployment/rollback manifest. See
README.md and docs/runtime.md for current controls; earlier investigation controls
and timer limits are historical. This build has not yet been installed; installation
requires the user's approval following an automatic approval-review rejection.


### Hotkey follow-up

Timing 0.5.1 / Grid 0.8.1 / Playthrough Diagnostics 0.1.1 move the automatic
controls to F9 (corrections), F10 (debug recording) and F11 (incident marker),
leaving the game's F1-F4 shortcuts alone. The automatic runtime continues to
suppress legacy standalone experiment actions, including their F9/F10 actions.
F8 remains an optional specialist field/render-hierarchy capture, unnecessary
for routine playthrough testing and independent of the correction runtime.
This source update is not an installation; the installed timed build is unchanged.

### Runtime installation and FFIV preparation

The user authorized installation on 2026-10-04. The FFVI 0.5.1 / 0.8.1 / 0.1.1 set is now installed, with matched previous-DLL backups and verified hashes. FFIV has only the generic loader prerequisites installed; its native movement code has a midpoint timer clamp absent from FFVI. See docs/ffiv-compatibility.md for evidence and the first-launch dependency. Neither installation establishes live correction compatibility in FFIV.
