#if TCCC_TESTS
// Compiled only for the isolated -quicktest validation build, never into the shipped DLL.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace.ModernWar;
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
        private int savedCharges;
        private CompMedicalPouch pouch;
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
            Scribe_Values.Look(ref savedCharges, "savedCharges");
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
                        HediffDef cqbLow = DefDatabase<HediffDef>.GetNamed("HD_CQBTraining_LOW");
                        Check(!TacticalTraining.HasCqbLow(actor) && !TacticalTraining.HasCqbHigh(actor),
                            "CQB skills require training");
                        actor.health.AddHediff(cqbLow);
                        Check(TacticalTraining.HasCqbLow(actor) && !TacticalTraining.HasCqbHigh(actor),
                            "basic CQB training does not unlock advanced skills");
                        actor.health.RemoveHediff(actor.health.hediffSet.GetFirstHediffOfDef(cqbLow));
                        actor.health.AddHediff(DefDatabase<HediffDef>.GetNamed("HD_CQBTraining_HIGH"));
                        Check(TacticalTraining.HasCqbLow(actor) && TacticalTraining.HasCqbHigh(actor),
                            "advanced CQB training includes basic skills");
                        Check(!TcccUtility.CanAct(actor) && !TcccUtility.CanTreat(actor, actor),
                            "TCCC commands and treatments require training");
                        HediffDef lowTraining = DefDatabase<HediffDef>.GetNamed("HD_TCCCTraining_LOW");
                        actor.health.AddHediff(lowTraining);
                        Check(!TcccUtility.CanTreat(actor, actor, TcccTreatment.Hemostasis),
                            "TCCC hemostasis needs a pouch installed on worn body armor");
                        pouch = EquipLargePouch(actor);
                        Thing stock = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_TCCC_HemostaticAgent"));
                        stock.stackCount = 2;
                        Check(actor.inventory.innerContainer.TryAdd(stock), "Hemostatic stock enters caregiver inventory");
                        stock = actor.inventory.innerContainer.First(item => item.def.defName == "HD_TCCC_HemostaticAgent");
                        Check(pouch.TryLoad(actor, stock) && pouch.Hemostatic == 1,
                            "Large pouch loads hemostatic agents separately");
                        Check(TcccUtility.CanTreat(actor, actor, TcccTreatment.Hemostasis)
                            && TcccUtility.CanTreat(actor, actor, TcccTreatment.AttachDrag)
                            && !TcccUtility.CanTreat(actor, actor, TcccTreatment.Analgesic)
                            && !TcccUtility.CanTreat(actor, actor, TcccTreatment.Hemostatic)
                            && !TcccUtility.CanTreat(actor, actor, TcccTreatment.Mist)
                            && TcccUtility.Gizmos(actor).Count() == 2,
                            "basic TCCC training unlocks only hemostasis and casualty movement");
                        actor.health.RemoveHediff(actor.health.hediffSet.GetFirstHediffOfDef(lowTraining));
                        actor.health.AddHediff(DefDatabase<HediffDef>.GetNamed("HD_TCCCTraining_HIGH"));
                        Check(Enum.GetValues(typeof(TcccTreatment)).Cast<TcccTreatment>()
                            .All(treatment => TcccUtility.CanTreat(actor, actor, treatment))
                            && TcccUtility.Gizmos(actor).Count() == 5,
                            "advanced TCCC training includes all basic and advanced skills");
                        Check(baselineBleed > 0 && TcccUtility.CanAct(actor), "self-care actor is capable and bleeding");
                        Check(TcccDrugs.SelectedDef(actor)?.defName == "BD_Fentanyl",
                            "Normal breathing selects fentanyl");
                        float addiction = DefDatabase<ThingDef>.GetNamed("BD_Fentanyl")
                            .comps.OfType<CompProperties_Drug>().First().addictiveness;
                        Check(TcccDrugs.Administer(actor, actor)
                            && Math.Abs(ObservedAddictionChance - addiction * .70f) < .000001f
                            && pouch.Charges == 7, "Full native fentanyl dose uses one charge and 70 percent addiction risk");
                        baselineBleed = actor.health.hediffSet.BleedRateTotal;
                        TcccUtility.Start(actor, actor, TcccTreatment.Hemostasis);
                        Next(1); break;
                    case 1:
                        if (WorkTicks < 199) break;
                        Check(Math.Abs(actor.health.hediffSet.BleedRateTotal / baselineBleed - 1f) < .02f, "no bleeding reduction before pressure stage");
                        Next(2); break;
                    case 2:
                        if (WorkTicks < 201) break;
                        Check(Math.Abs(actor.health.hediffSet.BleedRateTotal / baselineBleed - .30f) < .02f, "70 percent reduction starts after two thirds of the treatment");
                        Next(3); break;
                    case 3:
                        if (TcccUtility.Effect(actor, PartHemostasis.DressingDefName) == null) break;
                        Check(Math.Abs(actor.health.hediffSet.BleedRateTotal / baselineBleed - .20f) < .02f, "80 percent reduction after completed self-care");
                        Check(TcccUtility.Effect(actor, PartHemostasis.DressingDefName).expiresTick - Find.TickManager.TicksGame >= 29998, "completed effect lasts 12 hours");
                        TcccUtility.RemoveEffect(actor, PartHemostasis.DressingDefName);
                        TcccUtility.Start(actor, actor, TcccTreatment.Hemostasis);
                        Next(4); break;
                    case 4:
                        if (WorkTicks < 201) break;
                        actor.jobs.EndCurrentJob(JobCondition.InterruptForced);
                        Check(TcccUtility.Effect(actor, "HD_TCCC_Pressure") == null
                            && TcccUtility.Effect(actor, PartHemostasis.DressingDefName) == null, "interruption removes temporary pressure without granting completed effect");
                        TcccUtility.Start(actor, actor, TcccTreatment.Hemostatic);
                        Next(5); break;
                    case 5:
                        if (TcccUtility.Effect(actor, "HD_TCCC_Hemostatic") == null) break;
                        Check(Elapsed >= 60 && Elapsed < 75, "hemostatic administration takes 1 second");
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
                        savedCharges = pouch.Charges;
                        GameDataSaveLoader.SaveGame("TcccIntegrationCheckpoint");
                        GameDataSaveLoader.LoadGame("TcccIntegrationCheckpoint");
                        break;
                    case 9:
                        Check(actor.Map.GetComponent<MapComponent_TcccDragging>().IsDragging(actor)
                            && casualty.Spawned, "save/load preserves drag link and casualty");
                        pouch = MedicalPouchUtility.WornPouches(actor).Single();
                        Check(pouch.Charges == savedCharges && pouch.Hemostatic == 0,
                            "Mounted pouch charge and separate hemostatic stock survive save/load");
                        Thing medicine = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
                        actor.inventory.innerContainer.TryAdd(medicine);
                        medicine = actor.inventory.innerContainer.First(item => item.def == ThingDefOf.MedicineIndustrial);
                        int beforeLoad = pouch.Charges;
                        Check(pouch.TryLoad(actor, medicine) && pouch.Charges == Math.Min(8, beforeLoad + 2),
                            "One industrial medicine restores two treatment charges up to pouch capacity");
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
            Pawn pawn = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("HD_GW_HelodRifleman"), Faction.OfPlayer);
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
            Check(TcccRules.HemostasisDuration(3) == 300 && TcccRules.HemostasisDuration(6) == 1200
                && TcccRules.HemostaticTicks == 60 && TcccRules.DragAttachTicks == 60
                && PartHemostasis.DressingTicks == 30000 && TcccRules.DrugEffectTicks == 15000,
                "Updated TCCC timing boundaries");
            Check(Math.Abs(TcccDrugs.ReducedAddictionChance(.25f) - .175f) < .000001f,
                "TCCC drug administration reduces addiction chance by thirty percent");
        }
        private CompMedicalPouch EquipLargePouch(Pawn pawn)
        {
            ThingDef armorDef = DefDatabase<ThingDef>.GetNamed("HD_Apparel_IBTVAssault");
            Apparel armor = (Apparel)ThingMaker.MakeThing(armorDef, GenStuff.DefaultStuffFor(armorDef));
            pawn.apparel.Wear(armor, false);
            var mounted = armor.TryGetComp<CompModularArmor>().InstallPalsPart(
                DefDatabase<ModularArmorPartDef>.GetNamed("HD_IOTVPart_MediL"),
                DefDatabase<ModularArmorPalsPanelDef>.GetNamed("HD_IOTV_PALS_Front"), 0, 0,
                ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_ModularPart_MediL")));
            Check(mounted?.InstalledItem != null, "Large medical pouch is installed on body armor");
            var pouch = mounted.InstalledItem.TryGetComp<CompMedicalPouch>();
            Check(armor.Wearer == pawn, "Body armor is worn by test caregiver");
            Check(armor.TryGetComp<CompModularArmor>().Props.armorTags?.Contains("HD_IOTVGen4") == true,
                "Body armor has the expected modular armor tag");
            Check(MedicalPouchUtility.WornPouches(pawn).Contains(pouch),
                "Installed large pouch is available to its wearer");
            return pouch;
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

