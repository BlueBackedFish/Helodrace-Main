"""Read-only final R7 collection coverage/identity inspection, never CPU acceptance.

Re-verify native functional evidence separately. Full44 is the default; an
explicit finalFunctionalSkipped collection preserves the user's partial scope.
This checks CPU coverage and its journal link, not the functional actions.
Then run engine-compare and Compare-R7Unprofiled.py on the emitted directories.
All matched groups, CPU ratios, phase costs and final requirements still need
review; neither a collected flag nor this inspection establishes R7 completion.
"""
import argparse
from datetime import datetime
import hashlib
import json
import ntpath
from pathlib import Path
import re

CONTENT = {
    "assemblySha256": "Assemblies\\Helodrace.dll",
    "newJobDefinitionsSha256": "Defs\\Organization\\NewTacticalJobs.xml",
    "breachDefinitionsSha256": "Defs\\ColdWar\\BreachExplosive_ColdWar.xml",
    "hammerJobDefinitionsSha256": "Defs\\GreatWar\\Sledgehammer_Breach.xml",
}
TARGETS = ["Verse.AI.Pawn_JobTracker::StartJob", "Verse.PathFinder::CreateRequest",
           "Helodrace.Tactics.TacticalCommunications::Send",
           "Helodrace.Tactics.TacticalCommunications::Deliver",
           "Helodrace.Tactics.TacticalCooperationState::CanStart"]


def require(value, message):
    if not value:
        raise ValueError(message)


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def canonical(path):
    return ntpath.normpath(str(path).replace("/", "\\")).casefold()


def specifications():
    specs = []
    for speed in (1, 3):
        for population, ticks in ((0, 3000), (12, 6000), (50, 8000), (100, 10000), (200, 14000), (400, 20000)):
            specs.append(dict(name=f"scale-{population}-{speed}x", population=population, speed=speed,
                              fixtureCase="normal", workload="sapper-wall", high=False, repeats=1,
                              ticks=ticks, methodProfile=True, category="scale"))
    representatives = [("cqb-low", 48, False, "r7-cqb-cpu", "sapper-wall", 20000),
                       ("cqb-high", 52, True, "r7-cqb-cpu", "sapper-wall", 20000),
                       ("field-low", 48, False, "r6-field-cpu", "sapper-wall", 12000),
                       ("field-high", 52, True, "r6-field-cpu", "sapper-wall", 12000),
                       ("ordinary-open", 48, False, "normal", "open-approach", 8000),
                       ("cqb-400", 400, False, "r7-cqb-cpu", "sapper-wall", 20000)]
    for profile in (True, False):
        category = "profiled" if profile else "unprofiled"
        for name, population, high, case, workload, ticks in representatives:
            specs.append(dict(name=f"{name}-{category}", population=population, speed=3, fixtureCase=case,
                              workload=workload, high=high, repeats=3, ticks=ticks,
                              methodProfile=profile, category=category))
    return specs


def inspect_headers(functional, collection):
    specs = specifications()
    if collection.get("finalFunctionalSkipped") is True:
        rows = functional.get("records", [])
        names = functional.get("requestedCases", [])
        verified = [r["name"] for r in rows if r.get("status") == "passed"]
        require(verified and len(names) == 44 and len(set(names)) == 44
                and [r["name"] for r in rows] == names[:len(rows)]
                and all(r.get("status") in ("passed", "running") and r.get("nativeExceptions") == 0 for r in rows)
                and collection.get("verifiedFunctionalCases") == verified,
                "Explicit partial functional evidence is missing, failed or inconsistent")
    else:
        require(functional.get("fullFunctionalQueue") is True and functional.get("allSpecifiedPassed") is True
            and len(functional.get("requestedCases", [])) == 44
            and len(set(functional["requestedCases"])) == 44
            and len(functional.get("records", [])) == 44
            and [r["name"] for r in functional["records"]] == functional["requestedCases"]
            and all(r.get("status") == "passed" and r.get("nativeExceptions") == 0
                    for r in functional["records"]), "Full functional journal is incomplete/failed")
    require(set(functional.get("pinned", {})) == set(CONTENT.values())
            and all(re.fullmatch(r"[0-9a-f]{64}", h) for h in functional["pinned"].values()),
            "Functional content pins missing/invalid")
    require(collection.get("pinned") == functional["pinned"], "Functional/CPU content mismatch")
    require(collection.get("requestedGroups") == [s["name"] for s in specs]
            and collection.get("allSpecifiedCollected") is True
            and len(collection.get("records", [])) == len(specs)
            and [r["name"] for r in collection["records"]] == collection["requestedGroups"]
            and all(r.get("status") == "collected" and r.get("error") is None for r in collection["records"]),
            "Missing, duplicate, reordered or incomplete final24 collection groups")
    require(collection.get("profileTargets") == ";".join(TARGETS), "Final method selectors differ")
    return specs


def inspect_matrix(matrix, spec, pinned):
    expected = [dict(engine=engine, workload=spec["workload"], repeat=repeat,
                     requestedPopulation=spec["population"], speed=spec["speed"], fixtureCase=spec["fixtureCase"],
                     warmupTicks=0, sampleTicks=spec["ticks"], methodProfile=spec["methodProfile"], cpuWindows=True)
                for repeat in range(1, spec["repeats"] + 1)
                for engine in (("vanilla", "new") if repeat % 2 else ("new", "vanilla"))]
    require(matrix.get("pinned") == pinned, "Matrix content differs from functional build")
    require(matrix.get("planned") == expected and matrix.get("allSpecifiedPassed") is True
            and len(matrix.get("records", [])) == len(expected), "Matrix plan/count incomplete or differs")
    previous = None
    for row, plan in zip(matrix["records"], expected):
        require(row.get("status") == "passed" and row.get("error") is None
                and all(row.get(k) == plan[k] for k in ("engine", "workload", "repeat", "speed", "fixtureCase")),
                "Actual alternating engine/repeat order differs or run failed")
        require(isinstance(row.get("pid"), int) and row["pid"] > 0 and row.get("startedUtc"),
                "Native PID/creation identity missing")
        creation = datetime.fromisoformat(row["startedUtc"].replace("Z", "+00:00"))
        require(creation.tzinfo is not None and (previous is None or creation > previous),
                "Native creation times do not follow recorded collection order")
        previous = creation
    return matrix["records"]


def inspect_run(path, row, spec, pinned):
    a, launcher = read(path / "audit.json"), read(path / "launcher.json")
    for field, content in CONTENT.items():
        require(launcher.get(field) == pinned[content], "Launcher content mismatch: " + field)
    conditions = dict(engine=row["engine"], workload=spec["workload"], fixtureCase=spec["fixtureCase"],
                      requestedPopulation=spec["population"], speed=spec["speed"], warmupTicks=0,
                      sampleTicks=spec["ticks"], seed="hd-r1-20261007")
    require(all(a.get(k) == v and launcher.get(k) == v for k, v in conditions.items()),
            "Actual audit/launcher/spec conditions differ")
    require(launcher.get("high") == spec["high"] and launcher.get("methodProfile") == spec["methodProfile"]
            and launcher.get("cpuWindows") is True and launcher.get("headless") is False
            and launcher.get("defaultEngine") == (row["engine"] == "new")
            and launcher.get("targets") == ";".join(TARGETS)
            and not launcher.get("reload") and not launcher.get("retainedReload"),
            "Profile/graphics/default/selectors/load conditions differ")
    require(a.get("complete") and a.get("error") is None and a.get("isolationVerified")
            and a.get("environmentControlled") and a.get("unexpectedPawns") == []
            and a.get("r7TickRateMinimum") == a.get("r7TickRateMaximum") == spec["speed"]
            and a.get("r7RateSamples", 0) > 0 and spec["ticks"] <= a.get("measuredTicks", 0) <= spec["ticks"] + 10,
            "Uncontrolled, incomplete or different actual native window")
    require(all(row.get(k) == a.get(k) for k in ("population", "entered", "objectiveReached")),
            "Matrix/native progression record differs")
    args = launcher["arguments"]
    savedata = [arg for arg in args if "-savedatafolder=" in arg]
    require(len(savedata) == 1, "Missing or ambiguous native savedata identity")
    identity = canonical(savedata[0].split("-savedatafolder=", 1)[1].strip('"'))
    require(identity == canonical(row["root"]), "Native savedata/journal root differs")
    graphics = ["-screen-fullscreen", "0", "-screen-width", "800", "-screen-height", "600"]
    require(any(args[i:i+len(graphics)] == graphics for i in range(len(args)))
            and "-nographics" not in args and "-batchmode" not in args, "Native graphics conditions differ")
    captures = list((path / "profiles").glob("capture-*.json"))
    if spec["methodProfile"]:
        require(len(captures) == 1, "Missing or duplicate method capture")
        c = read(captures[0])
        require(c.get("complete") and c.get("dropped") == 0 and c.get("assemblySha256") == pinned[CONTENT["assemblySha256"]]
                and c.get("selectedEngine") == row["engine"] and c.get("cpuCheckpoints") == a.get("r7CpuCheckpoints")
                and isinstance(c.get("cpuCheckpoints"), list) and len(c["cpuCheckpoints"]) >= 2,
                "Incomplete, mixed-build or missing CPU-window capture")
        methods = c["methods"]
        require(len({m["method"] for m in methods}) == len(methods) and all(m.get("exceptions") == 0 for m in methods),
                "Duplicate/error method capture")
        for target in TARGETS:
            require(any(m["method"].startswith(target.replace("::", ".") + "(") for m in methods),
                    "Final selected method not registered: " + target)
    else:
        require(not (path / "profiles").exists(), "Unprofiled control installed profiler")
    # The existing CPU comparators perform timing/input/functional validation;
    # this inspector records native coverage and does not approve their results.
    return identity


def inspect(functional_root, performance_root, repository):
    f, p = read(functional_root / "checks.json"), read(performance_root / "checks.json")
    specs = inspect_headers(f, p)
    require(canonical(p.get("functionalRoot", "")) == canonical(functional_root), "Functional journal link differs")
    for file, expected in f["pinned"].items():
        current = repository / Path(file.replace("\\", "/"))
        require(hashlib.sha256(current.read_bytes()).hexdigest() == expected, "Current repository content differs: " + file)
    identities, groups = set(), []
    for record, spec in zip(p["records"], specs):
        require(record.get("category") == spec["category"], "Group category differs")
        group = performance_root / spec["name"]
        rows = inspect_matrix(read(group / "checks.json"), spec, f["pinned"])
        for row in rows:
            run = group / spec["workload"] / row["engine"] / f"run-{row['repeat']}"
            identity = inspect_run(run, row, spec, f["pinned"])
            require(identity not in identities, "Duplicate native savedata identity across final96")
            identities.add(identity)
        groups.append(dict(name=spec["name"], category=spec["category"], nativeRuns=len(rows),
                           repeated=spec["repeats"] >= 3,
                           baseline=str(group / spec["workload"] / "vanilla"),
                           candidate=str(group / spec["workload"] / "new")))
    require(len(identities) == 96, "Final native96 coverage incomplete")
    return dict(scope="Read-only R7 collection coverage/identity inspection only", finalR7Complete=False,
                cpuGateEvaluated=False, finalFunctionalSkipped=p.get("finalFunctionalSkipped", False),
                verifiedFunctionalCases=p.get("verifiedFunctionalCases", []),
                collectionCoverageVerified=True, nativeRuns=len(identities), groups=groups,
                warning="Functional native re-verification, matched CPU/input comparison with no unmatched groups, "
                        "Core/method/phase/spike reports, cooperation outcome review and final requirements audit remain necessary. "
                        "Scale single pairs cannot pass repeated CPU gates; collection coverage is not CPU acceptance.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("functional", type=Path)
    parser.add_argument("performance", type=Path)
    parser.add_argument("--repository", type=Path, default=Path(__file__).resolve().parents[3])
    args = parser.parse_args()
    try:
        print(json.dumps(inspect(args.functional, args.performance, args.repository), ensure_ascii=False, indent=2))
    except (ValueError, KeyError, OSError, TypeError) as error:
        parser.exit(1, "Collection inspection rejected: " + str(error) + "\n")
