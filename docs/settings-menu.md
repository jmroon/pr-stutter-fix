# Smooth movement settings

Press **F11** to open or close the menu. Pause the game first: the overlay does
not pause gameplay or block the game's own input. It releases the mouse cursor
while open and restores its previous state on close/focus loss. The menu scrolls
on small screens.

The menu saves changes automatically, separately for each game:

| Setting | Choices | First-run default |
| --- | --- | --- |
| Smooth movement | On / Off | On |
| Rendering | Native 320x180 / 4x 1280x720 / 8x 2560x1440 | 4x |
| Menu key | F11 / F10 / Insert | F11 |

4x is the practical default based on the latest FFVI comparison. Both modes
rendered with precision, timing and pacing active; the user found them hard to
distinguish, with perhaps a small advantage for 8x. 4x uses one-quarter the target
pixels of 8x. This does not establish a fourfold total performance improvement,
Steam Deck compatibility, or identical visual quality. The 23.89-second local
audit had 142 native, 150 4x and 146 8x samples, with no runtime fault flags.

**CRT must be off for 4x/8x.** With CRT on, rendering stays native and the menu
explains why. The movement corrections can remain enabled. This integration
does not replace the CRT filter or expand cinematic/vehicle coverage.

The selected resolution is user intent, separate from the active rendering
state. It survives restarts, focus loss, scene changes, and unsupported scenes.
Supported ordinary walking resumes higher resolution after a stable 250 ms
eligibility interval and fresh frames. Rendering is restored before abandoning
the old scene. Unsupported topology stays native; no repeated attempt occurs
until eligibility/context/settings change or **Retry rendering in this scene**
is selected. Restoration faults require restarting the game.

**Diagnostics and troubleshooting** expands the independent recorder controls
and rendering retry. Recording is optional, bounded to 60 seconds, and never
auto-starts from a saved setting. **Ctrl+F11** still starts/stops it. Recording
status appears only while recording; the latest result is visible in the menu.
Remove the audit DLL and correction/settings behavior still works.

Shift+F11 and Alt+F11 are retired. F9 and the legacy correction panel are
suppressed while the new settings controller is installed, including when the
master switch is off. The separate historical F8 logger is not part of the menu.

Settings are stored in `BepInEx/config/prstutter.settings.json`; ordinary use
does not require editing it. Writes use a flushed temporary file and atomic
replacement. Save failures are visible as **Session only** and preserve the
previous file. Malformed or newer-version settings are preserved, default the
master switch off, and allow session-only choices instead of overwriting them.

## Architecture and verification

Stock Movement 0.6.0 owns preferences and menu, Timing 0.8.0 owns timing/pacing,
Grid 0.10.0 supplies existing shared runtime services, Rendering 0.3.0 owns only
render textures/material bindings, and optional Audit 0.3.0 observes behavior.
Existing assembly/plugin IDs remain stable for upgrades and rollbacks.

The rendering module reads the settings/status contracts but cannot write them.
The movement assembly still has no render-target implementation, camera/pose
writes or rendering dependency. The menu queues explicit diagnostic commands;
the recorder never drives corrections. Closing the menu performs no recording
or file writes. Preference writes occur only on initial creation or changes.

Offline checks cover settings reload, invalid schema, blocked writes and cleanup;
fresh context/stable interval, one-attempt gating and rearming; existing native
patch, timing, render ownership/restoration and audit assembly boundaries. Both
FFIV and FFVI profiles compile; automated checks do not replace in-game UI and
transition verification. This menu and automatic render reacquisition still
need a live smoke test.

```powershell
./scripts/Deploy-SmoothMovement.ps1 -Game FFVI
# Or explicitly install the FFIV profile:
./scripts/Deploy-SmoothMovement.ps1 -Game FFIV
```

The installer requires the game closed, verifies the profile, runs checks, and
backs up every changed DLL/configuration in a hash-verified manifest. It retains
existing user settings. Roll back with
`scripts/Restore-ComparisonExperiment.ps1 -Manifest <printed-manifest>`.
User-created settings are preserved during rollback.

The pre-integration baseline is tagged `checkpoint/pre-settings-integration`.
Implementation is split into menu/persistence, render lifecycle, and bundled
deployment/documentation commits. The full tip is the intended test build.

For the first live check: launch with CRT off, open F11, change a setting, restart
to confirm persistence, then enter/leave a field and regain focus. Check that
the selected scale is remembered while the active status suspends/resumes.
