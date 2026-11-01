# Native preemptive-fire geometry checks

Build Main and the Main engine bridge, then run serially against actual RimWorld 1.7, Harmony 3 and mandatory Blancas:

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-preemptive -RuntimeRoot .codex-staging/migration17/preemptive-native17 -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/preemptive-native17 -TimeoutSeconds 45
```

Use a fresh runtime. An observation timeout leaves the game running; continue waiting on the same recorded PID/runtime. Do not launch another game until that process exits.

The frozen utility body comes from de5143c6 and retains game vectors/colors and the shared native scalar Mathf alias. Its Harmony patch classes and MapComponent are excluded, so it does not register duplicate product hooks. Only its isolated state and explicitly called methods run.

542 preview pairs cover three in-bounds origins (including edges and a nonzero input y), nine offsets (zero, cardinal, diagonal and long/outside-map), five controlled effective ranges, valid/invalid colors, a real equipped M16A4 verb or null, and null/out-of-bounds-map exits. The three line endpoints, widths/colors and destination ring center/radius/color are captured in call order. 96 active pairs cover eight directions, before/at/after-ready-tick colors, moved-origin rejection and wrong-map rejection. Constructed active states use the same frozen normalized direction on both sides; they do not claim to test Start by themselves.

24 additional pairs call each utility's actual Start with the same spawned, drafted colonist and equipped M16A4. Both execute the actual ordered Wait_Combat job, path stop, verb reset and stance paths. The test controls HasAccess, IsValidLine and EffectiveRange; it does not establish training/access, range/line-of-sight validation or targeting behavior. Actual resulting native direction, clamped length and all transient firing state fields match the frozen state, followed by paired DrawActive calls. Generated pawns/equipment are destroyed, isolated static states cleared and Harmony prefixes removed afterward.

Drawing is intercepted before submission. This is exact CPU geometry/state/call-order evidence, not rendered GPU frames, real target input, projectile flight/interception RNG, aim accuracy, shooting bursts or performance. The current game Projectile.Launch Harmony target retains UnityEngine.Vector3 explicitly; this is a host signature rather than internal state. No save keys/readers or firing rules change.

The first fixture used the nonexistent HD_M16A4 DefName and stopped during fixture construction. It was corrected to actual HD_Gun_M16A4_Weapon; no product behavior or comparison threshold was changed to resolve that failure. Failure evidence is retained.

Verified 2026-11-02: actual 1.7.4995 rev309 runtime native-preemptive-4995b passed seven engine checks. Total 662 paired calls (542 preview, 96 active, 24 Start plus active), 2,064 native-side submissions and 16,608 float components, all exactly equal (maximum difference zero). COMPLETE and process termination were observed; collected exit code is null. The only ERROR was the existing missing Workshop item. Product/Main/Blancas17 builds had zero warnings/errors. Product strings and the frozen utility match the baseline exactly (apart from the frozen namespace/import).

Product DLL SHA-256: 42BD91664BED70FD7F60C8CA538CD87568FD10DBA4E0F79DCCF08D39C29CB086. Raw PE geometry/color audit: 48 fields in 21 types, including XML/current host contracts and generated delegate fields, excluding locals/signatures. This is not a remaining API count or completion percentage. Main 0-3 remains in progress.
