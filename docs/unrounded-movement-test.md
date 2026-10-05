# Unrounded shared movement experiment

Version 0.1.0 tests removing XY rounding at the movement source in the inspected
FFIV/FFVI builds. This changes logical positions and is not yet a supported fix.
It is separate from the proposed presentation replacement, old timing/grid
corrections and read-only presentation audit. No scene-ID conditions are used.

## Controls and first comparison

Install with the game closed:

```powershell
./scripts/Deploy-UnroundedExperiment.ps1 -Game FFVI
# Or -Game FFIV
```

This installs the experiment and audit 0.1.1, backs up their previous DLLs and
the affected configs, and leaves old corrections/debug recording disabled.
Existing timing/grid DLLs and game/save files are not changed. The experiment
starts OFF every launch; there is no persistent enable setting.

- **Shift+F11**: enable/disable unrounded movement. The panel displays ON/OFF.
- **Ctrl+F11**: start/stop the independent 60-second spatial recording.
- The experiment restores its calls after 120 seconds, focus loss, quit or unload.
- Keep F9/F10 off. The experiment refuses activation if the old correction
  runtime is enabled or unreadable, and stops if that runtime becomes enabled.
- F12 is unchanged. Shift+F11 can also reach the old F11 incident marker if someone
  separately enables normal diagnostics; leave that recorder off for this test.

Start the audit, walk briefly with the test OFF, then press Shift+F11 and repeat
walking, turning, stopping and touching a wall. If convenient, include a moving
NPC or the scripted camera sequence. Stop with Ctrl+F11 when finished; there is
no need to fill 60 seconds. For the initial gameplay check, use an existing save
and avoid saving over it while the experiment is enabled.

Audit samples distinguish `unrounded-movement` from `corrections-disabled`.
The analyzer reports comparisons and fractional logical positions by condition
and entity role; it never counts an experiment sample as an unmodified baseline.
Unknown/faulted experiment state excludes comparisons. The observer has no
compile-time dependency on the experiment and performs no mutation.

This first comparison changes neither frame cap nor render-target resolution.
CRT setting can remain as it was. Coarse raster sampling, tile time loss and
native sprite animation frame changes remain. No visible improvement by itself
would not establish that the bypass failed: fractional logical observations
identify whether the intended path ran. Gameplay and final pixels require live
checks even when the position equations still match.

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

Both builds and the updated read-only audit pass offline checks. Live shared-path
coverage, collider/event behavior, cinematic behavior and visual benefit remain
unverified until the new experiment is run.
