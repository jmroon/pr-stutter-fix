# Unrounded shared movement experiment

Version 0.2.0 tests removing XY rounding at the movement source in the inspected
FFIV/FFVI builds. This changes logical positions and is not yet a supported fix.
It is separate from the proposed presentation replacement, old timing/grid
corrections and read-only presentation audit. It adds an optional resolution-only
8x comparison with no position compensation. No scene-ID conditions are used.

## Controls and first comparison

Install with the game closed:

```powershell
./scripts/Deploy-UnroundedExperiment.ps1 -Game FFVI
# Or -Game FFIV
```

This installs the experiment and audit 0.1.2, backs up their previous DLLs and
the affected configs, and leaves old corrections/debug recording disabled.
Existing timing/grid DLLs and game/save files are not changed. The experiment
starts OFF every launch; there is no persistent enable setting.

- **Shift+F11**: enable/disable unrounded movement. The panel displays ON/OFF.
- **Alt+F11**: while movement is ON, switch between stock and 8x field resolution.
  Turn CRT OFF first. The panel shows the resolution and completed render frames.
- **Ctrl+F11**: start/stop the independent 60-second spatial recording.
- The experiment restores its calls after 120 seconds, focus loss, quit or unload.
- Keep F9/F10 off. The experiment refuses activation if the old correction
  runtime is enabled or unreadable, and stops if that runtime becomes enabled.
- F12 is unchanged. Shift+F11 can also reach the old F11 incident marker if someone
  separately enables normal diagnostics; leave that recorder off for this test.

For the resolution comparison, turn CRT OFF, load an ordinary field area, press
Shift+F11 to enable unrounded movement, then Ctrl+F11 to start the audit. Walk
briefly at stock resolution; press Alt+F11 and confirm 8x with an increasing
completed-frame count. Repeat the same walking/turning/stopping, then Alt+F11
back to stock for another short comparison. Ctrl+F11 stops/saves. Stay in one
area for this first comparison; there is no need to fill 60 seconds or repeat a
cinematic. Use an existing save and avoid saving over it during the experiment.

Audit samples distinguish `unrounded-movement`, `unrounded-movement-8x` and
`corrections-disabled`. The 8x label is a request state, not proof of rendering:
`ResolutionCompletedFrames` records completed field-plus-compositor draws for
the current activation, and the analyzer reports the maximum by condition.
The analyzer reports comparisons and fractional logical positions by condition
and entity role; it never counts an experiment sample as an unmodified baseline.
Unknown/faulted experiment state excludes comparisons. The observer has no
compile-time dependency on the experiment and performs no mutation.

The comparison keeps the frame cap/VSync configuration unchanged. Larger targets
can change GPU load; activation also performs discovery and allocation, so this
does not promise unchanged frame delivery. Tile time loss and native sprite
animation frame changes remain. Fractional observations identify whether the
movement bypass ran. Gameplay and final pixels require live checks even when
the position equations still match.

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
