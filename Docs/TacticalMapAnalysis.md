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
the idle execution sequence; an active breach or support action finishes first.

## Raid execution

`MapComponent_RaidTacticalExecution` acts on organized hostile pawns in an
assault-colony Lord. It moves entry and security members to their assigned
positions, waits for the group or a bounded assembly timeout, then attempts a
power-cutter or C4 breach and a selected inventory grenade before issuing entry moves
to all available entry members in the same tick. A flank plan first moves its
entry members around the flank waypoint. Hold/regroup plans maintain
their assigned positions. Once assault moves are issued, normal Lord combat
behavior resumes. Replanning after casualties, commander succession, or a
large objective shift restarts coordination. Execution phase and assignments
appear in the developer plan window. Phase state is saved with the map.
The Great War faction permits vanilla sapper raids using riflemen as eligible
sappers. While its Lord is in the sapper or breaching toil, the tactical executor
leaves those duties alone; after that toil ends, ordinary tactical coordination
can take over.
During the sapper toil, three nearby members of the sapper's smallest combat
group occupy reachable front-flank and rear cells around the sapper. Members
already facing a visible nearby enemy keep fighting; other raiders retain their
vanilla duties. Escort positions remain on the sapper's side of a wall.
During a hold, designated response members move toward a current enemy near
the group at bounded intervals when that enemy is outside effective firing
range; fire-support and security members keep their assigned positions.
After an outdoor entry has had time to reach the objective, an isolated entry
member rejoins the nearest member of its smallest combat group. Movement toward
the objective and a nearby visible fight take priority over this regrouping.
An available sniper group takes a separate sightline within weapon range of
the nearest current enemy. Its companion stays near the sniper and does not
get pulled into the response group. If no reachable sightline exists, both
fall back to ordinary holding positions.
The planned entry delay is honored after the support action, allowing smoke or
grenade effects to take hold before the simultaneous entry order.
After all assigned members assemble, HIGH waits its short radio coordination
interval; LOW waits longer when members began separated or out of sight. The
assembly timeout includes that interval, and readiness resets if a member
moves away before coordination finishes.
After an indoor entry reaches the objective or its clearing timeout, available
entry members spread to separate cells near doors, wall corners, and passages
inside the room. Members already facing a nearby enemy remain under combat AI
instead of receiving a new movement order. Outdoor assaults end after entry.
Open doors receive more immediate guard priority than closed doors. After
indoor entry, nearby door state changes cause a bounded security reassessment;
members engaged with nearby enemies keep fighting.
The executor checks nearby doors every 30 ticks. If a door closes after being
open and a defender is just outside, an available raider throws smoke onto the
room side of that door, with a cooldown between responses.
HIGH entry members assigned a security sector use CQB focus toward the nearest
door, prioritizing open doors. The AI focus call does not show the player command
message.
For HIGH indoor assaults, entry order alternates targets on the two sides of
the doorway several cells inside the room, with sprint movement. If no
reachable cell exists on a side, the ordinary objective-cell selection is used.

Grenades are used only when their target is in range and sight, and damaging
throws are suppressed near friendlies. Indoor throws target reachable cells
inside the objective room near the entry. The executor uses the existing
inventory grenade, power-cutter, and C4 jobs; it does not create a separate
weapon or projectile implementation.
LOW indoor entries prefer an available MK III offensive grenade bearer over
an MK II fragmentation grenade bearer.
For C4, the executor uses an existing shock-tube igniter and installation job.
It sends all available members outside the charge's fragment radius before
triggering. If that separation cannot be achieved in time, it removes the
undetonated charge and continues without an explosion. An installed automated
charge is also removed if its raid execution state is discarded after a replan,
failed plan, or the departure of the raiders.
An outdoor plan can also rank a field grenade when armed enemies are near the
raid and suitable grenades are carried. The executor chooses a current hostile
position when it is ready to throw, so this target is not frozen in the map grid.
HIGH-doctrine wounded members with TCCC training perform self-hemostasis after
reaching their withdrawal position when they are bleeding.
The HIGH faction now has its own modern formation doctrine and combat pool;
generated raid members receive CQB and TCCC training at creation.
Modern team and squad leaders wear a ZAPER X26. During assembly, holding, and
room security, a leader with a ready device uses its existing paired-probe job
against a visible nearby enemy carrying a psychic shock or insanity lance.
If the probe tether holds and no other visible enemy threatens the target, the
leader attempts the existing contact-shock job. A downed target can then be
taken with the game's kidnapping job. Tactical movement and support orders leave
that follow-up alone, except for urgent withdrawal from a live breaching charge.
If an allied forward base has usable CAS or artillery, an outdoor raid can
request the existing service against a visible hostile well clear of friendly
and neutral pawns. The raid holds its assembly positions until the strike ends
and its munitions have had time to land. If friendlies move into the target
area before impact, it recalls the airstrike when possible or cancels remaining
artillery volleys. Service capacity and ammunition are consumed by the
existing forward-base support system.

## Developer tools

Under `Helodrace/Tactical AI`, use `Draw door and wall geometry map` to flash
scored cells, `Inspect tactical data under mouse` for the door/wall components,
and `Open raid tactical plans` to inspect the selected route and assignments.
The tactical POI overlay shows strategic sites.

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
