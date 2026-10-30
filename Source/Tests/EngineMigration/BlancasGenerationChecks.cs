using System;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tests
{
    internal static class BlancasGenerationChecks
    {
        private const string PawnName = "BlancasCompatibilityPlaceholders17";
        private static readonly string[] Chemicals = { "BD_ModafinilChemical", "BD_Caffeine" };
        private static readonly string[] Addictions = { "BD_ModafinilAddiction", "BD_CaffeineAddiction" };
        private static readonly string[] Needs = { "BD_Chemical_Modafinil", "BD_Chemical_Caffeine" };

        internal static void Create(Action<bool, string> check)
        {
            foreach (string name in Chemicals)
                check(!DefDatabase<ChemicalDef>.GetNamed(name).canBeAddicted, name + " is excluded from random addiction generation");
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            pawn.story.traits.allTraits.Clear();
            PawnKindDef originalKind = pawn.kindDef;
            pawn.kindDef = new PawnKindDef { race = pawn.def, chemicalAddictionChance = 1 };
            int generated = 0;
            Rand.PushState(170702);
            try
            {
                for (int i = 0; i < 64; i++)
                {
                    foreach (Hediff effect in pawn.health.hediffSet.hediffs.ToArray()) pawn.health.RemoveHediff(effect);
                    PawnAddictionHediffsGenerator.GenerateAddictionsAndTolerancesFor(pawn);
                    generated += pawn.health.hediffSet.hediffs.OfType<Hediff_Addiction>().Count();
                    if (pawn.health.hediffSet.hediffs.Any(h => Addictions.Contains(h.def.defName)))
                        throw new InvalidOperationException("A non-addictive placeholder was randomly generated.");
                }
            }
            finally { Rand.PopState(); pawn.kindDef = originalKind; }
            check(generated > 0, "64 actual random addiction generations retain ordinary addictions and exclude both placeholders (generated=" + generated + ")");
            foreach (Hediff effect in pawn.health.hediffSet.hediffs.ToArray()) pawn.health.RemoveHediff(effect);
            pawn.Name = new NameSingle(PawnName);
            GenSpawn.Spawn(pawn, Find.CurrentMap.Center, Find.CurrentMap);
            for (int i = 0; i < Addictions.Length; i++)
            {
                var addiction = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed(Addictions[i]), pawn);
                addiction.Severity = .4f + i * .2f;
                pawn.health.AddHediff(addiction);
                pawn.needs.AddOrRemoveNeedsAsAppropriate();
                var need = pawn.needs.TryGetNeed(DefDatabase<NeedDef>.GetNamed(Needs[i]));
                check(need != null, Addictions[i] + " existing published addiction/need remains usable despite generation exclusion");
                need.CurLevel = .3f + i * .2f;
            }
        }

        internal static void Verify(Action<bool, string> check)
        {
            Pawn pawn = Find.CurrentMap.mapPawns.AllPawnsSpawned.Single(p => p.Name?.ToStringShort == PawnName);
            for (int i = 0; i < Addictions.Length; i++)
            {
                var addiction = pawn.health.hediffSet.hediffs.Single(h => h.def.defName == Addictions[i]);
                var need = pawn.needs.TryGetNeed(DefDatabase<NeedDef>.GetNamed(Needs[i]));
                check(addiction is Hediff_Addiction && Math.Abs(addiction.Severity - (.4f + i * .2f)) < 1e-5f,
                    Addictions[i] + " published type and severity survive save/reload");
                check(need != null && Math.Abs(need.CurLevel - (.3f + i * .2f)) < 1e-5f,
                    Needs[i] + " published need and level survive save/reload");
            }
        }
    }
}
