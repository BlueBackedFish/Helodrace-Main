using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Helodrace
{
    public class RaidStrategyWorker_BreachingAssault : RaidStrategyWorker_ImmediateAttackBreaching
    {
        private const string FactionDefName = "HD_HelodCivilLowFaction";
        private const string RiflemanKindDefName = "HD_GW_HelodRifleman";
        private const string SmgDefName = "HD_Gun_M3A1_Weapon";
        private const string SniperDefName = "HD_Gun_M1D_Weapon";
        private const string LauncherDefName = "HD_Gun_M9A1_Weapon";
        private const string RocketBagDefName = "HD_Apparel_M6RocketBag";
        private const string HeatRocketDefName = "HD_Rocket_M6A3HEAT";

        protected override bool MatchesRequiredPawnKind(PawnKindDef kindDef)
        {
            return kindDef?.defName?.StartsWith("HD_GW_Helod") == true;
        }

        public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind)
        {
            return parms.faction?.def?.defName == FactionDefName
                && parms.target is Map
                && parms.points >= 700f
                && base.CanUseWith(parms, groupKind);
        }

        public override List<Pawn> SpawnThreats(IncidentParms parms)
        {
            List<Pawn> pawns = base.SpawnThreats(parms) ?? new List<Pawn>();
            Map map = (Map)parms.target;
            int sniperCount = parms.points >= 1400f ? 2 : 1;
            int minimumCount = 6 + sniperCount;

            while (pawns.Count < minimumCount)
            {
                pawns.Add(GenerateAndSpawn(RiflemanKindDefName, parms.faction, map, parms.spawnCenter));
            }

            List<Pawn> ordered = pawns
                .Where(pawn => pawn != null && !pawn.Dead)
                .OrderBy(pawn => pawn.thingIDNumber)
                .ToList();

            Pawn launcherOperator = ordered[0];
            Pawn rocketBearer = ordered[1];
            EquipWeapon(launcherOperator, LauncherDefName);
            EquipWeapon(rocketBearer, SmgDefName);

            for (int i = 0; i < sniperCount; i++)
            {
                EquipWeapon(ordered[2 + i], SniperDefName);
            }

            foreach (Pawn pawn in ordered.Skip(2 + sniperCount))
            {
                EquipWeapon(pawn, SmgDefName);
                EnableSharpshooterMode(pawn);
            }
            EnableSharpshooterMode(rocketBearer);

            CompM6RocketBag operatorBag = GiveFullRocketBag(launcherOperator);
            CompM6RocketBag bearerBag = GiveFullRocketBag(rocketBearer);
            if (operatorBag != null && bearerBag != null)
            {
                CompRecoillessWeapon launcher = launcherOperator.equipment?.Primary?.TryGetComp<CompRecoillessWeapon>();
                launcher?.ConfigureCrew(rocketBearer, true);
            }

            return pawns;
        }

        protected override LordJob MakeLordJob(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed)
        {
            return new LordJob_HelodBreachingAssault(parms.faction);
        }

        private static Pawn GenerateAndSpawn(string kindDefName, Faction faction, Map map, IntVec3 center)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamed(kindDefName);
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer, map.Tile));
            IntVec3 origin = center.IsValid ? center : CellFinder.RandomEdgeCell(map);
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(origin, map, 10);
            GenSpawn.Spawn(pawn, cell, map);
            return pawn;
        }

        private static void EquipWeapon(Pawn pawn, string weaponDefName)
        {
            ThingDef weaponDef = DefDatabase<ThingDef>.GetNamed(weaponDefName);
            pawn.equipment.DestroyAllEquipment(DestroyMode.Vanish);
            pawn.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(weaponDef));
        }

        private static void EnableSharpshooterMode(Pawn pawn)
        {
            CompSharpshooterWeapon mode = pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>();
            if (mode != null && mode.CanUseSharpshooterMode && !mode.altModeActive)
            {
                mode.PerformSwitch();
            }
        }

        private static CompM6RocketBag GiveFullRocketBag(Pawn pawn)
        {
            ThingDef bagDef = DefDatabase<ThingDef>.GetNamed(RocketBagDefName);
            ThingDef rocketDef = DefDatabase<ThingDef>.GetNamed(HeatRocketDefName);
            Apparel bag = (Apparel)ThingMaker.MakeThing(bagDef, bagDef.MadeFromStuff ? ThingDefOf.Cloth : null);

            if (pawn.apparel.CanWearWithoutDroppingAnything(bagDef))
            {
                pawn.apparel.Wear(bag, false);
            }
            else
            {
                pawn.apparel.Wear(bag, true);
            }

            CompM6RocketBag bagComp = bag.TryGetComp<CompM6RocketBag>();
            Thing rockets = ThingMaker.MakeThing(rocketDef);
            rockets.stackCount = 4;
            if (!pawn.inventory.innerContainer.TryAdd(rockets))
            {
                rockets.Destroy(DestroyMode.Vanish);
            }
            bagComp?.SetDesiredCount(rocketDef, 4);
            return bagComp;
        }
    }
}
