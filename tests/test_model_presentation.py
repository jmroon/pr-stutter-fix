"""Design regressions. These do not assert native compatibility or display quality."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('presentation', Path(__file__).resolve().parents[1] / 'scripts/model_presentation.py')
model = importlib.util.module_from_spec(spec)
spec.loader.exec_module(model)


class PresentationModelTests(unittest.TestCase):
    def test_player_proxy_is_wrong_at_a_boundary(self):
        mapping = lambda p: model.map_axis(p, 10.2)
        camera = model.camera_residual(10, .4, mapping)
        self.assertAlmostEqual(camera, .2)
        self.assertAlmostEqual(model.screen_residual(.4, camera), .2)
        self.assertNotAlmostEqual(camera, .4)  # Old player-equals-camera assumption.

    def test_stationary_camera_does_not_cancel_actor_movement(self):
        camera = model.camera_residual(11, .4, lambda p: model.map_axis(p, 10))
        self.assertEqual(camera, 0)
        self.assertEqual(model.screen_residual(.4, camera), .4)

    def test_followed_character_and_independent_npc(self):
        camera = model.camera_residual(3, .4, lambda p: model.map_axis(p, 10))
        self.assertAlmostEqual(model.screen_residual(.4, camera), 0)
        self.assertAlmostEqual(model.screen_residual(-.2, camera), -.6)
        self.assertAlmostEqual(model.screen_residual(0, camera), -.4)

    def test_offset_is_added_after_clamping(self):
        self.assertEqual(model.map_axis(12, 10, offset=5), 15)
        self.assertEqual(model.map_axis(-12, 10, offset=5), -5)
        self.assertEqual(model.map_axis(12, 0, offset=5), 5)
        self.assertEqual(model.map_axis(12, 10, offset=5, loop=True), 17)
        self.assertEqual(model.map_axis(12, 10, offset=5, clamp=False), 17)

    def test_arbitrary_segment_speed_and_direction(self):
        for start, end, duration in [((0, 0), (16, 0), .2), ((4, -8), (-28, 19), .73),
                                     ((0, 0), (100, -200), 2.31)]:
            for step in range(101):
                timer = duration * step / 100
                ideal = tuple(a + (b - a) * timer / duration for a, b in zip(start, end))
                rounded = tuple(round(v) for v in ideal)
                residual = model.fractional_residual(start, end, timer, duration, rounded)
                self.assertIsNotNone(residual)
                for actual, delta, expected in zip(rounded, residual, ideal):
                    self.assertAlmostEqual(actual + delta, expected)

    def test_already_fractional_and_incompatible_motion(self):
        self.assertEqual(model.fractional_residual((0, 0), (16, 0), .03, .2, (2.4, 0)), (0, 0))
        for timer, duration, observed in [(.03, .2, (100, 0)), (.3, .2, (16, 0)),
                                          (-.01, .2, (0, 0)), (0, 0, (0, 0)),
                                          (float('nan'), .2, (0, 0))]:
            self.assertIsNone(model.fractional_residual((0, 0), (16, 0), timer, duration, observed))

    def test_camera_pan_uses_its_own_source(self):
        # A moving scroll dummy can move the camera while the player stands still.
        camera = model.camera_residual(4, -.3, lambda p: model.map_axis(p, 100, offset=7))
        self.assertAlmostEqual(model.screen_residual(0, camera), .3)

    def test_projection_matches_continuous_geometry_across_rates_and_clamps(self):
        # Compare corrected rounded geometry with independently computed ideal
        # geometry, including crossing both camera boundaries and changing dt.
        for fps in (30, 60, 120, 144, 165, 240, 360):
            t = 0.0
            for frame in range(fps * 2):
                t += (1 + (.17 if frame % 2 else -.17)) / fps
                target, actor = -50 + 80 * t, 33 - 47 * t
                rt, ra = round(target), round(actor)
                mapping = lambda p: model.map_axis(p, 17.3, offset=4.7)
                dc = model.camera_residual(rt, target - rt, mapping)
                actual = ra - mapping(rt) + model.screen_residual(actor - ra, dc)
                self.assertAlmostEqual(actual, actor - mapping(target), places=10)

    def test_fractional_positions_do_not_recover_midpoint_time_loss(self):
        before, delta, half = .095, .012, .1
        residual = model.fractional_residual((0, 0), (16, 0), half, .2, (8, 0))
        self.assertEqual(residual, (0, 0))
        self.assertGreater(16 * (before + delta) / .2, 8)

    def test_native_resolution_still_quantizes_fractional_geometry(self):
        steps = [round((i + 1) * 80 / 60) - round(i * 80 / 60) for i in range(60)]
        self.assertEqual(set(steps), {1, 2})


if __name__ == '__main__':
    unittest.main()
