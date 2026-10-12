using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Helodrace.Squads;
using Helodrace.Tactics;
using Verse;
using Verse.AI;

internal static class TacticalMedicalEquipmentTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
        var rifle = new RoleDef { combatFunction = "Rifle" };
        var automatic = new RoleDef { combatFunction = "HAR" };
        var command = new RoleDef { isCommandRole = true };
        var squad = new CombatGroup();
        squad.roleAssignments.Add(new RoleAssignment { combatRole = rifle, commandRole = command });
        for (int team = 0; team < 3; team++)
        {
            var child = new CombatGroup(); squad.children.Add(child);
            child.roleAssignments.Add(new RoleAssignment { combatRole = rifle, commandRole = command });
            child.roleAssignments.Add(new RoleAssignment { combatRole = automatic });
            child.roleAssignments.Add(new RoleAssignment { combatRole = rifle });
            child.roleAssignments.Add(new RoleAssignment { combatRole = rifle });
        }
        RoleAssignment medic = OrganizationMedical.Select(squad, a => a != squad.children[0].roleAssignments[2]);
        Check(medic == squad.children[0].roleAssignments[3], "Medic must be an eligible existing rifle billet, leaving leaders and automatic arms intact.");
        Check(squad.AllAssignments.Count() == 13 && squad.children.All(c => c.roleAssignments.Count == 4), "Medical equipment must not add a pawn or change child-team size.");
        var patrol = new CombatGroup { roleAssignments = squad.children[0].roleAssignments };
        Check(OrganizationMedical.Select(patrol, a => true).commandRole == null, "Small LOW squads also need a non-command medic slot.");
        Check(OrganizationMedical.Select(patrol, a => false) == null, "Incompatible apparel must not silently strip required uniform/armor.");
        Check(TacticalMedicalEquipmentPolicy.Dropped(true,true,false,true)
            && !TacticalMedicalEquipmentPolicy.Dropped(true,false,false,true)
            && !TacticalMedicalEquipmentPolicy.Dropped(false,true,false,true)
            && !TacticalMedicalEquipmentPolicy.Dropped(true,true,true,true)
            && !TacticalMedicalEquipmentPolicy.Dropped(true,true,false,false), "Only a known actual dropped bag on this map is recoverable, not worn corpse/downed gear or unknown stock.");
        var bag = new Thing { thingIDNumber = 123 };
        var def = (JobDef)RuntimeHelpers.GetUninitializedObject(typeof(JobDef)); def.defName = TacticalMedicalEquipmentPolicy.RecoveryJob;
        var owned = new Job { def = def, loadID = 222, targetA = bag };
        var recovery = new TacticalBagRecovery { Bag = bag, SavedJobId = owned.loadID };
        recovery.RestoreJob(owned); Check(recovery.Job == owned, "Restore must bind the native owned job without copying it.");
        recovery.RestoreJob(new Job { def = def, loadID = 223, targetA = bag }); Check(recovery.Job == null, "Foreign job ID must not acquire a bag lease.");
        recovery.RestoreJob(new Job { def = def, loadID = 222, targetA = new Thing() }); Check(recovery.Job == null, "Correct job ID aimed at another bag must be rejected.");
        recovery.RestoreJob(null); Check(recovery.Job == null, "A finished pickup may not be restarted on load.");
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-medical-duty-" + Guid.NewGuid() + ".xml");
        try
        {
            var assignment = new RoleAssignment { medicalDuty = true, rankPriority = 8 };
            Scribe.saver.InitSaving(path,"role"); assignment.ExposeData(); Scribe.saver.FinalizeSaving();
            var restored = new RoleAssignment(); Scribe.loader.InitLoading(path); restored.ExposeData(); Scribe.loader.ForceStop();
            Check(restored.medicalDuty && restored.rankPriority == 8, "Native Scribe must preserve medical duty alongside the original billet.");
        }
        finally { Scribe.ForceStop(); if (File.Exists(path)) File.Delete(path); }
        XElement jobs = XDocument.Load(Path.Combine("Defs","Organization","NewTacticalJobs.xml")).Root;
        XElement pickup = jobs.Elements("JobDef").Single(d => (string)d.Element("defName") == TacticalMedicalEquipmentPolicy.RecoveryJob);
        Check((string)pickup.Element("driverClass") == typeof(JobDriver_TacticalRecoverMedibag).FullName,
            "Recovery definition must bind the actual budgeted pickup driver.");
        Console.WriteLine("PASS: " + checks + " A3 medic billet, drop eligibility, native job ownership/save and recovery binding checks.");
    }
}
