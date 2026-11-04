# Actual 1.7 Stage K checks

## T2-20 lineage redesign

Run `Start-Checks.ps1 -RuntimeRoot .codex-staging/Xenotypes/fresh-run -Xenotypes`, then collect the same native host with `Source/Tests/Wait-Game17Host.ps1`. `-GeneAudit` exports the actual loaded gene costs/effects without running the full lineage suite. `XenotypeChecks.cs` checks five actual heritable Helod lineages, endogene membership, no conflicts, native metabolic budgets/food factors, role aptitudes and supply requirements, the four native scenario slots, cold-field aiming boundaries, ally/radius/hostility/downed/human cohesion conditions, four native role distributions and 40,000 seeded selections, 25 parent combinations and an actual newborn generated from mixed inherited genes. Newborn generation allows downed babies as the native life-stage contract requires.

Birth requests with inherited genes and no explicit named xenotype start from an empty baseline; native generation otherwise applies an adult kind's xenotype before adding the inherited genes. Common sharpshooter combat values are preserved, while its archite cost is zero so native inheritance can carry it. The old skin-melanin entry was removed because Helod's own gene permission rules reject it. Evasion bonuses use native raw-score/postprocessing units, not an assumed flat final percentage. These issues and intermediate runs are retained under `.codex-staging/Xenotypes`. Current design/results: `Docs/T2_20_Xenotype_Redesign.md`.

Final `.codex-staging/Xenotypes/redesign-07`: **211 PASS, COMPLETE** (95 event/mood/gunslinger baseline + 116 lineage checks), no product errors or unresolved references. The existing missing Workshop folder message remains in the collected host log. Native generated lineage budgets/aptitudes and actual mixed newborn genes are retained in `Docs/Data/T2_20`. Conditional temperature input and other needs/interruptions are controlled; native gene application, stat workers/postprocessing, weighted selection, inheritance and newborn generation run normally. Full pregnancy/birth UI, long campaigns and combat DPS are outside this functional evidence.

Run `Start-Checks.ps1 -RuntimeRoot .codex-staging/StageK/fresh-name`, then collect the same PID with `Source/Tests/Wait-Game17Host.ps1` and a bounded timeout. This isolated host uses the actual installed 1.7 game, Main, Blancas and Harmony.

`Checks.cs` covers the unchanged native crisis worker, isolated pawn-kind replacement, untouched original/creep-joiner definitions and four real worker-generated rescuers with functional medical/violent work, medicine, BTX supplies and the selected existing lineage. `MoodChecks.cs` is the independent cigarette extension: nicotine absence, active +3, no duplicate rush and deactivation on removal. Its actor must be spawned and the test rush must have severity 1, as ordinary ingestion supplies.

2026-11-03 `.codex-staging/StageK/events-mood-03`: 35 PASS, COMPLETE. Early failed fixtures were corrected for the race request's random lineage fallback and native thought preconditions; the final run is the evidence. It does not claim an end-to-end crisis scheduler playthrough or a full smoking animation/tolerance soak.

2026-11-04 `.codex-staging/StageKFollowup/cowboy-03`: **95 PASS, COMPLETE**. `CowboyChecks.cs` adds four real native-generated rescuers' SAA, exact four-piece western outfit and individual colors, dedicated adult backstory/forced training trait, ×0.75 aiming and ×0.5 ranged cooldown versus the same pawn without that trait, and the actual SAA adjusted cooldown calculation. No extra command switch is needed. The native apparel generator needs `apparelRequired` for fixed garments; `specificApparelRequirements` supplies material/color, with faction material restrictions bypassed for this kind only. Early failed runs are retained.

Later 2026-11-04 gunslinger tuning changes those current trait expectations to **×0.8 warmup / ×0.85 cooldown**, display names **gunslinger / 총잡이**. Run `Start-Checks.ps1 -RuntimeRoot .codex-staging/Gunslinger/fresh-run -Pistols` and collect the same host with `Wait-Game17Host.ps1`. Final `.codex-staging/Gunslinger/pistols-02`: **226 PASS, COMPLETE**, explicitly confirming 80% warmup and 85% cooldown on all four generated rescuers. `PistolChecks.cs` exercises ten real pistols (including modular M1911/P320, core pistols and the optional Odyssey revolver), production mode selection, unchanged normal range, mode range, current build multiplier, fanning cooldown and trait stacking, native inside/outside range boundaries, actual completed firing beyond the previous mode limit and restoration of normal fire. Climate, clear map/weather, needs and interruptions are controlled; hit-rate/DPS and third-party pistol coverage are not claimed. Reproduction and profile table: `Docs/Gunslinger_Pistol_Balance.md`.

## Shooting recreation

Current 2026-11-04 revision: **1x1 launcher, automatic targets, no refueling component/consumable/crafting recipe**, 256px four-view textures with the same 12px outline. `.codex-staging/StageKFollowup/recreation-02` passed **123 checks, COMPLETE**, including the cowboy/event/mood baseline, unfilled clay shooting, hunting outcomes and actual native radio conversation. The early `recreation-01` fixture omitted the base native giver's PowerOn flag; corrected without changing product behavior.

Current save/load evidence: `.codex-staging/StageKFollowup/state-01`, **16 PASS, COMPLETE**. Both native reloads preserved the active job and target/projectile/tracking state; the resumed clay job launched additional targets without refilling. Shipping DLL matches both current hosts. Collected logs retain the preexisting missing Workshop folder message; no recreation mismatch, unresolved saved references or product exception occurred.

```powershell
& Source/Tests/StageK/Start-Checks.ps1 -RuntimeRoot .codex-staging/StageK/fresh-recreation -Recreation
& Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot .codex-staging/StageK/fresh-recreation -TimeoutSeconds 45
& Source/Tests/StageK/Start-Checks.ps1 -RuntimeRoot .codex-staging/StageK/fresh-state -State
& Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot .codex-staging/StageK/fresh-state -TimeoutSeconds 45
```

Do not launch the next host until the previous PID exits and its log is collected. A bounded observation timeout leaves that same host alive; inspect it instead of launching another. Evidence is in each runtime's `Evidence/checks.txt` and collected `Evidence/engine.log`.

`RecreationChecks.cs` advances actual native map/job/stance/projectile ticks. It tests loaded hunting classification, assault-rifle rejection, unfilled 1x1 trap eligibility, a pawn blocking the lane, the native clay joy giver, actual moving-target hits, real shooting records and automatic free targets, safe projectile flags, at least five game hours of hunting preparation, first-cartridge miss termination and real kill/success reward. Shotgun spread and aim deviation are controlled only for the two hunting outcome branches; this is functional coverage, not a natural hit-rate or weapon-balance experiment. Climate, other needs, animal movement and random interruptions are controlled. The native firearm, projectile damage and job execution run normally.

Historical 2x1/refueling version, 2026-11-03 `.codex-staging/StageK/recreation-06`: **57 PASS, COMPLETE**, including the original 35 event/mood checks. Early fixtures were corrected for nonexistent native `Shooting` JoyKind, the current building property contract, `DebugSettings.noAnimals` despawning test deer, native cast-position arguments and pooled Job references. Completion observes the driver's finish action rather than a pooled Job object's identity. The final product defines its own target-shooting JoyKind and uses the current native API.

`StateChecks.cs` saves and actually reloads twice: first with a flying target and pending projectile references, then during the long hunting tracking stage. It checks round ownership, target location, shot count, no-refill configuration, subsequent free launches, job continuation, remaining tracking time and no outcome bonus on interrupted preparation. It retains test colonists in world-pawn storage so native social references remain deep-saved. The current fixture observes subsequent free target launches and checks that cartridges are not duplicated across reload. Earlier stock/consumption checks applied to the old 2x1 refueling design. Early `state-01/02` results are not the final evidence.

Historical 2x1/refueling version, 2026-11-03 `.codex-staging/StageK/state-03`: **16 PASS, COMPLETE**, no unresolved/deep-save reference warnings. Seven observed post-load target launches consumed exactly seven targets. Both native reloads preserved active jobs. Its collected log retains the local missing-Workshop-folder message and a native hive-Lord warning caused by moving the quicktest map's insects into world-pawn storage; this is not a clean-whole-engine-log claim. No product recreation exception was logged. That historical shipped DLL SHA-256 equaled both runs: `0270C1FC5CB77A362230AEE0E136CA04C823956353C20D0B37ABB8FED69A006D`.

The Blender pipeline is independently checked by `python ArtSource/ClayShooting/verify.py`; recipe references by `Source/Verify-RecipePlacement.ps1` (86 explicit recipes, 535 concrete things after removal of the obsolete clay consumable and recipe). The native runs do not constitute in-game screenshot approval, long-term joy-selection balance, every hunting weapon's animation coverage, or all third-party ammunition compatibility.

The current recreation run also executes `RadioChatChecks.cs`: a native Social conversation at a Television listening building, with native job/joy ticks and observation of the exact prior `Log.ErrorOnce` message. Power availability is controlled for this one fixture radio, daily rumor rolls are suppressed, and the native giver, conversation job and JoyUtility remain real. Existing standalone Radio checks cover power loss and quality multipliers.


`-TextureItems` runs `TextureItemChecks.cs` independently of the earlier K-group checks. It uses the actual 1.7 power grid, work givers, jobs and physical recipe inputs/outputs for slate alumina, sandstone silicon, alloy, electronics and both new equipment recipes (plus ceramic armor consumption). It resolves inventory/projectile/worn-direction graphics and exercises directional melee blocking. Native Projectile.Launch observations check actual M2HB/CROWS belt order and refuel consumption. The CROWS fixture installs its weapon with physical materials and runs the real remote operation job. This is functional coverage, not a natural accuracy/balance experiment or visual screenshot approval.

```powershell
& Source/Tests/StageK/Start-Checks.ps1 -RuntimeRoot .codex-staging/TextureItems/fresh -TextureItems
& Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot .codex-staging/TextureItems/fresh -TimeoutSeconds 45
```


`-Minerals` runs the material portion of `TextureItemChecks.cs`: both slate/sandstone block and chunk routes, VIM-only silicon/alloy recipes and matching research/work giver, exact alumina/plasteel alloy consumption, downstream semiconductor production, and the new silicon stack graphics. It exits before the equipment/combat checks, which are unaffected by this follow-up. The full `-TextureItems` mode also uses these updated recipes.


`-Fibers` runs the common powered workshop fixture followed by `FiberChecks.cs`. It checks detached/adjacent facility states, both power and flick switches, active demand in both machines, an interrupted real nylon unfinished batch and resumption without double consumption, exact material/output counts for both recipes, native Fabric-stuff eligibility, graphics and reactor removal/reinstallation. Example: `Start-Checks.ps1 -RuntimeRoot .codex-staging/FiberSpinning/fresh -Fibers`.


`-ArmorMaterials` uses `ArmorMaterialChecks.cs` to compare actual stack main textures with Core Cloth, inspect the explicitly applied mask and color, audit 19 generated equipment bills and their default ingredient allowances, preserve ceramic/UHMWPE identities, and craft representative nylon armor, soft armor, a soft protector, a pouch, ceramic plates and the UHMWPE set. It controls needs/weather and uses the existing native bill fixture.
