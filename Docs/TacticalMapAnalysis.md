# Tactical map analysis

`MapComponent_TacticalMapAnalysis` stores strategic objectives and a small, static
geometry score. It does not project pawn, turret, temperature, or trap danger across
the map.

## Strategic objectives

Bedrooms, stockpiles, power, communications, production, research, medical rooms,
and defensive installations receive strategic value. This remains separate from
geometry. `BestObjective()` weighs target value, nearby geometry, and distance.

## Door and wall geometry

Each standable cell and door cell receives only two scores:

- Door: 12 on a door cell, 8 on a cardinally adjacent cell.
- Wall/passage: 6 in a one-cell passage with walls on opposite sides, or 4 at
  a wall corner.

The sum is `TotalThreat`, a route/guarding heuristic rather than an estimated
hit probability. The grid is rebuilt on demand and, while active, at most every
3,600 game ticks. Changes to walls or doors may therefore take up to that interval
to appear unless the developer rebuild action is used.

## Raid planning

The planner takes a snapshot of the current hostile pawns when it makes a plan.
If the objective is outdoors, it adds each hostile pawn's current position,
primary weapon range and minimum range, and line of sight to cells considered
for the entry, routes, staging, and withdrawal. Unarmed or melee pawns use a
short 2.9-cell range. These pawn scores are cached only within that planning
pass; they are not written to the map grid. Indoor plans use geometry alone.

Direct and flank approaches use four-direction A* over standable cells, after
RimWorld reachability checks for entry and flank waypoints. The planner compares
the average score of the actual candidate route cells plus a distance penalty,
then marks the most exposed point as the front/security anchor. Guard and support
positions are chosen near that anchor; entry staging stays away from the door.
The resulting path is shown in the raid plan developer overlay. The planner
still produces plans and display data only; it does not issue movement jobs.
Plan changes are evaluated when the organization changes or the normal plan
refresh expires.

## Developer tools

Under `Helodrace/Tactical AI`, use `Draw door and wall geometry map` to flash
scored cells, `Inspect tactical data under mouse` for the door/wall components,
and `Open raid tactical plans` to inspect the selected route and assignments.
The tactical POI overlay shows strategic sites.
