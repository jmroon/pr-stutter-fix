"""Analyze ETW presentation cadence, separated by swap chain and logged test mode.

Input is the pinned PresentMon 2.6.0 v1 CSV (--qpc_time) plus capture.json.
These are OS timing measurements, not pixel-motion or panel-response measurements.
"""
import argparse
import csv
import json
import math
import re
import statistics
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path


def number(value):
    try:
        value = float(value)
        return value if math.isfinite(value) else None
    except (TypeError, ValueError):
        return None


def percentile(values, fraction):
    values = sorted(values)
    at = (len(values) - 1) * fraction
    lo = int(at)
    hi = min(lo + 1, len(values) - 1)
    return values[lo] + (values[hi] - values[lo]) * (at - lo)


def stats(values):
    values = [v for v in values if v is not None and v > 0]
    if not values:
        return {"count": 0}
    median = statistics.median(values)
    long_count = sum(v > 1.5 * median for v in values)
    return dict(count=len(values), mean_ms=statistics.mean(values), median_ms=median,
                p95_ms=percentile(values, .95), p99_ms=percentile(values, .99),
                max_ms=max(values), stdev_ms=statistics.pstdev(values),
                rate_from_mean_hz=1000 / statistics.mean(values),
                above_1_5x_median=long_count, above_1_5x_median_percent=100 * long_count / len(values),
                common_rounded_intervals_ms=Counter(round(v, 2) for v in values).most_common(8))


def timestamp(text):
    return datetime.fromisoformat(text.replace("Z", "+00:00")).astimezone(timezone.utc).timestamp()


def mode_events(log, kind="GRID"):
    events = []
    for line in log.splitlines():
        parts = line.split(" ", 1)
        if len(parts) != 2:
            continue
        text = parts[1]
        match = re.search(rf"{kind} ON mode=([^ ]+)", text)
        if match:
            mode = match.group(1)
        elif f"{kind} OFF" in text or f"{kind} FAULT" in text or "ready, OFF by default" in text:
            mode = "unmodified-unmarked" if kind == "GRID" else "stock-or-unmarked"
        else:
            continue
        try:
            events.append((timestamp(parts[0]), mode))
        except ValueError:
            continue
    return sorted(events)


def summarize(directory, trim_seconds=1.0):
    directory = Path(directory)
    metadata = json.loads((directory / "capture.json").read_text(encoding="utf-8-sig"))
    if metadata.get("SchemaVersion") != 1 or metadata.get("QpcFrequency", 0) <= 0:
        raise ValueError("Invalid measurement metadata")
    if trim_seconds < 0:
        raise ValueError("Trim must be nonnegative")
    csv_path = directory / "presentmon.csv"
    with csv_path.open(newline="", encoding="utf-8-sig") as stream:
        reader = csv.DictReader(stream)
        canonical = {name.lower(): name for name in ("ProcessID", "SwapChainAddress", "TimeInQPC", "MsBetweenPresents",
                     "MsBetweenDisplayChange", "Dropped", "PresentMode", "SyncInterval", "AllowsTearing")}
        canonical["qpctime"] = "TimeInQPC"  # Actual pinned 2.6.0 legacy-v1 output.
        def normalize(row):
            return {canonical.get(key.lower(), key): value for key, value in row.items() if key is not None}
        columns = [canonical.get(key.lower(), key) for key in (reader.fieldnames or [])]
        required = {"ProcessID", "SwapChainAddress", "TimeInQPC", "MsBetweenPresents"}
        if not required.issubset(columns):
            raise ValueError("Expected PresentMon v1 --qpc_time columns; preserve CSV and inspect its header")
        rows = [normalize(row) for row in reader]
    if not rows:
        raise ValueError("No frame records; capture launch alone does not establish ETW access")
    log_path = directory / "grid-test.log"
    log = log_path.read_text(encoding="utf-8-sig") if log_path.exists() else ""
    events = sorted([(when, "grid", mode) for when, mode in mode_events(log)] +
                    [(when, "pace", mode) for when, mode in mode_events(log, "PACE")])
    anchor_qpc, frequency = int(metadata["QpcAnchor"]), int(metadata["QpcFrequency"])
    anchor_utc = timestamp(metadata["UtcAnchor"])
    groups = defaultdict(list)
    rejected = 0
    for row in rows:
        try:
            if int(row["ProcessID"]) != int(metadata["ProcessId"]):
                continue
            qpc = int(row["TimeInQPC"])  # Never round large QPC values through float.
        except (ValueError, TypeError):
            rejected += 1
            continue
        utc = anchor_utc + (qpc - anchor_qpc) / frequency
        mode, pace, run, near_transition = "unmodified-unmarked", "stock-or-unmarked", -1, False
        for index, (when, kind, value) in enumerate(events):
            if abs(utc - when) < trim_seconds:
                near_transition = True
            if when <= utc:
                if kind == "grid":
                    mode = value
                else:
                    pace = value
                run = index
        if near_transition:
            continue
        groups[(row["SwapChainAddress"], mode, pace, run)].append((qpc, row))
    output = []
    for (chain, mode, pace, run), frames in sorted(groups.items()):
        frames.sort(key=lambda x: x[0])
        if len(frames) < 3:
            continue
        # Remove capture/segment boundaries; display intervals refer to prior frames.
        first, last = frames[0][0], frames[-1][0]
        interior = [(qpc, row) for qpc, row in frames
                    if qpc > first + trim_seconds * frequency and qpc < last - trim_seconds * frequency]
        if not interior:
            continue
        data = [row for _, row in interior]
        dropped = [r.get("Dropped", "").strip().lower() for r in data]
        known_dropped = [v for v in dropped if v in {"0", "1", "false", "true"}]
        display = [number(r.get("MsBetweenDisplayChange")) for r in data
                   if r.get("Dropped", "").strip().lower() not in {"1", "true"}]
        output.append(dict(swap_chain=chain, mode=mode, pacing_mode=pace, run_index=run, rows=len(data),
                           first_qpc=interior[0][0], last_qpc=interior[-1][0],
                           duration_seconds=(interior[-1][0] - interior[0][0]) / frequency,
                           present_intervals=stats([number(r.get("MsBetweenPresents")) for r in data]),
                           display_intervals=stats(display),
                           dropped_frames=sum(v in {"1", "true"} for v in known_dropped) if known_dropped else None,
                           dropped_status_rows=len(known_dropped),
                           present_modes=dict(Counter(r.get("PresentMode", "unknown") for r in data)),
                           sync_intervals=dict(Counter(r.get("SyncInterval", "unknown") for r in data)),
                           allows_tearing=dict(Counter(r.get("AllowsTearing", "unknown") for r in data))))
    if not output:
        raise ValueError("No sufficiently long valid segments remain after trimming")
    return dict(schema_version=1, process_id=metadata["ProcessId"], source_rows=len(rows),
                rejected_timestamp_rows=rejected, display_column_available="MsBetweenDisplayChange" in columns,
                trim_seconds=trim_seconds, segments=output,
                limitations=["ETW presentation timing, not image displacement, optical panel response or proof of perceived judder.",
                             "Long intervals use 1.5x each segment's median; this is not automatically a missed-refresh count.",
                             "Unmodified-unmarked periods may include standing still; game log has no walking markers.",
                             "Swap chains and separate runs are kept separate; NA display data is unavailable, not zero.",
                             "UTC/QPC alignment uses a measured clock anchor; transition margins reduce boundary ambiguity."])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--trim-seconds", type=float, default=1.0)
    args = parser.parse_args()
    result = summarize(args.directory, args.trim_seconds)
    target = args.directory / "presentation-analysis.json"
    target.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    for segment in result["segments"]:
        timing = segment["display_intervals"]
        print(f"{segment['mode']} / {segment['pacing_mode']} / {segment['swap_chain']}: {segment['rows']} frames; " +
              (f"display median {timing['median_ms']:.3f} ms, p99 {timing['p99_ms']:.3f} ms, "
               f"max {timing['max_ms']:.3f} ms, long intervals {timing['above_1_5x_median']}"
               if timing["count"] else "display timing unavailable"))
    print(target)


if __name__ == "__main__":
    main()
