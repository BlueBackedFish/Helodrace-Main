using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using HarmonyLib;
using Helodrace;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI;

internal static class TacticalExplosiveEvasionTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        var idle = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        Job absent = null;
        Check(Patch_NewTactical_KeepExplosiveEvasion.Prefix(idle, ref absent) && absent == null,
            "The frequent no-explosive prefix must preserve native AI without a map, health query or squad lookup.");
        Patch_NewTactical_KeepExplosiveEvasion.Postfix(idle, ref absent);
        Check(absent == null, "No-explosive postfix must not create jobs or query map/squad state.");
        Job Job(string name)
        {
            var result = (Job)RuntimeHelpers.GetUninitializedObject(typeof(Job));
            result.def = new JobDef { defName = name }; return result;
        }
        Check(TacticalExplosiveEvasionPolicy.OwnsMovement(Job(TacticalExplosiveEvasionPolicy.JobName))
            && !TacticalExplosiveEvasionPolicy.OwnsMovement(Job("HD_NewTacticalPost"))
            && !TacticalExplosiveEvasionPolicy.OwnsMovement(Job("Goto"))
            && !TacticalExplosiveEvasionPolicy.OwnsMovement(null), "Only explosion evasion must keep tactical movement control.");
        var own = (Projectile)RuntimeHelpers.GetUninitializedObject(typeof(Projectile_Explosive));
        var enemy = (Projectile)RuntimeHelpers.GetUninitializedObject(typeof(Projectile_Explosive));
        var command = new TacticalSquadCommand { Phase = TacticalCommandPhase.Stack,
            OpeningAction = new TacticalOpeningAction { Projectile = own, Launched = true } };
        Check(TacticalExplosiveEvasionPolicy.Controlled(command, own)
            && !TacticalExplosiveEvasionPolicy.Controlled(command, enemy)
            && !TacticalExplosiveEvasionPolicy.Controlled(command, null),
            "A squad's controlled support grenade must not suppress reaction to a different enemy grenade.");
        var charge = (ThingWithComps)RuntimeHelpers.GetUninitializedObject(typeof(ThingWithComps));
        command.ChargeAction = new TacticalChargeAction { Charge = new CompInstalledBreachCharge { parent = charge } };
        Check(TacticalExplosiveEvasionPolicy.Controlled(command, charge)
            && !TacticalExplosiveEvasionPolicy.Controlled(command, enemy), "C4 withdrawal retains its controller without exempting hostile explosives.");

        var owner = (MapComponent_TacticalCommands)RuntimeHelpers.GetUninitializedObject(typeof(MapComponent_TacticalCommands));
        command.Plan = new TacticalLocalPlan(); TacticalLocalPlan plan = command.Plan;
        command.PhaseStarted = 40; command.EvasionStarted = 100;
        command.ContactResponse = new TacticalContactResponse { Started = 70 };
        command.FieldResponse = new TacticalFieldResponse { Started = 80, NextMove = 180 };
        command.FocusFire = new TacticalFocusFire { Started = 90 };
        MethodInfo pause = AccessTools.Method(typeof(MapComponent_TacticalCommands), "PauseForExplosiveEvasion");
        Check(!(bool)pause.Invoke(owner, new object[] { command, 200 }) && command.Plan == plan
            && command.Phase == TacticalCommandPhase.Stack && command.PhaseStarted == 140
            && command.ContactResponse.Started == 170 && command.FieldResponse.Started == 180
            && command.FieldResponse.NextMove == 280 && command.FocusFire.Started == 190
            && command.EvasionStarted == -1, "Resuming after danger preserves the selected opening/phase and adjusts paused clocks.");
        pause.Invoke(owner, new object[] { command, 201 });
        Check(command.PhaseStarted == 140, "An ended evasion must shift phase clocks exactly once.");

        int quietUntil = TacticalExplosiveEvasionPolicy.QuietUntil(100, false);
        Check(!TacticalExplosiveEvasionPolicy.CanReturn(189, quietUntil, true, false, false)
            && TacticalExplosiveEvasionPolicy.CanReturn(190, quietUntil, true, false, false),
            "Recovery must reassess before returning, then release an empty firing area without indefinite holding.");
        quietUntil = TacticalExplosiveEvasionPolicy.QuietUntil(130, true);
        Check(!TacticalExplosiveEvasionPolicy.CanReturn(219, quietUntil, true, false, false)
            && TacticalExplosiveEvasionPolicy.CanReturn(220, quietUntil, true, false, false),
            "An observed firing opportunity extends the quiet window before formation return.");
        Check(!TacticalExplosiveEvasionPolicy.CanReturn(400, quietUntil, true, true, false)
            && !TacticalExplosiveEvasionPolicy.CanReturn(400, quietUntil, false, false, false)
            && !TacticalExplosiveEvasionPolicy.CanReturn(400, quietUntil, true, false, true),
            "A target, denied observation budget or ongoing gun stance cannot be treated as a clear return route.");
        var route = new TacticalLocalPlan();
        route.Stack.Add(new IntVec3(10, 0, 20)); route.Stack.Add(new IntVec3(11, 0, 20));
        route.Positions.Add(new IntVec3(10, 0, 23)); route.Positions.Add(new IntVec3(11, 0, 23));
        Check(TacticalExplosiveEvasionPolicy.ReturnPost(route, TacticalCommandPhase.Stack, 1) == route.Stack[1]
            && TacticalExplosiveEvasionPolicy.ReturnPost(route, TacticalCommandPhase.Observe, 1) == route.Stack[1]
            && TacticalExplosiveEvasionPolicy.ReturnPost(route, TacticalCommandPhase.Clear, 1) == route.Positions[1],
            "One owned evasion/return job must preserve the member's existing stack or interior post.");
        Check(!TacticalExplosiveEvasionPolicy.ReturnPost(route, TacticalCommandPhase.Returning, 1).IsValid
            && !TacticalExplosiveEvasionPolicy.ReturnPost(route, TacticalCommandPhase.Stack, -1).IsValid
            && !TacticalExplosiveEvasionPolicy.ReturnPost(route, TacticalCommandPhase.Stack, 2).IsValid
            && !TacticalExplosiveEvasionPolicy.ReturnPost(null, TacticalCommandPhase.Stack, 0).IsValid,
            "Released control or missing seats must not invent stale return positions.");
        var remembered = new TacticalContact { EnemyId = 1, SeenTick = 100, Position = route.Stack[0] };
        Check(TacticalPostFirePolicy.RecheckKnown(remembered, 500, true)
            && !TacticalPostFirePolicy.RecheckKnown(remembered, 500, false),
            "Recovery may recheck an old observed tile after a long fuse without broadening normal entry memory freshness.");
        Check(!TacticalPostFirePolicy.RecheckKnown(remembered, 1900, true)
            && !TacticalPostFirePolicy.RecheckKnown(remembered, 99, true)
            && !TacticalPostFirePolicy.RecheckKnown(null, 100, true),
            "Expired, future or missing memory is not a usable recovery observation.");

        IntVec3 origin = new IntVec3(20, 0, 20);
        IntVec3 escape = TacticalExplosiveEvasionPolicy.LocalFallback(origin, origin - IntVec3.East * 4,
            c => c.z == origin.z && Math.Abs(c.x - origin.x) <= 7, _ => true, _ => false);
        Check(escape.IsValid && escape.DistanceToSquared(origin - IntVec3.East * 4) > 81
            && escape.DistanceToSquared(origin) <= 49, "A connected corridor retreat must find a nearby destination outside danger.");
        IntVec3 cover = origin + IntVec3.North;
        Check(TacticalExplosiveEvasionPolicy.LocalFallback(origin, origin - IntVec3.North,
            c => Math.Abs(c.x - origin.x) <= 1 && Math.Abs(c.z - origin.z) <= 1,
            _ => true, c => c == cover) == cover, "When a tiny room cannot supply a full flee distance, nearby connected hiding must remain usable.");
        escape = TacticalExplosiveEvasionPolicy.LocalFallback(origin, origin - IntVec3.North,
            c => c.z == origin.z && Math.Abs(c.x - origin.x) <= 7 && c != origin + IntVec3.East,
            _ => true, c => c == origin + IntVec3.East * 2);
        Check(escape.IsValid && escape.x < origin.x, "Local fallback must not pick a disconnected hiding point through a wall.");
        Check(!TacticalExplosiveEvasionPolicy.LocalFallback(origin, origin, _ => true, _ => false, _ => true).IsValid,
            "An occupied local floor must not invent an escape seat or overlap another pawn.");
        int floorProbes = 0, sightProbes = 0;
        escape = TacticalExplosiveEvasionPolicy.LocalFallback(origin, origin,
            _ => { floorProbes++; return true; }, _ => true, _ => { sightProbes++; return false; });
        Check(escape.IsValid && floorProbes <= 95 && sightProbes <= 8 && escape.DistanceToSquared(origin) <= 49,
            "Fallback floor and LOS work must remain bounded even when no fully safe destination exists.");

        foreach (Type patch in new[] { typeof(Patch_NewTactical_KeepExplosiveEvasion), typeof(Patch_NewTactical_NoticeHostileGrenade) })
        {
            HarmonyMethod info = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(patch));
            MethodInfo target = AccessTools.DeclaredMethod(info.declaringType, info.methodName, info.argumentTypes);
            Check(target != null, "Installed native explosive target missing: " + patch.Name);
            foreach (MethodInfo hook in patch.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "Prefix" || m.Name == "Postfix"))
                foreach (ParameterInfo parameter in hook.GetParameters())
                {
                    Type actual = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
                    Type expected = parameter.Name == "__result" ? target.ReturnType
                        : target.GetParameters().FirstOrDefault(p => p.Name == parameter.Name)?.ParameterType;
                    Check(expected != null && actual.IsAssignableFrom(expected), "Native evasion hook binding mismatch: " + patch.Name + ":" + parameter.Name);
                }
        }
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../.."));
        XElement xml = XDocument.Load(Path.Combine(root, "Defs/Organization/NewTacticalJobs.xml")).Root
            .Elements().Single(def => (string)def.Element("defName") == TacticalExplosiveEvasionPolicy.JobName);
        Check((string)xml.Element("driverClass") == typeof(JobDriver_TacticalEvadeExplosion).FullName
            && (string)xml.Element("casualInterruptible") == "false", "Evasion JobDef must bind its movement/hold driver and resist casual replacement.");
        Console.WriteLine("PASS: " + checks + " explosion evasion control, native hook bindings, controlled C4/support exclusions and same-plan resume checks.");
    }
}
