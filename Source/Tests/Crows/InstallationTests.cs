using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Helodrace;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

internal static partial class Program
{
    private static List<CrowsWeaponDef> WeaponProfiles;
    private static string RepositoryRoot
    {
        get
        {
            var root=new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName,"Defs/ModernWar/Buildings/CrowsWeapons.xml"))) root=root.Parent;
            return root.FullName;
        }
    }
    private static void LoadWeaponProfiles()
    {
        var xml=XDocument.Load(Path.Combine(RepositoryRoot,"Defs/ModernWar/Buildings/CrowsWeapons.xml"));
        var materials=new Dictionary<string,ThingDef>();
        WeaponProfiles=xml.Root.Elements("Helodrace.CrowsWeaponDef").Select(x=>new CrowsWeaponDef
        {
            defName=(string)x.Element("defName"),label=(string)x.Element("label"),
            gunDef=new ThingDef {defName=(string)x.Element("gunDef")},
            workToInstall=(float)x.Element("workToInstall"),fuelCapacity=(int)x.Element("fuelCapacity"),
            barrelRecoil=(bool?)x.Element("barrelRecoil")??false,
            frameTexPath=(string)x.Element("frameTexPath"),barrelTexPath=(string)x.Element("barrelTexPath"),
            bodyTexPath=(string)x.Element("bodyTexPath"),feedTexPath=(string)x.Element("feedTexPath"),iconTexPath=(string)x.Element("iconTexPath"),
            costList=x.Element("costList").Elements().Select(c=>
            {
                if(!materials.TryGetValue(c.Name.LocalName,out ThingDef material))materials[c.Name.LocalName]=material=new ThingDef{defName=c.Name.LocalName};
                return new ThingDefCountClass{thingDef=material,count=(int)c};
            }).ToList()
        }).ToList();
        Check(WeaponProfiles.Count==4,"Four selectable weapons were not defined.");
        DefDatabase<CrowsWeaponDef>.AllDefsListForReading=WeaponProfiles;
    }
    private static void Supply(Building_Crows turret)
    {
        foreach(var cost in turret.RequestedWeapon.costList)
        {
            var source=new ThingOwner();var thing=new Thing{def=cost.thingDef,stackCount=cost.count};source.Add(thing);
            Check(turret.AcceptMountingMaterial(source,thing,cost.count)==cost.count,"Material transfer failed.");
            Check(source.Count==0,"Transferred materials were duplicated at the source.");
        }
    }
    private static void ArmForTest(Building_Crows turret,CrowsWeaponDef weapon)
    {
        Check(turret.RequestWeapon(weapon),"Cannot select weapon on empty station.");Supply(turret);
        Check(turret.AddMountingWork(weapon.workToInstall),"Installation did not complete.");
    }
    private static void VerifyInstallation()
    {
        var map=new Map();var empty=Turret(map,false);var console=Console(map);console.Connect(empty);
        Check(!empty.HasWeapon&&!empty.CanSetForcedTargetForTest&&!empty.AttackVerb.Available(),"Fresh station is armed or targetable.");
        Check(!console.CanUseNow,"Empty station can be operated.");
        var graphic=new CompCrowsGraphic {parent=empty};empty.GraphicComp=graphic;Graphics.Draws.Clear();graphic.PostDraw();
        Check(Graphics.Draws.Count==2&&Graphics.Draws[1].material.Path.Contains("Unarmed"),"Empty station draws a weapon or chute.");
        Check(!empty.AddMountingWork(999999),"Unselected station accepted work.");
        foreach(var profile in WeaponProfiles)
        {
            Check(!profile.ConfigErrors().Any(),"Weapon configuration error: "+profile.defName);
            var turret=Turret(map,false);var original=turret.gun;
            Check(turret.RequestWeapon(profile)&&!turret.HasWeapon,"Selecting weapon mounted it instantly.");
            Check(!turret.AddMountingWork(profile.workToInstall),"Weapon mounted without its materials.");
            Supply(turret);
            Check(!turret.AddMountingWork(profile.workToInstall/2)&&!turret.HasWeapon,"Weapon mounted before work finished.");
            Near(turret.InstallationProgress,.5f,"Installation progress lost.");
            Check(turret.AddMountingWork(profile.workToInstall/2),"Full material/work did not install weapon.");
            Check(turret.InstalledWeapon==profile&&turret.gun.def==profile.gunDef&&original.Destroyed,"Wrong gun installed or old internal gun retained.");
            Check(turret.Ammo.Fuel==0,"Installation granted free ammunition.");
            Check(turret.GunCompEq.AllVerbs.All(v=>v.caster==turret&&v.castCompleteCallback!=null),"New gun lost vanilla caster/cooldown callback.");
            Check(!turret.InstallationPending&&turret.MountingMaterials.Count==0,"Completion retained a bill or construction materials.");
            Check(!turret.AddMountingWork(profile.workToInstall)&&!turret.RequestWeapon(WeaponProfiles[0]),"Completed station can manufacture another free gun.");
            Check(!turret.AttackVerb.Available(),"Installed but unoperated weapon can shoot.");
            var comp=new CompCrowsGraphic {parent=turret};Graphics.Draws.Clear();comp.PostDraw();var rest=Graphics.Draws.ToArray();Graphics.Draws.Clear();
            Check(rest.Length==5&&rest[1].material.Path==profile.frameTexPath&&rest[4].material.Path==profile.feedTexPath,"Wrong weapon art mounted.");
            comp.NotifyShotFired();comp.PostDraw();
            Near(Graphics.Draws[2].matrix.pos.z-rest[2].matrix.pos.z,profile.barrelRecoil?-.0375f:0f,"Fixed barrel incorrectly recoils.");
            foreach(var path in new[]{profile.frameTexPath,profile.barrelTexPath,profile.bodyTexPath,profile.feedTexPath,profile.iconTexPath})
                Check(File.Exists(Path.Combine(RepositoryRoot,"Textures",path+".png")),"Missing selectable weapon art "+path);
        }
        var pending=Turret(map,false);pending.RequestWeapon(WeaponProfiles[1]);Supply(pending);pending.AddMountingWork(100);
        int delivered=pending.MountingMaterials.Sum(t=>t.stackCount);
        Scribe.mode=LoadSaveMode.Saving;pending.ExposeData();
        var restored=Turret(map,false);Scribe.mode=LoadSaveMode.LoadingVars;restored.ExposeData();Scribe.mode=LoadSaveMode.Inactive;
        Check(restored.RequestedWeapon==pending.RequestedWeapon&&restored.MountingMaterials.Sum(t=>t.stackCount)==delivered,"Pending weapon/material state did not restore.");
        Near(restored.InstallationProgress,pending.InstallationProgress,"Work progress did not restore.");
        map.CanDropMaterials=false;Check(!pending.CancelWeaponInstallation()&&pending.InstallationPending&&pending.MountingMaterials.Sum(t=>t.stackCount)==delivered,"Failed placement erased delivered materials.");
        map.CanDropMaterials=true;int before=map.Materials.Sum(t=>t.stackCount);
        Check(pending.CancelWeaponInstallation()&&!pending.InstallationPending&&pending.MountingMaterials.Count==0,"Cancellation did not clear the pending operation.");
        Check(map.Materials.Sum(t=>t.stackCount)==before+delivered,"Cancellation lost materials.");
        var changed=Turret(new Map(),false);changed.RequestWeapon(WeaponProfiles[0]);Supply(changed);changed.AddMountingWork(100);
        Check(changed.RequestWeapon(WeaponProfiles[2])&&changed.InstallationProgress==0&&changed.MountingMaterials.Count==0&&changed.Map.Materials.Count>0,"Reselection retained old work/materials.");
        VerifyAmmunitionIsolation();VerifyInstallationJobs();
        System.Console.WriteLine("PASS: empty state; four material/work-gated installations; cancellation/refunds; pending persistence; weapon art/verbs; per-instance ammo; construction jobs.");
    }
    private static void VerifyAmmunitionIsolation()
    {
        var shared=new CompProperties_CrowsAmmo();shared.fuelFilter.SetAllow(ThingDefOf.Steel,true);
        var a=Turret(new Map(),false);var b=Turret(new Map(),false);
        a.Ammo=new CompCrowsAmmo{parent=a};b.Ammo=new CompCrowsAmmo{parent=b};a.Ammo.Initialize(shared);b.Ammo.Initialize(shared);
        a.Ammo.ConfigureWeapon();b.Ammo.ConfigureWeapon();
        Check(!a.Ammo.Props.fuelFilter.Allows(ThingDefOf.Steel)&&!a.Ammo.CompGetGizmosExtra().Any(),"Empty station can refuel or shows ammo gizmos.");
        ArmForTest(a,WeaponProfiles[1]);ArmForTest(b,WeaponProfiles[2]);
        Check(a.Ammo.Props.fuelCapacity==48&&b.Ammo.Props.fuelCapacity==200&&shared.fuelCapacity==100,"Mounted weapon changed another station's capacity.");
        Check(a.Ammo.Props.fuelFilter.Allows(ThingDefOf.Steel)&&a.Ammo.CompGetGizmosExtra().Any()&&shared.fuelFilter.Allows(ThingDefOf.Steel),"Installed weapon cannot refuel or shared definition was mutated.");
    }
    private static void VerifyInstallationJobs()
    {
        var map=new Map();var turret=Turret(map,false);turret.RequestWeapon(WeaponProfiles[0]);var pawn=new Pawn {Map=map,Faction=Faction.OfPlayer};
        var giver=new WorkGiver_InstallCrowsWeapon();Check(giver.JobOnThing(pawn,turret)==null,"Hauling job fabricated missing stock.");
        var cost=WeaponProfiles[0].costList[0];map.Materials.Add(new Thing {def=cost.thingDef,stackCount=cost.count+3});
        Job haul=giver.JobOnThing(pawn,turret);Check(haul?.def==CrowsDefOf.HD_HaulCrowsMountMaterial&&haul.count==cost.count,"Wrong construction hauling count/job.");
        pawn.skills.Construction.Level=5;Check(giver.JobOnThing(pawn,turret)==null,"Unskilled worker accepted weapon installation.");pawn.skills.Construction.Level=8;
        var extra=new Thing{def=cost.thingDef,stackCount=cost.count+3};pawn.carryTracker.innerContainer.Add(extra);
        int groundCount=map.Materials.Sum(t=>t.stackCount);
        var hauler=new JobDriver_HaulCrowsMountMaterial{pawn=pawn,job=haul};hauler.ToilsForTest().Last().initAction();
        Check(pawn.carryTracker.CarriedThing==null&&turret.MissingMaterial(cost.thingDef)==0,"Delivery did not leave excess materials on the map.");
        Check(map.Materials.Sum(t=>t.stackCount)==groundCount+3,"Delivery erased its excess material stack.");
        Check(turret.CancelWeaponInstallation()&&turret.RequestWeapon(WeaponProfiles[0]),"Cannot reset delivery fixture.");
        Supply(turret);Job install=giver.JobOnThing(pawn,turret);Check(install?.def==CrowsDefOf.HD_InstallCrowsWeapon,"Supplied station did not schedule installation.");
        pawn.jobs.CurJob=install;var driver=new JobDriver_InstallCrowsWeapon{pawn=pawn,job=install};
        Check(driver.TryMakePreToilReservations(false),"Installer failed reservation.");var toil=driver.ToilsForTest().Last();toil.initAction();toil.tickIntervalAction((int)WeaponProfiles[0].workToInstall);
        Check(turret.HasWeapon&&pawn.jobs.CurJob==null,"Construction toil did not complete the mounted weapon.");
    }
}
