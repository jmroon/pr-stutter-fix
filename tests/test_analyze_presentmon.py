import csv
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("analyze_presentmon", ROOT / "scripts/analyze_presentmon.py")
analyzer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analyzer)


class PresentationTests(unittest.TestCase):
    def fixture(self, display="16.666667", spike=False, second_chain=False):
        temporary = tempfile.TemporaryDirectory(dir=ROOT / "artifacts/tests")
        self.addCleanup(temporary.cleanup)
        directory = Path(temporary.name)
        anchor = 9007199254740993
        metadata = dict(SchemaVersion=1, ProcessId=77, QpcFrequency=60000,
                        QpcAnchor=anchor, UtcAnchor="2026-10-03T05:00:00Z")
        (directory / "capture.json").write_text(json.dumps(metadata))
        rows = []
        for frame in range(600):
            rows.append(dict(ProcessID="77", SwapChainAddress="0x1", TimeInQPC=str(anchor + frame * 1000),
                             MsBetweenPresents="16.666667", MsBetweenDisplayChange=("33.333333" if spike and frame == 300 else display),
                             Dropped="0", PresentMode="Hardware: Independent Flip", SyncInterval="1", AllowsTearing="0"))
        if second_chain:
            rows.extend(dict(row, SwapChainAddress="0x2", MsBetweenDisplayChange="8.333333") for row in rows.copy())
        with (directory / "presentmon.csv").open("w", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
            writer.writeheader()
            writer.writerows(rows)
        return directory

    def test_regular_cadence_and_large_qpc(self):
        result = analyzer.summarize(self.fixture(), .1)
        segment = result["segments"][0]
        self.assertEqual(segment["display_intervals"]["above_1_5x_median"], 0)
        self.assertAlmostEqual(segment["display_intervals"]["median_ms"], 16.666667)
        self.assertGreater(segment["duration_seconds"], 9)
        self.assertEqual(result["rejected_timestamp_rows"], 0)

    def test_long_display_hold(self):
        timing = analyzer.summarize(self.fixture(spike=True), .1)["segments"][0]["display_intervals"]
        self.assertEqual(timing["above_1_5x_median"], 1)
        self.assertAlmostEqual(timing["max_ms"], 33.333333)

    def test_unavailable_display_is_not_zero(self):
        segment = analyzer.summarize(self.fixture(display="NA"), .1)["segments"][0]
        self.assertEqual(segment["display_intervals"], {"count": 0})
        self.assertGreater(segment["present_intervals"]["count"], 0)

    def test_swap_chains_not_averaged(self):
        segments = analyzer.summarize(self.fixture(second_chain=True), .1)["segments"]
        self.assertEqual(len(segments), 2)
        self.assertAlmostEqual(segments[0]["display_intervals"]["median_ms"], 16.666667)
        self.assertAlmostEqual(segments[1]["display_intervals"]["median_ms"], 8.333333)

    def test_modes_and_transition_exclusion(self):
        directory = self.fixture()
        (directory / "grid-test.log").write_text(
            "2026-10-03T05:00:02Z GRID ON mode=fractional-motion-4x (15s)\n"
            "2026-10-03T05:00:05Z GRID OFF (manual); restored\n"
            "2026-10-03T05:00:06Z GRID ON mode=fractional-motion-8x (15s)\n"
            "2026-10-03T05:00:09Z GRID OFF (manual); restored\n")
        segments = analyzer.summarize(directory, .1)["segments"]
        modes = {s["mode"] for s in segments}
        self.assertEqual(modes, {"unmodified-unmarked", "fractional-motion-4x", "fractional-motion-8x"})
        for segment in segments:
            if segment["mode"] != "unmodified-unmarked":
                self.assertLess(segment["duration_seconds"], 3)
                self.assertGreater(segment["duration_seconds"], 2)

    def test_missing_schema_refused(self):
        directory = self.fixture()
        (directory / "presentmon.csv").write_text("FrameTime\n16.6\n")
        with self.assertRaises(ValueError):
            analyzer.summarize(directory)

    def test_pacing_and_grid_changes_kept_separate(self):
        directory = self.fixture()
        (directory / "grid-test.log").write_text(
            "2026-10-03T05:00:01Z PACE ON mode=vsync-display (90s)\n"
            "2026-10-03T05:00:03Z GRID ON mode=fractional-motion-8x (15s)\n"
            "2026-10-03T05:00:05Z PACE OFF (focus_lost)\n"
            "2026-10-03T05:00:07Z GRID OFF (timeout)\n")
        segments = analyzer.summarize(directory, .1)["segments"]
        pairs = {(s["mode"], s["pacing_mode"]) for s in segments}
        self.assertEqual(pairs, {
            ("unmodified-unmarked", "stock-or-unmarked"),
            ("unmodified-unmarked", "vsync-display"),
            ("fractional-motion-8x", "vsync-display"),
            ("fractional-motion-8x", "stock-or-unmarked")})
        self.assertEqual(len(segments), 5)

    def test_actual_legacy_header_aliases(self):
        directory = self.fixture()
        path = directory / "presentmon.csv"
        text = path.read_text().replace("TimeInQPC", "QPCTime").replace("MsBetweenPresents", "msBetweenPresents").replace("MsBetweenDisplayChange", "msBetweenDisplayChange")
        path.write_text(text)
        result = analyzer.summarize(directory, .1)
        self.assertTrue(result["display_column_available"])
        self.assertAlmostEqual(result["segments"][0]["display_intervals"]["median_ms"], 16.666667)


if __name__ == "__main__":
    unittest.main()
