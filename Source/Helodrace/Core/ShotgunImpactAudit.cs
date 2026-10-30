#if SHOTGUN_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    // Trace the whole native flight/health pipeline, including losses hidden by
    // launch-only accuracy sampling. Never included in the production assembly.
    public sealed partial class ShotgunInGameChecks
    {
        private int auditShot;
        private bool auditWaiting;
        private bool auditPrepared;
        private Verb_ShootShotgun auditVerb;
        private readonly int[] auditHits = new int[8];
        private readonly int[] auditDamaging = new int[8];
        private string AuditResults => Path.Combine(GenFilePaths.SaveDataFolderPath, "shotgun-impact-audit.txt");
        private void TickImpactAudit()
        {
            try
            {
                const int repeats = 8;
                const int scenarios = 8;
                if (auditWaiting)
                {
                    ShotgunImpactTrace trace = ShotgunImpactTrace.current;
                    int visible = victim.health.hediffSet.hediffs.OfType<Hediff_Injury>()
                        .Sum(injury => GameComponent_ShotgunWounds.Current.For(injury)?.pelletCount ?? 0);
                    string summary = $"SHOT case={auditShot / repeats}, repeat={auditShot % repeats}, initial={trace.initial}, impacts={trace.impacts}, damaging={trace.damaging.Count}, visible={visible}, rejectedParts={trace.rejectedParts}, damage={trace.damage:0.00}, centre={trace.centre}, dead={victim.Dead}, posture={victim.GetPosture()}";
                    File.AppendAllText(AuditResults, summary + "\n" + string.Join("\n", trace.events) + "\n");
                    auditHits[auditShot / repeats] += trace.impacts;
                    auditDamaging[auditShot / repeats] += trace.damaging.Count;
                    ShotgunImpactTrace.current = null;
                    auditWaiting = false;
                    auditShot++;
                }
                if (auditShot == repeats * scenarios)
                {
                    for (int caseIndex = 0; caseIndex < scenarios; caseIndex++)
                    {
                        float hits = (float)auditHits[caseIndex] / repeats;
                        float damaging = (float)auditDamaging[caseIndex] / repeats;
                        File.AppendAllText(AuditResults, $"MEAN case={caseIndex}, impacts={hits:0.00}, damaging={damaging:0.00}\n");
                        if (hits < 4.5f || damaging < 4f) throw new InvalidOperationException("Insufficient actual close hits in case " + caseIndex);
                    }
                    File.AppendAllText(AuditResults, "COMPLETE\n");
                    finished = true;
                    Waylith.EngineApplication.Instance.Quit(0);
                    return;
                }
                if (auditPrepared)
                {
                    FireAuditShot();
                    auditPrepared = false;
                    return;
                }
                if (auditShot == 0) File.WriteAllText(AuditResults, "Native shotgun impact pipeline audit; 8 shots per scenario\n"
                    + "Cases: 0 human 5 stationary; 1 human 8 stationary; 2 Helod 5 stationary; 3 Helod 8 stationary; "
                    + "4 M1887 human 8 stationary; 5 human 8 strafe; 6 human 8 away; 7 human 8 approaching\n");
                int scenario = auditShot / repeats;
                bool helod = scenario == 2 || scenario == 3;
                int distance = scenario == 0 || scenario == 2 ? 5 : 8;
                string weapon = scenario == 4 ? "HD_Gun_WinM1887_Weapon" : "HD_Gun_M1897_Weapon";
                auditVerb = PrepareShot(helod ? DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist") : PawnKindDefOf.Colonist, distance, 0, weapon);
                if (scenario >= 5)
                {
                    IntVec3 direction = scenario == 5 ? new IntVec3(1, 0, 0) : scenario == 6 ? new IntVec3(0, 0, 1) : new IntVec3(0, 0, -1);
                    IntVec3 goal = victim.Position + direction * 10;
                    foreach (IntVec3 cell in CellRect.FromLimits(victim.Position, goal).ExpandedBy(1))
                        foreach (Thing thing in cell.GetThingList(victim.Map).ToArray())
                            if (!(thing is Pawn) && (thing.def.category == ThingCategory.Plant || thing.def.category == ThingCategory.Building
                                || thing.def.Fillage != FillCategory.None)) thing.Destroy();
                    victim.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, goal), JobCondition.InterruptForced);
                }
                // Observe established movement, as a normal weapon does after
                // warmup, rather than firing before the path request exists.
                auditPrepared = true;
                waitUntil = Find.TickManager.TicksGame + 10;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            }
            catch (Exception error)
            {
                finished = true;
                File.AppendAllText(AuditResults, "FAIL " + error + "\n");
                Waylith.EngineApplication.Instance.Quit(1);
            }
        }
        private void FireAuditShot()
        {
            var traceNext = new ShotgunImpactTrace { pawn = victim };
            ShotgunImpactTrace.current = traceNext;
            if (!(bool)AccessTools.Method(typeof(Verb_ShootShotgun), "TryCastShot").Invoke(auditVerb, null)) throw new InvalidOperationException("Audit shot failed");
            var pellets = shooter.Map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile).OfType<Projectile_ShotgunPellet>()
                .Where(pellet => pellet.Launcher == shooter).ToArray();
            if (pellets.Length != 9) throw new InvalidOperationException("Expected nine pellets");
            if (pellets.Any(pellet => pellet.UpdateRateTicks != 1)) throw new InvalidOperationException("Pellets must tick every game tick");
            traceNext.shell = pellets[0].Shell;
            traceNext.centre = traceNext.shell.CentreFor(victim, pellets[0].DamageDef)?.Label;
            traceNext.initial = pellets.Count(pellet => pellet.usedTarget.Thing == victim && pellet.HitsAt(victim, traceNext.shell.targetPoint));
            traceNext.events.Add($"START tick={Find.TickManager.TicksGame}, scenario={auditShot / 8}, size={victim.BodySize:0.00}, position={ShotgunBallistics.Position(victim)}, velocity={ShotgunBallistics.Velocity(victim)}, draw={victim.DrawPos}, aim={traceNext.shell.targetPoint}, error={traceNext.shell.aimError}");
            auditWaiting = true;
            waitUntil = Find.TickManager.TicksGame + 45;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
        }
    }

    internal sealed class ShotgunImpactTrace
    {
        internal static ShotgunImpactTrace current;
        internal Pawn pawn;
        internal ShotgunShell shell;
        internal int initial, impacts, rejectedParts;
        internal string centre;
        internal float damage;
        internal readonly HashSet<int> damaging = new HashSet<int>();
        internal readonly List<string> events = new List<string>();
    }

    [HarmonyPatch(typeof(Projectile_ShotgunPellet), "Impact")]
    internal static class Patch_ShotgunImpactTrace
    {
        private static void Prefix(Projectile_ShotgunPellet __instance, Thing hitThing, bool blockedByShield)
        {
            ShotgunImpactTrace trace = ShotgunImpactTrace.current;
            if (trace == null || __instance.Shell != trace.shell) return;
            if (hitThing == trace.pawn && !blockedByShield) trace.impacts++;
            trace.events.Add($"IMPACT tick={Find.TickManager.TicksGame}, pellet={__instance.thingIDNumber}, hit={hitThing?.def.defName ?? "ground"}, shield={blockedByShield}, posture={trace.pawn.GetPosture()}, position={ShotgunBallistics.Position(trace.pawn)}, pose={trace.shell.PoseFor(trace.pawn).position}, draw={trace.pawn.DrawPos}, fits={__instance.HitsPawn(trace.pawn)}");
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class Patch_ShotgunDamageTrace
    {
        private static void Postfix(Thing __instance, DamageWorker.DamageResult __result)
        {
            ShotgunImpactTrace trace = ShotgunImpactTrace.current;
            ShotgunImpact impact = ShotgunSpreadContext.Impact;
            if (trace == null || __instance != trace.pawn || impact?.shell != trace.shell) return;
            if (__result.totalDamageDealt > 0f) trace.damaging.Add(impact.pellet.thingIDNumber);
            trace.damage += __result.totalDamageDealt;
            trace.events.Add($"DAMAGE pellet={impact.pellet.thingIDNumber}, part={impact.surface?.Label ?? "null"}, dealt={__result.totalDamageDealt:0.00}, wounded={__result.wounded}, deflected={__result.deflected}, dead={trace.pawn.Dead}");
        }
    }

    [HarmonyPatch(typeof(DamageWorker_AddInjury), "GetExactPartFromDamageInfo")]
    internal static class Patch_ShotgunPartTrace
    {
        private static void Postfix(Pawn pawn, BodyPartRecord __result)
        {
            ShotgunImpactTrace trace = ShotgunImpactTrace.current;
            if (trace?.pawn == pawn && ShotgunSpreadContext.Impact?.shell == trace.shell && __result == null) trace.rejectedParts++;
        }
    }

}
#endif
