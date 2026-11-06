using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tests
{
    // Main + Blancas: Helod go through Blancas stimulant crashes on the patched weak stage and lose BTX.
    internal static class BlancasHelodCrashChecks
    {
        private static readonly string[] Drugs = { "Desoxyn", "Benzedrine", "Dexedrine", "Modafinil", "CocaineCola" };

        internal static void Run(Action<bool, string> check)
        {
            var created = new List<Pawn>();
            NeedDef btx = DefDatabase<NeedDef>.GetNamed("Chemical_BTX");
            HediffDef deepSleep = DefDatabase<HediffDef>.GetNamed("BD_DeepSleep");
            Pawn Spawn(bool helod)
            {
                PawnKindDef kind = helod ? DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist") : PawnKindDefOf.Colonist;
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction.OfPlayer, fixedBiologicalAge: 25,
                    forcedXenotype: helod ? null : XenotypeDefOf.Baseliner, allowAddictions: false));
                GenSpawn.Spawn(pawn, Find.CurrentMap.Center + new IntVec3(created.Count % 10, 0, 3 + created.Count / 10), Find.CurrentMap);
                created.Add(pawn);
                return pawn;
            }
            Hediff Crash(Pawn pawn, string drug)
            {
                Thing item = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("BD_" + drug));
                try { item.Ingested(pawn, 0); } finally { if (!item.Destroyed) item.Destroy(); }
                HediffDef effectDef = DefDatabase<HediffDef>.GetNamed("BD_" + drug + "High");
                pawn.health.hediffSet.GetFirstHediffOfDef(effectDef).Severity = .1f; // below every crash threshold
                pawn.health.HealthTickInterval(1);
                return pawn.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("BD_" + drug + "Crash"));
            }
            int startTick = Find.TickManager.TicksGame;
            try
            {
                foreach (string drug in Drugs)
                {
                    Pawn helod = Spawn(true), baseliner = Spawn(false);
                    check(HelodRace.IsHelod(helod) && !HelodRace.IsHelod(baseliner), drug + " fixture has one Helod and one Baseliner");
                    Hediff h = Crash(helod, drug), b = Crash(baseliner, drug);
                    check(h != null && b != null && h.GetType().FullName == "HelodBlancasDrug.Hediff_DrugCrash", drug + " both pawns enter the Blancas crash hediff");
                    check(h.CurStageIndex == 1 && h.CurStage == h.def.stages[1] && b.CurStageIndex == 0 && b.CurStage == h.def.stages[0],
                        drug + " Helod use the weak BTX stage; Baseliners keep the original crash stage");
                    float Offset(HediffStage s) => s.capMods.Where(m => m.capacity == PawnCapacityDefOf.Consciousness).Sum(m => m.offset);
                    check(Math.Abs(Offset(h.CurStage) - .2f * Offset(b.CurStage)) < 1e-5f && Math.Abs(Offset(h.CurStage)) < Math.Abs(Offset(b.CurStage)),
                        drug + " Helod consciousness penalty is 20% of the original " + Offset(h.CurStage) + " vs " + Offset(b.CurStage));
                    float perDay = h.def.GetModExtension<HelodBlancasDrug.DrugCrashVariants>().variants.Single().needFallPerDay;
                    Need need = helod.needs.TryGetNeed(btx);
                    check(need != null && baseliner.needs.TryGetNeed(btx) == null, drug + " only the Helod has a BTX need");
                    need.CurLevel = .8f; float before = need.CurLevel;
                    h.TickInterval(6000);
                    check(Math.Abs(before - need.CurLevel - perDay * .1f) < 1e-5f && perDay > 0f, drug + " crash drains BTX by " + perDay + "/day (0.1 day: " + (before - need.CurLevel) + ")");
                    check(h.Description.Contains("HD_BlancasCrash_HelodNote".Translate()) && !b.Description.Contains("HD_BlancasCrash_HelodNote".Translate()),
                        drug + " only the Helod crash description carries the BTX note");
                    ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail("BD_" + drug + "CrashThought");
                    if (thought != null)
                    {
                        ThoughtState hs = thought.Worker.CurrentState(helod), bs = thought.Worker.CurrentState(baseliner);
                        check(hs.Active && hs.StageIndex == 1 && bs.Active && bs.StageIndex == 0
                            && Math.Abs(thought.stages[1].baseMoodEffect) < Math.Abs(thought.stages[0].baseMoodEffect),
                            drug + " Helod crash mood uses the milder thought stage " + thought.stages[1].baseMoodEffect + " vs " + thought.stages[0].baseMoodEffect);
                    }
                    var recovery = h.def.CompProps<HelodBlancasDrug.HediffCompProperties_OnRemoval>().hediffToGive;
                    helod.health.AddHediff(deepSleep);
                    for (int i = 0; i < 1000 && helod.health.hediffSet.hediffs.Contains(h); i++)
                    {
                        Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + 1);
                        helod.health.HealthTickInterval(1);
                    }
                    check(!helod.health.hediffSet.hediffs.Contains(h) && helod.health.hediffSet.HasHediff(recovery), drug + " deep sleep still ends the Helod crash with recovery");
                }
            }
            finally
            {
                Find.TickManager.DebugSetTicksGame(startTick);
                foreach (Pawn pawn in created) if (!pawn.Destroyed) pawn.Destroy();
            }
        }
    }
}
