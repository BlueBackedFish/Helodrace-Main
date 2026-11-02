using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Helodrace.Tactics;
using RimWorld;
using Verse;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public bool r7DoorApproachIsolated, r7DoorApproachPreserved, r7DoorOpeningUsed;
    }

    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private bool DoorFunctionalFixture => result.fixtureCase == "r7-door";
        private static readonly IntVec3 DoorFunctionalOpening = new IntVec3(100, 0, 108);

        private bool DoorApproachIsolated()
        {
            for (int z = 100; z <= fixtureTop; z++)
            {
                IntVec3 cell = new IntVec3(99, 0, z);
                if (z == DoorFunctionalOpening.z)
                { if (!cell.Standable(map)) return false; }
                else if (cell.GetTerrain(map) != TerrainDefOf.WaterDeep || cell.Walkable(map)) return false;
            }
            return true;
        }

        private void InitializeDoorFunctionalFixture()
        {
            if (!DoorFunctionalFixture) return;
            if (result.workload != "sapper-door") throw new InvalidOperationException("The door functional fixture requires sapper-door.");
            // A real impassable terrain strip leaves the door as the sole usable
            // west-front approach. LOW still uses its normal wall-first doctrine;
            // no planner/tool/LOS/path result is mocked or forced.
            for (int z = 100; z <= fixtureTop; z++)
                if (z != DoorFunctionalOpening.z) map.terrainGrid.SetTerrain(new IntVec3(99, 0, z), TerrainDefOf.WaterDeep);
            var door = DoorFunctionalOpening.GetEdifice(map) as Building_Door;
            result.r7DoorApproachIsolated = door != null && !door.Open && DoorApproachIsolated();
            if (!result.r7DoorApproachIsolated) throw new InvalidOperationException("The door fixture needs a closed door and one real walkable approach.");
        }

        private void CaptureDoorFunctionalFixture(IEnumerable<TacticalSquadCommand> commands)
        {
            if (!DoorFunctionalFixture) return;
            result.r7DoorApproachPreserved = DoorApproachIsolated();
            var door = DoorFunctionalOpening.GetEdifice(map) as Building_Door;
            result.r7DoorOpeningUsed = door != null && DoorBreachFaultUtility.Jammed(door)
                && commands.Any() && commands.All(c => c.SecuredPlans.Any(p => p.Opening == DoorFunctionalOpening));
        }
    }
}
