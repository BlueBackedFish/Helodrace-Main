#if HELOD_REPRODUCTION_TESTS
// Isolated -quicktest validation build only; excluded from the shipped assembly.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    public sealed class HelodReproductionChecks : GameComponent
    {
        private bool finished;
        private string Results => Path.Combine(GenFilePaths.SaveDataFolderPath, "reproduction-results.txt");
        public HelodReproductionChecks(Game game) { }

        private void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            File.AppendAllText(Results, "PASS " + message + "\n");
        }

        private static void Patched(MethodBase method, Type patch)
        {
            Patches info = Harmony.GetPatchInfo(method);
            if (info == null || !info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers)
                .Any(p => p.PatchMethod.DeclaringType == patch))
                throw new InvalidOperationException("Missing patch " + patch.Name + " on " + method.Name);
        }

        private static Pawn Make(PawnKindDef kind, Gender gender, IntVec3 cell)
        {
            var request = new PawnGenerationRequest(kind, Faction.OfPlayer, fixedGender: gender,
                fixedBiologicalAge: 25f, fixedChronologicalAge: 25f, forceNoBackstory: false);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            foreach (TraitDef def in new[] { TraitDefOf.Gay, TraitDefOf.Bisexual, TraitDefOf.Asexual })
            {
                Trait trait = pawn.story.traits.GetTrait(def);
                if (trait != null) pawn.story.traits.RemoveTrait(trait);
            }
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs.ToList())
                if (hediff.def.defName.Contains("Sterilized") || hediff.def.defName.Contains("Vasectomy")
                    || hediff.def.defName.Contains("Lactating") || hediff.def.defName.Contains("Infertile"))
                    pawn.health.RemoveHediff(hediff);
            GenSpawn.Spawn(pawn, cell, Find.CurrentMap);
            return pawn;
        }

        public override void GameComponentTick()
        {
            if (finished || Find.CurrentMap == null) return;
            finished = true;
            try
            {
                File.WriteAllText(Results, "Helod reproduction checks\n");
                Patched(AccessTools.Method(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.SecondaryLovinChanceFactor)), typeof(Patch_HelodCrossRaceRomance));
                Patched(AccessTools.Method(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.CompatibilityWith)), typeof(Patch_HelodCrossRaceRomance));
                Patched(AccessTools.Method(typeof(PregnancyUtility), nameof(PregnancyUtility.CanEverProduceChild)), typeof(Patch_HelodCanProduceChild));
                Patched(AccessTools.Method(typeof(PregnancyUtility), nameof(PregnancyUtility.PregnancyChanceForPartners)), typeof(Patch_HelodPartnerPregnancyChance));
                Patched(AccessTools.Method(typeof(HumanOvum), "CanFertilizeReport"), typeof(Patch_HelodOvumFertilize));
                Patched(AccessTools.Method(typeof(HumanEmbryo), "CanImplantReport"), typeof(Patch_HelodEmbryoImplant));
                Check(true, "all reproduction and romance patches applied");

                Map map = Find.CurrentMap;
                IntVec3 center = map.AllCells.OrderBy(c => c.DistanceToSquared(map.Center)).First(c =>
                    CellRect.CenteredOn(c, 3).Cells.All(p => p.InBounds(map) && p.Standable(map) && p.GetEdifice(map) == null));
                PawnKindDef helodKind = DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist");
                PawnKindDef maleKind = DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodMaleColonist");
                Pawn helodF = Make(helodKind, Gender.Female, center);
                Pawn helodM = Make(maleKind, Gender.Male, center + new IntVec3(1, 0, 0));
                Pawn humanM = Make(PawnKindDefOf.Colonist, Gender.Male, center + new IntVec3(0, 0, 1));
                Pawn humanF = Make(PawnKindDefOf.Colonist, Gender.Female, center + new IntVec3(1, 0, 1));
                Pawn generatedMale = PawnGenerator.GeneratePawn(maleKind, Faction.OfPlayer);

                Check(generatedMale.def == HelodRace.Def && generatedMale.gender == Gender.Male,
                    "male Helod colonist kind generates a male Helod");
                int males = 0;
                const int samples = 300;
                for (int i = 0; i < samples; i++)
                {
                    Pawn sample = PawnGenerator.GeneratePawn(helodKind, Faction.OfPlayer);
                    if (sample.gender == Gender.Male) males++;
                    sample.Destroy();
                }
                // 0.3 male chance: 3.5 sigma bounds over 300 samples.
                Check(males >= 63 && males <= 117, "ordinary Helod generation yields males at ~30% (" + males + "/" + samples + ")");
                Check(helodM.story.bodyType == BodyTypeDefOf.Female && HelodRace.HeadTypes.Contains(helodM.story.headType),
                    "male Helod uses the female body and Helod head textures");
                Check(helodM.Drawer.renderer.BaseHeadOffsetAt(Rot4.South) == helodF.Drawer.renderer.BaseHeadOffsetAt(Rot4.South),
                    "male Helod head sits where the female head does");

                string crossKey = "HD_Race_CrossRaceReproduction";
                AcceptanceReport cross1 = PregnancyUtility.CanEverProduceChild(helodF, humanM);
                AcceptanceReport cross2 = PregnancyUtility.CanEverProduceChild(humanF, helodM);
                string expected = HelodReproduction.CrossRaceReport(helodF, humanM).Reason;
                Check(!cross1.Accepted && cross1.Reason == expected, "Helod woman and human man cannot conceive: " + cross1.Reason);
                Check(!cross2.Accepted && cross2.Reason.Length > 0 && crossKey.CanTranslate(), "human woman and Helod man cannot conceive");
                AcceptanceReport same = PregnancyUtility.CanEverProduceChild(helodF, helodM);
                Check(same.Accepted, "Helod pair can conceive (" + same.Reason + ")");
                Check(PregnancyUtility.CanEverProduceChild(humanF, humanM).Accepted, "human pair keeps vanilla conception");
                Check(PregnancyUtility.PregnancyChanceForPartners(helodF, humanM) == 0f
                    && PregnancyUtility.PregnancyChanceForPartners(humanF, helodM) == 0f, "cross-race lovin' pregnancy chance is zero");
                Check(PregnancyUtility.PregnancyChanceForPartners(helodF, helodM) > 0f, "Helod pair lovin' pregnancy chance stays positive");

                Check(helodF.relations.SecondaryLovinChanceFactor(humanM) > 0f && humanM.relations.SecondaryLovinChanceFactor(helodF) > 0f,
                    "Helod and human can be romantically attracted");
                Check(helodF.relations.SecondaryLovinChanceFactor(helodM) > 0f && humanF.relations.SecondaryLovinChanceFactor(humanM) > 0f,
                    "same-race attraction is unchanged");
                Check(helodF.relations.CompatibilityWith(humanM) != 0f, "Helod and human have social compatibility");

                helodF.relations.AddDirectRelation(PawnRelationDefOf.Lover, humanM);
                Check(LovePartnerRelationUtility.LovePartnerRelationExists(helodF, humanM), "Helod and human can become lovers");
                helodF.relations.TryRemoveDirectRelation(PawnRelationDefOf.Lover, humanM);
                helodF.relations.AddDirectRelation(PawnRelationDefOf.Spouse, humanM);
                Check(helodF.GetSpouses(false).Contains(humanM), "Helod and human can marry");
                helodF.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.GotSomeLovin, humanM);
                Check(helodF.needs.mood.thoughts.memories.Memories.Any(m => m.def == ThoughtDefOf.GotSomeLovin && m.otherPawn == humanM),
                    "Helod keeps a lovin' memory of a human partner");

                var ovum = (HumanOvum)ThingMaker.MakeThing(ThingDefOf.HumanOvum);
                ovum.TryGetComp<CompHasPawnSources>().AddSource(helodF);
                GenSpawn.Spawn(ovum, center + new IntVec3(-1, 0, 0), map);
                MethodInfo fertilize = AccessTools.Method(typeof(HumanOvum), "CanFertilizeReport");
                var ovumHuman = (AcceptanceReport)fertilize.Invoke(ovum, new object[] { humanM });
                var ovumHelod = (AcceptanceReport)fertilize.Invoke(ovum, new object[] { helodM });
                Check(!ovumHuman.Accepted && ovumHuman.Reason == expected, "human man cannot fertilize a Helod ovum");
                Check(ovumHelod.Accepted, "Helod man can fertilize a Helod ovum (" + ovumHelod.Reason + ")");

                var embryo = (HumanEmbryo)ThingMaker.MakeThing(ThingDefOf.HumanEmbryo);
                var sources = embryo.TryGetComp<CompHasPawnSources>();
                sources.AddSource(humanF);
                sources.AddSource(humanM);
                GenSpawn.Spawn(embryo, center + new IntVec3(-1, 0, 1), map);
                MethodInfo implant = AccessTools.Method(typeof(HumanEmbryo), "CanImplantReport");
                var implantHelod = (AcceptanceReport)implant.Invoke(embryo, new object[] { helodF });
                var implantHuman = (AcceptanceReport)implant.Invoke(embryo, new object[] { humanF });
                Check(!implantHelod.Accepted, "human embryo cannot be implanted into a Helod (" + implantHelod.Reason + ")");
                Check(implantHuman.Accepted, "human embryo can be implanted into a human (" + implantHuman.Reason + ")");

                File.AppendAllText(Results, "COMPLETE\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(Results, "FAIL " + ex + "\n");
            }
            Waylith.EngineApplication.Instance.Quit(0);
        }
    }
}
#endif
