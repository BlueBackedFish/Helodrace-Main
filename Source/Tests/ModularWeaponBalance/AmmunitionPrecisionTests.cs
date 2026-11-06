using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.ModernWar;
using Verse;

internal static partial class Program
{
    private static Dictionary<string,ModularWeaponConvertedStats> AmmoPrecisionBuilds(string weapon, string preset, string[] names)
    {
        var result=new Dictionary<string,ModularWeaponConvertedStats>();
        foreach(string name in names)
        {
            var nodes=BuildAssembly(weapon,definitions[preset]);
            var ammo=nodes.Single(n=>!string.IsNullOrEmpty(n.Props.ammunitionType));
            ammo.comp.props=Properties(Resolve(definitions[name]));
            var value=Convert(nodes);result[name]=value;
            foreach(var band in new[]{value.Touch,value.Short,value.Medium,value.Long})
                Check(band.Accuracy<=band.MechanicalCeiling && band.MechanicalCeiling<=.97f,"Ammo remains inside the normal-quality mechanical cap: "+name);
            Console.WriteLine(name+" precision="+value.AmmunitionPrecisionFactor.ToString("P0")
                +" accuracy="+string.Join("/",new[]{value.Touch,value.Short,value.Medium,value.Long}.Select(b=>b.Accuracy.ToString("P1"))));
        }
        return result;
    }

    private static void AmmunitionPrecisionTests()
    {
        int count=0;
        foreach(var def in definitions.Values.Where(d=>d.Name.LocalName=="ThingDef"))
        {
            var xml=Resolve(def);
            var comp=xml.Element("comps")?.Elements().FirstOrDefault(e=>(string)e.Attribute("Class")==CompClass);
            if(string.IsNullOrEmpty((string)comp?.Element("ammunitionType"))) continue;
            var raw=Properties(xml).internalStats;count++;
            Check(raw.ammunitionPrecisionFactor>=.5f && raw.ammunitionPrecisionFactor<=1.35f,"Every cartridge has an explicit bounded consistency factor");
            Near(0,raw.mechanicalPrecision,"Ammo does not also add generic precision points");
            Near(0,raw.aimStability,"Ammo cannot add hold stability");
            Near(0,raw.aimSpeed,"Ammo cannot add acquisition speed");
        }
        Check(count==19,"All shipped cartridge selectors and aliases were balanced");
        string[] rifle556={"HD_Ammunition_556M193_Ammo","HD_Ammunition_556M855_Ammo","HD_Ammunition_556M995_Ammo",
            "HD_Ammunition_556MK262_Ammo","HD_Ammunition_556MK318_Ammo","HD_Ammunition_556M855A1_Ammo"};
        var builds=AmmoPrecisionBuilds("HD_Gun_M4A1_Weapon","HD_WeaponPreset_ModularM4_Default",rifle556);
        var service=builds[rifle556[5]];var match=builds[rifle556[3]];var ap=builds[rifle556[2]];
        Near(1,service.AmmunitionPrecisionFactor,"M855A1 preserves the stock accuracy reference");
        Near(service.Long.Accuracy*1.18f,match.Long.Accuracy,"5.56 MATCH has a meaningful long-range improvement");
        Near(service.Long.Accuracy*.90f,ap.Long.Accuracy,"5.56 AP pays a precision cost for its existing penetration role");
        Check(match.Long.Accuracy-ap.Long.Accuracy>.13f,"5.56 MATCH/AP differ by over thirteen percentage points on the same M4");
        Check(match.Medium.Accuracy/ap.Medium.Accuracy>1.20f,"Medium-range ammo choice is meaningful too");
        var ordered=new[]{rifle556[2],rifle556[1],rifle556[0],rifle556[5],rifle556[4],rifle556[3]};
        for(int i=1;i<ordered.Length;i++)
            Check(builds[ordered[i]].Long.Accuracy>builds[ordered[i-1]].Long.Accuracy,"Every 5.56 variant has a distinct precision role");
        foreach(var value in builds.Values)
        {
            Near(service.AimStability,value.AimStability,"5.56 ammo does not change hold");
            Near(service.AimSpeed,value.AimSpeed,"5.56 ammo does not change acquisition");
            Near(service.VanillaCycleFactor,value.VanillaCycleFactor,"Accuracy balancing cannot change RPM");
            Near(service.VanillaWarmupFactor,value.VanillaWarmupFactor,"Equal-mass 5.56 consistency changes cannot change warmup");
            Near(service.Long.SightEfficiency,value.Long.SightEfficiency,"Ammo does not change optics");
        }
        Check(match.CEShotSpread<service.CEShotSpread && ap.CEShotSpread>service.CEShotSpread,"CE angular spread follows ammo precision, not sway");
        Near(match.CESwayFactor,service.CESwayFactor,"Match ammo does not stabilize the shooter in CE");
        var belt=Properties(Resolve(definitions["HD_ModularPart_Ammunition_M249_M855A1_Part"]));
        Near(service.AmmunitionPrecisionFactor,belt.internalStats.ammunitionPrecisionFactor,"Linked M249 EPR and loose STANAG EPR share the same factor");

        string[] rifle762={"HD_ModularPart_Ammunition_762M80_Part","HD_Ammunition_76251M80A1_Ammo","HD_Ammunition_76251M198_Ammo",
            "HD_Ammunition_76251M948_Ammo","HD_Ammunition_76251M993_Ammo","HD_Ammunition_76251M1158_Ammo",
            "HD_Ammunition_76251MK316_Ammo","HD_Ammunition_76251MK319_Ammo"};
        var nato=AmmoPrecisionBuilds("HD_Gun_M14_Weapon","HD_WeaponPreset_ModularM14_Default",rifle762);
        Check(nato.Values.Select(v=>v.AmmunitionPrecisionFactor).Distinct().Count()==8,"Every 7.62 variant has a distinct consistency profile");
        Check(nato[rifle762[6]].Long.Accuracy>nato[rifle762[0]].Long.Accuracy*1.10f,"7.62 MATCH keeps a substantial advantage even when the cap is reached");
        Check(nato[rifle762[6]].Long.Accuracy-nato[rifle762[2]].Long.Accuracy>.25f,"Duplex has a real per-projectile accuracy cost, independent of secondary spread");
        Check(nato[rifle762[7]].Long.Accuracy>nato[rifle762[0]].Long.Accuracy
            && nato[rifle762[4]].Long.Accuracy<nato[rifle762[0]].Long.Accuracy,"OTM and AP have distinct precision roles");
        Check(nato[rifle762[3]].Long.Accuracy<nato[rifle762[5]].Long.Accuracy
            && nato[rifle762[5]].Long.Accuracy<nato[rifle762[4]].Long.Accuracy,"SLAP/ADVAP/AP have differentiated precision costs");
        AmmoPrecisionContractTests();
        Console.WriteLine("PASS: all 19 ammunition factors, 6 distinct 5.56 roles, 8 NATO profiles, no stacking, distance scaling and unchanged aim/terminal channels.");
    }

    private static void AmmoPrecisionContractTests()
    {
        var body=Node(new CompProperties_ModularWeaponNode {isAssemblyRoot=true,internalStats=new ModularWeaponInternalStats
            {massKg=1,mechanicalPrecision=60,aimSpeed=60,aimStability=65,naturalSupport=.65f,gasFlow=1,operatingReliability=.97f}});
        var cartridge=Node(new CompProperties_ModularWeaponNode {ammunitionType="fixture",projectileOverride=Bare<ThingDef>(),overridePriority=50,
            internalStats=new ModularWeaponInternalStats {massKg=.012f,ammunitionPrecisionFactor=1}});
        var nodes=new List<ModularRenderNode>{body,cartridge};
        var baseline=Convert(nodes);cartridge.Props.internalStats.ammunitionPrecisionFactor=1.18f;var match=Convert(nodes);
        Near(1.027f,match.Touch.Accuracy/baseline.Touch.Accuracy,"Touch receives only 15% of the ammunition difference");
        Near(1.072f,match.Short.Accuracy/baseline.Short.Accuracy,"Short receives 40% of the ammunition difference");
        Near(1.135f,match.Medium.Accuracy/baseline.Medium.Accuracy,"Medium receives 75% of the ammunition difference");
        Near(1.18f,match.Long.Accuracy/baseline.Long.Accuracy,"Long receives the full ammunition difference");
        Near(baseline.VanillaCooldownFactor,match.VanillaCooldownFactor,"Precision alone cannot improve recoil recovery");
        Near(baseline.DamageTransferFactor,match.DamageTransferFactor,"Precision alone cannot change damage transfer");
        Near(baseline.PenetrationEfficiencyFactor,match.PenetrationEfficiencyFactor,"Precision alone cannot change AP");
        Near(baseline.MuzzleVelocityOffsetMetersPerSecond,match.MuzzleVelocityOffsetMetersPerSecond,"Precision cannot change fragmentation velocity");
        Near(baseline.RangeFactor,match.RangeFactor,"Precision is not a second range bonus");
        var bullet=Projectile("HD_Bullet_556M855A1_Proj");
        var baseBallistics=Ballistics(bullet,baseline);var matchBallistics=Ballistics(bullet,match);
        foreach(float distance in new[]{0f,12f,25f,40f})
        {
            Near(baseBallistics.DamageAt(distance),matchBallistics.DamageAt(distance),"Actual ballistic damage ignores cartridge precision");
            Near(baseBallistics.PenetrationAt(distance),matchBallistics.PenetrationAt(distance),"Actual ballistic AP ignores cartridge precision");
            Near(baseBallistics.FragmentationChanceAt(distance),matchBallistics.FragmentationChanceAt(distance),"Actual fragmentation ignores cartridge precision");
        }
        cartridge.Props.internalStats.mechanicalPrecision=100;cartridge.Props.internalStats.aimStability=100;cartridge.Props.internalStats.aimSpeed=100;
        var wrongFields=Convert(nodes);
        Near(match.Long.Accuracy,wrongFields.Long.Accuracy,"Generic ammo precision/stability cannot create a duplicate hit channel");
        Near(match.VanillaWarmupFactor,wrongFields.VanillaWarmupFactor,"Generic ammo acquisition cannot bypass role isolation");
        var spare=Node(new CompProperties_ModularWeaponNode {ammunitionType="other",projectileOverride=Bare<ThingDef>(),overridePriority=49,
            internalStats=new ModularWeaponInternalStats {massKg=.012f,ammunitionPrecisionFactor=1.35f}});
        nodes.Add(spare);Near(1.18f,Convert(nodes).AmmunitionPrecisionFactor,"Inactive/spare selector factors never multiply");
        spare.Props.overridePriority=51;spare.Props.internalStats.ammunitionPrecisionFactor=.84f;
        Near(.84f,Convert(nodes).AmmunitionPrecisionFactor,"The actual higher-priority projectile selects its cartridge precision");
        spare.Props.overridePriority=50;Near(.84f,Convert(nodes).AmmunitionPrecisionFactor,"Equal priority uses the same last-in-tree tie rule as projectile selection");
        spare.Props.isWeaponSubassembly=true;Near(1.18f,Convert(nodes).AmmunitionPrecisionFactor,"Underbarrel ammunition cannot affect the primary gun");
        spare.Props.isWeaponSubassembly=false;spare.Props.ammunitionType=null;spare.Props.overridePriority=100;
        Near(1,Convert(nodes).AmmunitionPrecisionFactor,"A winning non-ammo projectile override cannot borrow another selector's bonus");
        spare.Props.ammunitionType="other";spare.Props.internalStats.ammunitionPrecisionFactor=10;
        Near(1.35f,Convert(nodes).AmmunitionPrecisionFactor,"Malformed runtime factors cannot exceed the supported maximum");
        spare.Props.internalStats.ammunitionPrecisionFactor=.1f;Near(.5f,Convert(nodes).AmmunitionPrecisionFactor,"Runtime factors have a lower bound too");
        spare.Props.internalStats.ammunitionPrecisionFactor=0;Near(1,Convert(nodes).AmmunitionPrecisionFactor,"Unspecified cartridge precision is neutral");
        body.Props.internalStats.ammunitionPrecisionFactor=1.35f;
        Near(1,Convert(new List<ModularRenderNode>{body}).AmmunitionPrecisionFactor,"Weapon/optic parts cannot supply cartridge consistency");
    }
}
