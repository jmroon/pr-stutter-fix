# Pre-offshoot checkpoint

The requested approximately 9:46 PM checkpoint is **`fa00647`**, committed
**2026-10-04 21:45:46 America/Toronto**, titled “Record FFVI cinematic audit and
unresolved transition disagreement”. Tag: **`checkpoint/pre-unrounded-offshoot`**.
Do not roll back yet: the coordinated timing/pacing A/B comparison is pending.

If this direction is abandoned, return source/runtime behavior to that checkpoint
without erasing the experiment history. The user's conversation fork supplies the
earlier context. Keep the findings below as evidence, not as enabled runtime code.

## Findings worth retaining

- Shared movement rounding bypass reached FFVI player movement, NPCs and scripted
  camera targets. The first capture recorded fractional logical positions for all
  three. It was not merely a player sprite correction.
- Unrounded movement alone produced **no noticeable improvement** for the user.
- Resolution-only 8x completed 1,103 rendered frames across two activations without
  reported faults. The user still saw **no noticeable improvement**. Timing and
  pacing were disabled for those tests, so the combined case remained open.
- FFIV's apparent duplicated sprite movement path is a native tail jump into the
  shared entity update. It does not require a separate scene-specific patch.
- The collider-offset concern is an unverified compatibility risk, not an observed
  failure. Native code recomputes destination-minus-current offset; this alone
  does not establish safety.
- Coordinating timing exposed two rounded assumptions in our own timing fix:
  position validation and the carried position. The new comparison handles both
  only while B is active. Otherwise a nominal combined test could skip timing
  work or reintroduce rounded carry positions.

The independent audit is useful even if the movement bypass is removed. The native
call patch and resolution experiment are isolated modules and can be removed.
None of these findings establishes a generalized visual fix or gameplay safety.

## Local installation recovery

Git source and installed DLLs are separate. Every comparison deployment saves four
DLLs and two configs under `artifacts/plugin-backups/comparison-<game>-<UTC>/`.
`Restore-ComparisonExperiment.ps1` uses that manifest to restore the immediate
pre-comparison state, validating hashes before writing anything.

Earlier unrounded/audit manifests remain available for the rest of the offshoot.
The first unrounded installation backups are:

| Game | Experiment manifest directory | Audit/config manifest directory |
| --- | --- | --- |
| FFVI | `unrounded-FFVI-20261005-020013-053` | `audit-FFVI-20261005-020015-205` |
| FFIV | `unrounded-FFIV-20261005-020023-370` | `audit-FFIV-20261005-020025-423` |

All are beneath `artifacts/plugin-backups/`, with `manifest.json` in each.
Those first experiment installs had no previous experiment DLL; their audit
backups contain the earlier auditor and configs. Restore newer manifests in reverse
installation order before older ones, or construct and verify an explicit target
inventory if game-written config headers have changed. Never force past a hash
conflict or substitute rebuilding HEAD for restoring the recorded binaries.
Save files and the game executables are outside these installers' scope.
