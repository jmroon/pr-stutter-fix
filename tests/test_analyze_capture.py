import csv
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("analyze_capture", ROOT / "scripts/analyze_capture.py")
analyzer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analyzer)


class CaptureAnalysisTests(unittest.TestCase):
    def capture(self, fractional=False):
        temporary = tempfile.TemporaryDirectory(dir=ROOT / "artifacts/tests")
        self.addCleanup(temporary.cleanup)
        path = Path(temporary.name) / "trace.csv"
        rows = []
        for frame in range(1, 31):
            candidate = frame * 4 / 3
            result = candidate if fractional else round(candidate)
            for phase in ("MovementResult", "CameraAfter", "PreCull"):
                rows.append(dict(phase=phase, frame=frame, entity_id=1, controller_id=2, camera_id=3,
                                 player_move_state=7 if fractional else 0, qpc=9007199254740993 + frame * 1000,
                                 observer_ticks=10, unscaled_delta_time=1 / 60, move_duration=.24,
                                 candidate_x=candidate, candidate_y=0, result_x=result, result_y=0,
                                 camera_world_x=result, camera_world_y=0, target_width=480, target_height=270))
        with path.open("w", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
        metadata = dict(SchemaVersion=1, QpcFrequency=60000, StartQpc=9007199254740993, Rows=len(rows),
                        StopReason="duration", DroppedRows=0, Cameras=[dict(Id=3, Name="Field")])
        path.with_suffix(".json").write_text(json.dumps(metadata))
        return path

    def test_quantized_movement_is_detected(self):
        summary = analyzer.summarize(self.capture(), 0)
        self.assertEqual(summary["movement_by_state"]["0"]["matching_round_xy"], 30)
        self.assertEqual(summary["movement_by_state"]["0"]["integer_output_xy"], 30)
        self.assertEqual(set(summary["cameras"][0]["absolute_step_histogram_game_units"]), {1, 2})
        self.assertEqual(summary["warnings"], [])
        self.assertAlmostEqual(summary["unity_frame_ms"]["median"], 1000 / 60)

    def test_fractional_movement_is_not_misidentified_as_snapped(self):
        summary = analyzer.summarize(self.capture(fractional=True), 0)
        self.assertEqual(summary["movement_by_state"]["7"]["integer_output_xy"], 10)
        self.assertAlmostEqual(summary["movement_by_state"]["7"]["max_error"], 0)
        self.assertEqual(set(summary["cameras"][0]["absolute_step_histogram_game_units"]), {1.3333})

    def test_truncated_capture_is_rejected(self):
        path = self.capture()
        lines = path.read_text().splitlines()
        path.write_text("\n".join(lines[:-1]) + "\n")
        with self.assertRaisesRegex(ValueError, "incomplete"):
            analyzer.summarize(path, 0)

    def test_warmup_exclusion_keeps_qpc_integer_precision(self):
        summary = analyzer.summarize(self.capture(), .25)
        self.assertEqual(summary["phase_counts"]["MovementResult"], 16)

    def test_empty_after_warmup_is_not_a_success_claim(self):
        summary = analyzer.summarize(self.capture(), 2)
        self.assertIsNone(summary["unity_frame_ms"]["median"])
        self.assertTrue(summary["warnings"])
        json.dumps(summary, allow_nan=False)

    def test_regression_capture_is_flagged(self):
        path = self.capture()
        metadata = json.loads(path.with_suffix(".json").read_text())
        metadata["PluginVersion"] = "0.2.0"
        path.with_suffix(".json").write_text(json.dumps(metadata))
        self.assertTrue(any("INVALID BASELINE" in w for w in analyzer.summarize(path, 0)["warnings"]))

    def test_polling_samples_camera_without_claiming_native_outputs(self):
        path = self.capture()
        with path.open(newline="") as stream:
            rows = [r for r in csv.DictReader(stream) if r["phase"] == "PreCull"]
        for row in rows:
            row["phase"] = "LateUpdate"
            for column in ("candidate_x", "candidate_y", "result_x", "result_y"):
                row[column] = "NaN"
        with path.open("w", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
        metadata = json.loads(path.with_suffix(".json").read_text())
        metadata.update(SchemaVersion=2, ObservationMode="LateUpdatePolling", PluginVersion="0.3.0", Rows=len(rows),
                        StopReason="field_camera_inactive")
        path.with_suffix(".json").write_text(json.dumps(metadata))
        summary = analyzer.summarize(path, 0)
        self.assertEqual(summary["movement_by_state"], {})
        self.assertEqual(summary["observation_mode"], "LateUpdatePolling")
        self.assertEqual(set(summary["cameras"][0]["absolute_step_histogram_game_units"]), {1, 2})
        self.assertTrue(any("ordering is unspecified" in w for w in summary["warnings"]))
        self.assertTrue(any("field_camera_inactive" in w for w in summary["warnings"]))
        self.assertFalse(any("confirm hooks" in w for w in summary["warnings"]))
        json.dumps(summary, allow_nan=False)


if __name__ == "__main__":
    unittest.main()
