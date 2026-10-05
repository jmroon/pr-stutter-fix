import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('audit', Path(__file__).resolve().parents[1]/'scripts/analyze_presentation_audit.py')
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class AuditAnalysisTests(unittest.TestCase):
    def test_resolution_addon_is_not_a_stock_spatial_pass(self):
        for scale in (4,8):
            with self.subTest(scale=scale):
                d = self.fixture()
                s = self.stock_sample()
                mode = f'resolution-comparison-{scale}x'
                s.update(Baseline=mode, RequestedRenderScale=scale, ResolutionCompletedFrames=123)
                d['Samples'] = [s]
                report = audit.summarize(d)
                self.assertEqual(report['outcome'], 'no-clean-comparisons')
                self.assertEqual(report['resolution_max_completed_frames'], {mode:123})
                self.assertEqual(report['baselines'], {mode:1})

    def stock_sample(self, qpc=0, mode='stock-manual'):
        s = self.sample(qpc, baseline=mode)
        s['MapModel'] = 10
        s['Runtime'] = dict(Enabled=True, PrecisionActive=True, TimingActive=mode=='stock-manual',
                            PacingActive=mode!='stock-precision-only', Faulted=False,
                            ComparisonMode=mode, ContextField=1, ContextMap=10, ContextArea=39,
                            ContextFrame=qpc, Generation=1, TimingSession=1, ContextIdentity='field-1')
        return s

    def test_scripted_capture_requires_actual_flags_and_current_field(self):
        d = self.fixture()
        good = self.stock_sample(mode='stock-scripted')
        bad = self.stock_sample(50)
        bad['Runtime']['PacingActive'] = False
        stale = self.stock_sample(100)
        stale['Runtime']['ContextMap'] = 99
        d['Samples'] = [good, bad, stale]
        r = audit.summarize(d)
        self.assertEqual(r['experiment_samples'], 1)
        self.assertEqual(r['baselines'], {'stock-scripted':1, 'stock-invalid':1, 'stock-context-mismatch':1})
        self.assertEqual(r['checks']['camera'], {'match':1, 'baseline-excluded':2})

    def test_stock_label_alone_is_not_evidence(self):
        d = self.fixture(); d['Samples'] = [self.sample(baseline='stock-manual')]
        self.assertEqual(audit.summarize(d)['outcome'], 'no-clean-comparisons')

    def test_lifecycle_without_field_rows_is_coverage_not_a_spatial_pass(self):
        d = self.fixture()
        d['Lifecycle'] = [dict(Qpc=i*250, Frame=i, Runtime=dict(ContextKind=k, ComparisonMode='stock-suspended', Enabled=True))
                          for i,k in enumerate(['Battle','Battle','Menu'])]
        r = audit.summarize(d)
        self.assertEqual(r['outcome'], 'no-clean-comparisons')
        self.assertEqual(r['lifecycle_context_samples'], {'Battle':2,'Menu':1})
        self.assertEqual(len(r['lifecycle_transitions']), 2)

    def test_carry_and_motion_do_not_bridge_reacquired_session(self):
        d = self.fixture()
        d['Samples'] = [self.stock_sample(i*50) for i in range(4)]
        for i,s in enumerate(d['Samples']):
            s['CarriedTiles'] = i*2
            s['Runtime']['TimingSession'] = 1 if i < 2 else 2
        d['Samples'][2]['TargetWorld'] = dict(X=10,Y=0)
        d['Samples'][3]['TargetWorld'] = dict(X=10,Y=0)
        r = audit.summarize(d)
        self.assertEqual(r['timing_carried_tiles_observed'], {'stock-manual':4})
        self.assertEqual(r['observed_patterns'], {})

    def test_comparison_counts_carries_without_crossing_switches_or_gaps(self):
        d = self.fixture()
        rows = []
        for qpc, mode, count in [(0, 'timing-pacing', 0), (50, 'timing-pacing', 2),
                                 (100, 'timing-pacing-unrounded-8x', 4),
                                 (150, 'timing-pacing-unrounded-8x', 7),
                                 (900, 'timing-pacing-unrounded-8x', 20),
                                 (950, 'comparison-invalid', 22)]:
            s = self.sample(qpc, baseline=mode)
            s['CarriedTiles'] = count
            rows.append(s)
        d['Samples'] = rows
        result = audit.summarize(d)
        self.assertEqual(result['clean_samples'], 0)
        self.assertEqual(result['experiment_samples'], 5)
        self.assertEqual(result['timing_carried_tiles_observed'],
                         {'timing-pacing': 2, 'timing-pacing-unrounded-8x': 3})
        self.assertEqual(result['checks_by_condition']['comparison-invalid']['camera'],
                         {'baseline-excluded': 1})

    def test_stock_unrounded_comparison_is_distinct_from_old_a(self):
        d = self.fixture()
        modes = ['timing-pacing', 'timing-pacing-unrounded-stock',
                 'timing-pacing-unrounded-stock', 'timing-pacing-unrounded-8x',
                 'timing-pacing-unrounded-stock', 'comparison-invalid']
        d['Samples'] = [self.sample(i*50, baseline=m) for i, m in enumerate(modes)]
        for i, row in enumerate(d['Samples']):
            row['CarriedTiles'] = i*2
            row['Entities'][0]['LogicalWorld'] = dict(X=.25,Y=0)
        result = audit.summarize(d)
        self.assertEqual(result['experiment_samples'], 5)
        self.assertEqual(result['clean_samples'], 0)
        self.assertEqual(result['checks_by_condition']['timing-pacing-unrounded-stock']['camera'], {'match':3})
        self.assertEqual(result['timing_carried_tiles_observed']['timing-pacing-unrounded-stock'], 2)
        self.assertEqual(result['fractional_logical_observations']['timing-pacing-unrounded-stock']['player'], 3)
        self.assertEqual(result['checks_by_condition']['comparison-invalid']['camera'], {'baseline-excluded':1})

    def test_resolution_mode_is_separate_and_reports_actual_completed_frames(self):
        d=self.fixture();a=self.sample(baseline='unrounded-movement');b=self.sample(50,baseline='unrounded-movement-8x')
        b['ResolutionCompletedFrames']=0
        c=self.sample(100,baseline='unrounded-movement-8x');c['ResolutionCompletedFrames']=7
        d['Samples']=[a,b,c];r=audit.summarize(d)
        self.assertEqual(r['clean_samples'],0)
        self.assertEqual(r['experiment_samples'],3)
        self.assertEqual(r['checks_by_condition']['unrounded-movement-8x']['camera'],{'match':2})
        self.assertEqual(r['resolution_max_completed_frames'],{'unrounded-movement-8x':7})

    def test_unrounded_is_measured_but_not_counted_as_native_baseline(self):
        d=self.fixture();a=self.sample();b=self.sample(50,baseline='unrounded-movement')
        b['Entities'][0]['LogicalWorld']=dict(X=.25,Y=0)
        b['TargetWorld']=dict(X=.25,Y=0)
        d['Samples']=[a,b];r=audit.summarize(d)
        self.assertEqual(r['clean_samples'],1)
        self.assertEqual(r['experiment_samples'],1)
        self.assertEqual(r['checks_by_condition']['unrounded-movement']['visual'],{'match':1})
        self.assertEqual(r['fractional_logical_observations'],{'unrounded-movement':{'player':1}})
        self.assertEqual(r['observed_patterns'],{}) # Do not infer motion across a toggle.

    def test_faulted_unrounded_status_is_excluded(self):
        d=self.fixture();d['Samples']=[self.sample(baseline='unrounded-status-unknown-or-fault')]
        self.assertEqual(audit.summarize(d)['outcome'],'no-clean-comparisons')

    def fixture(self):
        return dict(Kind='presentation-adapter-audit',SchemaVersion=1,QpcFrequency=1000,Game='FFIV',Reason='manual',Phase='postfix',Samples=[])

    def sample(self, qpc=0, baseline='corrections-disabled', error=0):
        point = dict(X=0,Y=0)
        check = dict(Status='match',Expected=point,Observed=dict(X=error,Y=0))
        entity = dict(Id=2,Role='player',FollowTarget=True,LogicalWorld=point,VisualCheck=check)
        return dict(Qpc=qpc,Frame=qpc,Area=39,Controller=1,TargetId=2,CameraId=3,View=0,
                    Baseline=baseline,Status='sample',CameraCheck=check,MapCheck=check,
                    TargetWorld=point,CameraWorld=point,Entities=[entity])

    def test_empty_does_not_pass(self):
        self.assertEqual(audit.summarize(self.fixture())['outcome'],'no-clean-comparisons')

    def test_clean_and_scope_exclusions(self):
        d=self.fixture();s=self.sample();s['Entities'][0]['VisualCheck']=dict(Status='screen-space-entity')
        d['Samples']=[s];r=audit.summarize(d)
        self.assertEqual(r['checks']['camera'],{'match':1})
        self.assertEqual(r['checks']['visual'],{'screen-space-entity':1})

    def test_recomputes_error_instead_of_trusting_status(self):
        d=self.fixture();d['Samples']=[self.sample(error=.25)]
        r=audit.summarize(d)
        self.assertEqual(r['outcome'],'disagreements-found')
        self.assertEqual(r['max_error_units']['visual'],.25)

    def test_contaminated_capture_does_not_pass(self):
        d=self.fixture();d['Samples']=[self.sample(baseline='corrections-enabled')]
        r=audit.summarize(d)
        self.assertEqual(r['outcome'],'no-clean-comparisons')
        self.assertEqual(r['checks']['camera'],{'baseline-excluded':1})

    def test_target_camera_divergence_and_identity_reset(self):
        d=self.fixture();a=self.sample();b=self.sample(50);b['TargetWorld']=dict(X=1,Y=0)
        c=self.sample(100);c['Area']=2;c['TargetWorld']=dict(X=2,Y=0)
        d['Samples']=[a,b,c];r=audit.summarize(d)
        self.assertEqual(r['observed_patterns']['target-moves-camera-still'],1)

    def test_nonfinite_is_not_a_match(self):
        d=self.fixture();d['Samples']=[self.sample(error=float('nan'))]
        self.assertEqual(audit.summarize(d)['checks']['camera'],{'invalid-check':1})

    def test_serialized_nonfinite_motion_does_not_crash_patterns(self):
        d=self.fixture();a=self.sample();b=self.sample(50)
        b['TargetWorld']=dict(X='NaN',Y=0)
        d['Samples']=[a,b]
        self.assertEqual(audit.summarize(d)['observed_patterns'],{})

    def test_no_field_input_and_long_gaps(self):
        d=self.fixture();a=self.sample();b=self.sample(500);b['TargetWorld']=dict(X=1,Y=0)
        d['Samples']=[a,dict(Status='missing-field-input',Baseline='corrections-disabled'),b]
        self.assertEqual(audit.summarize(d)['observed_patterns'],{})

    def test_npc_motion_and_unknown_status(self):
        d=self.fixture();a=self.sample();b=self.sample(50)
        for s in (a,b):
            s['Entities'][0]['Role']='npc'
        b['Entities'][0]['LogicalWorld']=dict(X=1,Y=0)
        d['Samples']=[a,b]
        self.assertEqual(audit.summarize(d)['observed_patterns']['moving-npc-sample-pairs'],1)
        for s in (a,b):s['Baseline']='runtime-status-unknown'
        self.assertEqual(audit.summarize(d)['outcome'],'no-clean-comparisons')
