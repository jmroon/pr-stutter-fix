"""Report native adapter agreement, never infer coverage from an empty capture."""
import argparse
from collections import Counter, defaultdict
import json
import math
from pathlib import Path


def summarize(data):
    if (data.get('Kind') != 'presentation-adapter-audit' or data.get('SchemaVersion') != 1
            or data.get('QpcFrequency', 0) <= 0):
        raise ValueError('Unsupported audit capture')
    checks = {k: Counter() for k in ('camera', 'map', 'visual')}
    errors = {k: [] for k in checks}
    baselines, rows, roles, target_roles = Counter(), Counter(), defaultdict(Counter), Counter()
    examples = []
    patterns = Counter()
    previous = None
    previous_entities = {}
    clean_samples = 0
    experiment_samples = 0
    by_condition = defaultdict(lambda: {k: Counter() for k in checks})
    fractional = defaultdict(Counter)
    comparable = ('runtime-absent', 'corrections-disabled', 'unrounded-movement')

    def check(kind, value, sample, entity=None):
        status = value.get('Status', 'missing-check')
        if sample['Baseline'] not in comparable:
            status = 'baseline-excluded'
        # Stored status is not enough: independently recompute observed XY error.
        if status in ('match', 'mismatch'):
            try:
                pairs = [(value['Expected'][axis], value['Observed'][axis]) for axis in ('X', 'Y')]
                if not all(isinstance(x, (int, float)) and math.isfinite(x) for pair in pairs for x in pair):
                    raise ValueError('Nonfinite')
                error = max(abs(a - b) for a, b in pairs)
                errors[kind].append(error)
                status = 'match' if error <= .002 else 'mismatch'
            except (KeyError, TypeError, ValueError):
                status = 'invalid-check'
        checks[kind][status] += 1
        by_condition[sample['Baseline']][kind][status] += 1
        if entity is not None:
            roles[entity['Role']][status] += 1
        if status in ('mismatch', 'invalid-check', 'nonfinite') and len(examples) < 12:
            examples.append(dict(frame=sample['Frame'], area=sample['Area'], component=kind,
                                 entity=entity.get('Id') if entity else None, check=value))

    def xy(point):
        return point['X'], point['Y']

    def finite_xy(point):
        return all(isinstance(v, (int, float)) and math.isfinite(v) for v in xy(point))

    for s in data['Samples']:
        rows[s['Status']] += 1
        baselines[s['Baseline']] += 1
        if s['Status'] != 'sample':
            previous = None
            previous_entities.clear()
            continue
        clean = s['Baseline'] in comparable
        clean_samples += s['Baseline'] in ('runtime-absent', 'corrections-disabled')
        experiment_samples += s['Baseline'] == 'unrounded-movement'
        check('camera', s.get('CameraCheck', {}), s)
        check('map', s.get('MapCheck', {}), s)
        for entity in s['Entities']:
            check('visual', entity.get('VisualCheck', {}), s, entity)
            if clean and entity.get('FollowTarget'):
                target_roles[entity['Role']] += 1
            if clean and finite_xy(entity['LogicalWorld']) and any(abs(v-round(v)) > .002 for v in xy(entity['LogicalWorld'])):
                fractional[s['Baseline']][entity['Role']] += 1
        identity = (s['Controller'], s['Area'], s['TargetId'], s['CameraId'], s['View'], s['Baseline'])
        if not finite_xy(s['TargetWorld']) or not finite_xy(s['CameraWorld']):
            previous = None
            previous_entities.clear()
            continue
        if (clean and previous is not None and previous[0] == identity and
                0 < s['Qpc'] - previous[1]['Qpc'] <= data['QpcFrequency'] / 5):
            old = previous[1]
            target_delta = [b-a for a, b in zip(xy(old['TargetWorld']), xy(s['TargetWorld']))]
            camera_delta = [b-a for a, b in zip(xy(old['CameraWorld']), xy(s['CameraWorld']))]
            moving = any(abs(v) > .002 for v in target_delta)
            fixed = all(abs(v) <= .002 for v in camera_delta)
            if moving and fixed:
                patterns['target-moves-camera-still'] += 1
            if moving and any(abs(a-b) > .002 for a, b in zip(target_delta, camera_delta)):
                patterns['target-camera-steps-differ'] += 1
            if moving and all(abs(a-b) <= .002 for a, b in zip(target_delta, camera_delta)):
                patterns['target-camera-steps-agree'] += 1
        if previous is None or previous[0] != identity or not clean:
            previous_entities.clear()
        for entity in s['Entities']:
            key = entity['Id']
            if not finite_xy(entity['LogicalWorld']):
                previous_entities.pop(key, None)
                continue
            old = previous_entities.get(key)
            if clean and entity['Role'] == 'npc' and old is not None and 0 < s['Qpc'] - old[0] <= data['QpcFrequency']:
                if any(abs(a-b) > .002 for a, b in zip(xy(old[1]), xy(entity['LogicalWorld']))):
                    patterns['moving-npc-sample-pairs'] += 1
            previous_entities[key] = (s['Qpc'], entity['LogicalWorld'])
        # Bound analysis bookkeeping even if malformed captures cycle entity IDs.
        if len(previous_entities) > 512:
            previous_entities = {e['Id']: (s['Qpc'], e['LogicalWorld']) for e in s['Entities'] if finite_xy(e['LogicalWorld'])}
        previous = (identity, s) if clean else None
    compared = sum(c['match'] + c['mismatch'] for c in checks.values())
    failed = sum(c['mismatch'] + c['invalid-check'] + c['nonfinite'] for c in checks.values())
    overhead = data.get('Overhead', {})
    count = overhead.get('SampleCount', 0)
    return dict(game=data['Game'], reason=data['Reason'], phase=data['Phase'], samples=dict(rows),
                clean_samples=clean_samples, experiment_samples=experiment_samples, baselines=dict(baselines),
                outcome='disagreements-found' if failed else 'observed-checks-match' if compared else 'no-clean-comparisons',
                checks={k: dict(v) for k, v in checks.items()},
                checks_by_condition={b: {k: dict(v) for k, v in c.items()} for b, c in by_condition.items()},
                fractional_logical_observations={b: dict(v) for b, v in fractional.items()},
                max_error_units={k: max(v, default=None) for k, v in errors.items()},
                visual_by_role={k: dict(v) for k, v in roles.items()}, follow_target_roles=dict(target_roles),
                observed_patterns=dict(patterns), examples=examples,
                observer_mean_ms=overhead.get('TotalTicks', 0)*1000/data['QpcFrequency']/count if count else None,
                observer_max_ms=overhead.get('MaxTicks', 0)*1000/data['QpcFrequency'],
                limitations='Sampled XY mapping agreement only; unrounded-movement samples are an experiment, not an unmodified baseline. Fractional observations do not establish gameplay safety. Unobserved roles/modes are untested; excluded checks are not matches. Camera-step patterns do not identify their cause. No final-render, shadow/material, jitter or timing-correction proof.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path)
    args = parser.parse_args()
    report = summarize(json.loads(args.capture.read_text(encoding='utf-8-sig')))
    args.capture.with_suffix('.analysis.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(report, indent=2))
