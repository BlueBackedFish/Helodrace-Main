using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace.ModernWar;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    // With the squad tactical engine off, raiders keep vanilla AI except for
    // two self-contained behaviours: sledgehammer breaching and underbarrel
    // grenade launchers. Both stay inert while the tactical engine owns raids.
    public static class BasicRaidAI
    {
        private const int RetryTicks = 900, UnderbarrelCheckTicks = 180, ReloadScanTicks = 120;
        private static readonly Dictionary<int, (int target, int tick)> lastBreach = new Dictionary<int, (int, int)>();
        private static readonly Dictionary<int, int> nextUnderbarrelCheck = new Dictionary<int, int>();

        public static bool Active => TacticalEngineSelection.Kind != TacticalEngineKind.New;

        private static bool Raider(Pawn pawn) => pawn?.Spawned == true && pawn.Faction != null
            && pawn.Faction != Faction.OfPlayer && !pawn.Dead && !pawn.Downed && !pawn.InMentalState
            && pawn.RaceProps.Humanlike;

        // Vanilla bashes a blocking door (path follower, breach raids) or digs
        // through a wall (sappers) with melee/mining jobs. A raider wearing a
        // sledgehammer does the same work with the breaching tool instead.
        public static Job SledgehammerFor(Pawn pawn, Job job)
        {
            if (job == null || job.def != JobDefOf.AttackMelee && job.def != JobDefOf.Mine) return null;
            if (!(job.targetA.Thing is Building target) || target.def.building?.isNaturalRock == true
                || target.Faction == null || !Raider(pawn) || target.Faction == pawn.Faction
                || !pawn.HostileTo(target.Faction)) return null;
            CompSledgehammerBreach tool = CompSledgehammerBreach.WornBy(pawn);
            if (tool == null || !CompSledgehammerBreach.IsValidTarget(pawn, target)) return null;
            int tick = GenTicks.TicksGame;
            // A breach that just failed on this barrier falls back to vanilla
            // rather than restarting every tick.
            if (lastBreach.TryGetValue(pawn.thingIDNumber, out var last) && last.target == target.thingIDNumber
                && tick - last.tick < RetryTicks) return null;
            if (!pawn.CanReserve(target) || !CompSledgehammerBreach.TryFindInteractionCell(pawn, target, out IntVec3 cell))
                return null;
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail(CompSledgehammerBreach.JobDefName);
            if (def == null) return null;
            lastBreach[pawn.thingIDNumber] = (target.thingIDNumber, tick);
            Job breach = JobMaker.MakeJob(def, target, cell, tool.parent);
            breach.locomotionUrgency = job.locomotionUrgency;
            return breach;
        }

        public static Verb SelectUnderbarrel(Pawn pawn, Thing target, Verb fallback)
        {
            if (target == null || fallback is Verb_ShootModularUnderbarrel || !Raider(pawn)
                || pawn.pather.Moving || pawn.stances.FullBodyBusy) return fallback;
            int tick = GenTicks.TicksGame;
            if (nextUnderbarrelCheck.TryGetValue(pawn.thingIDNumber, out int due) && tick < due) return fallback;
            nextUnderbarrelCheck[pawn.thingIDNumber] = tick + UnderbarrelCheckTicks;
            Verb_ShootModularUnderbarrel verb = pawn.equipment?.PrimaryEq?.AllVerbs
                .OfType<Verb_ShootModularUnderbarrel>().FirstOrDefault();
            CompModularUnderbarrelLauncher launcher = ModularUnderbarrelLauncherUtility.Installed(pawn.equipment?.Primary);
            if (verb == null || launcher?.Loaded != true || !target.HostileTo(pawn)
                || target is Pawn enemy && (enemy.Dead || enemy.Downed) || !verb.CanHitTarget(target)) return fallback;
            return MapComponent_TacticalCommands.SafeExplosiveShot(pawn, launcher.ProjectileDef, pawn.Position,
                target.Position, scatterOverride: verb.verbProps.ForcedMissRadius + 2) ? verb : fallback;
        }

        internal static void ReloadUnderbarrels(Map map)
        {
            JobDef reload = DefDatabase<JobDef>.GetNamedSilentFail("HD_ReloadModularUnderbarrel");
            if (reload == null) return;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (!Raider(pawn) || pawn.pather.Moving || pawn.stances.FullBodyBusy
                    || pawn.CurJob?.playerForced == true || pawn.CurJobDef == reload
                    || pawn.CurJobDef?.defName == CompSledgehammerBreach.JobDefName) continue;
                CompModularUnderbarrelLauncher launcher = ModularUnderbarrelLauncherUtility.Installed(pawn.equipment?.Primary);
                if (launcher == null || launcher.Loaded || !launcher.HasAmmo(pawn)) continue;
                pawn.jobs.StartJob(JobMaker.MakeJob(reload, pawn.equipment.Primary), JobCondition.InterruptForced,
                    resumeCurJobAfterwards: pawn.CurJob?.def.suspendable == true);
            }
        }

        internal static void Prune()
        {
            int tick = GenTicks.TicksGame;
            foreach (int key in lastBreach.Where(entry => tick - entry.Value.tick >= RetryTicks).Select(entry => entry.Key).ToList())
                lastBreach.Remove(key);
            foreach (int key in nextUnderbarrelCheck.Where(entry => entry.Value <= tick).Select(entry => entry.Key).ToList())
                nextUnderbarrelCheck.Remove(key);
        }

        internal const int ScanTicks = ReloadScanTicks;
    }

    public sealed class MapComponent_BasicRaidAI : MapComponent
    {
        public MapComponent_BasicRaidAI(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if (!BasicRaidAI.Active || (GenTicks.TicksGame + map.uniqueID) % BasicRaidAI.ScanTicks != 0) return;
            BasicRaidAI.ReloadUnderbarrels(map);
            if (GenTicks.TicksGame % 2500 < BasicRaidAI.ScanTicks) BasicRaidAI.Prune();
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_BasicRaidAI_Sledgehammer
    {
        public static void Prefix(Pawn ___pawn, ref Job newJob)
        {
            if (!BasicRaidAI.Active) return;
            Job breach = BasicRaidAI.SledgehammerFor(___pawn, newJob);
            if (breach != null) newJob = breach;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.TryGetAttackVerb))]
    public static class Patch_BasicRaidAI_Underbarrel
    {
        public static void Postfix(Pawn __instance, Thing target, ref Verb __result)
        {
            if (__result == null || !BasicRaidAI.Active || __instance.Faction == Faction.OfPlayer) return;
            __result = BasicRaidAI.SelectUnderbarrel(__instance, target, __result);
        }
    }
}
