# TOW moving-platform integration

`Projectile_Tow` and `CompTowLauncher` share the same guidance controller for fixed launchers and vehicles. The source is a `ThingWithComps` with `CompTowLauncher`. Active missile registration, target-change commands, continuous steering, motor behavior, wire rendering, tree/roof-support obstruction checks and channel occupancy use the existing implementation.

A platform can implement `ITowGuidanceSource` on one of its comps:

- `CanGuideTow` reports whether its operator and launcher can currently guide.
- `TowWireOrigin` supplies its actual launcher outlet in world coordinates.

The main assembly does not reference Vehicle Framework. Arsenal's `CompBradley` implements this interface and its TOW turret supplies the outlet as the actual VF launch location. Fixed M220 launchers retain their standard turret-top endpoint. `LauncherPlatform` is saved as a source reference; no old building-only API or save-key alias is kept.

Validated in the isolated Bradley Unity/VF map probe: actual VF muzzle and ammunition consumption, vehicle source registration, guidance beyond the old building-only fallback interval, shared target-change gizmo and course correction, launcher wire endpoint, tree severing, loss of gunner, channel/reload interlocks and the existing M220 launcher. Main and Arsenal must be updated together.
