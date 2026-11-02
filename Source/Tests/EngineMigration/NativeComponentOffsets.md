# Native direct component adjustments

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-component-offsets -RuntimeRoot .codex-staging/migration17/native-component-offsets-4995 -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/native-component-offsets-4995 -TimeoutSeconds 45
```

Use a fresh root for a new run. Keep observing the same process after a timeout and collect completion once; inspect its engine log too.

Frozen baseline `c63db7e1`: head/baby-bed, Ponpeco hair, unspecified utility and weapon-height patch classes retain their old calculation bodies, with Harmony annotations removed. The unchanged internal utility membership helper is reached by reflection. The two canister fixture comps retain their ignition/burning bodies and share the same real parent. No auto-registered component or runtime product facade is added.

Actual 1.7.4995 rev309 `native-component-offsets-4995`: seven migration checks, COMPLETE and observed process termination, collected exit code null. Product/Main/Blancas17 bridge builds have no warnings/errors.

- Head: 8,640 cases over actual/null/empty/unsorted/tied settings, adjacent age floats, gender, four body-size factors, real Helod/human/null pawn inputs and eight coordinate inputs. Includes signed zero, subnormal, large finite values and NaN payload/infinity in unaffected components. Controlled age reads must bind to both copies in every Helod pair; 5,760 age/settings and 1,024 life-stage reads are observed. Both absolute and relative adjustments execute.
- Baby bed: 576 cases over all current posture enum values, baby/child/adult, Helod/human/null and the same inputs. This isolates the existing bed/posture/stage rule; no natural growth or sleeping job is claimed.
- Hair: 1,440 cases across actual race inputs, hair/non-hair/null nodes, matching/case-mismatched/nonmatching/null hair Def names, facings and coordinates. Utility: 288 cases using the actual cached vanilla-utility membership and rejected pants/null nodes, race and facing. Height: 40 cases using actual Main/vanilla ranged weapons, melee, item and null, with actual modContentPack ownership.
- Canisters: 32 pairs over both kinds, ignition/burning, four origins including edges/outside and two seeds. Execute real FleckMaker helpers with controlled visibility, intercept particle creation, compare all 80 helper requests and 72 FleckCreationData records. Both copies reach controlled parent.DrawPos once, 64 reads in total. Every pair preserves following Rand.Int and Rand.Value bits under balanced PushState/PopState.

All 34,064 component-bit comparisons match, including 28,572 finite component comparisons. The GUI patch inputs/outputs remain current game vectors; only owned adjustment arithmetic is native. Settings/life-stage/age/stage are supplied through controlled getters, and render nodes/hair Defs are synthetic and unregistered. Shared Defs/settings are not changed; temporary genders/hair/fixtures/patches are restored or cleaned up. No actual render-tree setup, natural age/sleep transition, item equipping/jobs, canister deployment/gas propagation, GPU/resource lifecycle, whole-world RNG or performance claim is made.

The final log's only ERROR is existing missing Workshop item 3781143823; native allocation warnings remain. Product SHA-256 `66D9E3D76DC19989ADB5486902A1A3BCD95F1F714226D689B5CA667A78225398`. Metrics: `Evidence/native-component-offsets.json`. Matching-PDB audit `native-boundary-audit-component-offsets4995.json` leaves only the three previously reviewed game-vector field writes that assign native results/restore host height at current callbacks. Other color/Rect/declaration audits and Blancas B2/R7 gates remain unfinished.
