#!/usr/bin/env python3
"""Exercise regression and correctness guards against the published measurements."""

import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("benchmark", ROOT / "eng/benchmark.py")
BENCHMARK = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(BENCHMARK)


class BenchmarkGuards(unittest.TestCase):
    def setUp(self):
        self.baseline = json.loads((ROOT / "benchmarks/baseline.json").read_text(encoding="utf-8"))
        self.current = copy.deepcopy(self.baseline)

    def test_measured_baseline_passes(self):
        self.assertEqual([], BENCHMARK.compare(self.current, self.baseline))

    def test_latency_and_memory_regressions_are_reported(self):
        summary = self.current["cases"]["Generated24x40"]["summary"]
        for key in ("FullScan.elapsedMilliseconds", "Workspace.allocatedBytes", "Graph.allocatedBytes"):
            summary[key]["median" if "median" in summary[key] else "measured"] = summary[key]["budget"] + 1
        failures = BENCHMARK.compare(self.current, self.baseline)
        self.assertEqual(3, len(failures))

    def test_different_inputs_or_results_cannot_pass_as_faster(self):
        for field, path in (("inputSha256", ()), ("fingerprint", ("validation",))):
            current = copy.deepcopy(self.current)
            target = current["cases"]["CleanArchitecture"]
            for part in path:
                target = target[part]
            target[field] = "changed"
            with self.assertRaises(RuntimeError):
                BENCHMARK.compare(current, self.baseline)

    def test_missing_case_is_rejected(self):
        del self.current["cases"]["Generated24x40"]
        with self.assertRaises(RuntimeError):
            BENCHMARK.compare(self.current, self.baseline)

    def test_comparison_cannot_overwrite_baseline(self):
        path = ROOT / "benchmarks/baseline.json"
        before = path.read_bytes()
        result = subprocess.run([sys.executable, str(ROOT / "eng/benchmark.py"), "--check", str(path), "--output", str(path)],
                                capture_output=True, text=True, check=False)
        self.assertEqual(2, result.returncode)
        self.assertIn("must not overwrite", result.stderr)
        self.assertEqual(before, path.read_bytes())


if __name__ == "__main__":
    unittest.main()
