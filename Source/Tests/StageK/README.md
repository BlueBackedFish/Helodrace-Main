# Actual 1.7 Stage K checks

Run `Start-Checks.ps1 -RuntimeRoot .codex-staging/StageK/fresh-name`, then collect the same PID with `Source/Tests/Wait-Game17Host.ps1` and a bounded timeout. This isolated host uses the actual installed 1.7 game, Main, Blancas and Harmony.

`Checks.cs` covers the unchanged native crisis worker, isolated pawn-kind replacement, untouched original/creep-joiner definitions and four real worker-generated rescuers with functional medical/violent work, medicine, BTX supplies and the selected existing lineage. `MoodChecks.cs` is the independent cigarette extension: nicotine absence, active +3, no duplicate rush and deactivation on removal. Its actor must be spawned and the test rush must have severity 1, as ordinary ingestion supplies.

2026-11-03 `.codex-staging/StageK/events-mood-03`: 35 PASS, COMPLETE. Early failed fixtures were corrected for the race request's random lineage fallback and native thought preconditions; the final run is the evidence. It does not claim an end-to-end crisis scheduler playthrough or a full smoking animation/tolerance soak.

## Shooting recreation

```powershell
& Source/Tests/StageK/Start-Checks.ps1 -RuntimeRoot .codex-staging/StageK/fresh-recreation -Recreation
& Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot .codex-staging/StageK/fresh-recreation -TimeoutSeconds 45
& Source/Tests/StageK/Start-Checks.ps1 -RuntimeRoot .codex-staging/StageK/fresh-state -State
& Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot .codex-staging/StageK/fresh-state -TimeoutSeconds 45
```

Do not launch the next host until the previous PID exits and its log is collected. A bounded observation timeout leaves that same host alive; inspect it instead of launching another. Evidence is in each runtime's `Evidence/checks.txt` and collected `Evidence/engine.log`.

`RecreationChecks.cs` advances actual native map/job/stance/projectile ticks. It tests loaded hunting classification, assault-rifle rejection, empty/loaded trap eligibility, a pawn blocking the lane, the native clay joy giver, actual moving-target hits, real shooting records and clay consumption, safe projectile flags, at least five game hours of hunting preparation, first-cartridge miss termination and real kill/success reward. Shotgun spread and aim deviation are controlled only for the two hunting outcome branches; this is functional coverage, not a natural hit-rate or weapon-balance experiment. Climate, other needs, animal movement and random interruptions are controlled. The native firearm, projectile damage and job execution run normally.

2026-11-03 `.codex-staging/StageK/recreation-06`: **57 PASS, COMPLETE**, including the original 35 event/mood checks. Early fixtures were corrected for nonexistent native `Shooting` JoyKind, the current building property contract, `DebugSettings.noAnimals` despawning test deer, native cast-position arguments and pooled Job references. Completion observes the driver's finish action rather than a pooled Job object's identity. The final product defines its own target-shooting JoyKind and uses the current native API.

`StateChecks.cs` saves and actually reloads twice: first with a flying target and pending projectile references, then during the long hunting tracking stage. It checks round ownership, target location, shot count, target stock, subsequent launch consumption, job continuation, remaining tracking time and no outcome bonus on interrupted preparation. It retains test colonists in world-pawn storage so native social references remain deep-saved. Target stock is compared with observed target **launches**: a session can end before shooting its final launched target, so targets and cartridge counts need not be equal. Early `state-01/02` results are not the final evidence.

2026-11-03 `.codex-staging/StageK/state-03`: **16 PASS, COMPLETE**, no unresolved/deep-save reference warnings. Seven observed post-load target launches consumed exactly seven targets. Both native reloads preserved active jobs. Its collected log retains the local missing-Workshop-folder message and a native hive-Lord warning caused by moving the quicktest map's insects into world-pawn storage; this is not a clean-whole-engine-log claim. No product recreation exception was logged. The shipped DLL SHA-256 equals both final native runs: `0270C1FC5CB77A362230AEE0E136CA04C823956353C20D0B37ABB8FED69A006D`.

The Blender pipeline is independently checked by `python ArtSource/ClayShooting/verify.py`; recipe references by `Source/Verify-RecipePlacement.ps1` (87 explicit recipes, 536 concrete things). The native runs do not constitute in-game screenshot approval, long-term joy-selection balance, every hunting weapon's animation coverage, or all third-party ammunition compatibility.
