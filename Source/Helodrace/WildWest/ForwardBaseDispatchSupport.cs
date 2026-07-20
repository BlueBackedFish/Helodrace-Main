using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Helodrace
{
    public static class HelodForwardBaseDispatchUtility
    {
        private const float InfantryDeploymentPoints = 700f;

        public static bool TryDispatch(HelodForwardBaseService service, Map map,
            HelodForwardBase forwardBase, Pawn caller)
        {
            if (map == null || forwardBase == null || caller == null || caller.Map != map
                || !forwardBase.HasService(service) || !forwardBase.HasServiceCapacity(service)
                || !IsInRange(service, map, forwardBase)
                || SCR300RadioUtility.IsBlackout(map))
            {
                Messages.Message("HD_SCR300_ServiceUnavailable".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            if (service == HelodForwardBaseService.InfantryDeployment)
            {
                return TryDeployInfantry(map, forwardBase, caller);
            }

            List<Thing> supplies = MakeSupplies(service);
            if (supplies.Count == 0)
            {
                Messages.Message("HD_SCR300_ServiceNotImplemented".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            if (!forwardBase.TryConsumeServiceUse(service, out string failReason))
            {
                Messages.Message(failReason ?? "HD_SCR300_ServiceUnavailable".Translate().ToString(),
                    MessageTypeDefOf.RejectInput);
                return false;
            }

            DropPodUtility.DropThingsNear(caller.Position, map, supplies, 110,
                false, false, true, false, true, forwardBase.Faction);
            Messages.Message("HD_SCR300_SuppliesDispatched".Translate(
                ServiceLabel(service), forwardBase.LabelCap), caller, MessageTypeDefOf.PositiveEvent);
            return true;
        }

        private static bool IsInRange(HelodForwardBaseService service, Map map,
            HelodForwardBase forwardBase)
        {
            int mapTile = map.Tile >= 0 ? map.Tile : map.Parent?.Tile ?? -1;
            return mapTile >= 0 && forwardBase.Tile >= 0
                && Find.WorldGrid.ApproxDistanceInTiles(forwardBase.Tile, mapTile)
                    <= HelodForwardBaseServiceUtility.SupportRange(service);
        }

        private static bool TryDeployInfantry(Map map, HelodForwardBase forwardBase, Pawn caller)
        {
            Faction faction = forwardBase.Faction;
            if (faction == null)
            {
                Messages.Message("HD_SCR300_ServiceUnavailable".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            PawnGroupMakerParms parms = new PawnGroupMakerParms
            {
                groupKind = PawnGroupKindDefOf.Combat,
                tile = map.Tile,
                faction = faction,
                points = InfantryDeploymentPoints,
                generateFightersOnly = true,
                dontUseSingleUseRocketLaunchers = true
            };
            List<Pawn> pawns = PawnGroupMakerUtility.GeneratePawns(parms).ToList();
            if (pawns.Count == 0)
            {
                Messages.Message("HD_SCR300_DeploymentFailed".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            if (!forwardBase.TryConsumeServiceUse(HelodForwardBaseService.InfantryDeployment,
                out string failReason))
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    pawns[i].Destroy();
                }
                Messages.Message(failReason ?? "HD_SCR300_ServiceUnavailable".Translate().ToString(),
                    MessageTypeDefOf.RejectInput);
                return false;
            }

            List<Pawn> spawned = new List<Pawn>();
            for (int i = 0; i < pawns.Count; i++)
            {
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(caller.Position, map, 8);
                Pawn pawn = (Pawn)GenSpawn.Spawn(pawns[i], cell, map);
                spawned.Add(pawn);
            }

            LordMaker.MakeNewLord(faction,
                new LordJob_AssistColony(Faction.OfPlayer, caller.Position), map, spawned);
            Messages.Message("HD_SCR300_InfantryDispatched".Translate(
                forwardBase.LabelCap, spawned.Count), caller, MessageTypeDefOf.PositiveEvent);
            return true;
        }

        private static List<Thing> MakeSupplies(HelodForwardBaseService service)
        {
            List<Thing> result = new List<Thing>();
            switch (service)
            {
                case HelodForwardBaseService.LogisticsFreshFood:
                    AddStack(result, ThingDefOf.MealFine, 30);
                    break;
                case HelodForwardBaseService.LogisticsPreservedFood:
                    AddStack(result, ThingDefOf.MealSurvivalPack, 40);
                    break;
                case HelodForwardBaseService.LogisticsMedicalSupplies:
                    AddStack(result, ThingDefOf.MedicineIndustrial, 24);
                    AddStack(result, ThingDefOf.MedicineHerbal, 12);
                    break;
                case HelodForwardBaseService.LogisticsWeapons:
                    AddThings(result, "HD_Gun_M1Garand_Weapon", 3);
                    AddThings(result, "HD_Gun_M1918A2HAR_Weapon", 1);
                    AddThings(result, "HD_Gun_M1911_Weapon", 2);
                    break;
            }
            return result;
        }

        private static void AddStack(List<Thing> things, ThingDef def, int count)
        {
            if (def == null || count <= 0)
            {
                return;
            }

            int remaining = count;
            while (remaining > 0)
            {
                Thing thing = ThingMaker.MakeThing(def);
                thing.stackCount = System.Math.Min(remaining, def.stackLimit);
                remaining -= thing.stackCount;
                things.Add(thing);
            }
        }

        private static void AddThings(List<Thing> things, string defName, int count)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            for (int i = 0; def != null && i < count; i++)
            {
                things.Add(ThingMaker.MakeThing(def));
            }
        }

        private static string ServiceLabel(HelodForwardBaseService service)
        {
            return ("HD_TelegraphTable_ForwardBase_Service_" + service).Translate().ToString();
        }
    }
}
