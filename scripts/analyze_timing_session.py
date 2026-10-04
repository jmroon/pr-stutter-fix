"""Correlate saved F5 captures with the timestamped pacing and smoothing logs."""
import argparse
from collections import Counter
import csv
from datetime import datetime
import json
from pathlib import Path
import re

from analyze_timing import summarize


def events(path):
    for line in path.read_text(encoding='utf-8-sig').splitlines():
        stamp, separator, message = line.partition(' ')
        if separator:
            try:
                yield datetime.fromisoformat(stamp.replace('Z', '+00:00')), message
            except ValueError:
                continue


def intervals(log_events, prefix):
    result, start = [], None
    for stamp, message in log_events:
        if message.startswith(prefix + ' ON '):
            if start is not None:
                raise ValueError('Nested activation: ' + prefix)
            start = stamp
        elif message.startswith((prefix + ' OFF ', prefix + ' FAULT')):
            if start is not None:
                result.append((start, stamp))
                start = None
    if start is not None:
        result.append((start, datetime.max.replace(tzinfo=start.tzinfo)))
    return result


def overlap_seconds(start, end, *groups):
    segments = [(start, end)]
    for group in groups:
        segments = [(max(a, c), min(b, d)) for a, b in segments for c, d in group
                    if max(a, c) < min(b, d)]
    return sum((b - a).total_seconds() for a, b in segments)


def saved_sessions(log_events):
    start = end = None
    reason = ''
    for stamp, message in log_events:
        if message.startswith('TIMING ON '):
            start, end = stamp, None
        elif message.startswith('TIMING OFF '):
            end, reason = stamp, message
        elif message.startswith('TIMING SAVED ') and start is not None and end is not None:
            match = re.search(r'(\d{8}-\d{6}-\d{3})\.csv$', message)
            if match:
                yield match[1], start, end, reason
                start = end = None


def analyze(directory, grid_log, version):
    grid = list(events(grid_log))
    pacing, smoothing = intervals(grid, 'PACE'), intervals(grid, 'GRID')
    results, all_rows = [], []
    for stem, start, end, stop in saved_sessions(events(directory / 'timing-test.log')):
        metadata = directory / (stem + '.txt')
        if not metadata.exists():
            continue  # A preserved subset may retain logs from older versions.
        if version and f'Version: {version}\n' not in metadata.read_text():
            continue
        with (directory / (stem + '.csv')).open(encoding='utf-8-sig', newline='') as source:
            rows = list(csv.DictReader(source))
        all_rows.extend(rows)
        duration = (end - start).total_seconds()
        both = overlap_seconds(start, end, pacing, smoothing)
        denied = [r for r in rows if float(r['remainder']) > 0 and r['action'] != 'carried']
        details = Counter()
        for r in denied:
            if r['action'] == 'no_fresh_same_direction_input':
                if int(r['input_frame']) != int(r['frame']):
                    details['input_from_different_frame'] += 1
                elif float(r['axis_x']) == 0 and float(r['axis_y']) == 0:
                    details['zero_input'] += 1
                else:
                    details['changed_direction'] += 1
            elif r['action'] == 'blocked_or_changed_next_tile':
                details['next_tile_not_accepted_or_changed'] += 1
            else:
                details[r['action']] += 1
        results.append(dict(capture=stem, start_utc=start.isoformat(), end_utc=end.isoformat(),
                            duration_seconds=duration, pacing_seconds=overlap_seconds(start, end, pacing),
                            smoothing_seconds=overlap_seconds(start, end, smoothing),
                            all_three_seconds=both, all_three_entire_capture=abs(both-duration)<1e-6,
                            refusals=dict(details), summary=summarize(rows), stop=stop))
    # Summaries must be computed per file: frame counters/approval histories can
    # reset between game launches and must never be matched across captures.
    totals = dict(captures=len(results), frames=len(all_rows),
                  carried_tiles=sum(x['summary']['carried_tiles'] for x in results),
                  arrival_tasks_stepped=sum(x['summary']['arrival_tasks_stepped'] for x in results),
                  carried_ms=sum(x['summary']['carried_ms'] for x in results),
                  full_combination_captures=sum(x['all_three_entire_capture'] for x in results),
                  carry_persistence={key: sum(x['summary']['carry_persistence'][key] for x in results)
                      for key in ('preserved_next_frame', 'changed_before_next_update', 'unobserved')},
                  max_carried_frame_error_units=max((x['summary']['max_carried_frame_error_units']
                      for x in results if x['summary']['max_carried_frame_error_units'] is not None), default=None),
                  actions=dict(Counter(r['action'] for r in all_rows)))
    return dict(version=version, totals=totals, captures=results,
                interpretation='Activation overlap uses UTC log timestamps, not per-frame display evidence. '
                'Carried-frame errors reconstruct game movement; they do not measure visible judder. '
                'Other ordinary updates can include unsupported paths and are not a conservation guarantee.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('directory', type=Path)
    parser.add_argument('--grid-log', type=Path, required=True)
    parser.add_argument('--version', default='0.2.0')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    report = analyze(args.directory, args.grid_log, args.version)
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report['totals'], indent=2))
    for capture in report['captures']:
        s = capture['summary']
        print(f"{capture['capture']}: carries={s['carried_tiles']}/{s['arrival_tasks_stepped']} steps; "
              f"all-three={capture['all_three_seconds']:.2f}/{capture['duration_seconds']:.2f}s; "
              f"refusals={capture['refusals']}; error={s['max_carried_frame_error_units']}")
