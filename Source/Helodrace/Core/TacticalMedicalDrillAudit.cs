using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Tactical;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private bool MedicalFixture => result.fixtureCase == "r6-care-drill" || result.fixtureCase == "r6-care-interrupt";
        private int medicalStep, medicalStarted;
        private Pawn medicalPatient;
        private TacticalSquadCommand medicalCommand;
        private List<Hediff_Injury> medicalWounds = new List<Hediff_Injury>();
        private List<string> medicalEvents = new List<string>();
        private bool medicalGuards = true, medicalObserved;
        private long medicalLoggedCompletions;
        private bool medicalThreatExposed;

        private static bool MedicalInterruptionReady(bool pressure, bool ownedBagTreatment,
            bool treatmentToil, int remainingTicks) => pressure || ownedBagTreatment && treatmentToil
                && remainingTicks > 0 && remainingTicks <= TcccRules.HemostasisTicks - TcccRules.PartialHemostasisTicks;

        private void MedicalEvent(string value)
        {
            string message = (GenTicks.TicksGame - started) + ":" + value;
            if (medicalEvents.Count < 32) medicalEvents.Add(message);
            Log.Message("R6 medical drill " + message);
        }
        private void ApplyMedicalDrill()
        {
            if (TacticalEngineSelection.Kind != TacticalEngineKind.New || medicalStep >= 3) return;
            int tick = GenTicks.TicksGame;
            MapComponent_TacticalCommands service = map.GetComponent<MapComponent_TacticalCommands>();
            if (medicalStep == 0)
            {
                medicalCommand = service.Commands.FirstOrDefault(c => c.Phase == TacticalCommandPhase.Clear
                    && c.RoomScan == null && c.SecuredPlans.Count == 1 && c.MedicalWindowUntil > tick);
                if (medicalCommand == null) return;
                medicalPatient = medicalCommand.Members.Last().Pawn;
                Pawn helper = medicalCommand.Members.Where(m => m.Pawn != medicalPatient)
                    .OrderBy(m => m.Pawn.Position.DistanceToSquared(medicalPatient.Position)).First().Pawn;
                Apparel bag = (Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Apparel_GreatWarMedibag"), ThingDefOf.Cloth);
                helper.apparel.Wear(bag, false);
                Thing supplies = ThingMaker.MakeThing(ThingDefOf.MedicineHerbal); supplies.stackCount = 3;
                helper.inventory.innerContainer.TryAdd(supplies);
                foreach (BodyPartRecord part in medicalPatient.RaceProps.body.AllParts
                    .Where(p => p.def.defName != "Brain" && p.def.defName != "Heart" && p.def.defName != "Lung" && p.def.defName != "Liver"
                        && p.coverageAbs > 0 && p.def.GetMaxHealth(medicalPatient) > 10))
                {
                    var wound = (Hediff_Injury)HediffMaker.MakeHediff(HediffDefOf.Cut, medicalPatient, part);
                    wound.Severity = 4; medicalPatient.health.AddHediff(wound, part);
                    if (wound.BleedRate <= 0) { medicalPatient.health.RemoveHediff(wound); continue; }
                    medicalWounds.Add(wound); if (medicalWounds.Count == 4) break;
                }
                if (medicalWounds.Count != 4) throw new InvalidOperationException("Care fixture requires four bleeding body parts.");
                Hediff blood = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, medicalPatient); blood.Severity = .5f;
                medicalPatient.health.AddHediff(blood); medicalPatient.health.AddHediff(HediffDefOf.Anesthetic);
                if (!medicalPatient.Downed) throw new InvalidOperationException("The other-pawn care fixture must be downed.");
                medicalStep = 1; medicalStarted = tick; result.caseTriggered = true;
                MedicalEvent("downed patient=" + medicalPatient.thingIDNumber + " stocked bag wearer=" + helper.thingIDNumber); return;
            }
            TacticalMedicalCare care = medicalCommand.MedicalCare;
            if (service.MedicalCompleted > medicalLoggedCompletions)
            {
                medicalLoggedCompletions = service.MedicalCompleted;
                MedicalEvent("completed=" + medicalLoggedCompletions + " wounds=" + string.Join(";", medicalWounds.Select(w =>
                    w.Part.def.defName + " sev=" + w.Severity + " bleed=" + w.BleedRate
                    + " present=" + medicalPatient.health.hediffSet.hediffs.Contains(w)
                    + " dressed=" + PartHemostasis.HasDressing(medicalPatient.health.hediffSet, w.Part, tick))));
            }
            if (care != null && !medicalObserved)
            {
                medicalObserved = true; MedicalEvent("owned treatment=" + care.Job.def.defName + " helper=" + care.Helper.Pawn.thingIDNumber);
            }
            if (care != null && tick - care.Started > 120)
            {
                medicalGuards &= medicalCommand.Plan == medicalCommand.SecuredPlans[0]
                    && medicalCommand.Members.Where(m => m != care.Helper && m != care.Patient && m.Pawn.Spawned && !m.Pawn.Downed)
                        .All(m => m.Pawn.CurJob == m.Job && m.Job?.def.defName == "HD_NewTacticalContactGuard"
                            && m.Pawn.Position == m.Job.targetA.Cell);
            }
            if (medicalStep == 1)
            {
                if (result.fixtureCase == "r6-care-interrupt")
                {
                    bool pressure = medicalPatient.health.hediffSet.hediffs.Any(h => h.def.defName == "HD_TCCC_Pressure");
                    JobDriver driver = care?.Helper.Pawn.jobs.curDriver;
                    // Other-pawn bag care has movement gate, goto, then the
                    // delayed treatment toil. It never creates TCCC pressure.
                    bool bagTreatment = care != null && !care.Finished && care.Helper.Pawn.CurJob == care.Job
                        && driver is JobDriver_MedibagHemostasis;
                    if (!medicalThreatExposed && care != null && MedicalInterruptionReady(pressure, bagTreatment,
                        driver?.CurToilIndex == 2, driver?.ticksLeftThisToil ?? 0))
                    {
                        IntVec3 threat = GenAdj.CardinalDirections.Select(d => medicalPatient.Position + d * 3)
                            .First(c => c.Standable(map) && c.GetFirstPawn(map) == null
                                && GenSight.LineOfSight(medicalPatient.Position, c, map, true));
                        owner.Position = threat; medicalThreatExposed = true;
                        MedicalEvent("visible close threat exposed during owned treatment " + care.Job.def.defName
                            + " remaining=" + driver?.ticksLeftThisToil + " pressure=" + pressure + " at=" + threat); return;
                    }
                    if (!medicalThreatExposed || service.MedicalAborted == 0 || care != null || pressure || service.ContactShots == 0
                        || medicalWounds.Any(w => PartHemostasis.HasDressing(medicalPatient.health.hediffSet, w.Part, tick))) return;
                    result.newMedicalInterruptionSafe = true; result.newMedicalNoVanillaTend = medicalWounds.All(w => !w.IsTended());
                    owner.Position = new IntVec3(180,0,180);
                    foreach (Hediff anesthesia in medicalPatient.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.Anesthetic).ToArray())
                        medicalPatient.health.RemoveHediff(anesthesia);
                    medicalStep = 2; MedicalEvent("care interrupted; pressure removed; threat hidden"); return;
                }
                result.newMedicalDressings = medicalWounds.All(w => PartHemostasis.HasDressing(medicalPatient.health.hediffSet, w.Part, tick));
                result.newMedicalPlasmaApplied = medicalPatient.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("HD_PlasmaTransfusion"))
                    && (medicalPatient.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0) < .1f;
                if (!result.newMedicalDressings || !result.newMedicalPlasmaApplied || service.MedicalCompleted < 2) return;
                result.newMedicalNoVanillaTend = medicalWounds.All(w => !w.IsTended());
                foreach (Hediff anesthesia in medicalPatient.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.Anesthetic).ToArray())
                    medicalPatient.health.RemoveHediff(anesthesia);
                medicalStep = 2; MedicalEvent("dressings + plasma complete; patient recovered"); return;
            }
            if (medicalStep == 2 && medicalCommand.MedicalCare == null && medicalCommand.SecuredPlans.Count > 1)
            { result.newMedicalMissionResumed = true; medicalStep = 3; MedicalEvent("next-room mission resumed"); }
        }
        private void FinishMedicalDrill()
        {
            if (!MedicalFixture) return;
            MapComponent_TacticalCommands service = map.GetComponent<MapComponent_TacticalCommands>();
            result.newMedicalMemberChecks = service.MedicalMemberChecks; result.newMedicalTreatments = service.MedicalTreatments;
            result.newMedicalCompleted = service.MedicalCompleted; result.newMedicalAborted = service.MedicalAborted;
            result.newMedicalPlasma = service.MedicalPlasma;
            result.newMedicalRejoins = service.MedicalRejoins;
            result.newMedicalGuardsHeld = medicalObserved && medicalGuards;
            result.newMedicalNoVanillaTend = medicalObserved && medicalWounds.Count == 4 && medicalWounds.All(w => !w.IsTended());
            result.newMedicalEvents = medicalEvents.ToArray();
        }
    }
}
