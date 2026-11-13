using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Helodrace.Squads;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace.SquadAI
{
    // Field squad AI on top of vanilla raid lords. Each organized squad is
    // thought about every ThinkTicks on a fixed stagger; the result is only a
    // duty (post + enemy) per pawn, executed by the duty's think tree.
    [SquadTactical]
    public sealed partial class MapComponent_SquadAI : MapComponent
    {
        private static readonly ConditionalWeakTable<Map, MapComponent_SquadAI> byMap = new ConditionalWeakTable<Map, MapComponent_SquadAI>();
        private readonly List<Squad> squads = new List<Squad>();
        private readonly Dictionary<Pawn, Squad> byPawn = new Dictionary<Pawn, Squad>();
        private int discoverAt;

        public MapComponent_SquadAI(Map map) : base(map) { byMap.AddOrUpdate(map, this); }
        public static MapComponent_SquadAI For(Map map) => map != null && byMap.TryGetValue(map, out var component) ? component : null;
        public IReadOnlyList<Squad> Squads => squads;
        public Squad SquadOf(Pawn pawn) => pawn != null && byPawn.TryGetValue(pawn, out Squad squad) ? squad : null;

        public override void MapRemoved()
        {
            byMap.Remove(map); squads.Clear(); byPawn.Clear();
        }

        public static bool IsAssaultToil(LordToil toil) => toil != null && (toil.GetType() == typeof(LordToil_AssaultColony)
            || toil is LordToil_AssaultColonySappers || toil is LordToil_AssaultColonyBreaching);

        public override void MapComponentTick()
        {
            int tick = Find.TickManager.TicksGame;
            if (tick >= discoverAt) { discoverAt = tick + SquadPolicy.DiscoverTicks; Discover(tick); }
            for (int i = squads.Count - 1; i >= 0; i--)
            {
                Squad squad = squads[i];
                if (tick < squad.NextThink) continue;
                squad.NextThink = tick + SquadPolicy.ThinkTicks;
                try { Think(squad, tick); }
                catch (Exception e)
                {
                    // A failing squad returns to vanilla instead of freezing its pawns.
                    Log.ErrorOnce("[Helodrace] Squad AI failed for " + squad.Id + ": " + e, squad.Id.GetHashCode());
                    Remove(squad);
                }
            }
        }

        public void Discover(int tick)
        {
            foreach (Lord lord in map.lordManager.lords.ToArray()) Register(lord, tick);
        }

        public void Register(Lord lord, int tick)
        {
            if (lord?.faction == null || !lord.faction.HostileTo(Faction.OfPlayer) || lord.LordJob is LordJob_HelodMortar) return;
            bool staleDuty = false;
            foreach (Pawn pawn in lord.ownedPawns)
            {
                if (byPawn.ContainsKey(pawn)) continue;
                // Saved squad duties without a live squad (after a load) go back to vanilla.
                if (SquadDuties.Owned(pawn.mindState?.duty?.def)) staleDuty = true;
                RaidTacticalUnit unit = RaidTacticalUnit.ForPawn(pawn);
                if (unit == null) continue;
                Squad squad = squads.Find(s => s.Id == unit.Id);
                if (squad == null) { squad = new Squad(unit, tick); squads.Add(squad); }
                foreach (Pawn member in unit.Members) byPawn[member] = squad;
            }
            if (staleDuty) lord.CurLordToil?.UpdateAllDuties();
        }

        private void Remove(Squad squad)
        {
            bool owned = squad.Stage != SquadStage.Vanilla;
            ReleaseWeaponModes(squad);
            squads.Remove(squad);
            foreach (Pawn pawn in byPawn.Where(pair => pair.Value == squad).Select(pair => pair.Key).ToArray()) byPawn.Remove(pawn);
            if (owned && IsAssaultToil(squad.Lord?.CurLordToil)) squad.Lord.CurLordToil.UpdateAllDuties();
        }

        private void Think(Squad squad, int tick)
        {
            bool membersChanged = RefreshMembers(squad);
            if (squad.Live.Count == 0)
            {
                if (!squad.Unit.Members.Any(pawn => pawn.Spawned && pawn.Map == map && !pawn.Dead)) Remove(squad);
                else SetVanilla(squad, tick);
                return;
            }
            if (!IsAssaultToil(squad.Lord?.CurLordToil) || squad.Live.Count < 2) { SetVanilla(squad, tick); return; }
            IntVec3 centroid = squad.Centroid();
            squad.Range = RepresentativeRange(squad.Live);
            squad.Contacts.Scan(squad, map, centroid, tick);
            SquadStage next = Decide(squad, tick);
            if (next == SquadStage.Vanilla) { SetVanilla(squad, tick); return; }
            IntVec3 focus = squad.Contacts.Focus();
            if (next != squad.Stage)
            {
                squad.Stage = next; squad.StageStarted = tick;
                Plan(squad, focus, tick, true);
            }
            else if (membersChanged || tick >= squad.NextPlan || !squad.Focus.IsValid
                || focus.DistanceToSquared(squad.Focus) > SquadPolicy.FocusShift * SquadPolicy.FocusShift)
                Plan(squad, focus, tick, membersChanged);
            else Progress(squad, tick);
            UpdateWeaponModes(squad);
        }

        // Live members on this map under the squad's current lord. Returns true
        // when the set changed since the previous think.
        private bool RefreshMembers(Squad squad)
        {
            squad.Live.Clear();
            Lord lord = null; int signature = 17;
            foreach (Pawn pawn in squad.Unit.Members)
            {
                if (!pawn.Spawned || pawn.Map != map || pawn.Dead || pawn.Downed || pawn.InMentalState
                    || pawn.Faction != squad.Unit.Faction) continue;
                Lord current = pawn.GetLord();
                if (current == null) continue;
                lord = lord ?? current;
                if (current != lord) continue;
                squad.Live.Add(pawn); signature = signature * 31 + pawn.thingIDNumber;
            }
            squad.Lord = lord;
            bool changed = signature != squad.MemberSignature;
            squad.MemberSignature = signature;
            return changed;
        }

        private static float RepresentativeRange(List<Pawn> pawns)
        {
            var ranges = new List<float>(pawns.Count);
            foreach (Pawn pawn in pawns)
            {
                Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
                if (verb?.verbProps?.Ranged == true && !verb.IsMeleeAttack) ranges.Add(verb.EffectiveRange);
            }
            if (ranges.Count == 0) return SquadPolicy.DefaultRange;
            ranges.Sort();
            return Math.Max(12f, Math.Min(45f, ranges[ranges.Count / 2]));
        }

        private static SquadStage Decide(Squad squad, int tick)
        {
            SquadContact nearest = squad.Contacts.Nearest();
            if (nearest == null) return SquadStage.Vanilla;
            float range = nearest.Range;
            bool harmed = tick - squad.Contacts.LastHarm <= SquadPolicy.HarmMemoryTicks;
            float engage = squad.Range * (squad.Stage == SquadStage.Engage ? SquadPolicy.EngageHoldFactor : SquadPolicy.EngageRangeFactor);
            if (range <= engage) return SquadStage.Engage;
            float brace = squad.Range * SquadPolicy.BraceRangeFactor;
            // Under fire from beyond our range: radio squads close by bounds,
            // the others take cover where they are rather than charging.
            if (harmed) return squad.Radio && FireteamCount(squad) >= 2 ? SquadStage.Advance : SquadStage.Brace;
            if (range <= brace && (nearest.Approaching || squad.Stage == SquadStage.Brace && tick - squad.StageStarted < SquadPolicy.ReplanTicks))
                return SquadStage.Brace;
            if (squad.Radio && FireteamCount(squad) >= 2) return SquadStage.Advance;
            return SquadStage.Vanilla;
        }

        private static int FireteamCount(Squad squad) =>
            squad.Unit.Group.children.Count(group => group.Members.Any(squad.Live.Contains));

        private void SetVanilla(Squad squad, int tick)
        {
            if (squad.Stage == SquadStage.Vanilla && squad.Teams.Count == 0) return;
            bool owned = squad.Stage != SquadStage.Vanilla;
            ReleaseWeaponModes(squad);
            squad.Stage = SquadStage.Vanilla; squad.StageStarted = tick;
            squad.Teams.Clear(); squad.Focus = IntVec3.Invalid;
            if (owned && IsAssaultToil(squad.Lord?.CurLordToil)) squad.Lord.CurLordToil.UpdateAllDuties();
        }

        // Vanilla just rewrote every duty of this lord; put the squad duties back.
        internal void ReapplyAfterVanilla(Lord lord)
        {
            foreach (Squad squad in squads)
                if (squad.Lord == lord && squad.Stage != SquadStage.Vanilla) ApplyDuties(squad, false);
        }

        internal void Harmed(Pawn victim, Thing instigator)
        {
            Squad squad = SquadOf(victim);
            if (squad == null || instigator == null || !instigator.HostileTo(victim)) return;
            int tick = Find.TickManager.TicksGame;
            squad.Contacts.Harmed(instigator, victim.Position, tick);
            squad.NextThink = Math.Min(squad.NextThink, tick + 5);
        }

        private List<IntVec3> OtherPosts(Squad except)
        {
            var posts = new List<IntVec3>();
            foreach (Squad squad in squads)
                if (squad != except && squad.Stage != SquadStage.Vanilla)
                    foreach (SquadTeam team in squad.Teams) posts.AddRange(team.Posts.Values);
            return posts;
        }

        private void ApplyDuties(Squad squad, bool interrupt)
        {
            foreach (SquadTeam team in squad.Teams)
                foreach (KeyValuePair<Pawn, IntVec3> pair in team.Posts)
                {
                    Pawn pawn = pair.Key;
                    if (!squad.Live.Contains(pawn) || pawn.mindState == null) continue;
                    bool moving = team.Task == TeamTask.Move;
                    DutyDef def = moving ? SquadDuties.Move : SquadDuties.Hold;
                    PawnDuty old = pawn.mindState.duty;
                    pawn.mindState.duty = new PawnDuty(def, pair.Value, squad.Focus,
                        moving ? SquadPolicy.MoveArrivalRadius - .1f : SquadPolicy.HoldRadius)
                        { locomotion = moving ? LocomotionUrgency.Sprint : LocomotionUrgency.Jog };
                    if (interrupt && (old?.def != def || !old.focus.IsValid || old.focus.Cell != pair.Value)) InterruptIdle(pawn);
                }
        }

        // Only a movement or waiting job is cut short; shots, reloads, flight
        // from explosives, treatment and forced orders finish on their own.
        private static void InterruptIdle(Pawn pawn)
        {
            Job job = pawn.CurJob;
            if (job == null || job.playerForced) return;
            if (job.def == JobDefOf.Goto || job.def == JobDefOf.Wait_Combat || job.def == JobDefOf.Wait
                || job.def == JobDefOf.Wait_Wander || job.def == JobDefOf.GotoWander)
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true, true);
        }

        private static void UpdateWeaponModes(Squad squad)
        {
            foreach (SquadTeam team in squad.Teams)
                foreach (Pawn pawn in team.Members)
                {
                    CompSharpshooterWeapon weapon = pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>();
                    if (weapon == null || !weapon.Props.DefensiveAI) continue;
                    // Defensive marksman modes only at a held post.
                    bool holding = team.Task == TeamTask.Hold && team.Posts.TryGetValue(pawn, out IntVec3 post)
                        && pawn.Position.InHorDistOf(post, SquadPolicy.HoldRadius);
                    if (holding) { if (!weapon.altModeActive && weapon.CanSwitchMode) weapon.TrySetMode(true, true); }
                    else weapon.ReleaseAIMode();
                }
        }

        private static void ReleaseWeaponModes(Squad squad)
        {
            foreach (Pawn pawn in squad.Unit.Members)
                pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>()?.ReleaseAIMode();
        }
    }
}
