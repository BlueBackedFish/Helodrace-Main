using System;
using System.Collections.Generic;
using Helodrace.ModernWar;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    // Separate from ranged contact knowledge. Values describe a sighting,
    // never an enemy reference, hidden position or future path.
    public sealed class TacticalMeleeThreat : IExposable
    {
        public int EnemyId, SeenTick, Urgency;
        public IntVec3 Position;
        public bool Shielded;
        public string Origin;
        public TacticalMeleeThreat Copy() => new TacticalMeleeThreat { EnemyId = EnemyId, SeenTick = SeenTick,
            Urgency = Urgency, Position = Position, Shielded = Shielded, Origin = Origin };
        public void ExposeData()
        {
            Scribe_Values.Look(ref EnemyId, "enemyId"); Scribe_Values.Look(ref SeenTick, "seenTick");
            Scribe_Values.Look(ref Urgency, "urgency"); Scribe_Values.Look(ref Position, "position");
            Scribe_Values.Look(ref Shielded, "shielded"); Scribe_Values.Look(ref Origin, "origin");
        }
    }

    public static class TacticalFocusPolicy
    {
        public const int CandidateLimit = 6, FreshTicks = 180, DecisionInterval = 30, HoldTicks = 90;
        public const int NormalMinimumTicks = 90;
        public static readonly IReadOnlyList<IntVec3> FollowOffsets = BuildFollowOffsets();
        private static List<IntVec3> BuildFollowOffsets()
        {
            var offsets = new List<IntVec3>(29);
            for (int radius = 0; radius <= 3; radius++)
                for (int z = -radius; z <= radius; z++)
                    for (int x = -radius; x <= radius; x++)
                        if (Math.Max(Math.Abs(x), Math.Abs(z)) == radius && x * x + z * z <= 9)
                            offsets.Add(new IntVec3(x, 0, z));
            return offsets;
        }
        public static int Urgency(bool intent, int distanceSquared) => !intent || distanceSquared > 900 ? 0
            : distanceSquared <= 9 ? 3 : distanceSquared <= 100 ? 2 : 1;
        public static bool Fresh(TacticalMeleeThreat threat, int tick) => threat != null
            && threat.SeenTick <= tick && tick - threat.SeenTick < FreshTicks;
        public static int Score(TacticalMeleeThreat threat, IntVec3 anchor) => threat.Urgency * 10000
            + (threat.Shielded ? 2000 : 0) - Math.Min(1600, anchor.DistanceToSquared(threat.Position));
        public static TacticalMeleeThreat Select(IReadOnlyList<TacticalMeleeThreat> entries, int currentId,
            int selectedAt, IntVec3 anchor, int tick)
        {
            TacticalMeleeThreat current = null, best = null;
            foreach (TacticalMeleeThreat threat in entries)
            {
                if (!Fresh(threat, tick) || threat.Urgency == 0 || !threat.Position.IsValid) continue;
                if (threat.EnemyId == currentId) current = threat;
                if (best == null || Score(threat, anchor) > Score(best, anchor)) best = threat;
            }
            // Imminent contact overrides shield preference and the hold period.
            if (current != null && best != current && best.Urgency <= current.Urgency
                && (tick - selectedAt < HoldTicks || Score(best, anchor) < Score(current, anchor) + 1200)) return current;
            return best;
        }
    }

    public sealed class TacticalMeleeMemory : IExposable
    {
        private List<TacticalMeleeThreat> entries = new List<TacticalMeleeThreat>(TacticalFocusPolicy.CandidateLimit);
        public IReadOnlyList<TacticalMeleeThreat> Entries => entries;
        public TacticalMeleeThreat Find(int id) => entries.Find(t => t.EnemyId == id);
        public bool Receive(TacticalMeleeThreat threat, int tick)
        {
            if (!TacticalFocusPolicy.Fresh(threat, tick) || !threat.Position.IsValid
                || threat.Urgency < 0 || threat.Urgency > 3) return false;
            TacticalMeleeThreat own = Find(threat.EnemyId);
            if (own != null && own.SeenTick >= threat.SeenTick) return false;
            if (own != null) entries.Remove(own);
            if (entries.Count == TacticalFocusPolicy.CandidateLimit)
            {
                int oldest = 0;
                for (int i = 1; i < entries.Count; i++) if (entries[i].SeenTick < entries[oldest].SeenTick) oldest = i;
                entries.RemoveAt(oldest);
            }
            entries.Add(threat.Copy()); return true;
        }
        public bool Dangerous(int tick) => entries.Exists(t => TacticalFocusPolicy.Fresh(t, tick) && t.Urgency > 0);
        public void Expire(int tick) => entries.RemoveAll(t => !TacticalFocusPolicy.Fresh(t, tick));
        public void ExposeData()
        {
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (entries == null) entries = new List<TacticalMeleeThreat>();
                entries.RemoveAll(t => t == null || !t.Position.IsValid || t.Urgency < 0 || t.Urgency > 3);
                if (entries.Count > TacticalFocusPolicy.CandidateLimit)
                    entries.RemoveRange(TacticalFocusPolicy.CandidateLimit, entries.Count - TacticalFocusPolicy.CandidateLimit);
            }
        }
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        private void ObserveMeleeThreat(TacticalSquadCommand command, Pawn enemy, Pawn observer, int tick, bool ranged)
        {
            // Called only AFTER shooting-geometry LOS passes. Reading a current
            // melee job is an intent sample, not permission to track it later.
            TacticalMeleeThreat previous = command.Contacts.Melee.Find(enemy.thingIDNumber);
            Pawn victim = enemy.CurJob?.targetA.Thing as Pawn;
            bool attack = enemy.CurJobDef == JobDefOf.AttackMelee && victim?.Spawned == true
                && victim.Map == map && victim.Faction == observer.Faction;
            IntVec3 anchor = attack && byPawn.TryGetValue(victim, out TacticalSquadCommand victimCommand)
                && victimCommand == command ? victim.Position : observer.Position;
            bool closing = previous != null && previous.Origin == command.Id && TacticalFocusPolicy.Fresh(previous, tick)
                && previous.Position.DistanceToSquared(anchor) > enemy.Position.DistanceToSquared(anchor);
            bool melee = enemy.equipment?.PrimaryEq?.PrimaryVerb?.IsMeleeAttack == true || !ranged;
            bool intent = !enemy.Dead && !enemy.Downed && !enemy.WorkTagIsDisabled(WorkTags.Violent)
                && (attack || melee && enemy.InAggroMentalState && closing);
            int urgency = TacticalFocusPolicy.Urgency(intent, enemy.Position.DistanceToSquared(anchor));
            if (urgency > 0) command.SecuredKnowledge.ObserveThreat(enemy.Position, enemy.Position, tick);
            // A non-aggressive sample is only retained for an existing identity
            // or an aggro melee approach. Zero urgency retracts old reports.
            if (urgency == 0 && previous == null && !(melee && enemy.InAggroMentalState)) return;
            command.Contacts.Melee.Receive(new TacticalMeleeThreat { EnemyId = enemy.thingIDNumber,
                Position = enemy.Position, SeenTick = tick, Urgency = urgency,
                Shielded = urgency > 0 && ObservedShield(enemy), Origin = command.Id }, tick);
        }
        private static bool ObservedShield(Pawn enemy)
        {
            if (enemy.TryGetComp<CompShield>()?.ShieldState == ShieldState.Active) return true;
            if (enemy.apparel == null) return false;
            foreach (Apparel apparel in enemy.apparel.WornApparel)
                if (apparel.TryGetComp<CompShield>()?.ShieldState == ShieldState.Active
                    || apparel.TryGetComp<CompDirectionalBallisticShield>() != null) return true;
            return false;
        }
    }
}
