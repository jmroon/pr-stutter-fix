"""Summarize passive PR Stutter traces. Coordinates are game units, not proven display pixels."""
import argparse
import csv
import json
import math
import statistics
from collections import Counter, defaultdict
from pathlib import Path


def percentile(values, fraction):
    if not values:
        return None
    values = sorted(values)
    index = (len(values) - 1) * fraction
    low = int(index)
    high = min(low + 1, len(values) - 1)
    return values[low] + (values[high] - values[low]) * (index - low)


def summarize(path, warmup_seconds=1.0):
    path = Path(path)
    if warmup_seconds < 0:
        raise ValueError("Warmup must be nonnegative")
    metadata = json.loads(path.with_suffix(".json").read_text(encoding="utf-8"))
    if metadata.get("SchemaVersion") not in {1, 2} or metadata.get("QpcFrequency", 0) <= 0:
        raise ValueError("Unsupported capture schema or invalid QPC frequency")
    polling = metadata.get("ObservationMode") == "LateUpdatePolling"
    if metadata.get("SchemaVersion") == 2 and not polling:
        raise ValueError("Unsupported observation mode")
    with path.open(newline="", encoding="utf-8-sig") as stream:
        reader = csv.DictReader(stream)
        required = {"phase", "frame", "entity_id", "controller_id", "camera_id", "player_move_state", "qpc",
                    "observer_ticks", "unscaled_delta_time", "move_duration", "candidate_x", "candidate_y",
                    "result_x", "result_y", "camera_world_x", "camera_world_y", "target_width", "target_height"}
        if not required.issubset(reader.fieldnames or []):
            raise ValueError("Capture columns are incomplete")
        rows = list(reader)
    if len(rows) != metadata.get("Rows"):
        raise ValueError("CSV row count does not match metadata; capture may be incomplete")
    cutoff = int(metadata["StartQpc"]) + int(warmup_seconds * metadata["QpcFrequency"])
    rows = [r for r in rows if int(r["qpc"]) >= cutoff]
    phases = Counter(r["phase"] for r in rows)
    warnings = []
    if metadata.get("PluginVersion") == "0.2.0":
        warnings.append("INVALID BASELINE: plugin 0.2.0 caused a camera regression. Use this capture only to investigate the instrumentation failure.")
    if polling:
        warnings.append("LateUpdate polling does not observe native movement outputs or final pre-render camera state; script ordering is unspecified.")
    if not rows:
        warnings.append("No rows remain after warmup exclusion.")
    if metadata.get("DroppedRows", 0):
        warnings.append("Capture buffer filled; some rows were dropped.")
    if metadata.get("StopReason") in {"observer_error", "shutdown", "capacity", "field_camera_inactive"}:
        warnings.append("Capture ended early: " + metadata["StopReason"])
    if not polling and not phases["MovementResult"]:
        warnings.append("No movement probe rows; move during capture and confirm hooks loaded.")
    if not polling and not phases["CameraAfter"]:
        warnings.append("No camera-follow rows; this path was not observed.")
    if not polling and not phases["PreCull"]:
        warnings.append("No pre-cull callbacks; final camera sampling remains unverified.")

    movement = defaultdict(lambda: {"samples": 0, "matching_round_xy": 0, "integer_output_xy": 0, "max_error": 0.0})
    frame_times, observer_ticks = {}, defaultdict(int)
    cameras = defaultdict(dict)
    camera_descriptions = {int(c["Id"]): c for c in metadata.get("Cameras", [])}
    for row in rows:
        frame = int(row["frame"])
        dt = float(row["unscaled_delta_time"])
        if math.isfinite(dt) and dt > 0:
            frame_times[frame] = dt * 1000
        observer_ticks[frame] += int(row["observer_ticks"])
        if not polling and row["phase"] == "MovementResult":
            candidates = [float(row["candidate_" + a]) for a in "xy"]
            results = [float(row["result_" + a]) for a in "xy"]
            if all(math.isfinite(v) for v in candidates + results):
                stats = movement[row["player_move_state"]]
                stats["samples"] += 1
                stats["matching_round_xy"] += all(abs(round(c) - r) < 1e-4 for c, r in zip(candidates, results))
                stats["integer_output_xy"] += all(abs(round(r) - r) < 1e-4 for r in results)
                stats["max_error"] = max(stats["max_error"], *(abs(c - r) for c, r in zip(candidates, results)))
        if row["phase"] == ("LateUpdate" if polling else "PreCull"):
            # A camera can render repeatedly within one frame; use its last callback for step summaries.
            cameras[int(row["camera_id"])][frame] = row
    camera_stats = []
    for camera_id, samples in sorted(cameras.items()):
        deltas = []
        ordered = sorted(samples.items())
        for (first_frame, first), (second_frame, second) in zip(ordered, ordered[1:]):
            if second_frame != first_frame + 1:
                continue
            if any(first[k] != second[k] for k in ("entity_id", "controller_id", "player_move_state")):
                continue
            if min(float(first["move_duration"]), float(second["move_duration"])) <= 0:
                continue
            dx, dy = [float(second["camera_world_" + a]) - float(first["camera_world_" + a]) for a in "xy"]
            if math.isfinite(dx) and math.isfinite(dy):
                deltas.append((dx, dy))
        axis = 0 if sum(abs(d[0]) for d in deltas) >= sum(abs(d[1]) for d in deltas) else 1
        steps = Counter(round(abs(d[axis]), 4) for d in deltas)
        camera_stats.append({
            "id": camera_id, "name": camera_descriptions.get(camera_id, {}).get("Name", "unknown"),
            "frames": len(samples), "moving_frame_pairs": len(deltas), "dominant_axis": "xy"[axis],
            "absolute_step_histogram_game_units": dict(steps.most_common(8)),
            "render_target_sizes": sorted({(int(r["target_width"]), int(r["target_height"])) for r in samples.values()})
        })
    times = list(frame_times.values())
    overhead = [t / metadata["QpcFrequency"] * 1000 for t in observer_ticks.values()]
    return {
        "capture": str(path), "warmup_seconds_excluded": warmup_seconds, "rows_analyzed": len(rows),
        "observation_mode": "LateUpdatePolling" if polling else "NativeHooks",
        "phase_counts": dict(phases), "movement_by_state": dict(movement),
        "unity_frame_ms": {"median": statistics.median(times) if times else None, "p95": percentile(times, .95), "max": max(times, default=None)},
        "measured_observer_body_ms_per_frame": {"median": statistics.median(overhead) if overhead else None, "p95": percentile(overhead, .95), "max": max(overhead, default=None)},
        "cameras": camera_stats, "warnings": warnings,
        "interpretation": "Camera coordinates and Unity timing do not alone establish displayed pixel movement or presentation timing. Probe-body timing excludes callback and interop overhead. Native output comparisons are unavailable in polling mode."
    }


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--warmup-seconds", type=float, default=1.0)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    result = summarize(args.capture, args.warmup_seconds)
    text = json.dumps(result, indent=2, allow_nan=False)
    if args.output:
        args.output.write_text(text + "\n", encoding="utf-8")
    print(text)
