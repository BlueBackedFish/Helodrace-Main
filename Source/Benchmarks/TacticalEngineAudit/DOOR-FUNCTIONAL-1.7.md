# R7 door functional fixture

The `door` functional queue item uses `-Case r7-door -Workload sapper-door`, fixture version 35. LOW 12, seed `hd-r1-20261007`, speed 3, warmup 0 and sample 8000 remain. It still requires full `newFunctionalComplete`, an actual jammed door and all existing native content/environment/safety checks. The queue additionally requires initial/final approach isolation, secured use of the door opening, actual firing and actor withdrawal. This case is not a CPU comparison.

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
Source/Benchmarks/TacticalEngineAudit/Tests/Test-R7FunctionalQueue.ps1
Source/Benchmarks/TacticalEngineAudit/Run-R7Functional.ps1 -Cases door -AuditRoot C:/Users/Public/Documents/ESTsoft/CreatorTemp/r7-door-FRESH
Source/Benchmarks/TacticalEngineAudit/Verify-R7Functional.ps1 -Root C:/Users/Public/Documents/ESTsoft/CreatorTemp/r7-door-FRESH
```

Use a fresh root, serialize games and keep observing the same PID after an observation timeout. This verifies one selected functional item, not the complete 44-case queue or R7 CPU acceptance. `-RequireFinal` must still reject a single-case journal.

## Why the old stimulus failed

Actual 1.7.4995 rev309 baseline `hd-migration17-door-4995-baseline-20261102` preserves the former `normal` case unchanged: complete capture, no audit error, 12 alive, zero unsafe entries, but zero door faults and no mission completion. It made a wall opening and fired 443 times at an immortal hostile actor. At final tick 8002 its phase was Enter/CombatQuiet with combatUntil 8086 and one member still moving/firing instead of reaching the assigned post. This is consistent with the earlier preserved 1.6 baseline and 1.7 failure, not evidence of a newly introduced 1.7 numerical regression.

The production LOW doctrine ranks a closed door with a 600 penalty versus a wall's 0 within the bounded candidate set. The old scenario supplied many valid wall approaches and an actor protected from all damage for the entire run. A door's presence therefore did not require the planner to use it, and constant real firing kept the room-combat hold active. Changing LOW doctrine or removing combat safety to satisfy this stimulus would change gameplay.

## Door-specific stimulus

The new case uses the real WaterDeep terrain in column x=99, z=100..136 except z=108. The sole walkable west-front approach is outside the existing closed player door at (100,0,108). The planner, reachability, LOS, tool selection, door job, firing and room survey all run normally; no results are mocked or forced. Both initial and final terrain/walkability are checked. The final proof requires the actual jammed door and every command's secured portal history to contain that same opening.

The stationary protected actor has the same finite 6000-tick exposure already used by structural functional fixtures, then moves to (180,0,180). Its faction and named bed objective remain. Actual contact shots must occur; the test cannot pass by hiding it before contact. Full post-contact mission completion and all 12 entry assignments remain mandatory. Normal/matched CPU and the other dedicated fixture inputs are unchanged.

## Evidence

Exploratory actual 1.7 `hd-migration17-door-4995-isolated-20261102`: complete capture, no audit error, all three door/approach proofs true, actor withdrawn, one door fault, 314 contact shots, all 12 entry assignments, one completed unit, secured door opening (100,0,108), 1365 secured room cells, zero unsafe entries and full functional completion. This exploratory capture is not itself a queue-verifier result.

Formal `hd-migration17-r7-door-4995-verified-20261102/door` passed the unchanged R7FunctionalEvidence verifier with the new fixture-specific requirements: same one door fault, 314 shots, 12 assignments, one completed unit, 1365 secured cells and zero unsafe entries. All door/approach proofs are true, the actor withdrew at tick 6000, measured speed stayed 3/3, and native exception count is zero. The journal records collectionComplete/allSpecifiedPassed true, fullFunctionalQueue/finalR7Complete/cpuGateEvaluated false. Independent read-only Verify-R7Functional passes this requested item. Its `-RequireFinal` invocation correctly rejects this single-case journal.

Product SHA-256 is `6884D1DC6E643182B536391EC892F1A61F282931D84D20308DBA443F28F0BAB8`. Product and Main migration bridge builds have zero warnings/errors. The five queue-control mocks pass, including assertions that the door item retains its full functional/count/fixture proof requirements. Mock outputs are not game evidence. Both valid door-specific captures exited; collected host exit code is unavailable (null). Their only ERROR is the known missing Workshop item 3781143823. Normal baseline remains a failed functional stimulus, retained separately; no earlier failure has been relabeled as passing.
