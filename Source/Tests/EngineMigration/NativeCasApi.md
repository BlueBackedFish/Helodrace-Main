# Native CAS launch extension origin

HelodCasCombatBridge.ProjectileLaunchHandler and TryLaunchProjectile now take System.Numerics.Vector3. FireStrafeBurst passes its already-native origin directly. The actual vanilla Projectile.Launch call remains a game-vector output boundary. No old Main overload is retained; dispatch/null/refusal/exception policy is unchanged.

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-cas -RuntimeRoot .codex-staging/migration17/cas-api-FRESH -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/cas-api-FRESH -TimeoutSeconds 45
```

Actual 1.7.4995 rev309 `native-cas-api4995`: 17 PASS/COMPLETE, observed process exit, collected exit code null. Product/Main bridge builds have zero warnings/errors. Product SHA-256 `BB7D772ACDAB50E989F4FC96300D4F3E9EB0ED6B3634F9D943F3537388FC5EAF`. The only host ERROR is the known missing Workshop item 3781143823.

The frozen 97ab8f7b calculations retain game-vector math and marshal only their final extension argument in the test adapter. Capturing callbacks accept the actual native parameter. Existing checks compare 96 aircraft/attack/guidance/route profiles, 576 helper cases, 104,532 simulated flight ticks, 396 abort cases, 1,536 scatter/768 guidance/48 laser cases, 2,040 bursts/3,504 rounds/144 releases, 6,711 draws and 4,683,246 components with maximum difference zero. Submission counts are 6,438 meshes/4,304 lines; exact event/RNG checks remain.

New checks require native origin signatures and verify missing/refusing dispatch returns false. The refusing callback records observed forwarding independently of the dispatch result, including exact signed-zero/subnormal/NaN-payload bits, cell and map identity. Its synthetic null Def/caller probe does not spawn a projectile. Existing accepted real-def bursts verify actual Def/caller/origin/impact payloads. All ten aircraft phases and seven munition kinds still save/reload exact native field/reference witnesses before ticking, with explicit scalar vector nodes.

This is CPU state/math/request and selected save evidence. Mesh/line/sound/particle outputs and external launch are captured; no real CE launch/impact, complete vanilla fallback projectile execution, GPU appearance or performance is proven. The current CE sibling registers the earlier game-origin handler; its own 1.7 port must adopt this native signature, keep owned calculations native and marshal at its actual combat-engine boundary. This Main change does not claim CE 1.7 compatibility or change Blancas' published contracts.
