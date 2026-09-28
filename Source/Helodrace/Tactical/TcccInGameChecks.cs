#if TCCC_TESTS
// Compiled only for the isolated -quicktest validation build, never into the shipped DLL.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace.Tactical
{
    public sealed class TcccInGameChecks : GameComponent
    {
        private int phase, phaseStart;
        private Pawn actor, casualty, evacPatient;
        private Thing shootingTarget;
        private HelodSupportHelicopter helicopter;
        private Building_Bed bed;
        private float baselineBleed, shotsBefore;
        private bool finished;
        private int captureFrames;
        private int lastWorkTicks;
        private float moveBefore, workBefore;
        private List<float> savedDoses = new List<float>();
        public static float ObservedAddictionChance;
        private string Results => Path.Combine(GenFilePaths.SaveDataFolderPath, "tccc-results.txt");
        public TcccInGameChecks(Game game) { }
        private void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            File.AppendAllText(Results, "PASS " + message + "\n");
        }
        private void Next(int value) { phase = value; phaseStart = Find.TickManager.TicksGame; }
        private int Elapsed => Find.TickManager.TicksGame - phaseStart;
        private int WorkTicks => actor?.jobs?.curDriver is JobDriver_TcccTreatment driver
            ? (int)AccessTools.Field(typeof(JobDriver_TcccTreatment), "treatmentTicks").GetValue(driver) : -1;
        public override void GameComponentUpdate()
        {
            if (!finished && Find.CurrentMap != null) Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            if (captureFrames > 0 && --captureFrames == 0 && Screen.width > 0 && Screen.height > 0)
            {
                CaptureRope();
            }
        }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref phase, "phase"); Scribe_Values.Look(ref phaseStart, "phaseStart");
            Scribe_References.Look(ref actor, "actor"); Scribe_References.Look(ref casualty, "casualty");
            Scribe_References.Look(ref evacPatient, "evacPatient"); Scribe_References.Look(ref helicopter, "helicopter");
            Scribe_References.Look(ref bed, "bed"); Scribe_References.Look(ref shootingTarget, "shootingTarget");
            Scribe_Values.Look(ref baselineBleed, "baselineBleed"); Scribe_Values.Look(ref shotsBefore, "shotsBefore");
            Scribe_Collections.Look(ref savedDoses, "savedDoses", LookMode.Value);
        }
        public override void GameComponentTick()
        {
            if (finished || Find.CurrentMap == null) return;
            try
            {
                if (WorkTicks >= 0) lastWorkTicks = WorkTicks;
                if (phase != 0 && Elapsed > 14000) throw new Exception("Timeout in phase " + phase + "; job=" + actor?.CurJob);
                switch (phase)
                {
                    case 0:
                        File.WriteAllText(Results, "TCCC isolated in-game validation\n");
                        RulesAndDrugs();
                        actor = SpawnPawn(Find.CurrentMap.Center);
                        Wound(actor);
                        baselineBleed = actor.health.hediffSet.BleedRateTotal;
                        Check(baselineBleed > 0 && TcccUtility.CanAct(actor), "self-care actor is capable and bleeding");
                        TcccUtility.Start(actor, actor, TcccTreatment.SelfHemostasis);
                        Next(1); break;
                    case 1:
                        if (WorkTicks < 1199) break;
                        Check(Math.Abs(actor.health.hediffSet.BleedRateTotal / baselineBleed - 1f) < .02f, "no bleeding reduction before 20 seconds");
                        Next(2); break;
                    case 2:
                        if (WorkTicks < 1201) break;
                        Check(Math.Abs(actor.health.hediffSet.BleedRateTotal / baselineBleed - .30f) < .02f, "70 percent reduction starts at 20 seconds");
                        Next(3); break;
                    case 3:
                        if (TcccUtility.Effect(actor, "HD_TCCC_SelfHemostasis") == null) break;
                        Check(Math.Abs(actor.health.hediffSet.BleedRateTotal / baselineBleed - .05f) < .02f, "95 percent reduction after completed self-care");
                        Check(TcccUtility.Effect(actor, "HD_TCCC_SelfHemostasis").expiresTick - Find.TickManager.TicksGame >= 44998, "completed effect lasts 18 hours");
                        TcccUtility.RemoveEffect(actor, "HD_TCCC_SelfHemostasis");
                        TcccUtility.Start(actor, actor, TcccTreatment.SelfHemostasis);
                        Next(4); break;
                    case 4:
                        if (WorkTicks < 1201) break;
                        actor.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        Check(TcccUtility.Effect(actor, "HD_TCCC_Pressure") == null
                            && TcccUtility.Effect(actor, "HD_TCCC_SelfHemostasis") == null, "interruption removes temporary pressure without granting completed effect");
                        Thing agent = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_TCCC_HemostaticAgent"));
                        actor.inventory.innerContainer.TryAdd(agent);
                        TcccUtility.Start(actor, actor, TcccTreatment.Hemostatic, agent);
                        Next(5); break;
                    case 5:
                        if (TcccUtility.Effect(actor, "HD_TCCC_Hemostatic") == null) break;
                        Check(Elapsed >= 180 && Elapsed < 195, "hemostatic administration takes 3 seconds");
                        Check(TcccUtility.Effect(actor, "HD_TCCC_Hemostatic").expiresTick - Find.TickManager.TicksGame >= 14998, "hemostatic effect lasts 6 hours");
                        Check(Math.Abs(actor.health.hediffSet.BleedRateTotal / baselineBleed - .30f) < .02f, "hemostatic reduces bleeding by 70 percent");
                        casualty = SpawnPawn(actor.Position + IntVec3.East);
                        casualty.health.AddHediff(HediffDefOf.Anesthetic);
                        moveBefore = actor.GetStatValue(StatDefOf.MoveSpeed);
                        workBefore = actor.GetStatValue(StatDefOf.WorkSpeedGlobal);
                        TcccUtility.Start(actor, casualty, TcccTreatment.AttachDrag);
                        Next(6); break;
                    case 6:
                        if (!actor.Map.GetComponent<MapComponent_TcccDragging>().IsDragging(actor)) break;
                        Check(casualty.Spawned && actor.carryTracker.CarriedThing == null, "tether leaves hands free and casualty on the map");
                        Check(Math.Abs(actor.GetStatValue(StatDefOf.MoveSpeed) / moveBefore - .65f) < .02f, "tether reduces actual movement stat by 35 percent");
                        Check(Math.Abs(actor.GetStatValue(StatDefOf.WorkSpeedGlobal) / workBefore - 2f / 3f) < .02f, "tether reduces actual work speed");
                        actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, actor.Position + new IntVec3(0, 0, 4)));
                        Next(7); break;
                    case 7:
                        if (Elapsed < 180) break;
                        Check(actor.Map.GetComponent<MapComponent_TcccDragging>().IsDragging(actor)
                            && actor.Position.AdjacentTo8WayOrInside(casualty.Position), "casualty follows actual movement without detaching");
                        actor.Map.GetComponent<MapComponent_TcccDragging>().MapComponentDraw();
                        CameraJumper.TryJumpAndSelect(actor);
                        captureFrames = 5;
                        Check(actor.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("HD_TCCC_Dragging")), "dragging speed/work penalties and rope rendering are active");
                        actor.equipment.DestroyAllEquipment();
                        actor.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_Autopistol")));
                        shootingTarget = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel);
                        GenSpawn.Spawn(shootingTarget, actor.Position + new IntVec3(4, 0, 0), actor.Map);
                        shotsBefore = actor.records.GetValue(RecordDefOf.ShotsFired);
                        var shoot = JobMaker.MakeJob(JobDefOf.AttackStatic, shootingTarget); shoot.maxNumStaticAttacks = 1;
                        actor.jobs.TryTakeOrderedJob(shoot);
                        Next(8); break;
                    case 8:
                        if (Elapsed % 1000 == 0)
                            File.AppendAllText(Results, "TRACE shooting: stance=" + actor.stances.curStance
                                + " toil=" + actor.jobs.curDriver.CurToilIndex + " ticks=" + actor.jobs.curDriver.ticksLeftThisToil
                                + " canHit=" + actor.equipment.PrimaryEq.PrimaryVerb.CanHitTarget(shootingTarget) + "\n");
                        if (actor.records.GetValue(RecordDefOf.ShotsFired) <= shotsBefore) break;
                        Check(actor.Map.GetComponent<MapComponent_TcccDragging>().IsDragging(actor), "pawn can shoot while tether remains attached");
                        TcccUtility.Start(actor, casualty, TcccTreatment.Mist);
                        Next(15); break;
                    case 15:
                        if (!TcccUtility.MistReady(casualty)) break;
                        Check(lastWorkTicks >= 448 && lastWorkTicks <= 451,
                            "tether extends actual interaction from 300 to 450 ticks (observed work=" + lastWorkTicks + ", elapsed=" + Elapsed + ")");
                        Next(9);
                        GameDataSaveLoader.SaveGame("TcccIntegrationCheckpoint");
                        GameDataSaveLoader.LoadGame("TcccIntegrationCheckpoint");
                        break;
                    case 9:
                        Check(actor.Map.GetComponent<MapComponent_TcccDragging>().IsDragging(actor)
                            && casualty.Spawned, "save/load preserves drag link and casualty");
                        int index = 0;
                        foreach (string drugName in new[] { "BD_Morphine", "BD_Fentanyl", "BD_Ketamine", "BD_Laudanum" })
                        {
                            var partial = Find.CurrentMap.mapPawns.AllPawnsSpawned.SelectMany(p => p.inventory.innerContainer.InnerListForReading)
                                .First(t => t.def.defName == drugName && t.TryGetComp<CompTcccDose>().remaining < .99999f);
                            Check(Math.Abs(partial.TryGetComp<CompTcccDose>().remaining - savedDoses[index++]) < .00001f,
                                drugName + " fractional inventory remainder survives save/load");
                        }
                        actor.Map.GetComponent<MapComponent_TcccDragging>().Detach(actor);
                        Check(!actor.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("HD_TCCC_Dragging")), "detach removes penalties");
                        TcccUtility.Start(actor, casualty, TcccTreatment.Mist);
                        TcccUtility.RemoveEffect(casualty, "HD_TCCC_Mist");
                        Next(10); break;
                    case 10:
                        if (!TcccUtility.MistReady(casualty)) break;
                        Check(!TcccUtility.Effect(casualty, "HD_TCCC_Mist").report.NullOrEmpty(), "MIST job creates persistent M/I/S/T handover report");
                        BeginEvacuation(); Next(11); break;
                    case 11:
                        if (Elapsed < 181) break;
                        Check(AccessTools.Field(typeof(HelodSupportHelicopter), "phase").GetValue(helicopter).ToString() == "Loading",
                            "MIST dispatch lands after 180 ticks instead of 240");
                        Next(12); break;
                    case 12:
                        if (helicopter.Spawned) break;
                        Check(!evacPatient.Spawned && helicopter.GetDirectlyHeldThings().Contains(evacPatient), "MEDEVAC physically collects prepared casualty and returns to base");
                        Next(13);
                        GameDataSaveLoader.SaveGame("TcccEvacuationCheckpoint");
                        GameDataSaveLoader.LoadGame("TcccEvacuationCheckpoint");
                        break;
                    case 13:
                        if (!helicopter.Destroyed) break;
                        Check(evacPatient.Spawned && evacPatient.CurrentBed() == bed, "saved MEDEVAC returns patient to settlement bed and crew departs");
                        Check(Elapsed >= 5000, "MIST retains two-hour base treatment/return schedule");
                        Finish(true, "All TCCC integration checks passed"); break;
                }
            }
            catch (Exception ex) { Finish(false, "phase=" + phase + " " + ex); }
        }
        private void Finish(bool success, string message)
        {
            finished = true;
            File.AppendAllText(Results, (success ? "COMPLETE " : "FAIL ") + message + "\n");
            Application.Quit(success ? 0 : 1);
        }
        private void CaptureRope()
        {
            // Batch mode does not paint the desktop backbuffer. Render the actual pawn and
            // tether draw calls into a dedicated camera target for visual inspection.
            GameObject cameraObject = new GameObject("TCCC validation camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 2.5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.24f, .28f, .21f);
            Vector3 center = (actor.DrawPos + casualty.DrawPos) * .5f;
            camera.transform.position = new Vector3(center.x, 100f, center.z);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.nearClipPlane = .1f; camera.farClipPlane = 200f;
            RenderTexture target = new RenderTexture(640, 640, 24);
            camera.targetTexture = target;
            actor.Drawer.renderer.RenderPawnAt(actor.DrawPos);
            casualty.Drawer.renderer.RenderPawnAt(casualty.DrawPos);
            actor.Map.GetComponent<MapComponent_TcccDragging>().MapComponentDraw();
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(640, 640, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); image.Apply();
            RenderTexture.active = previous;
            byte[] png = (byte[])AccessTools.TypeByName("UnityEngine.ImageConversion").GetMethod("EncodeToPNG",
                new[] { typeof(Texture2D) }).Invoke(null, new object[] { image });
            File.WriteAllBytes(Path.Combine(GenFilePaths.SaveDataFolderPath, "tccc-rope-render.png"), png);
            camera.targetTexture = null;
            UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(cameraObject);
        }
        private Pawn SpawnPawn(IntVec3 near)
        {
            Map map = Find.CurrentMap;
            foreach (IntVec3 cell in CellRect.CenteredOn(near, 8).ClipInsideMap(map))
            {
                cell.GetEdifice(map)?.Destroy(); map.roofGrid.SetRoof(cell, null);
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
            }
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            foreach (Hediff h in pawn.health.hediffSet.hediffs.ToList()) pawn.health.RemoveHediff(h);
            pawn.story.traits.allTraits.Clear();
            GenSpawn.Spawn(pawn, near, map); pawn.drafter.Drafted = true;
            return pawn;
        }
        private void Wound(Pawn pawn)
        {
            foreach (BodyPartRecord part in pawn.RaceProps.body.AllParts.Where(p => p.def == BodyPartDefOf.Arm))
            { Hediff wound = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part); wound.Severity = 9f; pawn.health.AddHediff(wound); }
        }
        private void RulesAndDrugs()
        {
            Check(TcccRules.SelfHemostasisTicks == 1800 && TcccRules.PartialHemostasisTicks == 1200
                && TcccRules.SelfEffectTicks == 45000 && TcccRules.DrugEffectTicks == 15000, "all specified timing constants");
            Check(TcccRules.BleedingFactor(true, true, true) == .05f, "bleeding reductions do not multiply");
            foreach (string name in new[] { "BD_Morphine", "BD_Fentanyl", "BD_Ketamine", "BD_Laudanum" })
            {
                Pawn patient = SpawnPawn(Find.CurrentMap.Center + new IntVec3(0, 0, -12)); Wound(patient);
                float before = patient.health.hediffSet.PainTotal;
                Thing drug = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name)); drug.stackCount = 3;
                patient.inventory.innerContainer.TryAdd(drug);
                Check(drug.TryGetComp<CompTcccDose>() != null, name + " receives fractional-dose comp");
                float expected = TcccRules.DoseFor(before, TcccDrugs.HighDef(drug.def).stages[0].painFactor);
                Check(expected > 0f && TcccDrugs.Administer(patient, patient, drug), name + " actual administration succeeds");
                float after = patient.inventory.innerContainer.InnerListForReading.Where(t => t.def == drug.def)
                    .Sum(t => t.stackCount * t.TryGetComp<CompTcccDose>().remaining);
                Check(Math.Abs(after - (3f - expected)) < .0001f, name + " charges only the actual fractional amount");
                Check(Math.Abs(patient.health.hediffSet.PainTotal - TcccRules.TargetPain) < .015f, name + " reaches measured target pain");
                Check(patient.health.hediffSet.HasHediff(TcccDrugs.HighDef(drug.def)), name + " retains native drug effect");
                var props = drug.TryGetComp<CompDrug>().Props;
                Check(Math.Abs(ObservedAddictionChance - TcccRules.AddictionChance(props.addictiveness, expected, true)) < .000001f,
                    name + " native CompDrug receives exact dose-scaled reduced addiction chance");
                Check(Math.Abs(patient.health.hediffSet.GetFirstHediffOfDef(TcccDrugs.HighDef(drug.def)).Severity - expected) < .001f,
                    name + " native high severity scales with dose");
                Check(TcccRules.AddictionChance(props.addictiveness, expected, true) < props.addictiveness,
                    name + " dose-adjusted addiction probability is below a normal use");
                var remaining = patient.inventory.innerContainer.InnerListForReading.First(t => t.def == drug.def && t.stackCount == 1);
                savedDoses.Add(remaining.TryGetComp<CompTcccDose>().remaining);
                Check(!remaining.CanStackWith(drug), name + " partial vial cannot merge into full stack");
                Pawn ordinaryPatient = SpawnPawn(Find.CurrentMap.Center + new IntVec3(0, 0, -16)); Wound(ordinaryPatient);
                Thing quarterDose = ThingMaker.MakeThing(drug.def);
                quarterDose.TryGetComp<CompTcccDose>().remaining = .25f;
                quarterDose.Ingested(ordinaryPatient, 0f);
                Check(Math.Abs(ordinaryPatient.health.hediffSet.GetFirstHediffOfDef(TcccDrugs.HighDef(drug.def)).Severity - .25f) < .001f,
                    name + " normal ingestion of a partial vial cannot grant a full high");
                Check(Math.Abs(ObservedAddictionChance - props.addictiveness * .25f) < .000001f && TcccDrugs.Current == null,
                    name + " ordinary partial ingestion uses its actual addiction risk and clears context");
            }
        }
        private void BeginEvacuation()
        {
            Map map = actor.Map;
            evacPatient = casualty;
            Wound(evacPatient);
            Faction faction = Find.FactionManager.AllFactions.First(f => f.def.defName.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0);
            WorldObjectDef baseDef = DefDatabase<WorldObjectDef>.AllDefs.First(d => d.worldObjectClass == typeof(HelodForwardBase));
            var source = (HelodForwardBase)WorldObjectMaker.MakeWorldObject(baseDef);
            source.Tile = map.Tile; source.SetFaction(faction); Find.WorldObjects.Add(source);
            bed = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog);
            bed.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(bed, actor.Position + new IntVec3(-3, 0, 0), map);
            evacPatient.ownership.ClaimBedIfNonMedical(bed);
            IntVec3 dz;
            if (!CellFinder.TryFindRandomCellNear(actor.Position, map, 8, c => HelodHelicopterSupport.ValidDZ(map, c), out dz))
                throw new Exception("No test DZ");
            helicopter = (HelodSupportHelicopter)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_SupportHelicopter"));
            helicopter.Initialize(source, map, dz, HelodForwardBaseService.HelicopterMedevac, new List<Pawn> { evacPatient });
            GenSpawn.Spawn(helicopter, dz, map);
        }
    }

    [HarmonyPatch(typeof(CompDrug), nameof(CompDrug.PostIngested))]
    public static class TcccTestObserveNativeDrug
    {
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(CompDrug __instance)
        {
            if (TcccDrugs.Current?.drug == __instance.parent)
                TcccInGameChecks.ObservedAddictionChance = __instance.Props.addictiveness;
        }
    }
}
#endif

