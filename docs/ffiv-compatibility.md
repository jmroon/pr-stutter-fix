# FFIV compatibility preparation

FFVI runtime Timing 0.5.1 / Grid 0.8.1 / Playthrough Diagnostics 0.1.1 was
installed on 2026-10-04 after explicit user authorization. Existing DLLs were
backed up and installed hashes verified. Automatic transitions remain subject
to live verification. The local deployment manifest is
`artifacts/runtime-deployment.json`.

An FFIV preview is installed from the shared runtime. Initial live evidence
confirms tile-boundary carry and pacing, but rendering needed a layout adaptation. Its generic BepInEx loader
was prepared from the existing working local installation with
`scripts/Prepare-GameLoader.ps1`. This fresh-install helper copies only Doorstop,
.NET and BepInEx core files; it refuses existing destinations, checks the target
process, records a file manifest, and verifies every copied hash. No plugins,
patchers, configuration, cached Unity libraries or generated game bindings are
copied. First launch must generate FFIV's own interop assemblies.

## Inspected FFIV build

- GameAssembly SHA256: `bb2f4c9db44c8ee9b065696aeafb77561a1709492130f045433e6d215abc42ed`
- Metadata SHA256: `37400ca079eddb18cda06450c02c7bd8eb2c057175502e75e2e2c8bc09b31f41`
- UnityPlayer file version: `2019.4.26.14694700` (same version string as the local
  FFVI installation; the binaries have different hashes).

Read-only Il2CppDumper metadata and 19 targeted native method exports are kept
locally under `artifacts/ff4/`. They are excluded from Git. Native decompilation
is inferred C, not recovered source; semantic claims below were checked against
specific field accesses and the constant in the binary.

## Differences requiring an adaptation

FFIV `FieldEntity.UpdateEntity` (RVA `0xBDCBA0`) adds delta time, then clamps the
movement timer to `moveTime * 0.5` when the previous timer was below that threshold
and the new timer reaches or crosses it. The multiplier at VA `0x181AB9928` is
confirmed as float `0.5` from the PE file. This discards any excess time at the
midpoint, in addition to clearing the timer and duration at tile completion.
The inspected FFVI method (RVA `0xFE9E20`) does not contain that midpoint clamp.
The visible impact and any gameplay reason for the clamp remain unverified.
Removing it globally is not justified by this observation.

FFIV's arrival iterator is `FootMonitoring.<UpdateMonitor>d__36`, with MoveNext
at RVA `0x688BA0`; FFVI uses `d__41`. FFIV's inspected iterator retains wait states
1/2 and the callback followed by a state-3 yield. Its `TaskBase.Start` at
`0x68E790` sets Running, and `EnumeratorTaskProcess.Update` at `0x52A360` steps a
running iterator. These similarities do not establish complete hook compatibility.

## Preview adapter and verification

FFIV first launch completed and generated its own bindings. The shared runtime
now builds separately with `-p:PrGame=FFIV` against those bindings. FFVI remains
the default profile. FFIV build/intermediate outputs use `bin/FFIV` and `obj/FFIV`
so they cannot replace FFVI outputs. Compile checks require the corresponding
game data directory; deployment verifies the exact DLL/metadata hashes first.
Runtime gates also reject a mixed Timing/Grid profile; optional diagnostics
checks the runtime's game identity before subscribing.

`GameProfile.cs` isolates binary identity, vehicle checks and queued-path access.
FFIV has no `IsRiging` or public `MovementPositionList` property: its adapter
requires Walk state, rejects takeoff/landing and low flying, and reads the native
`movementPositionList` field. The arrival iterator alias is selected at compile
time. The carry algorithm, task admission policy and rendering algorithm remain
shared. No additional game writes or native hooks were introduced.

The preview deliberately preserves FFIV's midpoint clamp. The inspected character
update also computes collider offsets from its current/destination positions, and
sprite animation reads the movement timer. Their relationship to the clamp needs
live evidence before any further intervention. The panel labels this limitation.
Diagnostics include the game identity; the offline analyzer reports matching
midpoint clamps and estimated discarded milliseconds from Before/After records.
These observations require timing to be active and diagnostic recording enabled;
they do not report a correction or prove visible jitter.

Native MainGame.Update processes its task machine before the main-game subscene
controller. FieldController.UpdateController updates the camera before visual
instances. The five hooks retain their existing identity, fresh-input,
exclusive-arrival, manual-control and same-frame guards. Actual callback order,
carry persistence and camera layouts still need the first live FFIV capture.

Both profile builds and existing timing/rendering/diagnostic audits pass, including
800 simulated lifecycle transitions. The compiled timing audit checks that each
profile references its own arrival iterator and excludes the other's. Both
cross-game deployment profile mismatches are rejected. All 39 Python tests pass,
including midpoint candidate detection and refusal on changed state/long frames.
These checks do not establish live FFIV compatibility.

Build/install the preview with the game closed:

```powershell
./scripts/Deploy-Runtime.ps1 -Game FFIV
```

Controls match FFVI: F9 corrections, F10 diagnostics, F11 incident marker. Start
in an ordinary manual walking area with CRT off. Turn on F10, walk and stop,
change direction, and use F11 after any visible issue. Let three seconds pass,
then F10 off to save. Check the three component statuses; suspension is evidence
of an unsupported context, not permission to remove a guard. FFIV's old F8
specialist logger is not ported or installed by this runtime deployment.

## Collection compatibility plan

Treat each executable/build as a separate profile, even if signatures compile.
For each title, first inventory hashes and generate bindings; inspect movement
update, tile completion, task ownership, vehicle guards and render assumptions;
then run a small live matrix: ordinary walking/turning/stopping, camera clamps,
menu/focus recovery, and a scripted movement/battle transition. Use diagnostics
to identify broader playthrough requirements. A title-screen boot alone is not
compatibility validation. Full-game testing is not required to identify these
initial differences.

| Title | Current evidence |
| --- | --- |
| I, II, III, V | Not inspected; no supported build profile |
| IV | Tile carry/pacing activated live; two-camera render adaptation and bounded discovery await retest |
| VI | Earlier walking algorithms have live evidence; latest automatic lifecycle still needs live transition checks |

## First FFIV capture: underground overworld and castle visit

The log shows timing/pacing activation in area IDs 2 and 39, with smoothing
refused by the old four-camera/three-target check in both. Saved captures cover
area 2 only; the exact castle camera layout is not recorded in these captures.
The final 15.05-second capture contains 27 carried tiles, all preserved in the
next frame, with maximum reconstructed error approximately 0.00000108 units.
It also contains 30 native midpoint clamps, estimated to discard 158.15 ms.
The earlier incident captures overlap this final window; their counts must not
be added together.

Area 2 camera descriptors show CameraFieldMain and CameraTileMap sharing one
320x180 target, with the expected orthographic size 90 and tile mask 58867457.
Grid 0.9.1 accepts exactly this named two-camera/one-target topology for FFIV.
FFVI retains its named four-camera/three-target topology. Compositor, target
sharing, additional target users, shaders, player visuals, camera transforms,
manual control and restoration checks are unchanged. A single render root uses
the same translation helper with no transparency-camera children. New tests
reject incomplete, duplicate, cross-game and unknown layouts and exercise
single-root translation/restoration. This adaptation still needs live verification.

Debug overhead in the final capture averages 0.131 ms but reaches 16.545 ms;
recurring discovery dominates measured observer work. The previous discovery
cast every FieldEntity twice and built metadata for hundreds of objects although
it sampled only eight. Playthrough Diagnostics 0.2.1 finds players directly,
stops selection at eight unique entities, and describes only selected entities.
The full native FindObjectsOfType query still runs and is not guaranteed cheap.
Actual overhead improvement must be measured in a new capture; compare perceived
smoothness with diagnostics off first. The second sampled camera now prioritizes
CameraTileMap rather than an auxiliary world-map camera.

## Second FFIV capture: automatic-start ordering

The revised layout and all visual/compositor checks succeeded in both area 39
and area 2. Each smoothing attempt then failed with `Camera rendered before
target preparation`, reporting zero completed/motion frames. Timing continued:
the manual incident has 30 carried tiles, all observed unchanged next frame.
Its recordings overlap the final recording and must not be summed.

The automatic controller starts features in LateUpdate. When the grid driver's
LateUpdate has already run that frame, newly attached camera hooks can receive
render callbacks before Session.Prepare has ever executed. The earlier timed
startup from Update did not expose that ordering. Grid 0.9.2 adds an explicit
first-preparation gate: pre/post callbacks are ignored until preparation succeeds,
then stale or absent preparation remains an error. Recovery/restoration does not
reset the gate to unarmed. Tests cover both activation orders, ignored startup
callbacks, restoration, extra callbacks and missing later preparation. This is
shared automatic-runtime behavior; it is not an additional FFIV rendering layout.

The second diagnostic build still measured up to 20.443 ms observer cost; bounding
metadata construction did not resolve native scene-query cost. Diagnostics 0.2.2
removes periodic discovery, using coalesced scene/context changes with one-second
minimum spacing. It records discovery count/max duration. New actors in an
unchanged context may be missed until recording is restarted. No claim is made
that startup/transition scans are negligible or that live overhead is verified.
Both profiles build and pass rendering, timing and read-only diagnostic checks;
the new live smoothing result is still pending.
