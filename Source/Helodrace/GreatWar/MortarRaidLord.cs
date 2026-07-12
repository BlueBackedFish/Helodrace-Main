using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Helodrace
{
    public class LordJob_MortarRaidHold : LordJob
    {
        private IntVec3 anchor;

        public LordJob_MortarRaidHold() { }
        public LordJob_MortarRaidHold(IntVec3 anchor) { this.anchor = anchor; }

        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            LordToil_MortarRaidHold hold = new LordToil_MortarRaidHold(anchor);
            graph.AddToil(hold);
            graph.StartingToil = hold;
            return graph;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref anchor, "mortarRaidAnchor");
        }
    }

    public class LordToil_MortarRaidHold : LordToil
    {
        private IntVec3 anchor;
        private int lastLongRangeResponseTick = -99999;
        private Pawn responsePawn;
        private Thing responseTarget;
        private Thing groupEngagementTarget;
        private int lastTacticalResponseAttemptTick = -99999;
        private int nextGroupManeuverDecisionTick;
        private bool groupManeuverAllowed = true;

        public LordToil_MortarRaidHold() { }
        public LordToil_MortarRaidHold(IntVec3 anchor) { this.anchor = anchor; }

        public override void UpdateAllDuties()
        {
            var pawns = lord.ownedPawns.Where(p => !p.Dead).OrderBy(p => p.thingIDNumber).ToList();
            int dx = Map.Center.x - anchor.x;
            int dz = Map.Center.z - anchor.z;
            IntVec3 towardColony = System.Math.Abs(dx) >= System.Math.Abs(dz)
                ? new IntVec3(System.Math.Sign(dx), 0, 0)
                : new IntVec3(0, 0, System.Math.Sign(dz));
            IntVec3 side = new IntVec3(-towardColony.z, 0, towardColony.x);
            var automatic = pawns.Where(p => p.kindDef?.defName == "HD_GW_HelodAutomaticRifleman").ToList();

            foreach (Pawn pawn in pawns)
            {
                int autoIndex = automatic.IndexOf(pawn);
                IntVec3 post;
                float radius;
                if (autoIndex >= 0)
                {
                    post = anchor + towardColony * 2 + side * ((autoIndex - (automatic.Count - 1) / 2) * 2);
                    radius = 1.2f;
                }
                else
                {
                    int index = pawns.IndexOf(pawn);
                    post = anchor + new IntVec3((index % 5) - 2, 0, (index / 5) * 2 - 2);
                    radius = 2.5f;
                }
                if (!post.InBounds(Map) || !post.Walkable(Map)) post = anchor;
                pawn.mindState.duty = new PawnDuty(DutyDefOf.Defend, post, radius)
                {
                    locomotion = LocomotionUrgency.Walk,
                    maxDanger = Danger.Deadly
                };
            }
        }

        public override void LordToilTick()
        {
            base.LordToilTick();
            if (!Map.IsHashIntervalTick(30)) return;
            if (responseTarget != null && (!responseTarget.Spawned || responseTarget.Destroyed
                || (responseTarget is Pawn targetPawn && targetPawn.Downed)
                || responsePawn == null || responsePawn.Downed || responsePawn.Position.DistanceTo(anchor) > 45f))
            {
                responsePawn = null;
                responseTarget = null;
                UpdateAllDuties();
            }
            if (groupEngagementTarget != null && (!groupEngagementTarget.Spawned || groupEngagementTarget.Destroyed
                || (groupEngagementTarget is Pawn engagedPawn && engagedPawn.Downed)))
            {
                groupEngagementTarget = null;
                UpdateAllDuties();
            }
            foreach (Pawn pawn in lord.ownedPawns.Where(p => p.kindDef?.defName == "HD_GW_HelodAutomaticRifleman" && !p.Dead && !p.Downed))
            {
                CompSharpshooterWeapon mode = pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>();
                if (mode == null || !mode.CanUseSharpshooterMode) continue;
                bool settled = !pawn.pather.Moving && pawn.mindState.duty != null
                    && pawn.Position.DistanceTo(pawn.mindState.duty.focus.Cell) <= pawn.mindState.duty.radius;
                if (settled && !mode.altModeActive) mode.PerformSwitch();
                else if (!settled && mode.altModeActive) mode.PerformSwitch();
            }
            IssueDefensiveFire();
            DetectActiveLongRangeShooters();
        }

        private void DetectActiveLongRangeShooters()
        {
            foreach (Pawn attacker in Map.mapPawns.AllPawnsSpawned.Where(p => !p.Dead && !p.Downed
                && lord.faction.HostileTo(p.Faction) && p.CurJobDef == JobDefOf.AttackStatic))
            {
                Pawn intendedVictim = attacker.CurJob?.targetA.Pawn;
                if (intendedVictim == null || intendedVictim.GetLord() != lord || CanAnyDefenderFireAt(attacker))
                    continue;
                TryRespondToLongRangeThreat(intendedVictim, attacker, intendedVictim.Position);
                break;
            }
        }

        private void IssueDefensiveFire()
        {
            var targets = Map.mapPawns.AllPawnsSpawned
                .Where(p => !p.Dead && !p.Downed && lord.faction.HostileTo(p.Faction))
                .ToList();
            if (targets.Count == 0) return;

            var availableDefenders = lord.ownedPawns.Where(p => !p.Dead && !p.Downed && p.Spawned
                && !IsMortarWork(p.CurJobDef?.defName)).ToList();
            groupEngagementTarget = targets
                .OrderBy(t => t.Position.DistanceToSquared(anchor))
                .FirstOrDefault(t => availableDefenders.Any(d => CanFireAt(d, t)));
            if (Find.TickManager.TicksGame >= nextGroupManeuverDecisionTick)
            {
                nextGroupManeuverDecisionTick = Find.TickManager.TicksGame + 300;
                groupManeuverAllowed = Map.GetComponent<MapComponent_MortarRaidAI>()?.TacticalActionAllowed(lord.faction) ?? true;
            }

            foreach (Pawn defender in availableDefenders)
            {
                string currentJob = defender.CurJobDef?.defName;
                if (defender.CurJobDef == JobDefOf.AttackStatic)
                    continue;

                if (defender.kindDef?.defName == "HD_GW_HelodAutomaticRifleman"
                    && (defender.pather.Moving || defender.mindState.duty == null
                        || defender.Position.DistanceTo(defender.mindState.duty.focus.Cell) > defender.mindState.duty.radius))
                    continue;

                Pawn target = null;
                foreach (Pawn candidate in targets.OrderBy(t => defender.Position.DistanceToSquared(t.Position)))
                {
                    Verb candidateVerb = defender.TryGetAttackVerb(candidate, false, false);
                    if (candidateVerb != null && candidateVerb.CanHitTarget(candidate))
                    {
                        target = candidate;
                        break;
                    }
                }
                if (target == null)
                {
                    if (groupEngagementTarget == null || !groupManeuverAllowed) continue;
                    CompSharpshooterWeapon movingMode = defender.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>();
                    if (movingMode != null && movingMode.altModeActive) movingMode.PerformSwitch();
                    defender.mindState.duty = new PawnDuty(DutyDefOf.AssaultThing, groupEngagementTarget)
                    {
                        locomotion = LocomotionUrgency.Jog,
                        maxDanger = Danger.Deadly
                    };
                    if (defender.CurJob != null)
                        defender.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    continue;
                }

                Job attack = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                attack.expiryInterval = 120;
                attack.checkOverrideOnExpire = true;
                if (defender.CurJob != null)
                    defender.jobs.EndCurrentJob(JobCondition.InterruptForced);
                defender.jobs.TryTakeOrderedJob(attack, JobTag.Misc);
            }
        }

        private static bool IsMortarWork(string jobName)
        {
            return jobName == "HD_AssembleM1MortarMount" || jobName == "HD_AssembleM1MortarBarrel"
                || jobName == "HD_BeginM1MortarAssembly" || jobName == "ManTurret";
        }

        private static bool CanFireAt(Pawn defender, Thing target)
        {
            Verb verb = defender.TryGetAttackVerb(target, false, false);
            return verb != null && verb.CanHitTarget(target);
        }

        public override void Notify_PawnDamaged(Pawn victim, DamageInfo damageInfo)
        {
            base.Notify_PawnDamaged(victim, damageInfo);
            TryRespondToLongRangeAttack(victim, damageInfo);
        }

        public void TryRespondToLongRangeAttack(Pawn victim, DamageInfo damageInfo)
        {
            TryRespondToLongRangeThreat(victim, damageInfo.Instigator, victim?.Position ?? IntVec3.Invalid);
        }

        public void TryRespondToLongRangeThreat(Pawn victim, Thing attacker, IntVec3 impactCell)
        {
            if (victim == null || attacker == null || !attacker.Spawned || attacker.Map != Map
                || !attacker.HostileTo(victim) || !impactCell.IsValid)
                return;

            // If anyone in the position can already shoot the attacker, normal defensive fire handles it.
            // The response team sorties only against a shooter outside every defender's current reach/LOS.
            if (CanAnyDefenderFireAt(attacker))
                return;

            int tacticalTick = Find.TickManager.TicksGame;
            if (tacticalTick - lastTacticalResponseAttemptTick < 120)
                return;
            lastTacticalResponseAttemptTick = tacticalTick;
            if (!(Map.GetComponent<MapComponent_MortarRaidAI>()?.TacticalActionAllowed(lord.faction) ?? true))
                return;

            Building_TurretGun mortar = Map.listerThings
                .ThingsOfDef(DefDatabase<ThingDef>.GetNamed("HD_Building_M1_81mmMortar"))
                .OfType<Building_TurretGun>()
                .FirstOrDefault(t => t.Faction == victim.Faction && !t.Destroyed);
            if (mortar != null)
            {
                mortar.OrderAttack(attacker);
            }

            int now = Find.TickManager.TicksGame;
            if (now - lastLongRangeResponseTick < 300 && responsePawn != null && !responsePawn.Downed)
                return;

            responsePawn = lord.ownedPawns
                .Where(p => !p.Dead && !p.Downed && p.Spawned
                    && p.kindDef?.defName != "HD_GW_HelodAutomaticRifleman"
                    && !p.kindDef.defName.StartsWith("HD_GW_HelodMortar")
                    && p.kindDef.defName != "HD_GW_HelodFieldRationBearer")
                .OrderBy(p => ResponsePriority(p.kindDef.defName))
                .ThenBy(p => p.Position.DistanceToSquared(attacker.Position))
                .FirstOrDefault();
            if (responsePawn == null) return;

            CompSharpshooterWeapon mode = responsePawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>();
            if (mode != null && mode.altModeActive) mode.PerformSwitch();
            responseTarget = attacker;
            lastLongRangeResponseTick = now;
            responsePawn.mindState.duty = new PawnDuty(DutyDefOf.AssaultThing, attacker)
            {
                locomotion = LocomotionUrgency.Jog,
                maxDanger = Danger.Deadly
            };
            responsePawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }

        private bool CanAnyDefenderFireAt(Thing attacker)
        {
            foreach (Pawn defender in lord.ownedPawns.Where(p => !p.Dead && !p.Downed && p.Spawned))
            {
                string jobName = defender.CurJobDef?.defName;
                if (jobName == "HD_AssembleM1MortarMount" || jobName == "HD_AssembleM1MortarBarrel"
                    || jobName == "HD_BeginM1MortarAssembly" || jobName == "ManTurret")
                    continue;
                Verb verb = defender.TryGetAttackVerb(attacker, false, false);
                if (verb != null && verb.CanHitTarget(attacker))
                    return true;
            }
            return false;
        }

        private static int ResponsePriority(string kindDefName)
        {
            if (kindDefName == "HD_GW_HelodScout") return 0;
            if (kindDefName == "HD_GW_HelodAssistantSquadLeader") return 1;
            if (kindDefName == "HD_GW_HelodSquadLeader") return 2;
            if (kindDefName == "HD_GW_HelodRifleman") return 3;
            return 4;
        }
    }

    public class LordJob_MortarRaidRetreat : LordJob
    {
        public override StateGraph CreateGraph()
        {
            StateGraph graph = new StateGraph();
            LordToil_MortarRaidRetreat retreat = new LordToil_MortarRaidRetreat();
            graph.AddToil(retreat);
            graph.StartingToil = retreat;
            return graph;
        }
    }

    public class LordToil_MortarRaidRetreat : LordToil
    {
        public override bool AllowSatisfyLongNeeds => false;

        public override void UpdateAllDuties()
        {
            foreach (Pawn pawn in lord.ownedPawns.Where(p => !p.Dead && !p.Downed))
            {
                CompSharpshooterWeapon mode = pawn.equipment?.Primary?.TryGetComp<CompSharpshooterWeapon>();
                if (mode != null && mode.altModeActive)
                    mode.PerformSwitch();
                pawn.mindState.duty = new PawnDuty(DutyDefOf.ExitMapBest)
                {
                    locomotion = LocomotionUrgency.Sprint,
                    maxDanger = Danger.Deadly
                };
            }
        }
    }

    [HarmonyPatch(typeof(Lord), nameof(Lord.LordTick))]
    public static class Patch_LordTick_RepairMortarRaidGraph
    {
        public static bool Prefix(Lord __instance)
        {
            if (__instance?.CurLordToil != null)
                return true;

            if (__instance?.LordJob is LordJob_MortarRaidRetreat)
                __instance.SetJob(new LordJob_MortarRaidRetreat());
            else if (__instance?.LordJob is LordJob_MortarRaidHold hold)
            {
                // Reloading the same job rebuilds its graph from its serialized anchor.
                __instance.SetJob(hold);
            }

            return __instance?.CurLordToil != null;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_Pawn_PostApplyDamage_MortarRaidResponse
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            if (totalDamageDealt <= 0f || __instance == null || __instance.Dead)
                return;
            Lord lord = __instance.GetLord();
            if (lord?.CurLordToil is LordToil_MortarRaidHold hold)
                hold.TryRespondToLongRangeAttack(__instance, dinfo);
        }
    }

    [HarmonyPatch(typeof(Projectile), "Impact")]
    public static class Patch_Projectile_Impact_MortarRaidResponse
    {
        public static void Prefix(Projectile __instance)
        {
            Map map = __instance?.Map;
            Thing attacker = __instance?.Launcher;
            if (map == null || attacker == null || !attacker.Spawned)
                return;

            IntVec3 impactCell = __instance.Position;
            Pawn nearbyDefender = map.mapPawns.AllPawnsSpawned
                .Where(p => !p.Dead && !p.Downed && p.Position.DistanceTo(impactCell) <= 15f
                    && attacker.HostileTo(p))
                .OrderBy(p => p.Position.DistanceToSquared(impactCell))
                .FirstOrDefault(p => p.GetLord()?.CurLordToil is LordToil_MortarRaidHold);
            if (nearbyDefender?.GetLord()?.CurLordToil is LordToil_MortarRaidHold hold)
                hold.TryRespondToLongRangeThreat(nearbyDefender, attacker, impactCell);
        }
    }
}
