# Tactical map analysis

`MapComponent_TacticalMapAnalysis` stores only static structural features and
basic door-and-wall guarding scores. It does not identify valuable rooms or equipment and does not
project pawn, turret, temperature, or trap danger across the map. There is no
separate killzone detector.

## Door and wall geometry

Each standable cell and door cell records any applicable `Door`, `Opening`,
`Corner`, `Corridor`, and `Junction` flags. It also records the cardinal
directions leading to traversable neighboring cells. Several flags or open
directions can coexist at one cell, so separate doors and approach axes remain
distinguishable instead of becoming only one combined score.

An opening is a short gap between opposite wall segments rather than a
continuing corridor. Doors and openings carry an exterior-access hint when
their opposite sides differ in room exterior status or roofing. This hint is
structural and does not indicate that an enemy is present. Open/closed door
state is not cached.

The existing route and guarding heuristics still use two basic scores:

- Door: 12 on a door cell, 8 on a cardinally adjacent cell.
- Wall/passage: 6 in a one-cell passage with walls on opposite sides, or 4 at
  a wall corner or wall-backed junction.

The sum is `TotalThreat`, a route/guarding heuristic rather than an estimated
hit probability. The grid is built on first tactical use and reused for the
life of the map. Tactical replanning and changing door states do not rebuild it.
Later construction or destruction may leave stale features until the developer
rebuild action is used; consumers can validate a particular structure at use time.

## Raid planning

The planner targets the nearest currently visible defender within 40 cells of
an available raider. With no visible defender, it advances toward a reachable
cell near the map center, or toward the near side of an enclosed perimeter.
It does not use a precomputed value map of the colony.
The center cell is only an advance point, not a secured objective that triggers
withdrawal. If no such cell is reachable, the plan reports why it cannot proceed.
The planner takes a snapshot of the current hostile pawns when it makes a plan.
If the objective is outdoors, it adds each hostile pawn's current position,
equipped primary verb range and minimum range, and line of sight to cells considered
for the entry, routes, staging, and withdrawal. Unarmed or melee pawns use a
short 2.9-cell range. These pawn scores are cached only within that planning
pass; they are not written to the map grid. Indoor plans use geometry alone.
HIGH doctrine also reads hostile and unowned traps during planning and excludes
their nearby cells from route and staging candidates. LOW doctrine does not
use this trap information. Neither doctrine caches a trap-threat grid.
Organized HIGH assault Lords also request full trap avoidance from the game's
path finder, so actual movement jobs follow the doctrine instead of only the
displayed plan excluding trap cells.

Direct and flank approaches use four-direction A* over standable cells, after
RimWorld reachability checks for entry and flank waypoints. Each route step costs
one cell plus a small door/wall geometry penalty; outdoor plans also apply the
current hostile-pawn snapshot. The planner compares the average score of the
actual candidate route cells plus a distance penalty,
then marks the most exposed point as the front/security anchor. Guard and support
positions are chosen near that anchor; entry staging stays away from the door.
The resulting path is shown in the raid plan developer overlay. Plan changes
are evaluated when the organization changes or the normal plan refresh expires.
Entry members follow sampled route waypoints before taking their stack-up
positions. A waypoint has a bounded timeout so an obstructed path does not
stall the raid. The last waypoint stays outside the final entrance; the group
then assembles before breaching or entering.
When a new plan is calculated, assembly and holding phases adopt its current
field threat and assignments. A changed maneuver or displaced objective restarts
the idle execution sequence; an active breach or support a  and its munitions have had time to land. If friendlies move into the target
area before impact, it recalls the airstrike when possible or cancels remaining
artillery volleys. Service capacity and ammunition are consumed by the
existing forward-base support system.

## Developer tools

Under `Helodrace/Tactical AI`, use `Draw door and wall geometry map` to flash
scored cells, `Inspect tactical data under mouse` for features and directions,
`Draw exterior doors and openings` to inspect perimeter candidates, and
`Open raid tactical plans` to inspect the selected route and assignments.
The raid plan shows a fixed BREACH marker when the objective lies beyond an
impassable foreign perimeter. Its report lists the chosen outside work cell
and inside crossing cell. Approach checkpoints are shared by the entry and
support groups; crossing waits for the selected opening and the entry group.

## In-game verification

Spawn an organized hostile assault in developer mode and open the raid tactical
plan window. Check that the execution phase advances from Assemble to Breach or
Support and then Assault/Complete; entry members should receive their movement
orders together while security members remain at their assigned positions.
Repeat with an enemy door and a power-cutter bearer, with an inventory grenade,
and with HIGH doctrine traps near an approach. Save and reload during assembly
or a breach, then confirm the phase continues instead of restarting. Down a
member or commander and confirm that the plan and execution assignments are
re-evaluated. These game-runtime checks are still required in addition to the
source build and logic tests.
Also test `EdgeWalkInGroups` with LOW sledgehammer carriers against both an
ordinary closed door and a continuous player wall. Check that every active
member first gathers at the same start, follows the same exterior checkpoints,
and that entry members cross the marked breach before moving to the objective.
