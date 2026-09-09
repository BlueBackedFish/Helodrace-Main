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

## Assault planner and test UI

`TacticalAssaultPlanner.MakePlan()` builds either a heliborne or sabotage plan.
Heliborne planning selects a valid MH-60 hover/rope pair near a high-value objective.
Sabotage planning ranks map-edge ingress points, tests the six best candidates, and
prefers power generation, batteries, communications, and defenses as objectives.

The A* route cost combines movement, threat exposure, doors, and selected breach
equipment. Impassable cells are accepted only when the existing
`BreachExplosiveUtility.IsValidWall()` or
`CompPowerCutterBreach.IsValidBreachTarget()` accepts the structure. Explosive
steps use the installed-charge C-4 requirement and work settings; cutter steps use
the cutter's non-player HP curve model. The result records the selected attack
point, insertion/hover cells, objective, complete route, every breach and method,
total C-4, crossed killzones, cost, threat exposure, and expanded-node count.

After routing, the entry evaluator distinguishes direct entry, a single flank
breach, simultaneous explosive flanks, a smoke-covered assault, and avoidance. For
a strongly directional killzone it searches hostile walls whose breach normal is
roughly perpendicular to the firing axis, ranks them by local threat and objective
distance, and can nominate two or three separated C-4 points for simultaneous
detonation. The report includes their combined C-4 requirement and reduced exposure
estimate; this is planning data and does not itself issue breach jobs.

The evaluator also counts defenders in the first breached room (falling back to the
objective area) and scores their weapons and combat skills. It can suggest and then
select from available support:

- M111 offensive grenade (`HD_Grenade_M111_Item`) for a dense, high-threat compact
  room when destroying a high-value objective is not a concern.
- M84 flashbang (`HD_Grenade_M84_Item`) for close rooms or valuable objectives.
- M7A2 CS grenade (`HD_Grenade_M7A2_Item`) for multiple defenders in a room with
  enough volume for gas employment.
- M8 smoke grenade (`HD_Grenade_M8_Item`) to force a ranged barrel/shooting killzone.
  Smoke is not treated as mitigation when temperature or melee killzone traits are
  also crossed.

Open `Helodrace/Tactical AI > Open assault path tester` to use the standalone test
window. It provides:

- Heliborne or Sabotage mode.
- Independent checkboxes for explosive charges and power cutters.
- Independent availability checkboxes for offensive, flashbang, CS, and smoke
  grenades.
- Automatic or map-picked insertion and objective cells.
- Calculation timing and detailed breach/path report.
- Persistent route overlay: cyan route, green insertion, yellow attack point,
  magenta objective, red C-4 breaches, amber cutter breaches, separated synchronized
  charge rings, and the dominant incoming-fire axis.

Developer-mode actions under `Helodrace/Tactical AI` rebuild/summarize the maps,
draw the 3,000 highest-value or highest-threat cells, and inspect all channels
beneath the mouse cursor.
