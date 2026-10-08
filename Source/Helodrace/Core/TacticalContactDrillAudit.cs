using System.Collections.Generic;
using System.Linq;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    // Audit-only scripted sightings. Production observation still uses actual
    // pawn lean/LOS; this fixture never inserts a contact into memory directly.
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private Pawn contactActor;
        private int contactStep, contactStepAt;
        private long contactResumeBaseline;
        private long contactGuardBaseline;
        private TacticalSquadCommand contactCommand;
        private TacticalLocalPlan contactPlan;
        private int contactSecuredCount;
        private IntVec3 contactFirst = IntVec3.Invalid, contactSecond = IntVec3.Invalid;
        private readonly List<string> contactEvents = new List<string>();
        private bool contactFrozen = true, contactPreserved = true, unseenDoorIgnored;

        private void InitializeContactDrill()
        {
            contactActor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(contactActor, new IntVec3(150, 0, 150), map);
            contactActor.equipment.DestroyAllEquipment(); contactActor.drafter.Drafted = true;
            Job wait = JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture); wait.expiryInterval = 1000000;
            contactActor.jobs.StartJob(wait, JobCondition.InterruptForced);
            ProtectedRaiders.Add(contactActor);
            HideContactActors();
        }
        private void HideContactActors()
        {
            owner.Position = new IntVec3(150, 0, 150);
            contactActor.Position = new IntVec3(151, 0, 150);
        }
        private void ContactEvent(string message)
        {
            if (contactEvents.Count < 64)
            {
                string entry = (GenTicks.TicksGame - started) + ":" + message;
                contactEvents.Add(entry); Log.Message("R4 contact drill " + entry);
            }
        }
        private void ApplyContactDrill()
        {
            if (TacticalEngineSelection.Kind != TacticalEngineKind.New || contactStep >= 9) return;
            int tick = GenTicks.TicksGame;
            var service = map.GetComponent<MapComponent_TacticalCommands>();
            if (contactStep == 0)
            {
                contactCommand = service.Commands.FirstOrDefault(c => c.SecuredPlans.Count == 1 && c.Plan?.Opening.x == 114
                    && c.Phase == TacticalCommandPhase.Stack && c.Members.Count(m => m.Pawn.Spawned
                        && c.Plan.Stack.Contains(m.Pawn.Position)) >= 8);
                if (contactCommand == null) return;
                contactPlan = contactCommand.Plan; contactSecuredCount = contactCommand.SecuredCells.Count;
                Pawn anchor = contactCommand.Members[contactCommand.Members.Count / 2].Pawn;
                contactFirst = anchor.Position - contactPlan.Inward * 4;
                if (!contactFirst.Standable(map)) throw new System.InvalidOperationException("Rear drill needs connected secured floor.");
                owner.Position = contactFirst; result.caseTriggered = true;
                contactGuardBaseline = service.ContactGuardJobs;
                contactStepAt = tick; contactStep = 1; ContactEvent("rear exposed " + contactFirst); return;
            }
            var memory = contactCommand.Contacts.Memory.Entries;
            TacticalContact first = memory.FirstOrDefault(c => c.EnemyId == owner.thingIDNumber && c.SeenTick >= contactStepAt);
            if (contactStep == 1 || contactStep == 4 || contactStep == 7)
            {
                bool kind = contactStep == 1 ? service.RearResponses > 0 : contactStep == 4 ? service.DoorResponses > 0 : service.OpposedResponses > 0;
                bool second = contactStep != 7 || memory.Any(c => c.EnemyId == contactActor.thingIDNumber
                    && c.Position == contactSecond && c.SeenTick >= contactStepAt);
                if (first?.Position != contactFirst || !kind || !second || contactCommand.ContactResponse == null
                    || service.ContactGuardJobs < contactGuardBaseline + 2) return;
                if (contactStep == 4 && (!first.Door || first.Area == first.Position))
                    throw new System.InvalidOperationException("Door observation must record an adjacent floor area.");
                ContactEvent("observed " + contactStep + " at " + first.Position + " area=" + first.Area
                    + " rear=" + contactCommand.ContactResponse.Rear + " opposed=" + contactCommand.ContactResponse.Opposed);
                contactResumeBaseline = service.ContactResumes;
                HideContactActors(); contactStep++; ContactEvent("actors hidden"); return;
            }
            if (contactStep == 2 || contactStep == 5 || contactStep == 8)
            {
                TacticalContact retained = memory.FirstOrDefault(c => c.EnemyId == owner.thingIDNumber);
                contactFrozen &= retained == null || retained.Position == contactFirst;
                if (contactStep == 8)
                {
                    TacticalContact other = memory.FirstOrDefault(c => c.EnemyId == contactActor.thingIDNumber);
                    contactFrozen &= other == null || other.Position == contactSecond;
                }
                if (contactCommand.ContactResponse != null)
                    contactPreserved &= contactCommand.Plan == contactPlan && contactCommand.SecuredCells.Count == contactSecuredCount;
                if (contactStep == 2 && !unseenDoorIgnored)
                {
                    Building_Door unseen = new IntVec3(126, 0, 119).GetEdifice(map) as Building_Door;
                    if (unseen == null) throw new System.InvalidOperationException("Unseen-door drill lost its original partition.");
                    bool visible = contactCommand.Members.Any(m => m.Pawn.Spawned
                        && GenSight.LineOfSight(m.Pawn.Position, unseen.Position, map, true));
                    if (visible) throw new System.InvalidOperationException("Unseen-door stimulus must be outside member LOS.");
                    unseen.StartManualOpenBy(owner);
                    unseenDoorIgnored = unseen.Open && contactCommand.Plan == contactPlan;
                    ContactEvent("unseen door opened; plan unchanged");
                }
                if (service.ContactResumes <= contactResumeBaseline || contactCommand.ContactResponse != null) return;
                ContactEvent("resumed " + contactStep); contactStep++;
                if (contactStep == 9) return;
            }
            if (contactStep == 3)
            {
                // A second real doorway, distinct from the committed entrance.
                contactPlan = contactCommand.Plan; contactSecuredCount = contactCommand.SecuredCells.Count;
                contactFirst = new IntVec3(114, 0, System.Math.Min(134, contactPlan.Opening.z + 5));
                Building_Door door = (Building_Door)ThingMaker.MakeThing(ThingDefOf.Door, ThingDefOf.Steel);
                door.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(door, contactFirst, map, WipeMode.Vanish);
                owner.Position = contactFirst; door.StartManualOpenBy(owner);
                contactGuardBaseline = service.ContactGuardJobs;
                contactStepAt = tick; contactStep = 4; ContactEvent("other doorway exposed " + contactFirst); return;
            }
            if (contactStep == 6)
            {
                // Open the actual committed doorway so the same member can
                // observe actors on both sides, as in a corridor crossfire.
                contactPlan = contactCommand.Plan; contactSecuredCount = contactCommand.SecuredCells.Count;
                Building_Door door = contactPlan.Opening.GetEdifice(map) as Building_Door;
                if (door == null) return;
                door.StartManualOpenBy(owner);
                Pawn anchor = contactCommand.Members[contactCommand.Members.Count / 2].Pawn;
                contactFirst = anchor.Position - contactPlan.Inward * 3;
                contactSecond = contactPlan.Inside + contactPlan.Inward * 2;
                if (!contactFirst.Standable(map) || !contactSecond.Standable(map)) return;
                owner.Position = contactFirst; contactActor.Position = contactSecond;
                contactGuardBaseline = service.ContactGuardJobs;
                contactStepAt = tick; contactStep = 7; ContactEvent("opposed exposed " + contactFirst + " / " + contactSecond);
            }
        }
        private void FinishContactDrill()
        {
            result.newContactEvents = contactEvents.ToArray();
            result.newContactDrillComplete = contactStep == 9;
            result.newContactMemoryFrozen = contactFrozen && contactStep == 9;
            result.newContactPlanPreserved = contactPreserved && contactStep == 9;
            result.newUnseenDoorIgnored = unseenDoorIgnored && contactPreserved;
        }
    }
}
