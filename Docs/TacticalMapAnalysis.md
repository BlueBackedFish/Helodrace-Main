# Tactical map analysis

`MapComponent_TacticalMapAnalysis` is the shared perception layer for advanced
settlement-assault AI. It owns two cell grids and does not issue pawn jobs by itself.

## Strategic value map

Strategic scores describe the military effect of capturing or disabling a target;
`MarketValue` is never read. The initial target classes are bedrooms/barracks,
stockpiles, generators, batteries, communications, production, research, medical
rooms, and defensive installations. Each target has a center, bounds, category,
score, room ID, and optional source building. A soft value halo lets an insertion
planner compare nearby landing cells without treating open ground as an objective.

Use `BestObjective()` for a final assault objective or inspect `StrategicTargets`
when a caller needs to impose mission-specific priorities.

## Threat map

Threat is stored as separate ranged, melee, thermal/fire, trap, and chokepoint
channels. `TotalThreat` is their additive default, but callers can weight individual
channels for their squad composition.

The analyzer is demand-driven. With no AI request and no developer POI overlay it
does no periodic scan at all. A query keeps it active temporarily; while active,
static topology refreshes every 3,600 ticks and dynamic state every 900 ticks. Room
objects are cached by the static pass, temperature is evaluated once per room, only
the 24 highest-priority mobile defenders project threat, and stationary turret LOS
footprints are reused until the next static pass. Tight pathfinding loops use cached
cell reads instead of refreshing the analyzer per expanded node.

`LastStaticBuildMilliseconds`, `LastDynamicBuildMilliseconds`,
`LastLineOfSightChecks`, and `LastMobileDefendersScored` expose the cost of the last
pass. The rebuild/summarize developer action writes these values to the log.

Threat is faction-relative when the caller supplies an attacking faction. Passing
no faction treats player-owned pawns and turrets as the defenders, which is useful
for ordinary hostile raids.

## Killzone recognition

A room can carry more than one `KillzoneKind` because real layouts are hybrids.

- `Temperature`: enclosed, controllable room with live heat/fire or fuel plus an
  ignition source.
- `Barrel`: a long, narrow firing lane with chokepoints and ranged coverage.
- `TShaped`: three arms of at least three cells around a junction.
- `Diagonal`: repeated diagonal corner gates covered by ranged fire.
- `Melee`: compact one/two-exit choke suitable for body blocking.
- `Shooting`: low-cover room under substantial overlapping ranged fire.

Barrel and shooting killzones also have a room-independent spatial pass. It flood
fills contiguous exposed cells directly from the ranged-threat grid, so outdoor
lanes, roofless courtyards, and firing areas split across several `Room` objects are
still recognized. Spatial records retain their exact cells and use `RoomId = -1`.

Every ranged-threat contribution also accumulates a direction toward its shooter.
Cells and killzones expose the normalized dominant fire direction and a 0-1
directionality value. A high value means that most fire arrives along one axis and
therefore a perpendicular flank is materially safer; a low value means overlapping
fire from several directions.

Each detection records confidence and expected threat. These are heuristic signals,
not hard labels, so a decision layer should consider all flags and its available
weapons.

## Tactical POIs and developer overlay

`PointsOfInterest` merges judgments by room: a bedroom containing a generator and
also recognized as a killzone appears as one POI with all three facts. Exterior
installations remain separate site POIs. Each POI exposes its exact covered cells,
room role, strategic categories and summed value, killzone flags, confidence,
expected threat, and contributing building labels.

Developer mode adds these actions under `Helodrace/Tactical AI`:

- `Toggle tactical POI overlay`: master switch for persistent map markers and labels.
- `Toggle strategic room POIs`: filter strategic room/site markers.
- `Toggle killzone POIs`: filter killzone markers.
- `Inspect tactical data under mouse`: changes the cursor into a map inspection tool
  and writes the selected POI and all threat channels to the log.

With the overlay enabled, hovering any cell belonging to a POI opens a detailed
information panel. Cyan markers are strategic, red markers are killzones, and
magenta markers contain both judgments. Toggle state is developer-session state and
is intentionally not written into player saves.

## Decision APIs

- `TryFindAirInsertionCell()` scores valid cells near a strategic objective while
  penalizing local threat.
- `AssessApproach()` compares a supplied route with a caller-calculated breach cost,
  records every crossed killzone type, and returns advance/breach/avoid/air-insert.
- `At()`, `ThreatAt()`, and `StrategicValueAt()` are the low-level path-cost APIs.

## Raid tactical planning and developer view

The previous heliborne/sabotage path tester has been removed. The raid planner now
reads a spawned `CombatOrganization`, its surviving members and command state,
their actual grenade and breach equipment, and the tactical map analysis. It
chooses a strategic objective, measures direct and flank exposure, and ranks the
three best currently feasible maneuvers. Baseline choices remain available when
the force has no special equipment. Reduced command efficiency can select the
second or third choice.
When no strategic site exists, the planner uses the defending force's position as
its field objective.

LOW doctrine can plan a lethal grenade before an enclosed entry unless friendly
members are inside. HIGH doctrine favors identified nonlethal support and can
choose trap reconnaissance before advancing. Both can consider smoke, flanking,
defending, or regrouping. The plan assigns entry order (the available commander
is second), security and fire-support positions, and withdrawal positions for
critically wounded members. Staging cells stay at least three cells from the
entry and are checked for reachability; HIGH doctrine rejects detected trap cells.
LOW doctrine does not use trap information when it compares approaches.
The approach lines are tactical waypoints, not a complete movement path.
LOW doctrine adds coordination time when members are separated or blocked by walls;
HIGH doctrine uses a shorter radio-supported allowance.

`MapComponent_RaidTacticalPlans` refreshes plans after changes to surviving
members, commander, or grenade inventory, and otherwise at most every 900 ticks.
Plans are runtime data and are rebuilt after loading a save. Open
`Helodrace/Tactical AI > Open raid tactical plans` to inspect active organizations,
ranked scores, assigned positions, and map nodes, or force immediate evaluation.
The planner only produces decisions and visualizations; issuing movement, throw,
breach, or external-support jobs belongs to a separate execution layer.

Developer-mode actions under `Helodrace/Tactical AI` rebuild/summarize the maps,
draw the 3,000 highest-value or highest-threat cells, and inspect all channels
beneath the mouse cursor.
