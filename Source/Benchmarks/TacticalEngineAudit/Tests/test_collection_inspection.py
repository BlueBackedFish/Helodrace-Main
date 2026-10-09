"""Synthetic journal/identity safety checks, never native CPU evidence."""
import copy
import importlib.util
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
base = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("r7_collection", base / "Inspect-R7Collection.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CollectionTests(unittest.TestCase):
    def setUp(self):
        self.specs = module.specifications()
        self.pinned = {v: "a" * 64 for v in module.CONTENT.values()}
        names = [f"synthetic-functional-{i}" for i in range(44)]
        self.functional = dict(fullFunctionalQueue=True, allSpecifiedPassed=True, requestedCases=names,
                               pinned=self.pinned,
                               records=[dict(name=n, status="passed", nativeExceptions=0) for n in names])
        self.collection = dict(pinned=self.pinned, requestedGroups=[s["name"] for s in self.specs],
                               allSpecifiedCollected=True, profileTargets=";".join(module.TARGETS),
                               records=[dict(name=s["name"], category=s["category"], status="collected", error=None) for s in self.specs])

    def matrix(self, spec):
        planned = [dict(engine=engine, workload=spec["workload"], repeat=repeat,
                        requestedPopulation=spec["population"], speed=spec["speed"], fixtureCase=spec["fixtureCase"],
                        warmupTicks=0, sampleTicks=spec["ticks"], methodProfile=spec["methodProfile"], cpuWindows=True)
                   for repeat in range(1, spec["repeats"] + 1)
                   for engine in (("vanilla", "new") if repeat % 2 else ("new", "vanilla"))]
        return dict(pinned=self.pinned, planned=planned, allSpecifiedPassed=True,
                    records=[dict(p, status="passed", error=None, pid=100+i,
                                  startedUtc=f"2026-10-10T00:00:{i:02d}.0000000Z",
                                  root=f"C:/synthetic/{i}") for i, p in enumerate(planned)])

    def test_full24_native96_plan(self):
        self.assertEqual(len(module.inspect_headers(self.functional, self.collection)), 24)
        self.assertEqual(sum(s["repeats"] * 2 for s in self.specs), 96)
        self.assertEqual(sum(s["category"] == "scale" for s in self.specs), 12)
        self.assertEqual(sum(s["category"] == "profiled" for s in self.specs), 6)
        self.assertEqual(sum(s["category"] == "unprofiled" for s in self.specs), 6)

    def test_partial_functional_cannot_support_collection(self):
        self.functional["records"].pop()
        with self.assertRaisesRegex(ValueError, "functional journal"):
            module.inspect_headers(self.functional, self.collection)

    def test_subset_or_duplicate_groups_rejected(self):
        for mutate in (lambda c: c["records"].pop(),
                       lambda c: c["records"].__setitem__(1, c["records"][0])):
            c = copy.deepcopy(self.collection)
            mutate(c)
            with self.assertRaisesRegex(ValueError, "final24"):
                module.inspect_headers(self.functional, c)

    def test_different_functional_build_rejected(self):
        self.collection["pinned"] = dict(self.pinned, **{module.CONTENT["assemblySha256"]: "b"*64})
        with self.assertRaisesRegex(ValueError, "content mismatch"):
            module.inspect_headers(self.functional, self.collection)

    def test_missing_selected_methods_rejected(self):
        self.collection["profileTargets"] = ";".join(module.TARGETS[:2])
        with self.assertRaisesRegex(ValueError, "selectors"):
            module.inspect_headers(self.functional, self.collection)

    def test_actual_alternating_order_required(self):
        s = next(s for s in self.specs if s["category"] == "profiled")
        m = self.matrix(s)
        self.assertEqual([r["engine"] for r in module.inspect_matrix(m, s, self.pinned)],
                         ["vanilla", "new", "new", "vanilla", "vanilla", "new"])
        m["records"][2], m["records"][3] = m["records"][3], m["records"][2]
        with self.assertRaisesRegex(ValueError, "alternating"):
            module.inspect_matrix(m, s, self.pinned)

    def test_missing_repeat_cannot_pass(self):
        s = self.specs[-1]
        m = self.matrix(s)
        m["records"].pop()
        with self.assertRaisesRegex(ValueError, "plan/count"):
            module.inspect_matrix(m, s, self.pinned)

    def test_speed_plan_and_creation_order_rejected(self):
        s = self.specs[0]
        m = self.matrix(s)
        m["planned"][0]["speed"] = 3
        with self.assertRaisesRegex(ValueError, "plan/count"):
            module.inspect_matrix(m, s, self.pinned)
        m = self.matrix(s)
        m["records"][1]["startedUtc"] = m["records"][0]["startedUtc"]
        with self.assertRaisesRegex(ValueError, "creation times"):
            module.inspect_matrix(m, s, self.pinned)

    def test_missing_native_creation_rejected(self):
        s = self.specs[0]
        m = self.matrix(s)
        m["records"][0]["startedUtc"] = None
        with self.assertRaisesRegex(ValueError, "creation identity"):
            module.inspect_matrix(m, s, self.pinned)

    def test_savedata_normalization(self):
        self.assertEqual(module.canonical(r"C:\\Users\\Public\\Run"), module.canonical("c:/Users/Public/Run"))

    def test_collection_coverage_never_accepts_cpu_and_duplicate_identity_rejected(self):
        # Synthetic journals plus a mocked per-run reader exercise global
        # coverage only. No actual performance measurements are created.
        with tempfile.TemporaryDirectory() as root:
            repository=Path(root)/"repo"; functional=Path(root)/"functional"; performance=Path(root)/"performance"
            functional.mkdir(); performance.mkdir()
            for filename in self.pinned:
                file=repository/Path(filename.replace("\\","/")); file.parent.mkdir(parents=True,exist_ok=True)
                data=("synthetic pin: "+filename).encode(); file.write_bytes(data)
                self.pinned[filename]=hashlib.sha256(data).hexdigest()
            self.collection["functionalRoot"]=str(functional)
            (functional/"checks.json").write_text(json.dumps(self.functional),encoding="utf-8")
            (performance/"checks.json").write_text(json.dumps(self.collection),encoding="utf-8")
            for s in self.specs:
                group=performance/s["name"]; group.mkdir()
                m=self.matrix(s)
                for i,r in enumerate(m["records"]): r["root"]=f"C:/synthetic/{s['name']}/{i}"
                (group/"checks.json").write_text(json.dumps(m),encoding="utf-8")
            with patch.object(module,"inspect_run",side_effect=lambda path,row,spec,pinned:module.canonical(row["root"])):
                report=module.inspect(functional,performance,repository)
                self.assertEqual(report["nativeRuns"],96)
                self.assertFalse(report["cpuGateEvaluated"])
                self.assertFalse(report["finalR7Complete"])
            with patch.object(module,"inspect_run",return_value="synthetic-duplicate"):
                with self.assertRaisesRegex(ValueError,"Duplicate native savedata"):
                    module.inspect(functional,performance,repository)

    def test_actual_registration_and_savedata_binding(self):
        # Minimal synthetic file shapes test collection checks only. They have
        # no CPU values and cannot pass the separate engine CPU comparator.
        s = self.specs[0]
        row = dict(engine="new", root="C:/synthetic/run", population=0, entered=0, objectiveReached=0)
        conditions = dict(engine="new", workload=s["workload"], fixtureCase=s["fixtureCase"],
                          requestedPopulation=s["population"], speed=s["speed"], warmupTicks=0,
                          sampleTicks=s["ticks"], seed="hd-r1-20261007")
        a = dict(conditions, complete=True, error=None, isolationVerified=True, environmentControlled=True,
                 unexpectedPawns=[], r7TickRateMinimum=1, r7TickRateMaximum=1, r7RateSamples=1,
                 measuredTicks=s["ticks"], population=0, entered=0, objectiveReached=0,
                 r7CpuCheckpoints=[{"label": "start"}, {"label": "end"}])
        l = dict(conditions, **{k:self.pinned[v] for k,v in module.CONTENT.items()}, high=False,
                 methodProfile=True, cpuWindows=True, headless=False, defaultEngine=True,
                 targets=";".join(module.TARGETS), reload=False, retainedReload=False,
                 arguments=['"-savedatafolder=C:/synthetic/run"', "-screen-fullscreen", "0", "-screen-width",
                            "800", "-screen-height", "600"])
        c = dict(complete=True, dropped=0, assemblySha256=self.pinned[module.CONTENT["assemblySha256"]],
                 selectedEngine="new", cpuCheckpoints=a["r7CpuCheckpoints"],
                 methods=[dict(method=t.replace("::", ".")+"()", exceptions=0) for t in module.TARGETS])
        with tempfile.TemporaryDirectory() as root:
            path=Path(root); (path/"profiles").mkdir()
            for name,value in (("audit.json",a),("launcher.json",l),("profiles/capture-synthetic.json",c)):
                (path/name).write_text(json.dumps(value),encoding="utf-8")
            self.assertEqual(module.inspect_run(path,row,s,self.pinned),module.canonical(row["root"]))
            for extra in (1,10):
                a["measuredTicks"]=s["ticks"]+extra
                (path/"audit.json").write_text(json.dumps(a),encoding="utf-8")
                self.assertEqual(module.inspect_run(path,row,s,self.pinned),module.canonical(row["root"]))
            for extra in (-1,11):
                a["measuredTicks"]=s["ticks"]+extra
                (path/"audit.json").write_text(json.dumps(a),encoding="utf-8")
                with self.assertRaisesRegex(ValueError,"actual native window"):
                    module.inspect_run(path,row,s,self.pinned)
            a["measuredTicks"]=s["ticks"]
            (path/"audit.json").write_text(json.dumps(a),encoding="utf-8")
            c["methods"].pop()
            (path/"profiles/capture-synthetic.json").write_text(json.dumps(c),encoding="utf-8")
            with self.assertRaisesRegex(ValueError,"not registered"):
                module.inspect_run(path,row,s,self.pinned)
            l["arguments"][0]='"-savedatafolder=C:/different"'
            (path/"launcher.json").write_text(json.dumps(l),encoding="utf-8")
            with self.assertRaisesRegex(ValueError,"savedata/journal"):
                module.inspect_run(path,row,s,self.pinned)


if __name__ == "__main__":
    unittest.main()
