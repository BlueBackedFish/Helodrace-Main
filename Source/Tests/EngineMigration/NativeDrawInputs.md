# Native world-draw helper inputs

The renderer's own DrawRealtime/DrawAiming, Zaper's private DrawFlightWire and CQB's AdjustEquipmentDraw now take System.Numerics.Vector3. Only the actual DrawAt/DrawEquipmentAiming callbacks import game vectors. CQB exports the adjusted result in its Harmony prefix. TacticalMultiTargetFire's private marker uses Waylith.Math.Color and exports at GenDraw.DrawRadiusRing. The installed native/game green, red and yellow constant bodies have identical components, including yellow (1, .922, .016, 1). No legacy overload is retained.

Build product first, then the migration bridge; use the existing Run-Migration.ps1/Wait-Migration.ps1 procedures with fresh roots and serialize games. Actual 1.7.4995 rev309 results:

| Case / root below .codex-staging/migration17 | Result |
| --- | --- |
| native-wire-sniper / native-draw-inputs4995-wire | 7 PASS; 18 flight helper pairs, 576 wire pairs, 3,024 impact pairs, 325 trail pairs, 128 layers and 72 aim pairs. 18,722 compared components, maximum error 0, 4,179 RNG continuations. |
| native-tactical-geometry / native-draw-inputs4995-tactical | 7 PASS; 100 strike states including native helper output against the independent frozen game calculation. Existing focus/equipment/arc/slide checks retained; 17,795 components, maximum error 0. |
| native-geometry / native-draw-inputs4995-geometry | 37 PASS; existing marshalling/TRS, aiming/recoil, lights, icon and particle checks retained. Aiming uses its existing tolerance and does not record a maximum error. |

All reached COMPLETE and observed termination, collected exit codes null. Product and bridge builds have zero warnings/errors. The only ERROR in these logs is the pre-existing missing Workshop item 3781143823. The wire run preceded the unrelated CQB-only signature adjustment; tactical and geometry use the final product SHA-256 `28D52BBCE9D6248B1005AE7A237769E3915596F78B8178D8447FDD4BFC5AF5AB`.

These are existing controlled CPU/state/request comparisons, not full natural aiming, marker UI, GPU/resource lifecycle or performance tests. The marker change is a type/constant and explicit marshalling change, not a new end-to-end marker test. Frozen computations remain game-based; only their test input/output adapters change. See NativeWireSniper.md and NativeTacticalGeometry.md for their interception boundaries.

Matching-PDB report `native-boundary-audit-draw-inputs4995-final.json`: 10,937 methods, 10,876 bodies, 454,053 instructions, 4,161 Unity-containing calls, 156 geometry candidates, 977 field uses, 621 declaration candidates. These include current contracts and deferred resource/input work; counts are not a migration percentage or completion proof.
