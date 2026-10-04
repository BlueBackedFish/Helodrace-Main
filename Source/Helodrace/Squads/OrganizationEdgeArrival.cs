using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Squads
{
    internal static class OrganizationEdgeArrival
    {
        // Tactical units keep squads (including High child fireteams) together.
        // Separate headquarters/support units retain their real organization owner.
        internal static List<List<Pawn>> Split(List<Pawn> pawns, Func<Pawn, string> unitFor,
            out List<Pawn> unorganized)
        {
            var result = new List<List<Pawn>>();
            var byUnit = new Dictionary<string, List<Pawn>>();
            unorganized = new List<Pawn>();
            foreach (Pawn pawn in pawns)
            {
                string id = unitFor(pawn);
                if (id == null) { unorganized.Add(pawn); continue; }
                if (!byUnit.TryGetValue(id, out List<Pawn> members))
                {
                    byUnit[id] = members = new List<Pawn>();
                    result.Add(members);
                }
                members.Add(pawn);
            }
            return result;
        }
    }

    [HarmonyPatch(typeof(PawnsArrivalModeWorker_EdgeWalkInGroups), nameof(PawnsArrivalModeWorker_EdgeWalkInGroups.Arrive))]
    public static class Patch_EdgeWalkInGroups_Organization
    {
        public static bool Prefix(List<Pawn> pawns, IncidentParms parms)
        {
            List<List<Pawn>> units = OrganizationEdgeArrival.Split(pawns,
                pawn => RaidTacticalUnit.ForPawn(pawn)?.Id, out List<Pawn> unorganized);
            if (units.Count == 0) return true;
            Map map = (Map)parms.target;
            var groups = new List<Pair<List<Pawn>, IntVec3>>();
            foreach (List<Pawn> unit in units)
                groups.Add(new Pair<List<Pawn>, IntVec3>(unit,
                    PawnsArrivalModeWorkerUtility.FindNewMapEdgeGroupCenter(map, groups, arriveInPods: false)));
            // Non-organization guests retain vanilla random grouping, including mixed raids.
            foreach (var group in PawnsArrivalModeWorkerUtility.SplitIntoRandomGroupsNearMapEdge(unorganized, map, arriveInPods: false))
                groups.Add(new Pair<List<Pawn>, IntVec3>(group.First,
                    PawnsArrivalModeWorkerUtility.FindNewMapEdgeGroupCenter(map, groups, arriveInPods: false)));
            PawnsArrivalModeWorkerUtility.SetPawnGroupsInfo(parms, groups);
            foreach (var group in groups)
                foreach (Pawn pawn in group.First)
                    GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(group.Second, map, 8), map, parms.spawnRotation);
            return false;
        }
    }
}
