"""Comparison safety tests; synthetic repeats are never native CPU evidence."""
import copy
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True
base = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("r7_unprofiled", base / "Compare-R7Unprofiled.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
native = base / "Results/2026-10-09-r7/cpu-windows-unprofiled-smoke-01"


class CompareTests(unittest.TestCase):
    def setUp(self):
        self.run = module.read_run(native)

    def rejected_mutation(self, mutate, message):
        a = json.loads(module.text(native / "audit.json"))
        l = json.loads(module.text(native / "launcher.json"))
        mutate(a, l)
        with tempfile.TemporaryDirectory() as root:
            p = Path(root)
            (p / "audit.json").write_text(json.dumps(a), encoding="utf-8")
            (p / "launcher.json").write_text(json.dumps(l), encoding="utf-8")
            (p / "Player.log").write_text(module.text(native / "Player.log"), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, message):
                module.read_run(p)

    def repeats(self, count, ratio=1.5):
        # These are comparison arithmetic inputs, explicitly synthetic.
        baseline, candidate = [], []
        for i in range(count):
            a, b = copy.deepcopy(self.run), copy.deepcopy(self.run)
            a.update(engine="vanilla", identity=f"synthetic-baseline-{i}")
            b.update(identity=f"synthetic-candidate-{i}")
            b["mainCpuMsPerTick"] = a["mainCpuMsPerTick"] * ratio
            baseline.append(a)
            candidate.append(b)
        return baseline, candidate

    def test_real_archived_reader_and_duplicate_end_tick(self):
        self.assertEqual(self.run["mainCpuMsPerTick"], 15375 / 4000)
        points = self.run["audit"]["r7CpuCheckpoints"]
        self.assertEqual(points[-1]["tick"], points[-2]["tick"])

    def test_profiled_run_rejected(self):
        self.rejected_mutation(lambda a, l: l.update(methodProfile=True), "Not an unprofiled")

    def test_fake_tick_cpu_rejected(self):
        self.rejected_mutation(lambda a, l: a["r7CpuCheckpoints"][-1].update(tickCpuMs=1), "must not invent tick-root CPU")

    def test_nonmonotonic_cpu_rejected(self):
        self.rejected_mutation(lambda a, l: a["r7CpuCheckpoints"][2].update(mainCpuMs=1), "Nonmonotonic")

    def test_inconsistent_total_rejected(self):
        self.rejected_mutation(lambda a, l: a.update(uninstrumentedMainCpuMs=1), "CPU total/checkpoint mismatch")

    def test_bounded_frame_overshoot_uses_actual_ticks(self):
        # Explicitly synthetic frame-close timing derived from an archived
        # native run. It is comparator validation, not a CPU measurement.
        for extra in (1, 10):
            a = json.loads(module.text(native / "audit.json"))
            l = json.loads(module.text(native / "launcher.json"))
            a["measuredTicks"] += extra
            a["r7CpuCheckpoints"][-1]["tick"] += extra
            with tempfile.TemporaryDirectory() as root:
                p=Path(root)
                (p/"audit.json").write_text(json.dumps(a),encoding="utf-8")
                (p/"launcher.json").write_text(json.dumps(l),encoding="utf-8")
                (p/"Player.log").write_text(module.text(native/"Player.log"),encoding="utf-8")
                result=module.read_run(p)
                self.assertEqual(result["mainCpuMsPerTick"],a["uninstrumentedMainCpuMs"]/a["measuredTicks"])
        for extra in (-1, 11):
            self.rejected_mutation(lambda a,l:a.update(measuredTicks=a["sampleTicks"]+extra),
                                   "Preparation/window/population")

    def test_frame_overshoot_still_requires_actual_checkpoint_length(self):
        self.rejected_mutation(lambda a,l:a.update(measuredTicks=a["sampleTicks"]+1), "CPU window bounds")

    def test_graphics_mismatch_rejected(self):
        self.rejected_mutation(lambda a, l: l["arguments"].__setitem__(l["arguments"].index("800"), "1600"), "Graphics")

    def test_no_progress_rejected(self):
        self.rejected_mutation(lambda a, l: a.update(entered=0), "No actual movement/entry")

    def test_missing_field_input_rejected(self):
        def mutate(a, l):
            a.update(fixtureCase="r6-field-cpu", newFieldEvents=[])
            l.update(fixtureCase="r6-field-cpu")
        self.rejected_mutation(mutate, "Field fixed timeline")

    def test_single_repeat_cannot_pass(self):
        result = module.compare(*self.repeats(1))
        self.assertAlmostEqual(result["mainCpuRatio"], 1.5)
        self.assertFalse(result["repeatedControlEligible"])
        self.assertFalse(result["unprofiledMainControlPassed"])
        self.assertFalse(result["tickCpuGateEvaluated"])
        self.assertFalse(result["finalR7Complete"])

    def test_duplicate_native_identity_rejected(self):
        a, b = self.repeats(3)
        b[1]["identity"] = b[0]["identity"]
        with self.assertRaisesRegex(ValueError, "Duplicate native run"):
            module.compare(a, b)

    def test_mixed_build_rejected(self):
        a, b = self.repeats(3)
        b[0]["conditions"]["assemblySha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "conditions differ"):
            module.compare(a, b)

    def test_repeat_median_and_two_times_bound(self):
        self.assertTrue(module.compare(*self.repeats(3, 2))["unprofiledMainControlPassed"])
        self.assertFalse(module.compare(*self.repeats(3, 2.01))["unprofiledMainControlPassed"])


if __name__ == "__main__":
    unittest.main()
