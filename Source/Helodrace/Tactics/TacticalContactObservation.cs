using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    public sealed partial class MapComponent_TacticalCommands
    {
        public long ContactScans, ContactCandidates, ContactsSeen, DoorContactsSeen;
        private void ScanContacts(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            TacticalContactState state = command.Contacts;
            if (tick < state.NextScan) return;
            TacticalWorkBudget budget = Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if (!budget.TryObserve(tick)) { command.DeferredWork = true; return; }
            long started = Stopwatch.GetTimestamp();
            try
            {
                state.NextScan = tick + TacticalContactState.ScanInterval;
                state.Memory.Expire(tick); ContactScans++;
                // Three cheap stations alternate front, rear and middle. No
                // pawn-pair graph or repeated all-member LOS calculation.
                int station = state.ObserverCursor++ % 3;
                Pawn observer = active[station == 0 ? 0 : station == 1 ? active.Count - 1 : active.Count / 2].Pawn;
                var pawns = map.mapPawns.AllPawnsSpawned;
                int observed = 0;
                for (int i = 0; i < System.Math.Min(TacticalContactState.CandidateLimit, pawns.Count); i++)
                {
                    if (state.CandidateCursor >= pawns.Count) state.CandidateCursor = 0;
                    Pawn enemy = pawns[state.CandidateCursor++]; ContactCandidates++;
                    if (enemy.Dead || enemy.Downed || !enemy.HostileTo(observer)
                        || !TacticalContactSight.CanSee(map, observer.Position, enemy, state)) continue;
                    RecordContact(command, enemy.thingIDNumber, enemy.Position, observer.Position, tick);
                    if (++observed == 2) break;
                }
            }
            finally { budget.Account(tick, Stopwatch.GetTimestamp() - started); }
        }
        private void RecordContact(TacticalSquadCommand command, int enemyId, IntVec3 cell, IntVec3 source, int tick)
        {
            bool door = cell.GetEdifice(map) is Building_Door;
            IntVec3 area = cell;
            if (door)
            {
                // Interpret the door through its adjacent floor on the viewed
                // side. No live Room graph or hidden enemy position lookup.
                IntVec3 delta = cell - source;
                IntVec3 forward = System.Math.Abs(delta.x) >= System.Math.Abs(delta.z)
                    ? new IntVec3(System.Math.Sign(delta.x), 0, 0) : new IntVec3(0, 0, System.Math.Sign(delta.z));
                IntVec3 next = cell + forward;
                if (next.InBounds(map) && next.Walkable(map) && !(next.GetEdifice(map) is Building_Door)) area = next;
                else foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    next = cell + direction;
                    if (next.InBounds(map) && next.Walkable(map) && !(next.GetEdifice(map) is Building_Door))
                    { area = next; break; }
                }
                DoorContactsSeen++;
            }
            command.Contacts.Memory.Remember(enemyId, cell, area, door, tick); ContactsSeen++;
        }
    }
}
