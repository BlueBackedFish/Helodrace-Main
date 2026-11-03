# T2-32 chart measurement

This is an analysis-only module. `Start-Bench.ps1` copies the existing product DLL into an isolated actual RimWorld 1.7 runtime and adds this benchmark mod. It never builds or replaces the shipped DLL and never changes balance XML, collision, armor, damage or the native injury worker. Its Harmony probes only observe pellet impacts and returned damage.

```powershell
Source/Analysis/T2_32/Start-Bench.ps1 -RuntimeRoot .codex-staging/T2_32/fresh-name -Samples 128
Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot .codex-staging/T2_32/fresh-name -TimeoutSeconds 45
python Source/Analysis/T2_32/make_charts.py --evidence .codex-staging/T2_32/fresh-name/Evidence
```

Use one fresh runtime per run and preserve its PID while it is live. Collect the same host after exit. `status.txt` must end in COMPLETE before plotting.

- M1887 and M1897, normal and sharpshooter alternate modes, 3/7/15 cells, three actual armor loadouts. All 36 combinations are exported; nine are outside the selected mode's range and are not fired. The 27 valid cases have the same sample count.
- One healthy 25-year-old male baseliner shooter, shooting level 10 and the active HD sharpshooter gene in both modes. No traits, CQB part selection, tactical aiming, cover, movement or weather penalty. Normal-quality weapons. The shooter is returned to the same firing cell, its health and wait job are reset before every sample, and unrelated spawned pawns are removed from the benchmark map. Each sample gets a fresh identically generated healthy adult male baseliner target with traits removed, incoming damage factor 1, and normal-quality full-HP armor.
- Unarmored target; flak vest plus steel simple helmet; marine armor plus marine helmet. The light loadout intentionally leaves some body parts uncovered. `specs.json` records resolved armor values rather than treating an entire pawn as one armor percentage.
- Fixed generation seeds 70012/70013; shot seed 90000 + repeat. Each trigger pull launches nine actual pellets through the existing native shooting verb, then advances native game ticks until flight ends (at most 240 ticks). No artificial hit-part or armor result is imposed. A new target is used for every shot, so death/downing can naturally affect later pellets from that shell.
- Raw CSV damage sums native `Thing.TakeDamage` results on the target pawn, including existing internal penetration. Corpse damage is excluded. Physical hits count unique pellets striking the pawn or its resulting corpse. Damaging pellets require positive actual pawn damage. Duplicate positive damage calls for the same pellet are separately recorded; these are an audit observation, not automatically proof of a bug.
- Down-or-dead and death are checked immediately after flight; these do not predict delayed bleeding or treatment outcomes. Supplementary `vital_injury` only flags remaining injury hediffs on Brain/Heart/Lung/Liver/Kidney and is not a clinical severity score.
- Warmup is the engine's actual `Verb.WarmupTime` getter; cooldown is native `AdjustedCooldown`, including mode/stat effects. Modeled DPS = average fresh-target damage / (warmup + cooldown). Trigger casts intentionally bypass waiting through those phases, so the DPS plot is a cycle-time estimate, not a sustained AI firefight or ammunition/reload simulation. Flight time is excluded from the repeated firing cycle.
- Error bars: approximate normal 95% confidence intervals for means, Wilson 95% intervals for rates. They describe random shot variation for this fixed shooter/target configuration; they are not population-wide balance guarantees.

`make_charts.py` checks case coverage, equal sample counts, nine launched pellets, hit/damaging counts and per-shell damage budgets before exporting mean damage, hit count, modeled DPS and outcome rates. PNGs are standalone Matplotlib artifacts. No product API boundary is introduced: the module uses engine primitives, .NET arithmetic and Waylith for host shutdown.
