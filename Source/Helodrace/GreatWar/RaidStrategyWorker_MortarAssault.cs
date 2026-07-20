using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Helodrace
{
    public class RaidStrategyWorker_MortarAssault : RaidStrategyWorker_ImmediateAttack
    {
        protected virtual string MortarShellDefName => "HD_81mmMortarShell_M43HE";
        protected virtual bool IsChemicalRaid => false;

        private static readonly string[] RequiredKinds =
        {
            "HD_GW_HelodMortarmanA",
            "HD_GW_HelodMortarmanB",
            "HD_GW_HelodMortarmanC",
            "HD_GW_HelodMortarShellBearer",
            "HD_GW_HelodFieldRationBearer",
            "HD_GW_HelodAutomaticRifleman",
            "HD_GW_HelodSquadLeader"
        };

        public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind)
        {
            return base.CanUseWith(parms, groupKind)
                && parms.faction?.def?.defName == "HD_HelodCivilLowFaction"
                && parms.target is Map
                && parms.points >= 700f;
        }

        public override List<Pawn> SpawnThreats(IncidentParms parms)
        {
            List<Pawn> pawns = base.SpawnThreats(parms) ?? new List<Pawn>();
            Map map = (Map)parms.target;

            foreach (string defName in RequiredKinds)
            {
                if (!pawns.Any(p => p.kindDef?.defName == defName))
                    pawns.Add(GenerateAndSpawn(defName, parms.faction, map, parms.spawnCenter));
            }

            EnsureMortarPart(pawns, "HD_GW_HelodMortarmanA", "HD_Apparel_M1MortarBaseplate");
            EnsureMortarPart(pawns, "HD_GW_HelodMortarmanB", "HD_Apparel_M1MortarMount");
            EnsureMortarPart(pawns, "HD_GW_HelodMortarmanC", "HD_Apparel_M1MortarBarrel");

            // One automatic rifleman is guaranteed; larger raids have a strong chance for more.
            int bonusRolls = parms.points >= 1400f ? 2 : 1;
            for (int i = 0; i < bonusRolls; i++)
                if (Rand.Chance(0.75f))
                    pawns.Add(GenerateAndSpawn("HD_GW_HelodAutomaticRifleman", parms.faction, map, parms.spawnCenter));

            Pawn shellBearer = pawns.First(p => p.kindDef.defName == "HD_GW_HelodMortarShellBearer");
            Pawn rationBearer = pawns.First(p => p.kindDef.defName == "HD_GW_HelodFieldRationBearer");
            GiveInventory(shellBearer, MortarShellDefName, parms.points >= 1800f ? 24 : 16);
            GiveInventory(rationBearer, "HD_Hardtack", pawns.Count * 3);

            if (IsChemicalRaid)
            {
                foreach (Pawn pawn in pawns)
                    EnsureCbrnPouch(pawn);
            }

            return pawns;
        }

        protected override LordJob MakeLordJob(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed)
        {
            Pawn anchor = pawns.FirstOrDefault(p => p.kindDef?.defName == "HD_GW_HelodMortarmanA") ?? pawns.First();
            return new LordJob_MortarRaidHold(anchor.Position, IsChemicalRaid);
        }

        private static Pawn GenerateAndSpawn(string kindDefName, Faction faction, Map map, IntVec3 center)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed(kindDefName);
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer, map.Tile));
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(center.IsValid ? center : CellFinder.RandomEdgeCell(map), map, 10);
            GenSpawn.Spawn(pawn, cell, map);
            return pawn;
        }

        private static void GiveInventory(Pawn pawn, string thingDefName, int count)
        {
            Thing thing = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(thingDefName));
            thing.stackCount = count;
            if (!pawn.inventory.innerContainer.TryAdd(thing))
                GenPlace.TryPlaceThing(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near);
        }

        private static void EnsureMortarPart(List<Pawn> pawns, string pawnKindDefName, string apparelDefName)
        {
            Pawn carrier = pawns.First(p => p.kindDef?.defName == pawnKindDefName);
            ThingDef apparelDef = DefDatabase<ThingDef>.GetNamed(apparelDefName);
            if (carrier.apparel.WornApparel.Any(a => a.def == apparelDef))
                return;

            Apparel apparel = (Apparel)ThingMaker.MakeThing(apparelDef);
            if (carrier.apparel.CanWearWithoutDroppingAnything(apparelDef))
                carrier.apparel.Wear(apparel, false);
            else
                carrier.inventory.innerContainer.TryAdd(apparel);
        }

        private static void EnsureCbrnPouch(Pawn pawn)
        {
            ThingDef pouchDef = DefDatabase<ThingDef>.GetNamed("HD_Apparel_GreatWarCBRNPouch");
            if (pawn.apparel.WornApparel.Any(a => a.def == pouchDef))
                return;

            Apparel pouch = (Apparel)ThingMaker.MakeThing(pouchDef, pouchDef.MadeFromStuff ? ThingDefOf.Cloth : null);
            if (pawn.apparel.CanWearWithoutDroppingAnything(pouchDef))
                pawn.apparel.Wear(pouch, false);
            else
                pawn.apparel.Wear(pouch, true);
        }
    }

    public class RaidStrategyWorker_ChemicalMortarAssault : RaidStrategyWorker_MortarAssault
    {
        protected override string MortarShellDefName => "HD_81mmMortarShell_BA";
        protected override bool IsChemicalRaid => true;

        public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind)
        {
            return base.CanUseWith(parms, groupKind) && parms.points >= 900f;
        }
    }
}
