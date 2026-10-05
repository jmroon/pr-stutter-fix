# Unrounded shared movement experiment

Version 0.3.0 compares timing/pacing alone against timing/pacing plus the shared
movement rounding bypass and resolution-only 8x. It changes logical positions
in B and remains a temporary test, not a supported fix. No scene-ID conditions
are used. The observer remains independent of all correction modules.

## Current coordinated comparison

Install with both games closed, selecting each title separately:

```powershell
./scripts/Deploy-ComparisonExperiment.ps1 -Game FFVI
./scripts/Deploy-ComparisonExperiment.ps1 -Game FFIV
```

The bundle installs Timing 0.6.3, Grid 0.9.2, UnroundedExperiment 0.3.0 and
PresentationAudit 0.1.3. It backs up all four DLLs and both affected configs,
including FFVI's older timing/grid binaries. Automatic corrections and ordinary
debug recording remain disabled. The comparison always starts OFF.

- **Shift+F11:** start **A** or stop the comparison; maximum 120 seconds total.
- **Alt+F11:** switch A/B without restarting timing/pacing or their deadline.
- **A:** tile-time carry + scoped display-paced VSync, stock rounding/resolution.
- **B:** the same carry/pacing + unrounded shared movement + resolution-only 8x.
- **Ctrl+F11:** start/stop the independent 60-second audit.
- Old camera/player compensation is OFF in both. F9 is blocked during the test;
  leave F9/F10 off outside it. F12 is unchanged.
- Any component stopping, loss of focus, timeout, quit or unload ends the whole
  comparison. It never silently restarts or continues under a false A/B label.

Turn CRT OFF and load an ordinary walking area. Press Shift+F11, confirm A,
then Ctrl+F11. Walk the same stretch for roughly 10 seconds in A, press Alt+F11
and repeat in B, then return to A for another 10 seconds. Ignore the immediate
allocation/toggle hitch; compare steady walking. Watch for an increasing carried
count in both modes and completed render frames in B. Ctrl+F11 saves;
Shift+F11 stops. Stay in one area, away from encounters/cutscene triggers.

Timing now admits fractional positions and leaves the carried position unrounded
only while B owns the comparison lease. Its normal rounded checks remain the
default; native arrival approval, fresh input and collision guards are unchanged.
No timing/smoothing algorithm is copied into the disposable experiment. A narrow
reflection bridge starts/stops the existing timing and pacing components and
reports actual state. Failed startup cleans up partial ownership; failed cleanup
retains the lease and prevents automatic takeover.

Audit conditions are `timing-pacing` and `timing-pacing-unrounded-8x`.
`comparison-invalid` is excluded. The live comparison contract checks actual
component activity, native patch state, requested resolution and absence of old
compensation; it does not rely on the previous frame's automatic status snapshot.
`CarriedTiles` demonstrates timing work; the analyzer counts observed increases
within each continuous condition, excluding switches/gaps. `ResolutionCompletedFrames`
is execution evidence for field-plus-compositor draws, not proof of final pixels.
Historical unrounded-only labels remain supported. No experimental condition is
counted as an unmodified baseline. The 20 Hz audit cannot measure scanout judder.

Pacing requests VSync count 1 and leaves the native targetFrameRate value alone,
using the previously tested display-paced path. This is not a guarantee about
VRR/driver presentation or a universal uncapped mode. B adds GPU load and startup
allocation. This comparison closes the missing timing/pacing condition before
rejecting the rounding-bypass direction. Live results are pending.

Restore the complete pre-install bundle with
`Restore-ComparisonExperiment.ps1 -Manifest <printed-path>` (game closed).
It verifies file ownership and refuses to overwrite subsequent changes.
The earlier installers/restorers remain for historical deployments; use the
complete bundle for 0.3.0. See [the pre-offshoot checkpoint](offshoot-checkpoint.md).

## Resolution-only implementation

The optional module clones the existing 320x180 field render targets at 2560x1440,
preserving their formats/filtering/wrapping. It accepts only the inspected FFIV
two-camera/one-target and FFVI four-camera/three-target layouts. Existing material
consumers, including the final compositor and transparency bindings, are found
once at activation. No recurring full-scene scan is performed.

Before drawing, it temporarily substitutes those targets and material textures.
After all admitted field cameras and the final compositor finish, it restores
the original bindings. Update also recovers bindings if a previous callback was
missed. Resources are retained across ordinary frames. On stop, bindings are
restored immediately, but textures/passes are released in Update or unload,
outside render callbacks and only after successful restoration. There are no transform, projection, shader-float or pacing writes,
and no dependency on the older GridExperiment assembly or VisualMotion code.
Only pure layout/ownership/callback-gate helpers are shared as source.

Camera target switches and movement at map boundaries are not themselves grounds
for stopping. A changed map/model/view, camera topology, display size, material
binding or unsupported compositor effect stops resolution mode. It does not
automatically restart or stop the separate movement bypass. The label and log
give the reason; Alt+F11 can start a fresh comparison in a stable supported area.
A restoration failure is latched as a fault, retains resources for cleanup and
also stops the movement bypass. Shift+F11 OFF, timeout, focus loss, quit and unload
stop both components. The setting is never persisted across launches.

CRT is deliberately excluded because the original CRT shader did not work
correctly with enlarged targets in earlier experiments. This comparison does
not attempt to repair that filter or establish complete cinematic compatibility.

## Exact intervention

At each reviewed call site, the interpolated float is already in XMM0 and the
rounding helper returns a float in XMM0. Replacing only that five-byte CALL with
five NOPs leaves the interpolated value in place. The original native code then
assigns it and continues with its own collider, camera and event updates.
The shared rounding function itself is never patched.

| Game | Movement implementation | Rounding CALL RVAs |
| --- | --- | --- |
| VI | FieldEntity.UpdateMovingSetPosition | 0xFEA17F, 0xFEA19B |
| VI | FieldPlayer.UpdateMovingSetPosition override | 0xFF3EEC, 0xFF3F08 |
| IV | FieldEntity.UpdateEntity | 0xBDCE0F, 0xBDCE2D |

The base path serves ordinary entities, including NPCs and the scroll dummy
where they use inherited movement. VI's player override needs its own two sites;
its existing unrounded mode is untouched. Specialized subtype movement outside
these paths is not claimed to be covered. Arrival snaps, segment timers, speed,
midpoint behavior, collision decisions and animation updates are not patched.
They can still be affected indirectly by receiving fractional positions.

**Correction to earlier FFIV inspection:** the decompiler expanded the sprite
tail call as if movement were duplicated. Assembly at 0xBDFF2D actually jumps to
FieldEntity.UpdateEntity at 0xBDCBA0. Patching the two shared call sites therefore
covers that sprite path too. This is source-path evidence, not live coverage.

## Guarding and restoration

Before activation, both original game/metadata file hashes must match the known
profile, the loaded module path must match the game, and every call plus adjacent
instruction bytes must match. No signature scanning or best-effort fallback is
used. Changes affect the current process only. The controller runs on the owning
Unity thread between the inspected main-thread movement calls; this patcher is
not safe for arbitrary concurrently executing worker code.

All sites are checked before any write. Each write temporarily changes memory
protection, flushes the instruction cache, restores protection and verifies the
result. Partial activation attempts restore owned calls and latch a fault.
Restoration never overwrites an unfamiliar third-party modification. A cleanup
conflict remains a visible fault requiring restart; it is not reported as OFF.
No managed callbacks, allocations or logging are added to each movement call.

Disable with Shift+F11; native code updates positions normally on subsequent
movement updates. A small snap on toggling is possible. Restart gives pristine
code from disk even if live restoration fails. To remove/revert the plugin with
the game closed:

```powershell
./scripts/Restore-UnroundedExperiment.ps1 -Manifest '<printed manifest.json>'
```

That restores only the experiment DLL. Its manifest also names the separate
audit/config restore manifest; use Restore-PresentationAudit.ps1 if those changes
should also be reverted. Both restorers refuse to overwrite changed files.

## Verification

`Test-UnroundedExperiment.ps1 -Game FFVI` / `-Game FFIV` build each profile and
verify exact installed PE call bytes and target RVAs. Tests cover all-site
preflight, modified surrounding instructions, partial activation/deactivation,
restoration, foreign ownership, latched faults and wrong-thread refusal. A
separate Windows x64 test process executes a synthetic float CALL before, during
and after bypass, checking fractional argument preservation and real executable
memory protection/cache operations. It never loads or patches the game.

Version 0.2.0 also tests completed field/compositor callback ordering, missing or
repeated callbacks, first-frame admission and next-frame recovery, binding writes
that throw after mutation, restoration failures and native binding replacement.
Compiled assembly checks reject transform/projection/pacing writes and linkage
to older compensation code. The independent audit still passes its read-only
assembly checks; analyzer tests separate stock/8x requests and completed frames.

Both builds and the updated read-only audit pass offline checks. Live shared-path
coverage was initially unverified. The first FFVI result below establishes sampled
fractional movement coverage, but not broad collider/event safety or visual benefit.

## First FFVI live result: 2026-10-04

Capture `20261005-020941-678-183ff751.json` lasts 37.485 seconds and contains
20 OFF and 634 ON field samples. The user could not see a difference with the
experiment enabled. The log confirms activation of all four reviewed calls and
restoration on quit, with the old timing/pacing/smoothing corrections disabled.
No experiment or observer fault is recorded.

The intervention demonstrably reached shared movement, not just the player:

| Role | Fractional logical-position observations while ON |
| --- | ---: |
| Player | 91 |
| Scripted camera target (scroll dummy) | 73 |
| NPC | 31 |

The OFF samples have no fractional logical observations. Camera position, cached
map scroll and the actual map root each retain fractions in 164 ON samples.
Of the 31 fractional NPC logical observations, 29 also have fractional visual
positions; camera-relative subtraction can legitimately yield an integer for
the remainder or for a player centered by the camera.

All 4,267 evaluated ON entity visual positions match the mapping exactly.
Another 496 visual checks concern the non-drawable scroll dummy and are excluded.
Camera and map checks each match 633 ON samples and disagree once. That sample
repeats the earlier baseline's numerical transition pattern: expected (-48,328),
observed (-32,344), maximum XY disagreement 16 units. Its recurrence is not proof
of cause or final visibility; it remains a mismatch. The 20 OFF samples have
20 camera, 20 map and 151 visual matches.

Example: the player reaches Y=-6.661872, the camera/cache reach Y=9.338128,
and the map root reaches Y=-1.3381281. Thus it would be incorrect to explain
this result as failure to patch movement or as all fractions being erased before
the observed visual boundary. Equally, spatial agreement does not show smooth
delivered pixels or validate every gameplay consumer.

This experiment did not deliver a noticeable standalone improvement in the
tested setup. It is consistent with coarse raster sampling masking the extra
precision, while unchanged timing/pacing can also remain limiting. The short OFF
interval and 20 Hz spatial recorder do not quantify a visual A/B difference.
Mean sampled observer cost is 0.098 ms; maximum 17.350 ms, with no per-sample cost
trace to locate that peak.

The next discriminating comparison would hold pacing fixed and combine this
source change with a resolution-only rendering increase, without the old player
or camera compensation. That would test whether a simpler source-plus-raster
approach has value. Neither that combination nor a replacement renderer has
been implemented by this evidence checkpoint. FFIV live testing remains pending.
Raw capture, log, analyzer report and fractional-path breakdown are preserved
locally in ignored `artifacts/ff6/unrounded-first/`.

## First FFVI resolution-only comparison: 2026-10-04

Capture `20261005-022244-917-c5b30d8f.json` contains 201 unrounded/stock-resolution
samples and 308 unrounded/8x samples. The user reports no noticeable improvement.
The log verifies two 8x activations with 713 and 390 completed field/compositor
frames, three enlarged targets and 19 material bindings. Neither activation
faulted: the first stopped manually and the second stopped on quit. Native
movement rounding was also restored on quit. Timing/pacing corrections stayed off.

All evaluated comparisons agree exactly: 509 camera, 509 map and 3,785 entity
visual positions. The 8x portion contributes 308 camera, 308 map and 2,292 visual
matches. Fractional player logical positions occur in 99 stock-resolution and
146 8x observations. Every sampled follow target is the player; this run does
not add moving-NPC or cinematic coverage. No audit samples were overwritten.

The recorder sees up to 710 completed frames in an 8x sample. A stock-mode
sample retains the preceding session's final 713 count before deferred cleanup;
that is historical state, not evidence that stock mode renders at 8x. Counts
reset on a new activation and must not be added across sampled rows. The two
non-overlapping completed activation totals come from the log, not that maximum.

This is execution evidence for the intended comparison and a negative reported
visual result. It is not evidence that the patch failed to activate. Conversely,
successful render callbacks and matching CPU positions do not establish that
fractional motion survives the final compositor or that frames reach the display
at even intervals. The audit samples at 20 Hz and does not capture final pixels
or presentation timing. Mean observation cost was 0.114 ms, maximum 17.525 ms;
the peak cannot be localized from aggregate timing.

Pause further expansion of this branch. Before another visual correction, obtain
evidence at the final pixel/compositor boundary and measure frame presentation.
If comparing against the earlier successful three-part experiment, keep movement
timing and pacing settings identical: those components were disabled here, so
this result cannot attribute the earlier improvement to compensation alone.
There is no new deployed runtime change in this evidence checkpoint. Captures,
log and analyzer output remain local in `artifacts/ff6/unrounded-resolution-first/`.
