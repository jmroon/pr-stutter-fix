"""Check same-frame tile-time conservation in the bounded F5 experiment CSV."""
import argparse
import csv
import json
import math
from pathlib import Path


def continuous(row, prefix):
    duration = float(row[prefix + '_duration'])
    if duration <= 0:
        return tuple(float(row[prefix + '_' + axis]) for axis in ('x', 'y'))
    ratio = float(row[prefix + '_timer']) / duration
    return tuple(float(row[prefix + '_start_' + axis]) +
                 (float(row[prefix + '_dest_' + axis]) - float(row[prefix + '_start_' + axis])) * ratio
                 for axis in ('x', 'y'))


def summarize(rows):
    carried = [r for r in rows if r['action'] == 'carried']
    persistence = dict(preserved_next_frame=0, changed_before_next_update=0, unobserved=0)
    for i, row in enumerate(rows):
        if row['action'] != 'carried':
            continue
        if i + 1 == len(rows) or int(rows[i + 1]['frame']) != int(row['frame']) + 1:
            persistence['unobserved'] += 1
            continue
        following = rows[i + 1]
        keys = ('timer', 'duration', 'start_x', 'start_y', 'dest_x', 'dest_y', 'x', 'y')
        preserved = all(float(row['final_' + k]) == float(following['before_' + k]) for k in keys)
        persistence['preserved_next_frame' if preserved else 'changed_before_next_update'] += 1
    errors, carry_errors = [], []
    for r in rows:
        duration = float(r['before_duration'])
        if duration <= 0 or r['action'] not in ('ordinary', 'carried'):
            continue
        # An uncorrected end frame (including exact completion) remains measurable.
        a, b = continuous(r, 'before'), continuous(r, 'final')
        expected = tuple((float(r['before_dest_' + axis]) - float(r['before_start_' + axis])) *
                         float(r['delta_time']) / duration for axis in ('x', 'y'))
        error = max(abs(b[i] - a[i] - expected[i]) for i in range(2))
        if not math.isfinite(error):
            raise ValueError('Nonfinite movement in timing evidence')
        errors.append(error)
        if r['action'] == 'carried':
            carry_errors.append(error)
    actions = {}
    task_actions, task_states = {}, {}
    for r in rows:
        actions[r['action']] = actions.get(r['action'], 0) + 1
        task_action = r.get('arrival_task_action', 'none')
        if task_action != 'none':
            task_actions[task_action] = task_actions.get(task_action, 0) + 1
        if task_action.startswith('stepped_'):
            state = r['arrival_iterator_state']
            task_states[state] = task_states.get(state, 0) + 1
    arrival_delays = {}
    if rows and 'foot_finished_frame' in rows[0]:
        for i, row in enumerate(rows):
            if float(row.get('remainder', 0)) <= 0:
                continue
            frame = int(row['frame'])
            observed = next((r for r in rows[i:] if int(r['foot_finished_frame']) >= frame), None)
            key = 'unobserved' if observed is None else str(int(observed['foot_finished_frame']) - frame)
            arrival_delays[key] = arrival_delays.get(key, 0) + 1
    return dict(frames=len(rows), actions=actions, carried_tiles=len(carried),
                carried_ms=sum(float(r['applied']) * 1000 for r in carried),
                unapplied_completion_ms=sum(max(0, float(r.get('remainder', 0)) - float(r['applied'])) * 1000 for r in rows),
                max_carried_frame_error_units=max(carry_errors, default=None),
                max_ordinary_or_carried_frame_error_units=max(errors, default=None),
                fresh_input_rows=sum(int(r['input_frame']) == int(r['frame']) for r in rows),
                arrival_completion_delay_frames=arrival_delays,
                arrival_task_actions=task_actions, arrival_task_states_after_step=task_states,
                arrival_tasks_stepped=sum(count for action, count in task_actions.items() if action.startswith('stepped_')),
                carry_persistence=persistence,
                interpretation='Reconstructed game movement, not final pixels or physical frame delivery. Zero carried tiles means the intervention has not been demonstrated.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path)
    path = parser.parse_args().capture
    with path.open(encoding='utf-8-sig', newline='') as source:
        report = summarize(list(csv.DictReader(source)))
    path.with_suffix('.analysis.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2))
