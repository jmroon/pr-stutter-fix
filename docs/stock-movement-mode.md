# Stock-resolution smooth walking

UnroundedExperiment 0.4.0 promotes the tested stock-resolution combination to a
dedicated control. It remains an FFIV/FFVI preview, not an all-scene fix.

## Current controls

- Click **Enable smooth walking (stock resolution)** in the upper-right panel,
  or press **Shift+F11**, to start/stop.
- Load ordinary controllable field walking before starting. The mode starts OFF
  on each launch and has no fixed-duration timeout.
- **Ctrl+F11** starts/stops the independent 60-second audit.
- **Alt+F11** no longer switches resolution. Keep the older F9/F10 controls off.
- Native resolution, camera rendering and material bindings remain untouched by
  this mode. The 8x session/callback implementation is excluded from its assembly.
- The panel reports component state and the reason for stopping. Loss of manual
  control, a scene change, focus loss, component failure or quit still stops the
  whole preview. There is no automatic restart yet.

The button queues a command consumed in Update, outside GUI callbacks. Its
Shift+F11 fallback remains available if the panel fails. Timing 0.6.3 and Grid
0.9.2 provide the existing carry and pacing implementations; old compensation
is not activated. PresentationAudit 0.1.4 remains independent, using the existing
`timing-pacing-unrounded-stock` condition. It reports requested scale 1 and zero
high-resolution render frames.

Install while the selected game is closed:

```powershell
./scripts/Deploy-ComparisonExperiment.ps1 -Game FFVI
# Or -Game FFIV
```

The installer retains its historical name and six-file backup manifest format.
Use `Restore-ComparisonExperiment.ps1 -Manifest <printed-path>` to restore the
previous bundle. Source checkpoint `checkpoint/pre-stock-mode` (`39477cf`)
preserves the resolution comparison and its recorded result.

Both profile build/native-memory/ownership checks and the strengthened compiled
assembly checks must pass before deployment. The latter reject RenderTexture
references, the 8x session/callback classes, camera/transform writes and material
texture writes. The retained pure resolution tests exercise historical helpers;
they do not imply that the renderer remains in the new DLL. The on-screen button
still requires a live visibility/click check. Native CRT appearance also remains
visually unverified, despite no postprocessing replacement in this mode.

## Next implementation checkpoints

1. **Separate component lifetimes.** Keep the user's enabled intent separate
   from each component's current eligibility. Timing continues only for the
   inspected manual controller/arrival-task path. Pacing should depend on a
   supported active field context rather than a player follow target or input
   permission. The native rounding bypass has process-code ownership, not camera
   or texture ownership; examine its eligibility independently. One expected
   timing suspension must not shut down unrelated healthy components.
2. **Handle context replacement and faults explicitly.** Clear pending carry and
   old controller references at a transition; reacquire the new field/controller
   before resuming timing. Never carry time between scenes. A normal unsupported
   context may resume when a supported one returns. A failed patch, cleanup or
   unknown mutation must remain visibly faulted, with no repeated blind retries.
   Log context generations and per-component stop/resume reasons through optional
   diagnostics; do not put diagnostics in the correction's dependency path.
3. **Verify ordinary field transitions and scripted cameras.** Revisit the
   moogle-cave fall and Vector pan with pacing/precision demonstrably active and
   manual timing visibly suspended. Then test returning to player control,
   stationary-camera NPC motion, camera clamps, pause/focus and repeated map
   entry/exit. Only attribute remaining cinematic judder to another algorithm
   after confirming which components actually ran. Do not reuse player input or
   manually step cinematic tasks to make the manual timing fix apply there.
4. **Expand scene families and game adapters.** Battles, menus, world maps,
   transport and effects need classified coverage; they may use different
   movement/update paths and do not automatically inherit the field result.
   Inspect each additional PR game's binary/metadata and exact call sites before
   enabling it. Use shared context rules and narrow adapters, not scene names.

The next release should target checkpoint 1/2 with diagnostic evidence for 3.
It should not claim every scene is supported simply because the toggle survives
a transition. Start with CPU state/carry and component-lifetime evidence; use
final-pixel or PresentMon capture when visible judder persists with those states
verified. No new 8x or camera/player compensation is indicated by current evidence.

The older FFVI PlaythroughDiagnostics 0.1.1 dependency warning is an outstanding
packaging cleanup, separate from the working independent audit. Resolve that
before promoting a normal playthrough-debug workflow.
