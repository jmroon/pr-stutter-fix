import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('playthrough', Path(__file__).resolve().parents[1] / 'scripts/analyze_playthrough.py')
analysis = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analysis)


class PlaythroughAnalysisTests(unittest.TestCase):
    def fixture(self):
        return dict(SchemaVersion=1, QpcFrequency=1000, Frames=[], Movement=[], Motion=[],
                    Reason='manual-marker', CoverageQpcTicks={'suspended': 5000}, Loss={}, Overhead={})

    def test_empty_and_coverage(self):
        result = analysis.summarize(self.fixture())
        self.assertEqual(result['coverage_seconds']['suspended'], 5)
        self.assertIsNone(result['max_carried_error_units'])
        self.assertEqual(result['carried_tiles'], 0)

    def test_motion_and_frame_gaps(self):
        data = self.fixture()
        for frame, qpc, x in [(1,100,0), (2,110,1), (3,120,1), (5,150,90)]:
            data['Motion'].append(dict(Frame=frame, Qpc=qpc, CameraId=1, EntityId=2,
                ScreenX=x, ScreenY=0, CameraSize=90, PixelWidth=320, PixelHeight=180, Duration=.2))
        series = analysis.summarize(data)['motion_series'][0]
        self.assertEqual(series['intervals'], 2)
        self.assertEqual(series['mean_speed'], 50)
        self.assertEqual(series['zero_steps_while_entity_timer_active'], 1)

    def test_carried_diagonal_and_persistence(self):
        data = self.fixture()
        def walk(s, d, t):
            return dict(Sx=s,Sy=s,Dx=d,Dy=d,Timer=t,Duration=.28,X=s,Y=s)
        before, final = walk(0,16,.27), walk(16,32,.01)
        data['Movement'] = [dict(Action='carried', Sample=dict(Frame=1,Delta=.02,Before=before),Final=final),
                            dict(Action='ordinary', Sample=dict(Frame=2,Delta=.01,Before=final),Final=final)]
        report = analysis.summarize(data)
        self.assertEqual(report['diagonal_carries'],1)
        self.assertEqual(report['persistence']['preserved'],1)
        self.assertLess(report['max_carried_error_units'],1e-12)

    def test_schema_refused(self):
        data = self.fixture(); data['SchemaVersion'] = 99
        with self.assertRaises(ValueError): analysis.summarize(data)


if __name__ == '__main__':
    unittest.main()
