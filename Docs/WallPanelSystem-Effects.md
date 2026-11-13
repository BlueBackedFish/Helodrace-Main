# Panel effects — tuning and engine contracts

## Material values

| Material | HP | Added resistance | Ignition multiplier | Flame multiplier | Ballistic behavior |
|---|---:|---:|---:|---:|---|
| Mineral wool | 100 | 3 | 0.10 | 0.35 | Sacrificial HP |
| Expanded foam | 60 | 5 | 1.50 | 1.00 | Sacrificial HP |
| RHA | 350 | 0 | 0.25 | 0.90 | Existing RHA material, 8 mm, incidence/penetration/ricochet |
| Aramid | 250 | 0 | 0.50 | 0.75 | Sacrificial HP, no ricochet |
| EMI lining | 80 | 0 | 0.25 | 0.90 | Sacrificial HP, room boundary shielding |

All values are initial game tuning. Mineral wool alone leaves 25% of the original wall-path transfer; foam leaves about 16.7%. Opposing mineral-wool panels leave about 14.3%. The sandwich wall's intrinsic resistance of 7 leaves 12.5% before added panels. Burning foam resistance follows its remaining core fraction; repairing HP does not restore consumed core.

The 220-HP sandwich wall retains its construction work of 33.75, one quarter of the base wall's 135. Its nominal geometry thickness is .18 versus an assumed .24 reference, not a measured engine wall dimension.

## Thermal path boundary

The actual `RoomTempTracker.RegenerateEqualizeCells` records cells two steps from the source room, across an intervening wall. `WallEqualizationTempChangePerInterval` samples those records. Path factors inspect only matching source-room cells, their intervening supports, exposed panel axes and the filled second cell on the engine's thick-wall fallback. If several wall paths share a destination, their factors are averaged. No unrelated room boundary or outdoor/roof heat exchange receives a room-wide multiplier.

The direct-air and outdoor-fallback reads are blended toward the unchanged source temperature by the path factor. The engine still performs its original temperature difference, vacuum factor, half-outdoor fallback, sampling and cell-count normalization. Closed doors additionally scale the native `GenTemperature.EqualizeTemperaturesThroughBuilding` rate; open doors do not.

## Damage boundary

`Bullet.Impact` establishes a balanced per-thread context before the native base impact destroys the projectile. The incoming segment is intersected with the host's actual collision cell to identify one face and its outward normal, with a deterministic corner tie. The first matching `Thing.TakeDamage` resolves the physical RHA layer once. Native logs, other original impact effects and damage worker execution remain in place. Additional nonphysical damage does not re-run the same RHA physical interaction.

Damage passes through reinforcement, shielding and insulation in order. Physically modeled RHA uses condition-adjusted thickness and existing per-core-class coefficients. Perforation updates residual damage and vanilla penetration; stopping suppresses the main hit. Reflection launches a slower, weaker live round from just outside the surface, with no second-generation projectile. Unsupported physical data do not receive guessed velocities, masses or ricochet rolls.

Fire obtains its source from native fire-spread/damage scopes or the bullet's struck face. Spread rolls the reduced chance once; direct ignition also applies the face factor without duplicating that spread roll. Unlocated ignition uses the maximum multiplier among exposed faces. Flame reduction wears protective layers, while burning foam remains a separate finite fuel state.

## Core and smoke

- Foam panel/resource: 1,800 burning ticks and a total pulse budget of 24 normalized density-cell units per item.
- Sandwich wall: 3,000 burning ticks and 72 density-cell units. It produces three times the total toxic smoke of a single foam panel; ordinary structural fire has no polymer-smoke source.
- Emission starts from the panel's face or the wall/item's exposed adjacent air, then walks connected air within Manhattan distance two. A pulse is shared across that set. The existing byte-density gas grid may cap concentrated output; the budget is not a claim of exact real smoke mass.
- Exhausted cores emit no more gas and become nonflammable. Remaining core is saved with Scribe. Stack splits copy the fraction; merges average it by transferred item count. Smoke emitted per item stack scales with count, preserving finite per-unit fuel.
- Polymer smoke dissipation 3, diffusion threshold 10, transfer divisor 2, effect threshold 8. Inhalation gain at full density is 0.00008 per game tick before existing exposure resistance. Toxic buildup receives one quarter of the resisted dose, further adjusted by toxic resistance.
- The inhalation hediff begins affecting breathing at severity .10, .35 and .65, with offsets -.10, -.25 and -.45. Recovery begins after 600 unexposed ticks at .00004 per tick.

These are game simulation parameters, not real chemistry or protective-equipment specifications.

## Local solar exception

The exact blackout call in `PowerNet.PowerNetTick` can ignore only the SolarFlare portion while evaluating a network containing a protected participant. Unprotected consumers cannot request or report power, their output is unavailable, and unprotected batteries cannot charge or discharge. Stored energy itself is retained and becomes available after the flare.

The engine's facility and automatic-power getters directly consult the map blackout, so their exact calls are replaced with parent-device checks. Each transpiler validates that precisely one native query was replaced. `GameConditionManager.ElectricityDisabled` itself is never patched: even an unrelated query nested inside a network callback retains the original result. No ambient power-exception state exists to leak or unwind. Every non-solar electricity-disabling condition remains effective. World radio/support calls and the native comms-console external communication checks are deliberately unchanged.

## Live verification still needed

The automated fixtures exercise actual engine heat/network callbacks and real grids, with direct fixture population instead of map generation. They do not establish full AI hauling/building behavior, a loaded playable colony's save lifecycle, mod-specific projectile adapters, GPU appearance, smoke/PPE behavior in play or colony-scale timing. Use the inspection data to check one-sided insulation, an open/closed lined door, a missing panel/roof tile, inside/outside power sources and a foam-lined room fire in an actual save before balancing for release.
