import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('timing', Path(__file__).resolve().parents[1] / 'scripts/analyze_timing.py')
timing = importlib.util.module_from_spec(spec)
spec.loader.exec_module(timing)


class TimingEvidenceTests(unittest.TestCase):
    def sample(self):
        return dict(frame='25', input_frame='25', delta_time='.008', action='carried', applied='.007', remainder='.007',
                    before_timer='.199', before_duration='.2', before_start_x='0', before_start_y='0',
                    before_dest_x='16', before_dest_y='0', before_x='16', before_y='0',
                    final_timer='.007', final_duration='.2', final_start_x='16', final_start_y='0',
                    final_dest_x='32', final_dest_y='0', final_x='17', final_y='0')

    def test_same_frame_time_conservation(self):
        report = timing.summarize([self.sample()])
        self.assertEqual(report['carried_tiles'], 1)
        self.assertAlmostEqual(report['carried_ms'], 7)
        self.assertEqual(report['unapplied_completion_ms'], 0)
        self.assertLess(report['max_carried_frame_error_units'], 1e-10)

    def test_endpoint_loss_is_measurable(self):
        r = self.sample()
        r.update(action='ordinary', applied='0', final_timer='0', final_duration='0', final_x='16')
        report = timing.summarize([r])
        self.assertAlmostEqual(report['max_ordinary_or_carried_frame_error_units'], .56)
        self.assertIsNone(report['max_carried_frame_error_units'])

    def test_blocked_or_stale_input_not_claimed_as_a_carry(self):
        r = self.sample()
        r.update(action='no_fresh_same_direction_input', applied='0', input_frame='24')
        report = timing.summarize([r])
        self.assertEqual(report['carried_tiles'], 0)
        self.assertAlmostEqual(report['unapplied_completion_ms'], 7)
        self.assertEqual(report['fresh_input_rows'], 0)
        self.assertIsNone(report['max_ordinary_or_carried_frame_error_units'])

    def test_next_frame_arrival_and_missing_approval(self):
        endpoint = self.sample()
        endpoint.update(action='arrival_checks_not_ready_before_camera', applied='0', foot_finished_frame='1')
        later = dict(endpoint, frame='26', remainder='0', action='ordinary', foot_finished_frame='26')
        report = timing.summarize([endpoint, later])
        self.assertEqual(report['arrival_completion_delay_frames'], {'1': 1})
        self.assertEqual(timing.summarize([endpoint])['arrival_completion_delay_frames'], {'unobserved': 1})

    def test_same_frame_approval_and_old_format(self):
        r = self.sample()
        self.assertEqual(timing.summarize([r])['arrival_completion_delay_frames'], {})
        r['foot_finished_frame'] = '25'
        self.assertEqual(timing.summarize([r])['arrival_completion_delay_frames'], {'0': 1})

    def test_task_step_is_not_itself_a_successful_carry(self):
        wait = self.sample()
        wait.update(action='arrival_checks_not_ready_before_camera', applied='0',
                    arrival_task_action='stepped_yielded', arrival_iterator_state='1')
        carry = self.sample()
        carry.update(arrival_task_action='stepped_yielded', arrival_iterator_state='3')
        refused = dict(wait, arrival_task_action='queue_not_exclusive', arrival_iterator_state='0')
        report = timing.summarize([wait, carry, refused])
        self.assertEqual(report['arrival_tasks_stepped'], 2)
        self.assertEqual(report['arrival_task_states_after_step'], {'1': 1, '3': 1})
        self.assertEqual(report['arrival_task_actions']['queue_not_exclusive'], 1)
        self.assertEqual(report['carried_tiles'], 1)
        self.assertEqual(timing.summarize([self.sample()])['arrival_tasks_stepped'], 0)

    def test_carry_survives_next_update_or_reports_overwrite(self):
        carried = self.sample()
        following = dict(carried, frame='26', action='ordinary', applied='0', remainder='0')
        for key in ('timer', 'duration', 'start_x', 'start_y', 'dest_x', 'dest_y', 'x', 'y'):
            following['before_' + key] = carried['final_' + key]
        self.assertEqual(timing.summarize([carried, following])['carry_persistence']['preserved_next_frame'], 1)
        following['before_timer'] = '0'
        self.assertEqual(timing.summarize([carried, following])['carry_persistence']['changed_before_next_update'], 1)
        following['frame'] = '27'
        self.assertEqual(timing.summarize([carried, following])['carry_persistence']['unobserved'], 1)
        self.assertEqual(timing.summarize([carried])['carry_persistence']['unobserved'], 1)


if __name__ == '__main__':
    unittest.main()
