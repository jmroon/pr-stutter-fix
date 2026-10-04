"""Summarize bounded playthrough evidence; candidate irregularity is not proof of visible jitter."""
import argparse
from collections import Counter, defaultdict
import json
import math
from pathlib import Path
import statistics


def summarize(data):
    if data.get('SchemaVersion') != 1 or data.get('QpcFrequency', 0) <= 0:
        raise ValueError('Unsupported capture schema or clock')
    hz = data['QpcFrequency']
    frames = data['Frames']
    movement = data['Movement']
    carried = [r for r in movement if r['Action'] == 'carried']
    preserved = changed = missing = 0
    errors = []
    for index, row in enumerate(movement):
        if row['Action'] != 'carried':
            continue
        b, n, s = row['Sample']['Before'], row['Final'], row['Sample']
        if index + 1 < len(movement) and movement[index + 1]['Sample']['Frame'] == s['Frame'] + 1:
            if n == movement[index + 1]['Sample']['Before']:
                preserved += 1
            else:
                changed += 1
        else:
            missing += 1
        if b['Duration'] > 0 and n['Duration'] > 0:
            for axis in ('x', 'y'):
                start, dest = 'S' + axis, 'D' + axis
                before = b[start] + (b[dest] - b[start]) * b['Timer'] / b['Duration']
                after = n[start] + (n[dest] - n[start]) * n['Timer'] / n['Duration']
                expected = (b[dest] - b[start]) * s['Delta'] / b['Duration']
                errors.append(abs(after - before - expected))
    series = defaultdict(list)
    for row in data['Motion']:
        series[(row['CameraId'], row['EntityId'])].append(row)
    motion = []
    for (camera, entity), rows in series.items():
        speeds, stopped_steps, intervals = [], 0, 0
        for a, b in zip(rows, rows[1:]):
            if b['Frame'] != a['Frame'] + 1 or b['Qpc'] <= a['Qpc']:
                continue
            if any(a[k] != b[k] for k in ('CameraSize', 'PixelWidth', 'PixelHeight')):
                continue
            keys = ('ScreenX', 'ScreenY') if entity else ('CameraX', 'CameraY')
            values = [a[k] for k in keys] + [b[k] for k in keys]
            if not all(isinstance(v, (int, float)) and math.isfinite(v) for v in values):
                continue
            step = math.hypot(*(b[k] - a[k] for k in keys))
            speed = step * hz / (b['Qpc'] - a['Qpc'])
            if entity and (a['Duration'] > 0 or b['Duration'] > 0):
                intervals += 1
                stopped_steps += step < 1e-6
            speeds.append(speed)
        motion.append(dict(camera_id=camera, entity_id=entity, intervals=len(speeds),
                           units='projected pixels/second' if entity else 'camera world units/second',
                           mean_speed=statistics.mean(speeds) if speeds else None,
                           speed_stddev=statistics.pstdev(speeds) if speeds else None,
                           zero_steps_while_entity_timer_active=stopped_steps,
                           active_timer_intervals=intervals))
    costs = [f['ObserverTicks'] * 1000 / hz for f in frames]
    return dict(reason=data['Reason'], captured_frames=len(frames),
                actions=dict(Counter(r['Action'] for r in movement)), carried_tiles=len(carried),
                diagonal_carries=sum(r['Sample']['Before']['Sx'] != r['Sample']['Before']['Dx'] and
                                     r['Sample']['Before']['Sy'] != r['Sample']['Before']['Dy'] for r in carried),
                persistence=dict(preserved=preserved, changed=changed, unobserved=missing),
                max_carried_error_units=max(errors, default=None),
                observer_mean_ms=statistics.mean(costs) if costs else None,
                observer_max_ms=max(costs, default=None),
                coverage_seconds={k: v / hz for k, v in data['CoverageQpcTicks'].items()},
                loss=data['Loss'], overhead=data['Overhead'], motion_series=motion,
                interpretation='Logical projected motion and game update evidence only. Intentional easing, stops, frame gaps and camera changes can produce irregularity. Use PresentMon for delivery timing; no GPU pixels or display smoothness proof.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path)
    args = parser.parse_args()
    report = summarize(json.loads(args.capture.read_text(encoding='utf-8-sig')))
    output = args.capture.with_suffix('.analysis.json')
    output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, indent=2))
