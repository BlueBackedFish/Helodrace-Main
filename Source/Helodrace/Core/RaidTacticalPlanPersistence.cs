using Verse;

namespace Helodrace
{
    public sealed partial class RaidTacticalAssignment : IExposable
    {
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref GroupId, "groupId");
            Scribe_Values.Look(ref Task, "task");
            Scribe_Values.Look(ref Position, "position");
            Scribe_Values.Look(ref EntryOrder, "entryOrder");
        }
    }

    public sealed partial class RaidTacticalOption : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Maneuver, "maneuver");
            Scribe_Values.Look(ref Score, "score");
            Scribe_Values.Look(ref Reason, "reason");
        }
    }

    public sealed partial class RaidTacticalPlan : IExposable
    {
        private int selectedIndex;

        public void ExposeData()
        {
            Scribe_Values.Look(ref OrganizationId, "organizationId");
            Scribe_Values.Look(ref UnitId, "unitId");
            Scribe_Values.Look(ref GroupId, "groupId");
            Scribe_Values.Look(ref Doctrine, "doctrine");
            Scribe_Values.Look(ref Start, "start");
            Scribe_Values.Look(ref Objective, "objective");
            Scribe_Values.Look(ref FinalObjective, "finalObjective");
            Scribe_Values.Look(ref ObjectiveIsObservedEnemy, "observedEnemy");
            Scribe_Values.Look(ref ObjectiveIsNamedBed, "namedBed");
            Scribe_Values.Look(ref ObjectiveIsIntermediate, "intermediate");
            Scribe_Values.Look(ref ObjectiveIsRecheck, "recheck");
            Scribe_Values.Look(ref IsDefensive, "isDefensive");
            Scribe_Values.Look(ref Frontline, "frontline");
            Scribe_Values.Look(ref Flank, "flank");
            Scribe_Values.Look(ref Entry, "entry");
            Scribe_References.Look(ref PlannedBreach, "plannedBreach");
            Scribe_Values.Look(ref ReusePassage, "reusePassage");
            Scribe_Values.Look(ref CqbIntent, "cqbIntent");
            Scribe_Values.Look(ref OccupiedRoom, "occupiedRoom");
            Scribe_Values.Look(ref BreachCell, "breachCell", IntVec3.Invalid);
            Scribe_Values.Look(ref BreachInside, "breachInside", IntVec3.Invalid);

            Scribe_Collections.Look(ref MovementNodes, "movementNodes", LookMode.Deep);
            Scribe_Collections.Look(ref ApproachPath, "approachPath", LookMode.Value);
            Scribe_Collections.Look(ref SafeStackCells, "safeStackCells", LookMode.Value);
            Scribe_Collections.Look(ref SafeSupportCells, "safeSupportCells", LookMode.Value);
            Scribe_Collections.Look(ref AvoidedTrapCells, "avoidedTrapCells", LookMode.Value);
            Scribe_Collections.Look(ref Options, "options", LookMode.Deep);
            Scribe_Collections.Look(ref Assignments, "assignments", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.Saving) selectedIndex = Options.IndexOf(Selected);
            Scribe_Values.Look(ref selectedIndex, "selectedIndex", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                Selected = selectedIndex >= 0 && selectedIndex < Options.Count ? Options[selectedIndex] : null;
            Scribe_Values.Look(ref EntrySupport, "entrySupport");
            Scribe_Values.Look(ref EntryMethod, "entryMethod");
            Scribe_Values.Look(ref BreachSearch, "breachSearch");
            Scribe_Values.Look(ref Reason, "reason");
            Scribe_Values.Look(ref CommandEfficiency, "commandEfficiency");
            Scribe_Values.Look(ref CasualtyFraction, "casualtyFraction");
            Scribe_Values.Look(ref EntryDelayTicks, "entryDelayTicks");
            Scribe_Values.Look(ref CoordinationDelayTicks, "coordinationDelayTicks");
            Scribe_Values.Look(ref PlannedTick, "plannedTick");
            Scribe_Values.Look(ref PlanningMilliseconds, "planningMilliseconds");
            Scribe_Values.Look(ref BreachCandidates, "breachCandidates");
            Scribe_Values.Look(ref DetailedBreachChecks, "detailedBreachChecks");
        }
    }
}
