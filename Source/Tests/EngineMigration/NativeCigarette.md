# Native cigarette-butt geometry checks

Build Main and the Main engine bridge, then run serially from the repository root with an actual RimWorld 1.7 installation and the required Blancas/Harmony versions:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-cigarette -RuntimeRoot .codex-staging/migration17/cigarette-native17 -NoWindow -Wait
```

Use a fresh runtime. A launcher observation timeout leaves the game running; poll the recorded PID and resume `Wait-Migration.ps1` on the same runtime rather than starting another game.

The frozen body comes from `336e112a`, with game vectors and the same current native scalar Mathf alias. The actual product and frozen class each run 300 profiles: three origins, five durations (including zero/negative), five ember durations, two arc heights and two spin rates. Every tick is compared through flight and ember expiry, including two ticks beyond the last expiry. State fields must have identical bits. The 9,780 paired DrawAt calls compare position, altitude, spin, ember offset, fade/lifetime, and identical cached Graphic instances for the chosen texture/size/color/shader. Null texture/fallback and absent-fire paths are included.

Graphic.Draw is intercepted before GPU submission. The actual GraphicDatabase is used, but this is CPU geometry/state/cache-selection evidence, not a rendered image, native resource lifetime or performance result. Existing disposal/landing rules and RNG are unchanged.

Four actual spawned objects represent initial flight, mid-flight, landed with ember, and fully expired flight/ember. Their product fields, nullable texture paths, signed-zero/subnormal/adjacent-float native offsets are saved in a real 1.7 game and checked bit-for-bit in LoadedGame before simulation ticks. No legacy Main save reader is added. The test component's phase key is not a product key.

Save-probe cells remove objects which the actual `GenSpawn.SpawningWipes` predicate says would wipe the filth when respawned. The first trial had a compressed ChunkSandstone at (130,0,127), short hash 13103, which wiped the last filth during map load; the original save and failure are retained. Its other three cells had zero compressed hashes. Trial b passed all four states; c added edifice cleanup. The final predicate covers chunks too. Product landing behavior was not changed to resolve a fixture placement conflict.

Artifacts include native comparison metrics, initial state JSON, loaded object diagnostics, pre-cleanup cell contents, compressible Def hashes/wipe predicates, game save, launcher hashes, checks and host log. This does not test pawn habit/landing selection, full ingestion jobs, natural filth cleanup or actual rendered frames.

Verified 2026-11-01: actual 1.7.4995 rev309 final runtime `native-cigarette-4995d` passed 12 checks, including four exact save restores before the first tick. Comparison: 300 profiles, 9,780 paired draw/tick steps, 57,040 float components, maximum error zero. COMPLETE and process termination were observed; the collected exit code is null. The only ERROR was the existing missing Workshop item. Product/Main/Blancas17 builds had zero warnings/errors. Product DLL SHA-256: `98249FCD5933382201F0BC66DF2C47F08CFBFC50FE07AECB2A6D9E4572F405ED`.
