from datetime import datetime, timezone, timedelta
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from analyze_timing_session import analyze, intervals, overlap_seconds, saved_sessions


class SessionTests(unittest.TestCase):
    def t(self, seconds):
        return datetime(2026, 10, 4, tzinfo=timezone.utc) + timedelta(seconds=seconds)

    def test_staggered_timeouts_and_reactivation(self):
        pacing = [(self.t(0), self.t(90))]
        grid = [(self.t(2), self.t(17)), (self.t(30), self.t(45))]
        self.assertEqual(overlap_seconds(self.t(10), self.t(40), pacing, grid), 17)
        self.assertEqual(overlap_seconds(self.t(91), self.t(99), pacing, grid), 0)
        self.assertEqual(overlap_seconds(self.t(4), self.t(8), pacing, grid), 4)

    def test_fault_focus_loss_and_open_interval(self):
        log = [(self.t(0), 'GRID ON mode=8x'), (self.t(8), 'GRID FAULT: camera'),
               (self.t(9), 'GRID ON mode=8x'), (self.t(10), 'GRID OFF (focus_lost)'),
               (self.t(12), 'GRID ON mode=8x')]
        grid = intervals(log, 'GRID')
        self.assertEqual(overlap_seconds(self.t(0), self.t(15), grid), 12)
        with self.assertRaises(ValueError):
            intervals([(self.t(0), 'PACE ON a'), (self.t(1), 'PACE ON b')], 'PACE')

    def test_only_saved_complete_sessions_are_matched(self):
        log = [(self.t(0), 'TIMING ON test'), (self.t(5), 'TIMING OFF (manual)'),
               (self.t(6), 'TIMING SAVED D:\\timing\\20261004-000000-000.csv'),
               (self.t(8), 'TIMING ON test')]
        found = list(saved_sessions(log))
        self.assertEqual(len(found), 1)
        self.assertEqual(found[0][:3], ('20261004-000000-000', self.t(0), self.t(5)))

    def test_snapshot_with_older_sessions_in_log(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / 'timing-test.log').write_text(
                '2026-10-04T00:00:00Z TIMING ON test\n'
                '2026-10-04T00:00:05Z TIMING OFF (manual)\n'
                '2026-10-04T00:00:06Z TIMING SAVED D:\\timing\\20261004-000000-000.csv\n')
            grid = root / 'grid-test.log'
            grid.write_text('')
            self.assertEqual(analyze(root, grid, '0.2.0')['totals']['captures'], 0)


if __name__ == '__main__':
    unittest.main()
