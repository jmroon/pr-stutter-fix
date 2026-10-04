# FFIV compatibility preparation

FFVI runtime Timing 0.5.1 / Grid 0.8.1 / Playthrough Diagnostics 0.1.1 was
installed on 2026-10-04 after explicit user authorization. Existing DLLs were
backed up and installed hashes verified. Automatic transitions remain subject
to live verification. The local deployment manifest is
`artifacts/runtime-deployment.json`.

FFIV is not yet supported by the correction build. Its generic BepInEx loader
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

Next: launch FFIV to the title screen and close it; inspect the generated bindings,
finish verifying scheduler/player/camera ordering and rendering assumptions,
then build a separately gated FFIV adaptation. Do not bypass the FFVI hash gate
or install the FFVI binaries as if they supported FFIV.
