# Helodrace Aircraft Framework

This is an independent, XML-driven in-map aircraft core. It does not reference
or patch Aerocraft Framework.

## Current API

- save-safe parked, takeoff, flying and landing states
- direct navigation and automatic holding circles
- player commands for takeoff, destination, loiter, landing and go-around
- continuous position, heading, altitude-scale and shadow rendering
- public state and navigation methods for weapon, cargo and AI comps
- boundary recovery which steers aircraft back into the map
- vanilla hauling-based refueling with a per-aircraft fuel item selector
- configurable target fuel level and airborne-only fuel consumption
- pilot and passenger roles with continuously ticking pawn needs and hediffs
- mass-limited cargo designation, colonist hauling and unloading
- mass-limited bomb bay with direct ThingDef and tag-based allow lists
- one horizontal-bombing gizmo per loaded bomb type
- XML-configured fixed aircraft guns with low-altitude strafing runs

Weapons and cargo are intentionally separate modules. They can depend on
`AircraftThing.IsAirborne` without being coupled to the movement implementation.

## Minimal ThingDef

```xml
<ThingDef ParentName="HD_AircraftBase">
  <defName>HD_ExampleAircraft</defName>
  <label>example aircraft</label>
  <size>(3,3)</size>
  <graphicData>
    <texPath>YourMod/AircraftTexture</texPath>
    <graphicClass>Graphic_Single</graphicClass>
    <drawSize>(5,5)</drawSize>
  </graphicData>
  <!-- Replaces the inherited default extension with aircraft-specific values. -->
  <modExtensions Inherit="False">
    <li Class="Helodrace.Aircraft.AircraftDefExtension">
      <cruiseSpeed>0.18</cruiseSpeed>
      <turnRate>2.5</turnRate>
      <takeoffTicks>180</takeoffTicks>
      <takeoffDistance>12</takeoffDistance>
      <landingDistance>14</landingDistance>
      <arrivalRadius>1.25</arrivalRadius>
      <defaultLoiterRadius>9</defaultLoiterRadius>
      <airborneDrawScale>1.35</airborneDrawScale>
      <drawShadow>true</drawShadow>
      <cruiseAltitude>18</cruiseAltitude>
    </li>
  </modExtensions>
</ThingDef>
```

## Fuel configuration

`HD_AircraftBase` includes `CompRefuelable` and
`CompAircraftFuelSelector`. The default example permits chemfuel and wood so
the selector can be tested immediately. A concrete aircraft should replace the
inherited `comps` list and set the desired capacity, consumption and fuel defs.

The **Select fuel** command determines which one of the allowed ThingDefs may
be hauled to that particular aircraft. The vanilla fuel command controls the
exact target level. Colonists then use the normal refueling work giver,
including reservations and hauling paths.

## C# extension points

An aircraft weapon or AI comp can cast its parent to `AircraftThing` and use:

- `IsAirborne`, `FlightState`, `NavigationMode`
- `ExactPosition`, `HeadingDegrees`, `Destination`
- `TrySetDestination`, `TryLoiter`, `TryLand`, `AbortLanding`

All serialized field names are private implementation details. Extensions
should use the public API instead of reading them through reflection.

## Bomb definitions and horizontal bombing

Bomb bay capacity is independent from ordinary cargo capacity. An aircraft may
allow bombs by exact ThingDef, by tag, or by both:

```xml
<li Class="Helodrace.Aircraft.CompProperties_AircraftBombBay">
  <capacity>1200</capacity>
  <hasCenterSlot>true</hasCenterSlot>
  <wingSlotsPerSide>2</wingSlotsPerSide>
  <minimumImbalancedTurnRateFactor>0.4</minimumImbalancedTurnRateFactor>
  <defaultMaxCountPerSlot>1</defaultMaxCountPerSlot>
  <bombSlotLimits>
    <li>
      <bombDef>HD_Bomb_GP500</bombDef>
      <maxCountPerSlot>2</maxCountPerSlot>
    </li>
  </bombSlotLimits>
  <allowedBombDefs>
    <li>HD_Bomb_GP500</li>
  </allowedBombDefs>
  <allowedBombTags>
    <li>HD_AircraftBomb</li>
    <li>HD_HeavyBomb</li>
  </allowedBombTags>
</li>
```

`hasCenterSlot` adds one center hardpoint. Center and wing slots both use the
limits configured on this aircraft. `defaultMaxCountPerSlot` applies to bombs
without an exact entry in `bombSlotLimits`; an exact `bombDef` entry overrides
that default only for this aircraft.
`wingSlotsPerSide` sets the number of bomb slots on each wing. Selecting a wing
slot assigns the same bomb and quantity to the mirrored slot, so a quantity of
three requires six physical bombs across both wings. Takeoff is blocked until
the configured center bomb and every mirrored wing slot are fully loaded. Bombs
are released one at a time. The remaining
left/right bomb mass difference reduces normal turn rate linearly; a completely
one-sided load uses `minimumImbalancedTurnRateFactor` of the normal turn rate.

Every loadable bomb item needs an `AircraftBombDefExtension`. `projectile`
points to the projectile which performs the actual impact and explosion.

```xml
<modExtensions>
  <li Class="Helodrace.Aircraft.AircraftBombDefExtension">
    <projectile>HD_Projectile_GP500</projectile>
    <bombTags>
      <li>HD_AircraftBomb</li>
      <li>HD_HeavyBomb</li>
    </bombTags>
    <fallSpeed>0.12</fallSpeed>
    <weaponRange>30</weaponRange>
    <horizontalScatter>1.5</horizontalScatter>
    <cooldownTicks>30</cooldownTicks>
  </li>
</modExtensions>
```

Bomb definitions do not set slot capacity. `maxCountPerSlot` belongs to an
aircraft's `bombSlotLimits` entry and limits that bomb in one center or wing
slot. Mirroring doubles only the quantity assigned to a wing slot.

Tag matching also recognizes the ThingDef's native `tradeTags`, `weaponTags`
and `thingSetMakerTags`. A loaded bomb type receives its own drop gizmo. The
aircraft altitude is stored as a natural number. A weapon is usable only when
`weaponRange > currentAltitude`. The predicted forward distance is
`currentSpeed * (currentAltitude / fallSpeed)`;
independent longitudinal and lateral scatter are then applied.

## Aircraft expeditions

An aircraft in normal flight has a **Begin expedition** command. It keeps its
current heading, flies to the corresponding map edge, and is transferred into
an `HD_AircraftCaravan` world object on the map parent's tile. The world object
deep-holds the original aircraft, so crew, needs, hediffs, cargo, fuel, bombs,
damage and component state remain on the same saved Thing. Its world icon uses
the held aircraft's `uiIcon`.

The world object has a shuttle-style destination command. During targeting it
draws the round-trip radius in green and the one-way radius in amber. Range is
calculated from current fuel and the aircraft extension values below:

```xml
<worldFuelPerTile>3</worldFuelPerTile>
<worldRange>60</worldRange>
<worldFlightDurationTicks>4000</worldFlightDurationTicks>
```

One-way range is the lesser of `currentFuel / worldFuelPerTile` and
`worldRange`. Round-trip range reserves the same fuel cost for the return leg.
A destination between the two rings requires confirmation. Travel consumes
outbound fuel immediately and uses a fixed duration like a vanilla shuttle.

## Fixed aircraft guns and strafing

Aircraft guns reference an existing weapon ThingDef. The fixed mount uses that
weapon's first verb with a `defaultProjectile`; the turret mount enum is
reserved for the later turret implementation.

```xml
<li Class="Helodrace.Aircraft.CompProperties_AircraftGun">
  <gunDef>HD_Gun_M1917HMG_TurretGun</gunDef>
  <mountType>Fixed</mountType>
  <strafeLength>32</strafeLength>
  <strafeWidth>3</strafeWidth>
  <strafeAltitude>5</strafeAltitude>
  <roundsPerMinute>600</roundsPerMinute>
  <gunCount>2</gunCount>
  <fireLeadDistance>6</fireLeadDistance>
  <approachRadius>1.5</approachRadius>
  <recoveryDistance>12</recoveryDistance>
</li>
```

`gunCount` is the number of identical fixed guns mounted in this comp. Every
firing step launches one projectile from each gun simultaneously.
`roundsPerMinute` controls the fire rate per gun; the tick interval is
`3600 / roundsPerMinute`. The selected cell is the center
of the ground strafing line. The aircraft
descends during approach, follows a parallel flight line behind the ground
targets by `fireLeadDistance`, and fires at evenly spaced points. It then
continues forward through `recoveryDistance` while climbing to cruise altitude.
