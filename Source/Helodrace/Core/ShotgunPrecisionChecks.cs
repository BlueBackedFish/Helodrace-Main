#if SHOTGUN_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Helodrace.Tactical;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public sealed partial class ShotgunInGameChecks
    {
        private int precisionShot;
        private bool precisionWaiting;
        private ShotgunShell precisionShell;
        private BodyPartRecord precisionCentre;
        private static Dictionary<int, TacticalPrecisionPart> PrecisionSelections =>
            (Dictionary<int, TacticalPrecisionPart>)AccessTools.Field(typeof(TacticalPrecisionFireUtility), "selectedParts").GetValue(null);

        private void TickPrecisionAudit()
        {
            try
            {
                if (precisionWaiting)
                {
                    var trace = ShotgunImpactTrace.current;
                    Check(trace.impacts > 0 && trace.damaging.Count > 0, "precision shot reaches native collision and damage");
                    Check(trace.events.Any(entry => entry.StartsWith("DAMAGE ") && !entry.Contains("part=null")),
                        "precision shot uses shotgun surface selection rather than exact-part override");
                    Check(precisionShell.CentreFor(victim, DamageDefOf.Bullet) == precisionCentre,
                        "selection changes during flight preserve the fired shell centre");
                    File.AppendAllText(Results, $"SHOT {precisionShot}: centre={precisionCentre.Label}, impacts={trace.impacts}, damaging={trace.damaging.Count}\n"
                        + string.Join("\n", trace.events.Where(entry => entry.StartsWith("DAMAGE "))) + "\n");
                    ShotgunImpactTrace.current = null;
                    PrecisionSelections.Remove(shooter.thingIDNumber);
                    precisionWaiting = false;
                    precisionShot++;
                }
                if (precisionShot == 8)
                {
                    File.AppendAllText(Results, "COMPLETE\n");
                    finished = true;
                    Application.Quit(0);
                    return;
                }
                if (precisionShot == 0) File.WriteAllText(Results, "Native CQB shotgun precision checks\n");
                var requested = (TacticalPrecisionPart)(1 + precisionShot % 4);
                var kind = precisionShot < 4 ? PawnKindDefOf.Colonist : DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist");
                var verb = PrepareShot(kind, 5, 20, "HD_Gun_M1897_Weapon");
                PrecisionSelections[shooter.thingIDNumber] = requested;
                Check(!TacticalPrecisionFireUtility.TryGetRequestedPart(shooter, victim, verb, out _), "untrained shooter cannot use precision");
                shooter.health.AddHediff(DefDatabase<HediffDef>.GetNamed(
                    precisionShot < 4 ? "HD_CQBTraining_LOW" : "HD_CQBTraining_HIGH"));
                Check(!TacticalPrecisionFireUtility.TryGetRequestedPart(shooter, victim, verb, out _), "precision requires aiming");
                TacticalAimUtility.SetFocusForAI(shooter, victim.Position);
                Check(TacticalPrecisionFireUtility.TryGetRequestedPart(shooter, victim, verb, out precisionCentre),
                    kind.defName + " resolves " + requested);
                float expected = requested == TacticalPrecisionPart.Head ? 1.75f : requested == TacticalPrecisionPart.Chest ? 1f : 1.35f;
                Check(TacticalPrecisionFireUtility.TryGetWarmupMultiplier(verb, out float multiplier) && Mathf.Approximately(multiplier, expected),
                    "selected part preserves its warmup multiplier");
                TacticalAimUtility.SetFocusForAI(shooter, shooter.Position + new IntVec3(0, 0, -5));
                Check(!TacticalPrecisionFireUtility.TryGetRequestedPart(shooter, victim, verb, out _), "precision requires the focus arc");
                TacticalAimUtility.SetFocusForAI(shooter, victim.Position);

                // The ordinary projectile branch must still request an exact
                // part; shotgun shells deliberately use the shared centre.
                var ordinary = (Projectile)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Bullet_AssaultRifle"));
                TacticalPrecisionFireUtility.RegisterShot(ordinary, shooter, victim);
                object previous = TacticalPrecisionFireUtility.BeginImpact(ordinary);
                try
                {
                    var damage = new DamageInfo(DamageDefOf.Bullet, 1f, instigator: shooter);
                    TacticalPrecisionFireUtility.ApplyRequestedPart(victim, ref damage);
                    Check(damage.HitPart == precisionCentre, "ordinary projectile precision remains exact-part targeting");
                }
                finally { TacticalPrecisionFireUtility.EndImpact(ordinary, previous); }

                Check((bool)AccessTools.Method(typeof(Verb_ShootShotgun), "TryCastShot").Invoke(verb, null), "precision shell fires");
                var pellets = shooter.Map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile).OfType<Projectile_ShotgunPellet>()
                    .Where(pellet => pellet.Launcher == shooter).ToArray();
                Check(pellets.Length == 9 && pellets.Select(pellet => pellet.Shell).Distinct().Count() == 1, "all nine precision pellets share one shell");
                precisionShell = pellets[0].Shell;
                Check(precisionShell.CentreFor(victim, pellets[0].DamageDef) == precisionCentre, "launch hook replaces random centre with selected part");
                ShotgunImpactTrace.current = new ShotgunImpactTrace { pawn = victim, shell = precisionShell };
                PrecisionSelections[shooter.thingIDNumber] = requested == TacticalPrecisionPart.Head ? TacticalPrecisionPart.Leg : TacticalPrecisionPart.Head;
                foreach (var pellet in pellets) TacticalPrecisionFireUtility.RegisterShot(pellet, shooter, victim);
                precisionWaiting = true;
                waitUntil = Find.TickManager.TicksGame + 45;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
            }
            catch (Exception error)
            {
                finished = true;
                File.AppendAllText(Results, "FAIL " + error + "\n");
                Application.Quit(1);
            }
        }
    }
}
#endif
