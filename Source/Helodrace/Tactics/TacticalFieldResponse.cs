using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public enum TacticalObservedMotion { Unknown, Stationary, Approaching, Crossing, Leaving }
    public enum TacticalFieldStage { Forming, Defending, Screening, Moving }

    // No enemy references, orders, threat grid or room graph. All predictions
    // are based on the two reported/observed value samples only.
    public static class TacticalFieldPolicy
    {
        public const int SightRadius = 120, FocusInterval = 90;
        public const int PlayerApproachRadius = 55;
        public const int HighWidth = 8, HighDepth = 6, CoverLimit = 64, CoverBatch = 96, CoverRefresh = 600;
        public const float MinimumCover = .15f;
        public static bool ContactHandoffAllowed(TacticalContactResponse response, bool outside, bool openingOwned) =>
            !openingOwned && (response == null || outside && !response.DirectAttack && !response.Door);
        public static float CoverWeight(IntVec3 adjacent, IntVec3 threat)
        {
            if (threat == IntVec3.Zero) return 0;
            double dot = (double)adjacent.x * threat.x + (double)adjacent.z * threat.z;
            if (dot <= 0) return 0;
            double cross = (double)adjacent.x * threat.z - (double)adjacent.z * threat.x;
            double angle = Math.Abs(Math.Atan2(cross, dot) * 180 / Math.PI);
            if (adjacent.x != 0 && adjacent.z != 0) angle *= 1.75;
            return angle < 15 ? 1 : angle < 27 ? .8f : angle < 40 ? .6f : angle < 52 ? .4f : angle < 65 ? .2f : 0;
        }
        public static bool FormationAllowed(TacticalContact contact, IntVec3 anchor, int tick) => contact != null
            && TacticalContactMemory.Fresh(contact, tick)
            && (!contact.PlayerControlled || contact.Position.DistanceToSquared(anchor) <= PlayerApproachRadius * PlayerApproachRadius
                && Motion(contact, anchor) == TacticalObservedMotion.Approaching
                && contact.Position.DistanceToSquared(anchor) < contact.PreviousPosition.DistanceToSquared(anchor));
        public static TacticalObservedMotion Motion(TacticalContact contact, IntVec3 anchor)
        {
            if (contact == null || !contact.PreviousPosition.IsValid || contact.PreviousTick < 0
                || contact.SeenTick <= contact.PreviousTick
                || contact.SeenTick - contact.PreviousTick >= TacticalContactMemory.FreshTicks)
                return TacticalObservedMotion.Unknown;
            IntVec3 travel = contact.Position - contact.PreviousPosition;
            if (travel.LengthHorizontalSquared == 0) return TacticalObservedMotion.Stationary;
            IntVec3 toward = anchor - contact.PreviousPosition;
            long dot = (long)travel.x * toward.x + (long)travel.z * toward.z;
            if (dot * dot * 4 < (long)travel.LengthHorizontalSquared * toward.LengthHorizontalSquared)
                return TacticalObservedMotion.Crossing;
            return dot > 0 ? TacticalObservedMotion.Approaching : TacticalObservedMotion.Leaving;
        }
        public static IntVec3 Direction(IntVec3 from, IntVec3 to)
        {
            IntVec3 delta = to - from;
            return Math.Abs(delta.x) >= Math.Abs(delta.z)
                ? new IntVec3(Math.Sign(delta.x), 0, 0) : new IntVec3(0, 0, Math.Sign(delta.z));
        }
        public const int GatherTicks = 360;
        public static bool Ready(int count, int arrived) => count == 0 || arrived >= (count * 3 + 3) / 4;
        // Start the three fireteams on a line. Later withdrawal, not an initial
        // hard-coded bow, creates converging fields of fire.
        public static IntVec3 FirePoint(IntVec3 anchor, IntVec3 focus, IntVec3 forward, int point, int seed)
        {
            return TacticalFieldDoctrine.LinePoint(anchor, forward, point, seed);
        }
        public static int FirePointIndex(bool high, int fireteam) => high ? fireteam >= 0 && fireteam < 3 ? fireteam : 1 : 0;
    }

    public sealed class TacticalFireArea : IExposable
    {
        public List<IntVec3> Cells = new List<IntVec3>();
        public int Started, Cursor;
        public IntVec3 Seed = IntVec3.Invalid;
        public bool Prepared;
        public void ExposeData()
        {
            Scribe_Collections.Look(ref Cells, "cells", LookMode.Value);
            Scribe_Values.Look(ref Started, "started");
            Scribe_Values.Look(ref Cursor, "cursor");
            Scribe_Values.Look(ref Seed, "seed");
            Scribe_Values.Look(ref Prepared, "prepared");
        }
    }

    public sealed partial class TacticalFieldResponse
    {
        public int Started, LastSeen, EnemyId, FocusAt, NextMove, MoveStarted, SmokeRetryAt;
        public TacticalFieldStage Stage;
        public TacticalObservedMotion Motion;
        public bool High;
        public IntVec3 Anchor, Focus, Forward;
        public List<IntVec3> FirePoints = new List<IntVec3>(3);
        public List<int> Groups = new List<int>();
        public List<TacticalFireArea> Areas = new List<TacticalFireArea>();
        public int LeaderIndex = -1, LeaderTeam = -1;
        public HashSet<int> FireGroup = new HashSet<int>();
        public TacticalFieldSmoke Screen;
    }

    public sealed partial class MapComponent_TacticalCommands
    {
        public long FieldResponses, FieldResumes, FieldPostCandidates, FieldBounds, FieldNativeJobs;

        private bool AdvanceFieldResponse(TacticalSquadCommand command, List<TacticalMemberCommand> active, int tick)
        {
            // CQB explosives keep their own controller until safe. An indoor
            // contact or portal transit still belongs to the CQB response.
            if (command.ChargeAction != null || TacticalOpeningPolicy.OwnsContact(command, command.OpeningAction?.Enemy ?? IntVec3.Invalid)
                || command.OpeningAction?.Launched == true && !command.OpeningAction.EffectsCleared)
                return false;
            if (command.ContactResponse?.DirectAttack == true) return false;
            TacticalFieldResponse field = command.FieldResponse;
            bool support = WaitingForSupport(command, tick);
            if (field == null)
            {
                Pawn observer = active[active.Count / 2].Pawn;
                if (!support && observer.Position.Roofed(map)) return false;
                TacticalContact nearest = null;
                foreach (TacticalContact contact in command.Contacts.Memory.Entries)
                {
                    if (!TacticalContactMemory.Fresh(contact, tick) || contact.SeenTick <= command.ContactHandledAt
                        || contact.Door || contact.Position.Roofed(map)) continue;
                    if (nearest == null || observer.Position.DistanceToSquared(contact.Position)
                        < observer.Position.DistanceToSquared(nearest.Position)) nearest = contact;
                }
                if (nearest == null) return false;
                IntVec3 focus = nearest?.Position ?? command.SupportAim;
                var previous = command.ContactResponse;
                bool outsideContact = !observer.Position.Roofed(map) && (previous == null
                    || previous.First.IsValid && previous.First.InBounds(map) && !previous.First.Roofed(map));
                bool openingOwned = TacticalOpeningPolicy.OwnsContact(command, focus)
                    || previous != null && TacticalOpeningPolicy.OwnsContact(command, previous.First);
                if (!TacticalFieldPolicy.ContactHandoffAllowed(previous, outsideContact, openingOwned)) return false;
                if (command.ContactResponse != null)
                {
                    command.PhaseStarted += tick - command.ContactResponse.Started;
                    foreach (IntVec3 cell in command.ContactResponse.Posts)
                        if (cell.IsValid && !OriginalClaim(command, cell) && claims.TryGetValue(cell, out var owner) && owner == command) claims.Remove(cell);
                    command.ContactResponse = null; command.ContactRestoring = false;
                }
                field = new TacticalFieldResponse { Started = tick, LastSeen = nearest?.SeenTick ?? tick, EnemyId = nearest?.EnemyId ?? -1,
                    Anchor = command.Defensive ? command.DefenseAnchor : observer.Position, Focus = focus, FocusAt = tick, NextMove = tick + 180,
                    Forward = TacticalFieldPolicy.Direction(command.Defensive ? command.DefenseAnchor : observer.Position, focus),
                    Motion = TacticalFieldPolicy.Motion(nearest, observer.Position),
                    High = command.Link.Unit.Faction.def.defName == "HD_HelodCivilHighFaction"
                        && active.Exists(m => m.Fireteam >= 0) && TacticalFieldPolicy.FormationAllowed(nearest, observer.Position, tick) };
                field.Forward = TacticalFieldDoctrine.Facing(field.Anchor, focus);
                for (int point = 0; point < 3; point++)
                {
                    field.FirePoints.Add(TacticalFieldPolicy.FirePoint(field.Anchor, field.Focus, field.Forward, point,
                        unchecked(field.Started ^ field.EnemyId * 397)));
                    field.Areas.Add(new TacticalFireArea { Started = tick });
                }
                for (int i = 0; i < command.Members.Count; i++)
                    field.Groups.Add(TacticalFieldPolicy.FirePointIndex(field.High, command.Members[i].Fireteam));
                command.FieldResponse = field; FieldResponses++;
                field.NextFlank = tick + TacticalFieldDoctrine.RetreatDelay;
                field.NextMove = tick + TacticalFieldDoctrine.RetreatDelay;
                field.RetreatBases.AddRange(field.FirePoints);
                field.AppliedStep = 0;
                command.Link.Cooperation.LocalReady = false;
            }
            bool permitted = false;
            foreach (TacticalContact contact in command.Contacts.Memory.Entries)
                if (contact.EnemyId == field.EnemyId && TacticalContactMemory.Fresh(contact, tick))
                {
                    // HIGH formation still requires an approach to start;
                    // bounded individual fighting itself does not.
                    permitted = true;
                    if (contact.SeenTick > field.LastSeen) field.Motion = TacticalFieldPolicy.Motion(contact, field.Anchor);
                    field.LastSeen = Math.Max(field.LastSeen, contact.SeenTick);
                    if (tick - field.FocusAt >= TacticalFieldPolicy.FocusInterval)
                    {
                        field.Focus = contact.Position;
                        field.FocusAt = tick;
                    }
                }
            if (!permitted)
            {
                var screen = field.Screen;
                bool returning = screen?.ActiveJob != null && screen.Thrower?.Spawned == true && !screen.Thrower.Dead && !screen.Thrower.Downed;
                if (returning || screen?.Projectile?.Spawned == true && !screen.Projectile.Destroyed)
                { command.Due = Math.Min(command.Due, tick + 15); return true; }
                EndFieldResponse(command, tick); return false;
            }
            command.Due = Math.Min(command.Due, tick + 30);
            if (field.High && !active.Exists(m => m.Fireteam >= 0))
            { field.High = false; field.Forward = TacticalFieldDoctrine.Facing(field.Anchor, field.Focus); }
            if (field.Screen != null && AdvanceFieldSmoke(command, active, field, tick))
            {
                if (field.LowFlank) FieldSmokeAdvances++;
                field.Screen = null; field.Stage = TacticalFieldStage.Defending;
            }
            if (!field.High) return AdvanceLowField(command, active, field, tick, support);
            RefreshFieldLeader(command, active, field, tick);
            AdvanceHighFieldRetreat(command, field, tick);
            PrepareFieldCover(command, field, tick);
            AdvanceFieldCombatActors(command, active, field, tick);
            int count = 0, arrived = 0;
            foreach (var member in active)
            {
                count++;
                if (FieldLocusContains(command, member.Pawn, member.Pawn.Position)) arrived++;
            }
            if (field.Stage == TacticalFieldStage.Forming && (TacticalFieldPolicy.Ready(count, arrived)
                || tick - field.Started >= TacticalFieldPolicy.GatherTicks)) field.Stage = TacticalFieldStage.Defending;
            if (field.Stage == TacticalFieldStage.Moving && (TacticalFieldPolicy.Ready(count, arrived) || tick - field.MoveStarted >= 600))
            {
                field.Stage = TacticalFieldStage.Defending;
                field.NextMove = Math.Max(field.NextMove, tick + TacticalFieldDoctrine.RetreatInterval);
            }
            return true;
        }
        private void RefreshFieldLeader(TacticalSquadCommand command, List<TacticalMemberCommand> active, TacticalFieldResponse field, int tick)
        {
            if (!field.High) return;
            Pawn commander = command.Link.Unit.Commander;
            int index = command.Members.FindIndex(m => m.Pawn == commander);
            bool live = active.Exists(m => m.Fireteam == field.LeaderTeam && m.Fireteam >= 0);
            if (index == field.LeaderIndex && live) return;
            int previous = field.LeaderIndex;
            field.LeaderIndex = index; field.LeaderTeam = -1;
            int distance = int.MaxValue;
            foreach (TacticalMemberCommand member in active)
                if (member.Fireteam >= 0 && member.Fireteam < 3)
                {
                    int candidate = commander?.Position.DistanceToSquared(member.Pawn.Position) ?? 0;
                    if (candidate < distance) { distance = candidate; field.LeaderTeam = member.Fireteam; }
                }
            if (previous >= 0 && previous != index && command.Members[previous].Fireteam < 0)
                field.Groups[previous] = field.LeaderTeam < 0 ? 1 : field.LeaderTeam;
            if (index < 0 || field.LeaderTeam < 0 || command.Members[index].Fireteam >= 0) return;
            field.Groups[index] = field.LeaderTeam;
            command.Members[index].FieldDecisionAt = 0;
        }

        private bool FieldFloor(TacticalSquadCommand command, IntVec3 cell)
        {
            if (!cell.InBounds(map) || !cell.Standable(map) || cell.GetEdifice(map) is Building_Door
                || cell.Roofed(map) && command.SupportCaller == null) return false;
            TacticalLocalPlan plan = command.Plan;
            if (plan == null || plan.Direct) return true;
            int Depth(IntVec3 p) => (p.x - plan.Opening.x) * plan.Inward.x + (p.z - plan.Opening.z) * plan.Inward.z;
            return (Depth(cell) >= 1) == (Depth(command.FieldResponse.Anchor) >= 1)
                && cell != plan.Opening && cell != plan.Outside && cell != plan.Inside && cell != plan.EntryLane;
        }

        private void EndFieldResponse(TacticalSquadCommand command, int tick, bool preserveOpening = false)
        {
            TacticalFieldResponse field = command.FieldResponse;
            if (field == null) return;
            command.PhaseStarted += tick - field.Started;
            foreach (TacticalMemberCommand member in command.Members)
            {
                member.Entered = member.EntryAssignmentDone = false; member.RetryTick = 0; member.LastProgressTick = tick;
            }
            if (!preserveOpening && command.OpeningAction != null)
            {
                command.OpeningAction = null;
                if (command.Phase == TacticalCommandPhase.Observe || command.Phase == TacticalCommandPhase.Support)
                    command.Phase = TacticalCommandPhase.Observe;
            }
            command.ContactRestoring = command.Phase == TacticalCommandPhase.Clear && command.Plan != null;
            command.DefenseRestoring |= command.Defensive;
            command.ContactHandledAt = tick; command.FieldResponse = null; command.Due = tick + 1; FieldResumes++;
            foreach (var member in command.Members) pendingFieldJobs.Remove(member.Pawn);
        }
    }
}
