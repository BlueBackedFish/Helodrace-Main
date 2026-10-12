using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using HarmonyLib;
using Helodrace.Squads;
using Helodrace.Tactics;
using Verse;
using Verse.AI;
using RimWorld;
using Helodrace;

internal static class TacticalMedicalEquipmentTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
        CheckMedicalLayers(Check);
        Check(TacticalMedicalPolicy.WithinTreatmentRange(IntVec3.Zero,new IntVec3(20,0,0))
            && !TacticalMedicalPolicy.WithinTreatmentRange(IntVec3.Zero,new IntVec3(21,0,0)),
            "A settled medic may reach a room patient beyond eight cells, with a fixed 20-cell bound.");
        Check(TacticalMedicalPolicy.SafeSite(false,true,true,true,true)
            && !TacticalMedicalPolicy.SafeSite(false,true,true,true,false)
            && !TacticalMedicalPolicy.SafeSite(false,true,false,true,true)
            && !TacticalMedicalPolicy.SafeSite(false,false,true,true,true)
            && TacticalMedicalPolicy.SafeSite(true,false,false,false,false),
            "Actual completed exterior guards belong to a cleared entry's care sites, without treating unfinished stacks as safe.");
        var service=new MapComponent_TacticalCommands(null);
        var careCommand=new TacticalSquadCommand {Phase=TacticalCommandPhase.Clear,MedicalWindowUntil=5000,MedicalCursor=1};
        var outside=new TacticalMemberCommand {Pawn=new Pawn {Position=new IntVec3(90,0,90)}};
        careCommand.Members.Add(outside);
        AccessTools.Method(service.GetType(),"AdvanceMedicalCare").Invoke(service,new object[]{careCommand,new List<TacticalMemberCommand>{outside},2000});
        Check(careCommand.NextMedicalCheck==2600&&careCommand.MedicalCursor==0,
            "An exterior first roster member must not block completion of the whole medical candidate scan.");
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

    private static void CheckMedicalLayers(Action<bool, string> check)
    {
        XElement greatWar = XDocument.Load("Defs/GreatWar/Items/Items_GreatWar.xml").Root;
        XElement modern = XDocument.Load("Defs/ModernWar/Items/Apparel_ModernWar.xml").Root;
        XElement bagXml = greatWar.Elements("ThingDef").Single(d => (string)d.Element("defName") == OrganizationMedical.BagDefName);
        var groups = new Dictionary<string, BodyPartGroupDef>();
        var layers = new Dictionary<string, ApparelLayerDef>();
        BodyPartGroupDef Group(string name)
        {
            if (!groups.TryGetValue(name, out var value)) groups[name] = value = new BodyPartGroupDef { defName = name };
            return value;
        }
        ApparelLayerDef Layer(string name)
        {
            if (!layers.TryGetValue(name, out var value)) layers[name] = value = new ApparelLayerDef { defName = name };
            return value;
        }
        ThingDef ApparelDef(XElement xml)
        {
            var def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            def.defName = (string)xml.Element("defName");
            def.apparel = new ApparelProperties {
                bodyPartGroups = xml.Element("apparel").Element("bodyPartGroups").Elements("li").Select(e => Group(e.Value)).ToList(),
                layers = xml.Element("apparel").Element("layers").Elements("li").Select(e => Layer(e.Value)).ToList() };
            return def;
        }
        var bag = ApparelDef(bagXml);
        var rig = ApparelDef(greatWar.Elements("ThingDef").Single(d => (string)d.Element("defName") == "HD_Apparel_GreatWarStormFrontChestRig"));
        var body = new BodyDef { defName = "A3MedicLayerRegression" };
        foreach (XElement xml in XDocument.Load("Defs/Helod/Race/Body.xml").Descendants("groups"))
            body.AllParts.Add(new BodyPartRecord { groups = xml.Elements("li").Select(e => Group(e.Value)).ToList() });
        DefDatabase<BodyDef>.Add(body);
        var relevant = AccessTools.Field(typeof(ApparelProperties), "apparelRelevantGroups");
        object previous = relevant.GetValue(null);
        relevant.SetValue(null, groups.Values.ToArray());
        try
        {
            var legacy = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
            legacy.apparel = new ApparelProperties { bodyPartGroups = bag.apparel.bodyPartGroups,
                layers = new List<ApparelLayerDef> { Layer("Belt") } };
            check(!ApparelUtility.CanWearTogether(legacy, rig, body), "Reproduce the original native Belt/Shoulders conflict with the mandatory LOW chest rig.");
            check(ApparelUtility.CanWearTogether(bag, rig, body), "Issued medic bag must coexist with the mandatory LOW chest rig instead of aborting raid generation.");
            foreach (string name in new[] { "HD_Apparel_GreatWarStormFrontInner", "HD_Apparel_GreatWarStormFrontOuter", "HD_Apparel_GreatWarM1Helmet" })
                check(ApparelUtility.CanWearTogether(bag, ApparelDef(greatWar.Elements("ThingDef").Single(d => (string)d.Element("defName") == name)), body), "LOW required uniform/helmet must remain wearable with bag: " + name);
            foreach (string name in new[] { "HD_Apparel_UCPBlouse", "HD_Apparel_UCPPants", "HD_Apparel_IBTVAssault", "HD_Apparel_FASTMT" })
                check(ApparelUtility.CanWearTogether(bag, ApparelDef(modern.Elements("ThingDef").Single(d => (string)d.Element("defName") == name)), body), "HIGH required uniform/armor must remain wearable with bag: " + name);
            check((string)bagXml.Element("apparel").Element("layers").Attribute("Inherit") == "False", "Bag layer must replace inherited Belt, not merge it back into the resolved definition.");
            XElement layerDefs = XDocument.Load("Defs/GreatWar/ApparelLayers_GreatWar.xml").Root;
            check(bag.apparel.layers.All(l => layerDefs.Elements("ApparelLayerDef").Any(d => (string)d.Element("defName") == l.defName)), "Bag's dedicated layer must actually be defined.");
            bool utility = false;
            Patch_MedicalBagUtilityLayer.Postfix(bag.apparel.layers[0], ref utility);
            check(utility, "Medical layer must retain native pack texture paths instead of inventing body-type suffix textures.");
            utility = false; Patch_MedicalBagUtilityLayer.Postfix(Layer("Middle"), ref utility);
            check(!utility, "Ordinary armor layers must not become utility packs.");
            utility = true; Patch_MedicalBagUtilityLayer.Postfix(Layer("Belt"), ref utility);
            check(utility, "Vanilla Belt classification must remain intact.");
            check(AccessTools.PropertyGetter(typeof(ApparelLayerDef), nameof(ApparelLayerDef.IsUtilityLayer))?.ReturnType == typeof(bool), "Pack rendering patch must bind the installed native getter.");
        }
        finally { relevant.SetValue(null, previous); AccessTools.Method(typeof(DefDatabase<BodyDef>), "Remove").Invoke(null, new object[] { body }); }
    }
}
