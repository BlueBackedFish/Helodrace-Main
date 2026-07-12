using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public class CompProperties_MortarBaseplateAssembly : CompProperties
    {
        public CompProperties_MortarBaseplateAssembly()
        {
            compClass = typeof(CompMortarBaseplateAssembly);
        }
    }

    public class CompMortarBaseplateAssembly : ThingComp
    {
        private Pawn Wearer => (parent.ParentHolder as Pawn_ApparelTracker)?.pawn;

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
                yield return gizmo;

            Pawn wearer = Wearer;
            if (wearer == null || wearer.Faction != Faction.OfPlayer || !wearer.Spawned)
                yield break;

            Command_Action command = new Command_Action
            {
                defaultLabel = "Assemble M1 mortar",
                defaultDesc = "Call the nearest bipod carrier, then the nearest barrel carrier, to assemble an M1 81mm mortar here.",
                icon = ContentFinder<Texture2D>.Get("Building/Security/HD_M181mmMortar", true),
                action = () => BeginPlacement(wearer)
            };

            string reason = MortarAssemblyUtility.UnavailableReason(wearer);
            if (reason != null)
                command.Disable(reason);
            yield return command;
        }

        private static void BeginPlacement(Pawn carrier)
        {
            ThingDef mortarDef = DefDatabase<ThingDef>.GetNamed("HD_Building_M1_81mmMortar");
            TargetingParameters parameters = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetBuildings = false,
                canTargetItems = false,
                canTargetPawns = false,
                validator = target => MortarAssemblyUtility.CanPlaceMortarAt(carrier, target.Cell)
            };
            Find.Targeter.BeginTargeting(parameters,
                target =>
                {
                    JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_BeginM1MortarAssembly");
                    if (jobDef != null)
                        carrier.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, target.Cell), JobTag.Misc);
                },
                target =>
                {
                    bool valid = MortarAssemblyUtility.CanPlaceMortarAt(carrier, target.Cell);
                    GhostDrawer.DrawGhostThing(target.Cell, Rot4.North, mortarDef, null,
                        valid ? Color.white : Color.red, AltitudeLayer.Blueprint, null, true, null);
                });
            MapComponent_PersistentTargetingOverlay.Set(carrier.Map, target =>
            {
                bool valid = MortarAssemblyUtility.CanPlaceMortarAt(carrier, target.Cell);
                GhostDrawer.DrawGhostThing(target.Cell, Rot4.North, mortarDef, null,
                    valid ? Color.white : Color.red, AltitudeLayer.Blueprint, null, true, null);
            });
        }

        public static bool StartAssembly(Pawn baseplateCarrier)
        {
            Pawn mountCarrier = MortarAssemblyUtility.FindNearestCarrier(baseplateCarrier, MortarAssemblyUtility.MountDef);
            if (mountCarrier == null)
            {
                Messages.Message("No reachable M1 mortar bipod carrier is available.", MessageTypeDefOf.RejectInput, false);
                return false;
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_AssembleM1MortarMount");
            if (jobDef == null)
                return false;

            Job waitJob = JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture);
            waitJob.expiryInterval = 3000;
            baseplateCarrier.jobs.TryTakeOrderedJob(waitJob, JobTag.Misc);
            MortarAssemblyUtility.PlaceAssemblyBlueprint(baseplateCarrier);
            mountCarrier.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, baseplateCarrier), JobTag.Misc);
            return true;
        }
    }

    public class JobDriver_BeginM1MortarAssembly : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA.Cell, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !MortarAssemblyUtility.CanPlaceMortarAt(pawn, job.targetA.Cell)
                || MortarAssemblyUtility.Worn(pawn, MortarAssemblyUtility.BaseplateDef) == null);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return Toils_General.Do(() => CompMortarBaseplateAssembly.StartAssembly(pawn));
        }
    }

    public static class MortarAssemblyUtility
    {
        public static readonly ThingDef BaseplateDef = DefDatabase<ThingDef>.GetNamed("HD_Apparel_M1MortarBaseplate");
        public static readonly ThingDef MountDef = DefDatabase<ThingDef>.GetNamed("HD_Apparel_M1MortarMount");
        public static readonly ThingDef BarrelDef = DefDatabase<ThingDef>.GetNamed("HD_Apparel_M1MortarBarrel");

        public static Apparel Worn(Pawn pawn, ThingDef def) => pawn?.apparel?.WornApparel.FirstOrDefault(a => a.def == def);

        public static bool CanPlaceMortarAt(Pawn carrier, IntVec3 cell)
        {
            Map map = carrier?.Map;
            if (map == null || !cell.IsValid || !cell.InBounds(map) || !cell.Standable(map)
                || cell.GetEdifice(map) != null || map.terrainGrid.TerrainAt(cell).IsWater)
                return false;
            ThingDef mortarDef = DefDatabase<ThingDef>.GetNamed("HD_Building_M1_81mmMortar");
            IntVec3 interaction = ThingUtility.InteractionCellWhenAt(mortarDef, cell, Rot4.North, map);
            if (!interaction.IsValid || !interaction.InBounds(map) || !interaction.Standable(map)
                || map.terrainGrid.TerrainAt(interaction).IsWater)
                return false;
            AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(mortarDef, cell, Rot4.North, map,
                false, null, null, null, false, true, true);
            return report.Accepted && carrier.CanReach(cell, PathEndMode.OnCell, Danger.Deadly);
        }

        public static Pawn FindNearestCarrier(Pawn origin, ThingDef apparelDef)
        {
            return origin.Map.mapPawns.SpawnedPawnsInFaction(origin.Faction)
                .Where(p => p != origin && !p.Downed && Worn(p, apparelDef) != null && p.CanReach(origin, PathEndMode.Touch, Danger.Deadly))
                .OrderBy(p => p.Position.DistanceToSquared(origin.Position))
                .FirstOrDefault();
        }

        public static string UnavailableReason(Pawn carrier)
        {
            if (carrier.Position.GetEdifice(carrier.Map) != null)
                return "The assembly cell is occupied by a building.";
            if (FindNearestCarrier(carrier, MountDef) == null)
                return "No reachable M1 mortar bipod carrier is available.";
            if (FindNearestCarrier(carrier, BarrelDef) == null)
                return "No reachable M1 mortar barrel carrier is available.";
            return null;
        }

        public static void Consume(Pawn pawn, ThingDef def)
        {
            Apparel apparel = Worn(pawn, def);
            if (apparel == null) return;
            pawn.apparel.Remove(apparel);
            apparel.Destroy();
        }

        public static Blueprint_Build AssemblyBlueprintAt(IntVec3 cell, Map map)
        {
            return cell.GetThingList(map).OfType<Blueprint_Build>()
                .FirstOrDefault(b => b.BuildDef?.defName == "HD_Building_M1_81mmMortar");
        }

        public static void PlaceAssemblyBlueprint(Pawn baseplateCarrier)
        {
            if (baseplateCarrier?.Map == null || AssemblyBlueprintAt(baseplateCarrier.Position, baseplateCarrier.Map) != null)
                return;
            ThingDef mortarDef = DefDatabase<ThingDef>.GetNamed("HD_Building_M1_81mmMortar");
            GenConstruct.PlaceBlueprintForBuild(mortarDef, baseplateCarrier.Position, baseplateCarrier.Map,
                Rot4.North, baseplateCarrier.Faction, null, null, null, false);
        }

        public static void RemoveAssemblyBlueprint(IntVec3 cell, Map map)
        {
            Blueprint_Build blueprint = AssemblyBlueprintAt(cell, map);
            if (blueprint != null && !blueprint.Destroyed)
                blueprint.Destroy(DestroyMode.Vanish);
        }
    }

    public class CompProperties_M1MortarDisassembly : CompProperties
    {
        public CompProperties_M1MortarDisassembly()
        {
            compClass = typeof(CompM1MortarDisassembly);
        }
    }

    public class CompM1MortarDisassembly : ThingComp
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
                yield return gizmo;

            if (!parent.Spawned || parent.Faction != Faction.OfPlayer)
                yield break;

            yield return new Command_Action
            {
                defaultLabel = "Disassemble M1 mortar",
                defaultDesc = "Order the nearest available Helod to take down this M1 81mm mortar and recover its three portable components.",
                icon = ContentFinder<Texture2D>.Get("Item/HD_M181mmMortar_Base", true),
                action = BeginDisassembly
            };
        }

        private Pawn FindNearestHelod()
        {
            if (!parent.Spawned)
                return null;

            return parent.Map.mapPawns.FreeColonistsSpawned
                .Where(p => p.def.defName == "Helod"
                    && !p.Downed
                    && p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                    && p.CanReserveAndReach(parent, PathEndMode.Touch, Danger.Deadly))
                .OrderBy(p => p.Position.DistanceToSquared(parent.Position))
                .FirstOrDefault();
        }

        private void BeginDisassembly()
        {
            Pawn worker = FindNearestHelod();
            if (worker == null)
            {
                Messages.Message("No available Helod can reach the M1 mortar.", MessageTypeDefOf.RejectInput, false);
                return;
            }

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("HD_DisassembleM1Mortar");
            if (jobDef != null)
                worker.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, parent), JobTag.Misc);
        }

        public void FinishDisassembly()
        {
            if (!parent.Spawned)
                return;

            Map map = parent.Map;
            IntVec3 position = parent.Position;
            parent.Destroy(DestroyMode.Vanish);

            ThingDef[] parts =
            {
                MortarAssemblyUtility.BarrelDef,
                MortarAssemblyUtility.BaseplateDef,
                MortarAssemblyUtility.MountDef
            };

            foreach (ThingDef partDef in parts)
                GenPlace.TryPlaceThing(ThingMaker.MakeThing(partDef), position, map, ThingPlaceMode.Near);

            Messages.Message("M1 81mm mortar disassembled.", MessageTypeDefOf.PositiveEvent, false);
        }
    }

    public class JobDriver_DisassembleM1Mortar : JobDriver
    {
        private Thing Mortar => job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Mortar, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil work = Toils_General.Wait(180, TargetIndex.A);
            work.WithProgressBarToilDelay(TargetIndex.A);
            work.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            yield return work;

            yield return Toils_General.Do(() => Mortar?.TryGetComp<CompM1MortarDisassembly>()?.FinishDisassembly());
        }
    }

    public class JobDriver_AssembleM1MortarMount : JobDriver
    {
        private Pawn BaseCarrier => job.targetA.Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(BaseCarrier, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => BaseCarrier == null || BaseCarrier.Dead || !BaseCarrier.Spawned || MortarAssemblyUtility.Worn(BaseCarrier, MortarAssemblyUtility.BaseplateDef) == null || MortarAssemblyUtility.Worn(pawn, MortarAssemblyUtility.MountDef) == null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil work = Toils_General.Wait(600, TargetIndex.A);
            work.WithProgressBarToilDelay(TargetIndex.A);
            yield return work;
            yield return Toils_General.Do(() =>
            {
                Pawn barrelCarrier = MortarAssemblyUtility.FindNearestCarrier(BaseCarrier, MortarAssemblyUtility.BarrelDef);
                if (barrelCarrier == null)
                {
                    MortarAssemblyUtility.RemoveAssemblyBlueprint(BaseCarrier.Position, BaseCarrier.Map);
                    Messages.Message("Mortar assembly stopped: no barrel carrier is available.", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                MortarAssemblyUtility.Consume(pawn, MortarAssemblyUtility.MountDef);
                JobDef next = DefDatabase<JobDef>.GetNamedSilentFail("HD_AssembleM1MortarBarrel");
                if (next != null)
                    barrelCarrier.jobs.TryTakeOrderedJob(JobMaker.MakeJob(next, BaseCarrier), JobTag.Misc);
            });
        }
    }

    public class JobDriver_AssembleM1MortarBarrel : JobDriver
    {
        private Pawn BaseCarrier => job.targetA.Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(BaseCarrier, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => BaseCarrier == null || BaseCarrier.Dead || !BaseCarrier.Spawned || MortarAssemblyUtility.Worn(BaseCarrier, MortarAssemblyUtility.BaseplateDef) == null || MortarAssemblyUtility.Worn(pawn, MortarAssemblyUtility.BarrelDef) == null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil work = Toils_General.Wait(900, TargetIndex.A);
            work.WithProgressBarToilDelay(TargetIndex.A);
            yield return work;
            yield return Toils_General.Do(() =>
            {
                IntVec3 cell = BaseCarrier.Position;
                Map map = BaseCarrier.Map;
                if (cell.GetEdifice(map) != null)
                {
                    Messages.Message("Mortar assembly failed: the assembly cell is occupied.", MessageTypeDefOf.RejectInput, false);
                    return;
                }
                MortarAssemblyUtility.Consume(pawn, MortarAssemblyUtility.BarrelDef);
                MortarAssemblyUtility.Consume(BaseCarrier, MortarAssemblyUtility.BaseplateDef);
                MortarAssemblyUtility.RemoveAssemblyBlueprint(cell, map);
                Thing mortar = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Building_M1_81mmMortar"));
                mortar.SetFaction(BaseCarrier.Faction);
                GenSpawn.Spawn(mortar, cell, map, Rot4.North);
                BaseCarrier.jobs.EndCurrentJob(JobCondition.Succeeded);
                Messages.Message("M1 81mm mortar assembly complete.", mortar, MessageTypeDefOf.PositiveEvent, false);
            });
        }
    }
}
