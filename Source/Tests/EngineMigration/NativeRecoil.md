# Native modular recoil extension contract

Main is unreleased and 1.7-only. ModularWeaponRecoilResolver and the single public ModularWeaponRecoilUtility.Resolve now output System.Numerics.Vector3. The game-output Resolve wrapper and internal ResolveNative entry point are removed. The aiming pose and assembly renderer call the same native API; an accepted external value is assigned directly. Only the actual EquipmentUtility.Recoil call outputs a game vector, imported once before the existing gas multiplier. Resolver rejection/reset, exception handling and fallback policy are unchanged.

```powershell
dotnet build Source/Helodrace/Helodrace.csproj -p:IntermediateOutputPath=obj/Migration17/ -v:minimal
dotnet build Source/Tests/EngineMigration/EngineMigration.csproj -v:minimal
Source/Tests/EngineMigration/Run-Migration.ps1 -Case native-geometry -RuntimeRoot .codex-staging/migration17/recoil-native-FRESH -NoWindow
Source/Tests/EngineMigration/Wait-Migration.ps1 -RuntimeRoot .codex-staging/migration17/recoil-native-FRESH -TimeoutSeconds 45
```

Actual 1.7.4995 rev309 `native-recoil-api4995`: 37 PASS/COMPLETE, observed process exit, collected exit code null. Product/Main bridge builds: zero warnings/errors. Product SHA-256 `E6912680937DDC33680592D89AE139304DE99E0E52C7D5E2F0FE428B6FE9E649`. The only host ERROR is the known missing Workshop item 3781143823.

The existing frozen f2d09c7e aiming formula compares 12,348 components, 3,024 flip/recoil conditions and 12 cache age/angular boundaries. It covers external native resolver acceptance, refusal output reset and null-equipment fallback; reflection requires a native delegate output, one Resolve method and no ResolveNative wrapper. Numerical checks retain their existing 2e-5 relative tolerance; this fixture does not report a maximum aiming error. Other existing geometry/weapon/light/icon/particle checks also pass, including 13,284 marshalling, 25,272 transform and 3,200 matrix components.

This is API/CPU calculation evidence with synthetic resolver outputs and the existing frozen formula. It does not prove real CE integration, all vanilla verb recoil behavior, GPU appearance or performance. The actual EquipmentUtility boundary is unchanged. Existing settings, cache thresholds and gameplay math were not retuned.

## Extension consumers

A 1.7 combat adapter must implement `bool Resolve(Thing, float, out System.Numerics.Vector3, out float)`. If its combat engine still returns UnityEngine.Vector3, the adapter imports the three components at that actual engine call, rather than asking Main to roundtrip its internal value. For the currently inspected CE_Utility.Recoil signature, the adapter's output conversion belongs immediately after that call:

```csharp
CE_Utility.Recoil(equipment.def, verb, out UnityEngine.Vector3 gameOffset,
    out angleOffset, aimAngle, true);
drawOffset = new System.Numerics.Vector3(gameOffset.x, gameOffset.y, gameOffset.z);
```

The CE sibling currently registers the prior game-vector delegate. It still needs its own 1.7 port/build/runtime validation, which this Main unit does not claim. No old Main overload is retained to support that earlier contract. This is separate from Blancas' preserved published save/API policy.
