using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public static class TacticalFieldSpacingPolicy
    {
        // Incoming fire, not friendly-fire immunity: penalize neighbors and
        // near-collinear soldiers viewed from the observed enemy position.
        public static float Penalty(IntVec3 cell, IntVec3 peer, IntVec3 focus)
        {
            int distance = cell.DistanceToSquared(peer);
            float cost = distance < 4 ? 40 : distance < 16 ? 8 : 0;
            IntVec3 ray = cell - focus, delta = peer - cell;
            double length = ray.LengthHorizontalSquared;
            double cross = (double)ray.x * delta.z - (double)ray.z * delta.x;
            double dot = (double)ray.x * delta.x + (double)ray.z * delta.z;
            if (length > 0 && distance <= 900 && cross * cross < length * 4 && dot * dot < length * 900) cost += 16;
            return cost;
        }
        public static bool Close(IntVec3 a, IntVec3 b) => a.DistanceToSquared(b) < 4;
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        private float FieldSpacingCost(TacticalSquadCommand command, Pawn pawn, IntVec3 cell, bool hard = false)
        {
            float cost = 0; int checkedMembers = 0;
            foreach (var peer in command.Members)
            {
                // The command roster is already registered. Do not repeat the
                // organization lookup for every cast-position candidate.
                if (peer.Pawn == pawn || peer.Pawn?.Spawned != true || peer.Pawn.Map != map || peer.Pawn.Dead || peer.Pawn.Downed) continue;
                if (++checkedMembers > 16) break;
                IntVec3 position = peer.FieldPost.IsValid && (peer.FieldPostState == TacticalFieldPostState.Fixed
                    || peer.FieldPostState == TacticalFieldPostState.Moving) ? peer.FieldPost
                    : peer.Pawn.CurJob == peer.Job && peer.Job?.def == JobDefOf.Goto ? peer.Job.targetA.Cell : peer.Pawn.Position;
                if (hard && TacticalFieldSpacingPolicy.Close(cell, position)) return float.MaxValue;
                cost += TacticalFieldSpacingPolicy.Penalty(cell, position, command.FieldResponse.Focus);
            }
            return cost + FieldPeerSectorCost(command,cell,GenTicks.TicksGame);
        }
        private static float FieldPeerSectorCost(TacticalSquadCommand command, IntVec3 cell, int tick)
        {
            float cost = 0; var formation = command.FieldResponse.Formation;
            if (formation == null) return 0;
            foreach (var pair in formation.Sectors)
            {
                if (pair.Key == command.Id || !formation.Members.Contains(pair.Key)
                    || tick < pair.Value.At || tick - pair.Value.At >= TacticalFieldDoctrine.RadioLife) continue;
                cost += TacticalFieldSpacingPolicy.Penalty(cell,pair.Value.Origin,command.FieldResponse.Focus);
                if (pair.Value.Flank.IsValid) cost += TacticalFieldSpacingPolicy.Penalty(cell,pair.Value.Flank,command.FieldResponse.Focus);
            }
            return cost;
        }
        internal bool FieldSeparatedCover(TacticalSquadCommand command, Pawn pawn, IntVec3 cell)
        {
            if (!FieldCoveredCandidate(command,pawn,cell) || FieldSpacingCost(command,pawn,cell,true) == float.MaxValue) return false;
            foreach (Thing thing in cell.GetThingList(map))
                if (thing is Pawn other && other != pawn && !other.Dead && !other.Downed) return false;
            return true;
        }
    }
}
