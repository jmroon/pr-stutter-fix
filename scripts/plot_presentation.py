"""Plot raw PresentMon intervals from the exact windows selected by the analyzer.

Run using .tools/plot-python/Scripts/python.exe (matplotlib 3.10.7).
"""
import argparse
import csv
import json
from pathlib import Path
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("directory", type=Path)
parser.add_argument("--pacing-mode", default="stock-or-unmarked", help="Only plot one pacing mode; never mix limiter settings.")
parser.add_argument("--include-stock", action="store_true", help="Add a separately labeled stock baseline row.")
args = parser.parse_args()
report = json.loads((args.directory / "presentation-analysis.json").read_text())
meta = json.loads((args.directory / "capture.json").read_text(encoding="utf-8-sig"))
with (args.directory / "presentmon.csv").open(newline="", encoding="utf-8-sig") as stream:
    raw = [{k.lower(): v for k, v in r.items()} for r in csv.DictReader(stream)]
ordered = []
requests = [("unmodified-unmarked", args.pacing_mode, "Smoothing off"),
            ("fractional-motion-4x", args.pacing_mode, "4× smoothing"),
            ("fractional-motion-8x", args.pacing_mode, "8× smoothing")]
if args.include_stock and args.pacing_mode != "stock-or-unmarked":
    requests.insert(0, ("unmodified-unmarked", "stock-or-unmarked", "Stock 60 fps target"))
for mode, pace, title in requests:
    candidates = [s for s in report["segments"] if s["mode"] == mode and s.get("pacing_mode", "stock-or-unmarked") == pace]
    if not candidates:
        continue
    # Use the earliest sufficiently long interval from the dominant swap chain.
    chain = max(candidates, key=lambda s: s["rows"])["swap_chain"]
    selected = min((s for s in candidates if s["swap_chain"] == chain), key=lambda s: s["first_qpc"])
    ordered.append((selected, title + (" · VSync" if pace == "vsync-display" else "")))
if not ordered:
    raise ValueError("No plottable segments")
plt.rcParams.update({"font.family": "DejaVu Sans", "font.size": 10,
                     "axes.spines.top": False, "axes.spines.right": False})
fig, axes = plt.subplots(len(ordered), 2, figsize=(11.5, 2.1 * len(ordered) + 1.3), squeeze=False,
                         gridspec_kw={"width_ratios": [1.45, 1]})
for (segment, title), (trace, hist) in zip(ordered, axes):
    rows = [r for r in raw if r["swapchainaddress"] == segment["swap_chain"] and
            segment["first_qpc"] <= int(r.get("qpctime", r.get("timeinqpc"))) <= segment["last_qpc"]]
    present = [float(r["msbetweenpresents"]) for r in rows]
    displayed = [float(r["msbetweendisplaychange"]) for r in rows]
    window = min(120, len(rows))
    trace.plot(range(window), displayed[:window], color="#c36a28", lw=1, label="Display change interval")
    trace.plot(range(window), present[:window], color="#246887", lw=1.3, label="Game submission interval")
    reference = segment["display_intervals"]["median_ms"]
    upper = max(35, 5 * np.ceil(segment["display_intervals"]["max_ms"] / 5))
    trace.axhline(reference, color="#555555", ls="--", lw=.8)
    trace.set(title=title, ylabel="Interval (ms)", ylim=(0, upper))
    trace.grid(axis="y", alpha=.16)
    hist.hist(displayed, bins=np.arange(0, upper + .25, .25), weights=np.full(len(displayed), 100 / len(displayed)), color="#c36a28")
    hist.axvline(reference, color="#555555", ls="--", lw=.8)
    hist.set(title=f"All {len(rows):,} retained frames", ylabel="Frames (%)", xlim=(0, upper))
    hist.set_xticks([6.1, 12.1, 18.2, 24.3, 30.3])
    hist.grid(axis="y", alpha=.16)
axes[0, 0].legend(loc="upper right", fontsize=8, framealpha=.95)
axes[-1, 0].set_xlabel("First 120 retained game frames (different elapsed time per row)")
axes[-1, 1].set_xlabel("Display change interval (ms)")
fig.suptitle("Measured game submission and display cadence", fontsize=15, x=.06, ha="left")
fig.text(.06, .935, f"Pacing mode: {args.pacing_mode}" + ("; stock baseline shown separately." if args.include_stock else "."), fontsize=10)
fig.text(.06, .025, f"PresentMon 2.6.0 · {meta['DurationSeconds']}-second capture · dashed lines: each segment's display median\n"
         "Transitions excluded. ETW presentation timing only; image displacement and physical panel response were not captured.", fontsize=9, color="#555555")
fig.subplots_adjust(left=.07, right=.98, top=.89, bottom=.12, hspace=.65, wspace=.28)
fig.savefig(args.directory / "presentation-cadence.png", dpi=170)
fig.savefig(args.directory / "presentation-cadence.pdf")
print(args.directory / "presentation-cadence.png")
