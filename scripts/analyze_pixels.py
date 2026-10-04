"""Measure axis-aligned scenery translation independently of logged camera movement.

Uses cropped field-target RGBA samples, not physical display captures. Run with the
repository's .tools/plot-python Python (numpy, matplotlib and Pillow).
"""
import argparse
import csv
import json
from pathlib import Path
import numpy as np


def track(previous, current, radius=16):
    """Search both axes, using equal-size overlaps and independent image evidence.

    Positive dx/dy means scenery moves right/up in Unity's bottom-up pixel layout.
    Reject weak texture, large residuals, ambiguous repetitive patterns, and search
    boundary hits. No expected camera displacement is supplied to the matcher.
    """
    a = previous[..., :3].astype(np.float32).mean(axis=2)
    b = current[..., :3].astype(np.float32).mean(axis=2)
    h, w = a.shape
    if min(h, w) <= 2 * radius + 4:
        raise ValueError("Patch too small for search radius")
    template = a[radius:h-radius, radius:w-radius]
    contrast = float(template.std())
    if contrast < 2:
        return dict(valid=False, reason="low_texture", contrast=contrast)
    candidates = [(dx, 0) for dx in range(-radius, radius+1)] + [(0, dy) for dy in range(-radius, radius+1) if dy]
    scores = []
    for dx, dy in candidates:
        shifted = b[radius+dy:h-radius+dy, radius+dx:w-radius+dx]
        scores.append(float(np.mean((template-shifted)**2)) / max(contrast**2, 1))
    order = np.argsort(scores)
    best, second = int(order[0]), int(order[1])
    dx, dy = candidates[best]
    score, margin = scores[best], scores[second]-scores[best]
    reason = "ok"
    if margin < .025:
        reason = "ambiguous_pattern"
    elif abs(dx) == radius or abs(dy) == radius:
        reason = "search_boundary"
    elif score > .15:
        reason = "image_mismatch"
    return dict(valid=reason == "ok", reason=reason, dx=dx, dy=dy, score=score, margin=margin, contrast=contrast)


def motion_prediction(previous, current, screen_width, screen_height):
    """Observed field pipeline: native scroll follows the logical camera, while
    the tile camera contributes only the additional rendering displacement.
    A tile-camera transform by itself does not describe scenery scrolling.
    """
    result = {}
    for axis, factor in [('x',screen_width/320),('y',screen_height/180)]:
        delta = lambda key: float(current[key])-float(previous[key])
        result['render_d'+axis] = -(delta('camera_'+axis)+delta('render_'+axis))*factor
        result['candidate_d'+axis] = -delta('candidate_'+axis)*factor
    return result


def completion_event(previous, current, index, screen_width, screen_height):
    duration, timer = float(previous.get('move_duration',0)), float(previous.get('move_timer',0))
    if duration <= 0 or timer <= 0 or float(current.get('move_timer',1)) != 0 or float(current.get('move_duration',1)) != 0:
        return None
    if any(previous[k] != current[k] for k in ['start_x','start_y','dest_x','dest_y']):
        return None
    dt = float(current['delta_time'])
    remaining = duration-timer
    result = dict(index=index, frame=int(current['frame']), qpc=int(current['qpc']),
                  remaining_ms=remaining*1000, delta_ms=dt*1000, unused_ms=(dt-remaining)*1000)
    for axis,factor in [('x',screen_width/320),('y',screen_height/180)]:
        speed=(float(previous['dest_'+axis])-float(previous['start_'+axis]))/duration
        result['full_frame_d'+axis]=-speed*dt*factor
        result['observed_d'+axis]=-(float(current['candidate_'+axis])-float(previous['candidate_'+axis]))*factor
    return result


def analyze(directory):
    directory = Path(directory)
    meta = json.loads((directory / "capture.json").read_text())
    with (directory / "motion.csv").open(newline="", encoding="utf-8-sig") as stream:
        rows = list(csv.DictReader(stream))
    n, h, w = meta["count"], meta["patchHeight"], meta["packedWidth"]
    if n < 3 or len(rows) != n or w != 2*meta["patchWidth"]:
        raise ValueError("Incomplete or incompatible pixel capture")
    expected = n*h*w*4
    if (directory / "patches.rgba").stat().st_size != expected:
        raise ValueError("Pixel file size does not match metadata")
    pixels = np.memmap(directory / "patches.rgba", dtype=np.uint8, mode="r", shape=(n,h,w,4))
    sx, sy = meta["screenWidth"]/meta["sourceWidth"], meta["screenHeight"]/meta["sourceHeight"]
    results = []
    for i in range(1,n):
        matches = [track(pixels[i-1,:,j:j+w//2],pixels[i,:,j:j+w//2]) for j in (0,w//2)]
        valid = all(m["valid"] for m in matches) and (matches[0]["dx"],matches[0]["dy"]) == (matches[1]["dx"],matches[1]["dy"])
        consecutive = int(rows[i]["frame"])-int(rows[i-1]["frame"]) == 1
        valid = valid and consecutive
        prediction = motion_prediction(rows[i-1], rows[i], meta['screenWidth'], meta['screenHeight'])
        result = dict(index=i, frame=int(rows[i]["frame"]), qpc=int(rows[i]["qpc"]), valid=valid,
            measured_dx=matches[0].get("dx",0)*sx if valid else None,
            measured_dy=matches[0].get("dy",0)*sy if valid else None,
            **prediction,
            read_ms=float(rows[i]["read_ticks"])*1000/meta["qpcFrequency"],
            observer_ms=float(rows[i]["observer_ticks"])*1000/meta["qpcFrequency"],
            consecutive=consecutive, matches=matches)
        results.append(result)
    accepted=[r for r in results if r["valid"]]
    moving=[r for r in accepted if abs(r["candidate_dx"])+abs(r["candidate_dy"])>.05]
    costs=[r["read_ms"] for r in results]
    completions=[event for i in range(1,n) if (event := completion_event(rows[i-1],rows[i],i,meta['screenWidth'],meta['screenHeight'])) is not None]
    report=dict(schema_version=1, source=str(directory), frames=n, valid_pairs=len(accepted),
        valid_moving_pairs=len(moving), total_pairs=len(results),
        readback_median_ms=float(np.median(costs)), readback_p99_ms=float(np.percentile(costs,99)),
        render_prediction_max_error_px=max((max(abs(r['measured_dx']-r['render_dx']),abs(r['measured_dy']-r['render_dy'])) for r in accepted),default=None),
        tile_completions=completions,
        limitation=meta["limitation"],
        interpretation="Matches require agreement between both patches. Compare displacement with elapsed time; unequal steps alone are not proof of a timing defect. This records field pixels before composition, and readback can change pacing.",
        pairs=results)
    (directory/"pixel-analysis.json").write_text(json.dumps(report,indent=2)+"\n")
    from PIL import Image
    preview_indices=np.linspace(0,n-1,4,dtype=int)
    preview=np.concatenate([np.flipud(pixels[i,:,:,:3]) for i in preview_indices],axis=0)
    Image.fromarray(preview).resize((w*3,h*4*3),Image.Resampling.NEAREST).save(directory/"patch-preview.png")
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    t=[(r["qpc"]-int(rows[0]["qpc"]))/meta["qpcFrequency"] for r in results]
    axis="x" if sum(abs(r["candidate_dx"]) for r in results)>=sum(abs(r["candidate_dy"]) for r in results) else "y"
    fig,axs=plt.subplots(2,1,figsize=(11,6),sharex=True)
    axs[0].plot(t,[r['candidate_d'+axis] for r in results],lw=1,label="Reconstructed game movement")
    axs[0].plot(t,[r['render_d'+axis] for r in results],lw=1,label="Native scroll + render correction")
    axs[0].scatter([x for x,r in zip(t,results) if r['valid']],[r['measured_d'+axis] for r in accepted],s=5,label="Measured pixels (two patches agree)")
    for event in completions:
        when=(event['qpc']-int(rows[0]['qpc']))/meta['qpcFrequency']
        axs[0].axvline(when,color='#b42b35',alpha=.25,lw=.8)
    if completions:axs[0].plot([],[],color='#b42b35',alpha=.6,label='Tile completion')
    axs[0].set_ylabel(f"Scenery displacement {axis} (screen pixels)");axs[0].legend(fontsize=8)
    axs[1].plot(t,costs);axs[1].set_ylabel("Readback cost (ms)");axs[1].set_xlabel("Seconds since first captured frame")
    fig.suptitle(f"Pixel motion: {len(accepted)}/{len(results)} accepted pairs · {len(moving)} moving")
    fig.text(.08,.015,"Field target before final composition. Synchronous readback perturbs timing; missing matches remain unmeasured.",fontsize=9)
    fig.tight_layout(rect=(0,.045,1,.94));fig.savefig(directory/"pixel-motion.png",dpi=160)
    plt.close(fig)
    del pixels
    print(json.dumps({k:v for k,v in report.items() if k not in ('pairs','tile_completions')},indent=2))
    return report


if __name__ == "__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("directory",type=Path)
    analyze(parser.parse_args().directory)
