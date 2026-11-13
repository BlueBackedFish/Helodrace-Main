using System.Collections.Generic;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.SquadAI
{
    public sealed class SquadContact
    {
        public Thing Target;
        public IntVec3 Position;
        public int LastSeen;
        public IntVec3 SamplePosition;
        public int SampleTick;
        public float Range;
        public bool Approaching;
    }

    // What the squad has actually seen or been hit by. Positions are the last
    // observed cell; a contact out of sight keeps that cell until it expires.
    public sealed class SquadContacts
    {
        public readonly List<SquadContact> Entries = new List<SquadContact>();
        public int LastHarm = -100000;
        private int observerCursor;

        public static bool Threat(Thing thing, Pawn observer)
        {
            if (thing == null || !thing.Spawned || thing.Destroyed || !thing.HostileTo(observer)) return false;
            if (thing is Pawn pawn)
                return !pawn.Dead && !pawn.Downed && (TacticalThreatEligibility.Ranged(pawn)
                    || pawn.Position.InHorDistOf(observer.Position, SquadPolicy.CloseThreatRadius));
            return thing is Building_Turret;
        }

        public void See(Thing target, IntVec3 from, int tick)
        {
            float range = target.Position.DistanceTo(from);
            SquadContact entry = Entries.Find(e => e.Target == target);
            if (entry == null)
            {
                Entries.Add(new SquadContact { Target = target, Position = target.Position, LastSeen = tick,
                    SamplePosition = target.Position, SampleTick = tick, Range = range });
                return;
            }
            entry.Position = target.Position; entry.LastSeen = tick; entry.Range = range;
            if (tick - entry.SampleTick >= SquadPolicy.ApproachSampleTicks)
            {
                // The enemy's own movement, measured against where the squad is
                // now: our advance toward a standing enemy is not his approach.
                entry.Approaching = range < entry.SamplePosition.DistanceTo(from) - 3f;
                entry.SamplePosition = target.Position; entry.SampleTick = tick;
            }
        }

        public void Harmed(Thing instigator, IntVec3 from, int tick)
        {
            LastHarm = tick;
            if (instigator != null && instigator.Spawned) See(instigator, from, tick);
        }

        public void Scan(Squad squad, Map map, IntVec3 centroid, int tick)
        {
            Entries.RemoveAll(e => tick - e.LastSeen > SquadPolicy.ContactLifeTicks || e.Target.Destroyed
                || !e.Target.Spawned || e.Target.Map != map || e.Target is Pawn p && (p.Dead || p.Downed));
            // Targets vanilla combat already acquired are seen by definition.
            foreach (Pawn member in squad.Live)
                if (member.mindState.enemyTarget is Thing acquired && Threat(acquired, member)) See(acquired, centroid, tick);
            int checks = 0;
            for (int k = 0; k < SquadPolicy.ObserversPerThink && k < squad.Live.Count; k++)
            {
                Pawn observer = squad.Live[observerCursor++ % squad.Live.Count];
                foreach (IAttackTarget candidate in map.attackTargetsCache.GetPotentialTargetsFor(observer))
                {
                    if (checks >= SquadPolicy.LineChecksPerThink) return;
                    Thing thing = candidate.Thing;
                    if (!Threat(thing, observer)
                        || !thing.Position.InHorDistOf(observer.Position, SquadPolicy.ObservationRadius)) continue;
                    SquadContact known = Entries.Find(e => e.Target == thing);
                    if (known != null && known.LastSeen == tick) continue;
                    checks++;
                    if (GenSight.LineOfSight(observer.Position, thing.Position, map, true)) See(thing, centroid, tick);
                }
            }
        }

        public SquadContact Nearest()
        {
            SquadContact best = null;
            foreach (SquadContact entry in Entries) if (best == null || entry.Range < best.Range) best = entry;
            return best;
        }

        // The nearest contact, pulled toward the contacts within ten cells of it.
        public IntVec3 Focus()
        {
            SquadContact nearest = Nearest();
            if (nearest == null) return IntVec3.Invalid;
            long x = 0, z = 0; int n = 0;
            foreach (SquadContact entry in Entries)
                if (entry.Position.InHorDistOf(nearest.Position, 10f)) { x += entry.Position.x; z += entry.Position.z; n++; }
            return new IntVec3((int)(x / n), 0, (int)(z / n));
        }
    }
}
