# TOW moving-platform integration

`Projectile_Tow` and `CompTowLauncher` share the same guidance controller for fixed launchers and vehicles. The source is a `ThingWithComps` with `CompTowLauncher`. Active missile registration, target-change commands, continuous steering, motor behavior, wire rendering, tree/roof-support obstruction checks and channel occupancy use the existing implementation.

A platform can implement `ITowGuidanceSource` on one of its comps:

- `CanGuideTow` reports whether its operator and launcher can currently guide.
- `TowWireOrigin` supplies its actual launcher outlet as `System.Numerics.Vector3` world coordinates in the 1.7 Main API. Import a game/VF muzzle value only where that engine actually returns it.

The main assembly does not reference Vehicle Framework. Arsenal's `CompBradley` implements this interface and its TOW turret supplies the outlet as the actual VF launch location. Fixed M220 launchers retain their standard turret-top endpoint. `LauncherPlatform` is saved as a source reference; no old building-only API or save-key alias is kept.

The earlier isolated Bradley Unity/VF map probe validated muzzle/ammunition, source registration, continued guidance, retargeting, wire endpoint, obstruction, gunner loss and channel/reload behavior on that earlier engine. It is historical evidence, not a 1.7 VF compatibility claim. The current Arsenal source/patches/tests still use the prior game-vector endpoint; its planned 1.7 port must update those consumers together and validate the pinned 1.7 VF runtime. Main does not retain the old interface shape.

Actual 1.7 Main endpoint/flight/save checks and their synthetic-provider limits are recorded in [NativeTowApi.md](../Source/Tests/EngineMigration/NativeTowApi.md). Actual VF/Lobos 1.7 build/runtime validation remains in the later compatibility stage.
