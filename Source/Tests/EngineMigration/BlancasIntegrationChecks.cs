using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace.ModernWar;
using Helodrace.Tactical;
using RimWorld;
using Verse;

namespace Helodrace.Tests
{
    internal static class BlancasIntegrationChecks
    {
        private static CompDrug observedDrug;
        private static float observedChance;
        private static bool observedDose;
        private static int observations;

        private static void Observe(CompDrug __instance, Pawn ingester)
        {
            observedDrug = __instance;
            observedChance = __instance.Props.addictiveness;
            observedDose = TcccDrugs.Current?.drug == __instance.parent && TcccDrugs.Current?.patient == ingester;
            observations++;
        }

        internal static void Run(Action<bool, string> check)
        {
            var harmony = new Harmony("Helodrace.Tests.BlancasIntegration");
            var originalContext = TcccDrugs.Current;
            var created = new List<Pawn>();
            try
            {
                harmony.Patch(AccessTools.Method(typeof(CompDrug), nameof(CompDrug.PostIngested)),
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(BlancasIntegrationChecks), nameof(Observe))) { priority = Priority.Last });
                Pawn Spawn(bool helod = false)
                {
                    PawnKindDef kind = helod ? DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist") : PawnKindDefOf.Colonist;
                    Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind,
                        Faction.OfPlayer, fixedBiologicalAge: 25, forcedXenotype: helod ? null : XenotypeDefOf.Baseliner));
                    GenSpawn.Spawn(pawn, Find.CurrentMap.Center + new IntVec3(created.Count, 0, 0), Find.CurrentMap);
                    created.Add(pawn);
                    return pawn;
                }
                Pawn actor = Spawn(helod: true);
                actor.drafter.Drafted = true;
                actor.health.AddHediff(DefDatabase<HediffDef>.GetNamed("HD_TCCCTraining_HIGH"));
                actor.apparel.DestroyAll();
                ThingDef armorDef = DefDatabase<ThingDef>.GetNamed("HD_Apparel_IBTVAssault");
                var armor = (Apparel)ThingMaker.MakeThing(armorDef, GenStuff.DefaultStuffFor(armorDef));
                actor.apparel.Wear(armor, false);
                check(armor.Wearer == actor, "Helod caregiver is allowed to wear the dedicated modular armor");
                var modularArmor = armor.TryGetComp<CompModularArmor>();
                var panel = DefDatabase<ModularArmorPalsPanelDef>.GetNamed("HD_IOTV_PALS_Lower");
                foreach (var installed in modularArmor.InstalledParts.Where(p => p.IsPalsMounted && p.palsPanel == panel).ToArray())
                    check(modularArmor.RemovePalsPart(installed), "clear the selected medical pouch panel in the fixture");
                var mounted = modularArmor.InstallPalsPart(
                    DefDatabase<ModularArmorPartDef>.GetNamed("HD_IOTVPart_MediL"),
                    panel, 0, 0,
                    ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_ModularPart_MediL")));
                var pouch = mounted?.InstalledItem?.TryGetComp<CompMedicalPouch>();
                check(pouch != null && MedicalPouchUtility.WornPouches(actor).Contains(pouch), "large medical pouch is installed and available to the caregiver");
                foreach (string name in new[] { "BD_Fentanyl", "BD_Ketamine" })
                {
                    Pawn patient = Spawn();
                    var cut = HediffMaker.MakeHediff(HediffDefOf.Cut, patient,
                        patient.RaceProps.body.AllParts.First(p => p.def == BodyPartDefOf.Leg));
                    cut.Severity = 2;
                    patient.health.AddHediff(cut);
                    if (name == "BD_Ketamine")
                        patient.health.AddHediff(new HediffDef {
                            defName = "MigrationBreathingRestriction", label = "breathing fixture", hediffClass = typeof(HediffWithComps),
                            stages = new List<HediffStage> { new HediffStage { capMods = new List<PawnCapacityModifier> {
                                new PawnCapacityModifier { capacity = PawnCapacityDefOf.Breathing, setMax = .6f }
                            } } }
                        });
                    ThingDef drugDef = DefDatabase<ThingDef>.GetNamed(name);
                    var originalProperties = drugDef.GetCompProperties<CompProperties_Drug>();
                    float chance = originalProperties.addictiveness;
                    check(TcccDrugs.SelectedDef(patient) == drugDef, name + " selected from actual patient breathing capacity");
                    check(patient.health.hediffSet.PainTotal > 0 && TcccUtility.CanTreat(actor, patient, TcccTreatment.Analgesic), name + " patient is painful and eligible for actual TCCC care");
                    int charges = pouch.Charges;
                    observations = 0; observedDrug = null;
                    var sentinel = new TcccDrugs.DoseContext();
                    TcccDrugs.Current = sentinel;
                    Rand.PushState(170701);
                    try { check(TcccDrugs.Administer(actor, patient), name + " actual Ingested path administered successfully"); }
                    finally { Rand.PopState(); }
                    check(pouch.Charges == charges - 1, name + " spends exactly one pouch charge");
                    check(observations == 1 && observedDose && Math.Abs(observedChance - chance * .7f) < 1e-7f,
                        name + " actual CompDrug receives 70 percent addiction risk within its dose context");
                    check(ReferenceEquals(TcccDrugs.Current, sentinel) && ReferenceEquals(observedDrug.props, originalProperties),
                        name + " restores the enclosing context and original CompDrug properties");
                    var highDef = TcccDrugs.HighDef(drugDef);
                    var doseHigh = patient.health.hediffSet.GetFirstHediffOfDef(highDef);
                    check(doseHigh != null && patient.health.hediffSet.hediffs.Count(h => h.def == highDef) == 1,
                        name + " applies one actual analgesic high without duplication");

                    Pawn normalPatient = Spawn();
                    Thing normalDrug = ThingMaker.MakeThing(drugDef);
                    TcccDrugs.Current = null;
                    observations = 0;
                    Rand.PushState(170701);
                    try { normalDrug.Ingested(normalPatient, 0); }
                    finally { Rand.PopState(); if (!normalDrug.Destroyed) normalDrug.Destroy(); }
                    check(observations == 1 && !observedDose && observedChance == chance,
                        name + " ordinary ingestion keeps its published addiction risk");
                    var normalHigh = normalPatient.health.hediffSet.GetFirstHediffOfDef(highDef);
                    check(normalHigh != null && Math.Abs(normalHigh.Severity - doseHigh.Severity) < 1e-6f,
                        name + " TCCC and ordinary ingestion apply the same full-dose high severity");
                    check(ReferenceEquals(drugDef.GetCompProperties<CompProperties_Drug>(), originalProperties)
                        && originalProperties.addictiveness == chance, name + " shared definition is unchanged after both ingestion paths");
                }

                foreach (float breathing in new[] { .69999f, .7f, .70001f })
                    check(TcccDrugs.DrugNameForBreathing(breathing) == (breathing < .7f ? "BD_Ketamine" : "BD_Fentanyl"),
                        "TCCC selection boundary at breathing " + breathing);
                foreach (string name in new[] { "BD_Desoxyn", "BD_Benzedrine", "BD_Dexedrine", "BD_Ketamine", "BD_Ambien", "BD_Restoril" })
                {
                    ThingDef drug = DefDatabase<ThingDef>.GetNamed(name);
                    var recipes = DefDatabase<RecipeDef>.AllDefsListForReading.Where(r => r.products?.Any(p => p.thingDef == drug) == true).ToList();
                    // The published base definition declares bulkRecipeCount=4;
                    // actual 1.7 generates both the single and four-batch recipes.
                    check(recipes.Count == 2 && string.Join(",", recipes.Select(r => r.adjustedCount).OrderBy(n => n)) == "1,4",
                        name + " retains its single and four-batch generated recipes");
                    ThingDef reactor = DefDatabase<ThingDef>.GetNamed("HD_StirredTankReactor");
                    check(recipes.All(r => r.researchPrerequisite?.defName == "HelodTotalSynthesisMedicine"
                        && r.recipeUsers?.Contains(reactor) == true && reactor.AllRecipes.Contains(r)
                        && r.requiredGiverWorkType == WorkTypeDefOf.Crafting),
                        name + " both recipes have the mandatory research, actual stirred tank menu and crafting work type");
                }
                var dependency = DefDatabase<GeneDef>.GetNamed("HD_Gene_AmphetamineDependency");
                var chemical = DefDatabase<ChemicalDef>.GetNamed("BD_Amphetamines");
                check(dependency.chemical == chemical && dependency.geneClass == typeof(Gene_ChemicalDependency)
                    && dependency.minAgeActive == 13, "Main dependency gene binds the published Blancas chemical and built-in game gene class");
            }
            finally
            {
                TcccDrugs.Current = originalContext;
                harmony.UnpatchAll(harmony.Id);
                foreach (Pawn pawn in created) pawn.Destroy(DestroyMode.Vanish);
                observedDrug = null;
            }
        }
    }
}
