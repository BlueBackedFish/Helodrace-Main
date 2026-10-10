using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Tactics;
using RimWorld;
using Verse;

internal static class TacticalThreatTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        var binding = AccessTools.Field(typeof(DefOfHelper), "bindingNow");
        bool wasBinding = (bool)binding.GetValue(null);
        binding.SetValue(null, true);
        try { RuntimeHelpers.RunClassConstructor(typeof(DamageDefOf).TypeHandle); }
        finally { binding.SetValue(null, wasBinding); }
        var damage = new DamageDef { harmsHealth = true };
        var harmless = new DamageDef { harmsHealth = false };
        var emp = new DamageDef { harmsHealth = false };
        DamageDef previousEmp = DamageDefOf.EMP;
        DamageDefOf.EMP = emp;
        try
        {
            Verb Shoot(DamageDef hit, float range = 30)
            {
                var projectile = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
                projectile.projectile = new ProjectileProperties { damageDef = hit };
                var verb = (Verb_Shoot)RuntimeHelpers.GetUninitializedObject(typeof(Verb_Shoot));
                verb.verbTracker = new VerbTracker(new Pawn());
                verb.verbProps = new VerbProperties { verbClass = typeof(Verb_Shoot), defaultProjectile = projectile, range = range };
                return verb;
            }
            Check(!TacticalThreatEligibility.Ranged((Verb)null), "No verb is not a ranged threat.");
            Check(TacticalThreatEligibility.Ranged(Shoot(damage)), "Ordinary guns must be contacts.");
            Check(!TacticalThreatEligibility.Ranged(Shoot(harmless)), "A smoke-only projectile is not a combat contact.");
            Check(TacticalThreatEligibility.Ranged(Shoot(emp)), "EMP must remain a real ranged threat.");
            Check(!TacticalThreatEligibility.Ranged(Shoot(damage, 1.42f)), "Adjacent-only attacks are not ranged threats.");
            Verb statRange = Shoot(damage, 1.42f); statRange.verbProps.rangeStat = new StatDef();
            Check(TacticalThreatEligibility.Ranged(statRange), "Stat-based range must not be mistaken for a melee attack.");
            var melee = (Verb_MeleeAttackDamage)RuntimeHelpers.GetUninitializedObject(typeof(Verb_MeleeAttackDamage));
            melee.verbProps = new VerbProperties { verbClass = typeof(Verb_MeleeAttackDamage), range = 80, meleeDamageDef = damage };
            Check(!TacticalThreatEligibility.Ranged(melee), "Long-range metadata/DPS cannot turn melee into a ranged threat.");
            var beam = (Verb_ShootBeam)RuntimeHelpers.GetUninitializedObject(typeof(Verb_ShootBeam));
            beam.verbProps = new VerbProperties { verbClass = typeof(Verb_ShootBeam), range = 40 };
            Check(TacticalThreatEligibility.Ranged(beam), "Innate beam attacks need no ordinary projectile.");
            // No caster, target, world, stance or Available() is needed: distance
            // and temporary reload/cooldown cannot change the capability test.
            var memory = new TacticalContactMemory();
            var cell = new IntVec3(10, 0, 20);
            memory.Remember(1, cell, cell, false, false, 100);
            Check(memory.Entries.Count == 0, "Non-ranged sightings must never enter memory.");
            memory.Remember(1, cell, cell, true, true, 110, "sender");
            var report = memory.Entries.Single().Copy();
            Check(report.RangedThreat, "Sender eligibility must survive a detached report.");
            var receiver = new TacticalContactMemory();
            Check(receiver.Receive(report, 120), "Eligible snapshot must be received without querying a hidden pawn.");
            report.RangedThreat = false;
            Check(!new TacticalContactMemory().Receive(report, 120), "Ineligible reports must not enter memory.");
            memory.Remember(1, cell, cell, false, false, 130);
            Check(memory.Entries.Count == 0, "A new ineligible sighting must remove the old contact.");
            Check(receiver.Entries.Single().RangedThreat, "Report mutation must not alter recipient knowledge.");
            Check(AccessTools.Method(typeof(Pawn_MeleeVerbs), "TryMeleeAttack", new[] { typeof(Thing), typeof(Verb), typeof(bool) }) != null,
                "Contact guards must use the installed native melee attack API for actual adjacent attackers.");
            CheckScribe(receiver.Entries.Single(), Check);
        }
        finally { DamageDefOf.EMP = previousEmp; }
        Console.WriteLine($"PASS: {checks} A1 ranged capability, memory/report eligibility and native Scribe payload checks.");
    }

    private static void CheckScribe(TacticalContact contact, Action<bool, string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-A1-" + Guid.NewGuid() + ".xml");
        try
        {
            Scribe.saver.InitSaving(path, "contact"); contact.ExposeData(); Scribe.saver.FinalizeSaving();
            var restored = new TacticalContact();
            // Value payload has no cross-references. Full game finalization
            // calls Unity's profiler and cannot run in this .NET harness.
            Scribe.loader.InitLoading(path); restored.ExposeData(); Scribe.loader.ForceStop();
            check(restored.RangedThreat && restored.EnemyId == contact.EnemyId && restored.Position == contact.Position
                && restored.Door == contact.Door && restored.Origin == contact.Origin && restored.SeenTick == contact.SeenTick,
                "Saved contact must retain eligibility, door interpretation, original time and source.");
            var response = new TacticalContactResponse { DirectAttack = true, First = contact.Position,
                FirstId = contact.EnemyId, LastSeen = 200 };
            Scribe.saver.InitSaving(path, "response"); response.ExposeData(); Scribe.saver.FinalizeSaving();
            var loadedResponse = new TacticalContactResponse();
            Scribe.loader.InitLoading(path); loadedResponse.ExposeData(); Scribe.loader.ForceStop();
            check(loadedResponse.DirectAttack && loadedResponse.FirstId == response.FirstId && loadedResponse.First == response.First
                && loadedResponse.LastSeen == 200, "Local close-attack response must survive saving without becoming a contact report.");
        }
        finally { Scribe.ForceStop(); if (File.Exists(path)) File.Delete(path); }
    }
}
