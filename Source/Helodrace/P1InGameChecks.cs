#if P1_TESTS
// Compiled only for the isolated -quicktest validation build, never into the shipped DLL.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public sealed class P1InGameChecks : GameComponent
    {
        private bool finished;
        private string Results => Path.Combine(GenFilePaths.SaveDataFolderPath, "p1-results.txt");

        public P1InGameChecks(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (finished || Find.CurrentMap == null)
            {
                return;
            }

            finished = true;
            try
            {
                File.WriteAllText(Results, "Helodrace P1 isolated in-game validation\n");
                CheckRecipePlacement();
                CheckApparel();
                CheckMechanicalTemperature();
                CheckBtxJobSelection();
                CheckBuildingMaterials();
                CheckFacialAnimation();
                CaptureBuildingGraphics();
                Finish(true, "All P1 in-game checks passed");
            }
            catch (Exception ex)
            {
                Finish(false, ex.ToString());
            }
        }

        private void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }

            File.AppendAllText(Results, "PASS " + message + "\n");
        }

        private void Finish(bool success, string message)
        {
            File.AppendAllText(Results, (success ? "COMPLETE " : "FAIL ") + message + "\n");
            Application.Quit(success ? 0 : 1);
        }

        private void CheckRecipePlacement()
        {
            Dictionary<string, string[]> expected = new Dictionary<string, string[]>
            {
                { "HD_MakePressComponent", new[] { "HD_LineShaftHydraulicPress" } },
                { "HD_MakePressComponentBulk5", new[] { "HD_LineShaftHydraulicPress" } },
                { "HD_MakePressComponentBulk10", new[] { "HD_LineShaftHydraulicPress" } },
                { "HD_MillGeneralMachinePart", new[] { "HD_LineShaftMillingMachine" } },
                { "HD_MillGeneralMachinePartBulk5", new[] { "HD_LineShaftMillingMachine" } },
                { "HD_MillGeneralMachinePartBulk10", new[] { "HD_LineShaftMillingMachine" } },
                { "HD_TurnGeneralMachinePart", new[] { "HD_TreadleLathe", "HD_LineShaftTurretLathe" } },
                { "HD_TurnGeneralMachinePartBulk5", new[] { "HD_TreadleLathe", "HD_LineShaftTurretLathe" } },
                { "HD_TurnGeneralMachinePartBulk10", new[] { "HD_TreadleLathe", "HD_LineShaftTurretLathe" } },
                { "HD_RollUniformSteelPlate", new[] { "HD_LineShaftRollingMachine" } }
            };

            foreach (KeyValuePair<string, string[]> pair in expected)
            {
                RecipeDef recipe = DefDatabase<RecipeDef>.GetNamed(pair.Key);
                string[] actual = recipe.recipeUsers.Select(user => user.defName).ToArray();
                Check(actual.SequenceEqual(pair.Value), pair.Key + " resolves to its mapped worktable(s)");
                foreach (string userName in pair.Value)
                {
                    ThingDef worktable = DefDatabase<ThingDef>.GetNamed(userName);
                    Check(worktable.AllRecipes.Contains(recipe), userName + " exposes " + pair.Key);
                }
            }

            ThingDef helmet = DefDatabase<ThingDef>.GetNamed("HD_Apparel_GreatWarM1Helmet");
            ThingDef press = DefDatabase<ThingDef>.GetNamed("HD_LineShaftHydraulicPress");
            ThingDef basic = DefDatabase<ThingDef>.GetNamed("HD_BasicWorkbench");
            Check(press.AllRecipes.Any(recipe => recipe.ProducedThingDef == helmet), "hydraulic press exposes the M1 helmet recipe");
            Check(!basic.AllRecipes.Any(recipe => recipe.ProducedThingDef == helmet), "basic workbench no longer exposes the M1 helmet recipe");
            Check(helmet.apparel.drawData != null
                && Math.Abs(helmet.apparel.drawData.OffsetForRot(Rot4.North).z - 0.1f) < 0.0001f,
                "M1 helmet loaded with the corrected vertical draw offset");
        }

        private void CheckApparel()
        {
            ThingDef formal = DefDatabase<ThingDef>.GetNamed("HD_Apparel_WildWestFormalShirt");
            BodyPartGroupDef legs = DefDatabase<BodyPartGroupDef>.GetNamed("Legs");
            Check(formal.apparel.bodyPartGroups.Contains(legs), "formal shirt covers the Legs body-part group");
            Check(formal.apparel.layers.Contains(ApparelLayerDefOf.OnSkin), "formal shirt remains an OnSkin garment");

            ThingDef flak = DefDatabase<ThingDef>.GetNamed("HD_Apparel_M1952AFlakJacket");
            ThingCategoryDef armor = DefDatabase<ThingCategoryDef>.GetNamed("ApparelArmor");
            Check(flak.thingCategories.Contains(armor), "M1952 flak jacket is in the armor category");
            Check(flak.apparel.layers.Contains(ApparelLayerDefOf.Shell), "M1952 flak jacket loads on the Shell layer");
        }

        private void CheckMechanicalTemperature()
        {
            float expected = 2f * GenTicks.TickRareInterval / GenTicks.TicksPerRealSecond;
            float first = CompMechanicalTemperatureControl.EnergyPerRareTick(-2f, 1f);
            Check(Math.Abs(first - expected) < 0.0001f, "heat pump converts -2 W into one complete rare-tick energy quantum");
            bool deterministic = true;
            for (int index = 0; index < 100; index++)
            {
                deterministic &= Math.Abs(CompMechanicalTemperatureControl.EnergyPerRareTick(-2f, 1f) - first) < 0.000001f;
            }
            Check(deterministic, "heat pump rare-tick transfer is deterministic across 100 samples");
        }

        private void CheckBtxJobSelection()
        {
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(pawn, Find.CurrentMap.Center, Find.CurrentMap);
            NeedDef needDef = DefDatabase<NeedDef>.GetNamed(BTXUtility.ChemicalNeedDefName);
            Need_Chemical need = new Need_Chemical(pawn);
            AccessTools.Field(typeof(Need), "def").SetValue(need, needDef);
            Hediff addiction = HediffMaker.MakeHediff(
                DefDatabase<HediffDef>.GetNamed("HD_BTXAddiction"), pawn);
            pawn.health.AddHediff(addiction);
            JobGiver_SatisfyChemicalNeed giver = new JobGiver_SatisfyChemicalNeed();
            MethodInfo shouldSatisfy = AccessTools.Method(typeof(JobGiver_SatisfyChemicalNeed), "ShouldSatisfy");
            MethodInfo findDrug = AccessTools.Method(typeof(JobGiver_SatisfyChemicalNeed), "FindDrugFor");

            need.CurLevel = 0.34f;
            Check((bool)shouldSatisfy.Invoke(giver, new object[] { need }), "BTX job giver activates at 34 percent before deficiency");
            need.CurLevel = 0.36f;
            Check(!(bool)shouldSatisfy.Invoke(giver, new object[] { need }), "BTX job giver does not expand past the 35 percent refill buffer");
            Check(BTXUtility.AutomaticRefillThreshold > BTXUtility.DeficiencyNeedThreshold,
                "BTX automatic refill threshold precedes deficiency");

            ThingDef naphthaDef = DefDatabase<ThingDef>.GetNamed("HD_Naphtha");
            ThingDef aromaticDef = DefDatabase<ThingDef>.GetNamed("HD_AromaticBaseOil");
            pawn.drugs.CurrentPolicy[naphthaDef].allowedForAddiction = true;
            pawn.drugs.CurrentPolicy[aromaticDef].allowedForAddiction = true;

            Thing mapSource = ThingMaker.MakeThing(naphthaDef);
            GenSpawn.Spawn(mapSource, pawn.Position + IntVec3.East, Find.CurrentMap);
            need.CurLevel = 0.34f;
            Check(ReferenceEquals(findDrug.Invoke(giver, new object[] { pawn, need }), mapSource),
                "BTX job giver finds a reachable map source");

            Pawn reservingPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(reservingPawn, pawn.Position + IntVec3.South, Find.CurrentMap);
            Job reservationJob = JobMaker.MakeJob(JobDefOf.HaulToCell, mapSource, pawn.Position + IntVec3.North);
            Check(reservingPawn.Reserve(mapSource, reservationJob, 1, -1, null, true),
                "another pawn can reserve the BTX source for the contention test");
            Check(findDrug.Invoke(giver, new object[] { pawn, need }) == null,
                "BTX job giver rejects a source reserved by another pawn");
            Find.CurrentMap.reservationManager.ReleaseAllClaimedBy(reservingPawn);

            mapSource.SetForbidden(true, false);
            Check(findDrug.Invoke(giver, new object[] { pawn, need }) == null,
                "BTX job giver rejects a forbidden map source");

            Thing inventorySource = ThingMaker.MakeThing(aromaticDef);
            pawn.inventory.innerContainer.TryAdd(inventorySource);
            Check(ReferenceEquals(findDrug.Invoke(giver, new object[] { pawn, need }), inventorySource),
                "BTX job giver prefers an available inventory source");
        }

        private void CheckBuildingMaterials()
        {
            CheckMaterials("HD_CableToolRig", new[] { Rot4.North });
            CheckMaterials("HD_BasicPumpJack", new[] { Rot4.North });
            CheckMaterials("HD_LineShaftHydraulicPress", new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West });
            CheckMaterials("HD_LineShaftRollingMachine", new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West });
        }

        private void CheckMaterials(string defName, IEnumerable<Rot4> rotations)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(defName);
            Check(def.graphicData.shaderType == ShaderTypeDefOf.CutoutComplex, defName + " uses CutoutComplex");
            foreach (Rot4 rotation in rotations)
            {
                Graphic graphic = def.graphic ?? def.graphicData.Graphic;
                Material material = graphic.MatAt(rotation);
                Texture main = material.mainTexture;
                Texture mask = material.GetTexture("_MaskTex");
                Check(material.shader == ShaderDatabase.CutoutComplex, defName + " " + rotation + " loaded the CutoutComplex shader");
                Check(main != null && mask != null && mask != BaseContent.BadTex,
                    defName + " " + rotation + " loaded its main and mask textures");
                Check(main.width == mask.width && main.height == mask.height,
                    defName + " " + rotation + " main/mask dimensions match in Unity");
            }
        }

        private void CheckFacialAnimation()
        {
            Type lidShapeType = AccessTools.TypeByName("FacialAnimation.LidShapeDef");
            Type animationType = AccessTools.TypeByName("FacialAnimation.FaceAnimationDef");
            Check(lidShapeType != null && animationType != null, "Facial Animation P1 def types are loaded");
            foreach (string shapeName in new[] { "cry", "dead" })
            {
                object shape = GetNamedDef(lidShapeType, shapeName);
                bool disabled = (bool)AccessTools.Field(lidShapeType, "disableEyeball").GetValue(shape);
                Check(disabled, shapeName + " hides eyeballs after live patch application");
            }

            object highPain = GetNamedDef(animationType, "HD_FA_HelodPainHigh");
            IList frames = (IList)AccessTools.Field(animationType, "animationFrames").GetValue(highPain);
            object firstFrame = frames[0];
            Def mouth = (Def)AccessTools.Field(firstFrame.GetType(), "mouthShapeDef").GetValue(firstFrame);
            Check(mouth.defName == "down", "live high-pain expression uses the down mouth");

            Type partNode = AccessTools.TypeByName("FacialAnimation.NLFacialAnimationPartNode");
            MethodInfo graphicFor = AccessTools.Method(partNode, "GraphicFor");
            Patches patches = Harmony.GetPatchInfo(graphicFor);
            Check(patches != null && patches.Owners.Contains("BlueBackedFish.Helodrace.Facial.DeadLid"),
                "dead-lid Harmony visibility patch is installed");
        }

        private static object GetNamedDef(Type defType, string defName)
        {
            Type database = typeof(DefDatabase<>).MakeGenericType(defType);
            MethodInfo getNamed = database.GetMethod("GetNamed", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string), typeof(bool) }, null);
            return getNamed.Invoke(null, new object[] { defName, true });
        }

        private void CaptureBuildingGraphics()
        {
            string[] defs =
            {
                "HD_CableToolRig", "HD_BasicPumpJack", "HD_LineShaftHydraulicPress", "HD_LineShaftRollingMachine"
            };
            GameObject cameraObject = new GameObject("P1 building validation camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 25f;
            camera.aspect = 1.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.12f);
            camera.transform.position = new Vector3(0f, 100f, 0f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            RenderTexture target = new RenderTexture(1800, 1200, 24);
            camera.targetTexture = target;

            float[] topX = { -18f, -6f, 6f, 18f };
            for (int index = 0; index < 2; index++)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamed(defs[index]);
                Thing wood = ThingMaker.MakeThing(def, ThingDefOf.WoodLog);
                Thing steel = ThingMaker.MakeThing(def, ThingDefOf.Steel);
                wood.Graphic.Draw(new Vector3(topX[index * 2], 0f, 18f), Rot4.North, wood);
                steel.Graphic.Draw(new Vector3(topX[index * 2 + 1], 0f, 18f), Rot4.North, steel);
            }

            DrawMaterialRow(defs[2], ThingDefOf.WoodLog, 8f, topX);
            DrawMaterialRow(defs[2], ThingDefOf.Steel, 1f, topX);
            DrawMaterialRow(defs[3], ThingDefOf.WoodLog, -7f, topX);
            DrawMaterialRow(defs[3], ThingDefOf.Steel, -15f, topX);

            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(1800, 1200, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1800, 1200), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            byte[] png = (byte[])AccessTools.TypeByName("UnityEngine.ImageConversion").GetMethod("EncodeToPNG",
                new[] { typeof(Texture2D) }).Invoke(null, new object[] { image });
            File.WriteAllBytes(Path.Combine(GenFilePaths.SaveDataFolderPath, "p1-building-masks-render.png"), png);
            camera.targetTexture = null;
            UnityEngine.Object.Destroy(image);
            UnityEngine.Object.Destroy(target);
            UnityEngine.Object.Destroy(cameraObject);
            Check(png.Length > 10000, "building graphics rendered to an inspectable PNG");
        }

        private static void DrawMaterialRow(string defName, ThingDef stuff, float z, float[] positions)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(defName);
            Rot4[] rotations = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };
            for (int index = 0; index < rotations.Length; index++)
            {
                Thing building = ThingMaker.MakeThing(def, stuff);
                building.Graphic.Draw(new Vector3(positions[index], 0f, z), rotations[index], building);
            }
        }
    }
}
#endif
