using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using Helodrace.ModernWar;
using UnityEngine;

internal static partial class Program
{
    private static void StockSnapshot()
    {
        foreach(var preset in definitions.Values.Where(e=>e.Name.LocalName=="Helodrace.ModernWar.ModularWeaponPresetDef"))
        {
            string weapon=preset.Element("weaponDef").Value;
            var def=Resolve(definitions[weapon]);
            var verb=def.Element("verbs").Elements().First(e=>(string)e.Element("verbClass")=="Verb_Shoot");
            float warmup=Number(verb.Element("warmupTime")),cooldown=Number(def.Element("statBases").Element("RangedWeapon_Cooldown"));
            var nodes=BuildAssembly(weapon,preset);
            foreach(var stock in nodes.Where(n=>n.Props.stock?.adjustable==true))
            {
                stock.railOffset=stock.Props.stock.retractedOffset;var a=Convert(nodes);
                stock.railOffset=stock.Props.stock.extendedOffset;var b=Convert(nodes);
                Console.WriteLine(preset.Element("defName").Value+" warmup="+(warmup*a.VanillaWarmupFactor).ToString("F3")+"/"+(warmup*b.VanillaWarmupFactor).ToString("F3")
                    +" cooldown="+(cooldown*a.VanillaCooldownFactor).ToString("F3")+"/"+(cooldown*b.VanillaCooldownFactor).ToString("F3")
                    +" long="+a.Long.Accuracy.ToString("P1")+"/"+b.Long.Accuracy.ToString("P1"));
            }
        }
    }
    private static void StatDirectionTests()
    {
        var raw=new ModularWeaponInternalStats {massKg=1.5f,recoilImpulse=5,muzzleRise=4,
            aimSpeed=55,aimStability=65,mechanicalPrecision=60,operatingReliability=.97f,gasFlow=1,
            receiverLengthMeters=.18f,naturalSupport=.15f};
        var body=Node(new CompProperties_ModularWeaponNode {isAssemblyRoot=true,internalStats=raw});
        var profile=new ModularWeaponStockProperties {adjustable=true,retractedOffset=.04f,extendedOffset=-.04f};
        var stock=Node(new CompProperties_ModularWeaponNode {internalStats=new ModularWeaponInternalStats {massKg=.3f},stock=profile});
        var nodes=new List<ModularRenderNode>{body,stock};
        stock.railOffset=profile.retractedOffset; var retracted=Convert(nodes);
        stock.railOffset=profile.extendedOffset; var extended=Convert(nodes);
        Near(retracted.MassKg,extended.MassKg,"Extension does not change component mass");
        Near(retracted.VanillaCycleFactor,extended.VanillaCycleFactor,"Stock state does not change mechanical cycle");
        Near(retracted.Long.MechanicalCeiling,extended.Long.MechanicalCeiling,"Stock state cannot improve inherent precision");
        Check(extended.AimStability>retracted.AimStability && extended.Long.Accuracy>retracted.Long.Accuracy,"Extension improves sustained aim");
        Check(extended.VanillaCooldownFactor<retracted.VanillaCooldownFactor,"Extension improves recoil recovery");
        Check(extended.VanillaWarmupFactor>retracted.VanillaWarmupFactor,"Extension pays a handling-length cost");
        Check(extended.Long.HoldEfficiency-retracted.Long.HoldEfficiency>extended.Touch.HoldEfficiency-retracted.Touch.HoldEfficiency,
            "Stock support matters more at long range than touch range");
        Near(0,profile.Extension(.5f),"Retracted stop clamps in reverse-sign geometry");
        Near(1,profile.Extension(-.5f),"Extended stop clamps in reverse-sign geometry");
        var before=Convert(nodes);raw.aimSpeed+=20;var faster=Convert(nodes);
        Check(faster.VanillaWarmupFactor<before.VanillaWarmupFactor,"Aim speed improves pre-shot readiness");
        Near(before.VanillaCooldownFactor,faster.VanillaCooldownFactor,"Aim speed cannot improve cooldown");
        Near(before.VanillaCycleFactor,faster.VanillaCycleFactor,"Aim speed cannot improve mechanical RPM");
        Near(before.Long.Accuracy,faster.Long.Accuracy,"Aim speed cannot directly improve long-range accuracy");
        raw.aimSpeed-=20;raw.mechanicalPrecision+=10;var precise=Convert(nodes);
        Check(precise.Long.MechanicalCeiling>before.Long.MechanicalCeiling && precise.CEShotSpread<before.CEShotSpread,"Mechanical precision improves ceiling/CE spread");
        Near(before.Long.HoldEfficiency,precise.Long.HoldEfficiency,"Precision cannot improve hold efficiency");
        Near(before.CESwayFactor,precise.CESwayFactor,"Precision cannot improve CE sway");
        raw.mechanicalPrecision-=10;raw.aimStability-=20;var shaky=Convert(nodes);
        Near(before.Long.MechanicalCeiling,shaky.Long.MechanicalCeiling,"Hold stability cannot improve mechanical ceiling");
        Check(shaky.Long.Accuracy<before.Long.Accuracy && shaky.CESwayFactor>before.CESwayFactor,"Hold affects accuracy/CE sway");
        raw.aimStability+=20;
        foreach(float canvas in new[]{.25f,.5f,1f,2f})
        {
            body.transform=new ModularTransform2D {position=new Vector2(100,-20),angle=45,scale=Vector2.one*canvas};
            stock.transform=new ModularTransform2D {position=new Vector2(-30,100),angle=-80,scale=Vector2.one*canvas};
            stock.Props.graphicOffset=new Vector3(canvas,0,canvas);
            var same=Convert(nodes);
            Near(before.HandlingLengthMeters,same.HandlingLengthMeters,"Canvas/pivot cannot change physical length");
            Near(before.CenterOfMassMeters,same.CenterOfMassMeters,"Canvas/pivot cannot change mass balance");
            Near(before.VanillaWarmupFactor,same.VanillaWarmupFactor,"Animation/sprite transforms cannot change readiness");
            Near(before.VanillaCooldownFactor,same.VanillaCooldownFactor,"Animation/sprite transforms cannot change recovery");
        }
        Check(new ModularWeaponStockProperties {adjustable=true}.ConfigErrors().Any(),"Coincident adjustable endpoints rejected");
        Near(0,new ModularWeaponStockProperties {adjustable=true}.Extension(0),"Incomplete live stock authoring never produces NaN");
        Check(new ModularWeaponStockProperties {extendedSupport=float.NaN}.ConfigErrors().Any(),"Non-finite stock support rejected");
        Check(new ModularWeaponStockProperties {extendedLengthMeters=-1}.ConfigErrors().Any(),"Invalid physical stock lengths rejected");

        body.Props.realisticRoundsPerMinute=600;
        var privateFields=BindingFlags.Instance|BindingFlags.NonPublic;
        typeof(CompModularWeaponNode).GetField("performanceCacheDirty",privateFields).SetValue(body.comp,false);
        typeof(CompModularWeaponNode).GetField("resolvedConvertedStats",privateFields).SetValue(body.comp,before);
        typeof(CompModularWeaponNode).GetField("resolvedFireDelayMultiplier",privateFields).SetValue(body.comp,1f);
        float firstCooldown=1.7f;body.comp.ApplyFireDelayToCooldown(ref firstCooldown);
        int firstTicks=body.comp.EffectiveBurstIntervalTicks;
        typeof(CompModularWeaponNode).GetField("resolvedFireDelayMultiplier",privateFields).SetValue(body.comp,1.4f);
        float secondCooldown=1.7f;body.comp.ApplyFireDelayToCooldown(ref secondCooldown);
        Near(firstCooldown,secondCooldown,"Actual cooldown path does not multiply mechanical cycle a second time");
        Near(1.7f*before.VanillaCooldownFactor,secondCooldown,"Actual cooldown agrees with the conversion contract");
        Check(body.comp.EffectiveBurstIntervalTicks>firstTicks,"Actual burst interval ticks still follow the mechanical cycle");

        var dotProfile=ModularWeaponSightPerformance.For(ModularSightKind.Reflex,false,1);
        var scopeProfile=ModularWeaponSightPerformance.For(ModularSightKind.Scope,false,1);
        var groups=new List<ModularSightGroupStatus>{new() {performance=dotProfile},new() {performance=scopeProfile}};
        Near(dotProfile.Warmup,ModularWeaponSightPerformance.WarmupAtDistance(groups,3),"Close targets use the close sight group's acquisition cost");
        Near(scopeProfile.Warmup,ModularWeaponSightPerformance.WarmupAtDistance(groups,40),"Far targets pay the scope group's acquisition cost");
        Near(scopeProfile.Long,scopeProfile.AtDistance(90),"Sight distance interpolation clamps beyond long range");
        var blockedScope=ModularWeaponSightPerformance.For(ModularSightKind.Scope,false,.1f);
        Check(blockedScope.Touch<=scopeProfile.Touch && blockedScope.Warmup>=scopeProfile.Warmup,"Occlusion never improves a scope's close-range or readiness cost");

        var secondary=Node(new CompProperties_ModularWeaponNode {isWeaponSubassembly=true,
            internalStats=new ModularWeaponInternalStats {massKg=.5f,mechanicalPrecision=100,aimStability=100,
                barrelLengthInches=30,rangeAdjustmentPercent=100,gasFlow=1},
            sight=new ModularWeaponSightProperties {kind=ModularSightKind.Scope,axisOffset=new Vector3(0,0,.1f)}});
        var secondaryNodes=new List<ModularRenderNode>{body,stock,secondary};
        var own=Convert(nodes);var combined=Convert(secondaryNodes);
        Near(own.Long.MechanicalCeiling,combined.Long.MechanicalCeiling,"Secondary launcher cannot improve primary precision");
        Near(own.AimStability,combined.AimStability,"Secondary launcher cannot improve primary hold");
        Near(own.RangeFactor,combined.RangeFactor,"Secondary launcher optics and range cannot improve primary range");
        Near(own.BarrelLengthInches,combined.BarrelLengthInches,"Secondary barrel cannot change primary ballistics");
        Check(combined.MassKg>own.MassKg,"Secondary launchers still add handling mass");

        var schema=typeof(ModularWeaponInternalStats).GetFields();
        Check(!schema.Any(f=>new[]{"ergonomics","targetAcquisition","aimingPrecision"}.Contains(f.Name)),"Obsolete raw fields removed, no compatibility branches");
        var copy=raw.Clone(); copy.mechanicalPrecision=99;
        Near(60,raw.mechanicalPrecision,"Undo snapshots are independent stat objects");
        int states=0;
        foreach(var preset in definitions.Values.Where(e=>e.Name.LocalName=="Helodrace.ModernWar.ModularWeaponPresetDef"))
        {
            var assembly=BuildAssembly(preset.Element("weaponDef").Value,preset);
            var original=Convert(assembly);
            foreach(var band in new[]{original.Touch,original.Short,original.Medium,original.Long})
                Check(band.Accuracy<=band.MechanicalCeiling && band.SightEfficiency<=1 && band.HoldEfficiency<=1,"Preset accuracy is bounded by its mechanical ceiling");
            foreach(var adjustable in assembly.Where(n=>n.Props.stock?.adjustable==true))
            {
                var p=adjustable.Props.stock;
                Check(!p.ConfigErrors().Any(),"Authored stock profile is valid");
                adjustable.railOffset=p.retractedOffset; var a=Convert(assembly);
                adjustable.railOffset=p.extendedOffset; var b=Convert(assembly);
                Near(a.MassKg,b.MassKg,"Real preset extension keeps mass");
                Near(a.VanillaCycleFactor,b.VanillaCycleFactor,"Real preset extension keeps RPM");
                Near(a.Long.MechanicalCeiling,b.Long.MechanicalCeiling,"Real preset extension keeps precision");
                Near(a.Long.SightEfficiency,b.Long.SightEfficiency,"Stock state cannot change an optic profile");
                Check(b.Long.Accuracy>a.Long.Accuracy && b.VanillaCooldownFactor<a.VanillaCooldownFactor
                    && b.VanillaWarmupFactor>a.VanillaWarmupFactor,"AR/Flux/EBR/MP5 extension has a real support/handling tradeoff: "+preset.Element("defName"));
                var weaponDef=Resolve(definitions[preset.Element("weaponDef").Value]);
                var primaryVerb=weaponDef.Element("verbs").Elements().First(e=>(string)e.Element("verbClass")=="Verb_Shoot");
                float baseWarmup=Number(primaryVerb.Element("warmupTime"));
                Check(Verse.GenTicks.SecondsToTicks(baseWarmup*b.VanillaWarmupFactor)>Verse.GenTicks.SecondsToTicks(baseWarmup*a.VanillaWarmupFactor),
                    "Stock readiness difference survives actual 60-tick rounding: "+preset.Element("defName"));
                states++;
            }
        }
        Check(states>=6,"Real authored presets exercise adjustable stocks across platforms");
        // Frozen from the same source/Defs on actual 1.6 references before native
        // conversion, then compared byte-for-byte with the 1.7 result. Authoring
        // calibration targets in Program are separate from migration regression.
        using var migrationBaseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "EngineMigrationBaseline.json")));
        foreach(var pair in migrationBaseline.RootElement.EnumerateObject())
        {
            var def=Resolve(definitions[pair.Name]);var comp=def.Element("comps").Elements().Single(e=>(string)e.Attribute("Class")==CompClass);
            var value=Convert(BuildAssembly(pair.Name,definitions[comp.Element("spawnPreset").Value]));
            var bands=new[]{value.Touch,value.Short,value.Medium,value.Long};
            var expectedAccuracy = pair.Value.GetProperty("Accuracy").EnumerateArray().Select(e => e.GetSingle()).ToArray();
            for(int i=0;i<4;i++) Check(Math.Abs(bands[i].Accuracy-expectedAccuracy[i])<.002f,"Migration preserves stock normal-quality accuracy: "+pair.Name);
            var verb=def.Element("verbs").Elements().First(e=>(string)e.Element("verbClass")=="Verb_Shoot");
            float warmup=Number(verb.Element("warmupTime"))*value.VanillaWarmupFactor;
            float cooldown=Number(def.Element("statBases").Element("RangedWeapon_Cooldown"))*value.VanillaCooldownFactor;
            Check(Math.Abs(warmup-pair.Value.GetProperty("Warmup").GetSingle())<.005f && Math.Abs(cooldown-pair.Value.GetProperty("Cooldown").GetSingle())<.005f,
                "Migration preserves existing stock timing: "+pair.Name);
        }
        Console.WriteLine("PASS: split precision/hold/acquisition, physical canvas invariance, bounded accuracy and "+states+" real stock-state tradeoffs.");
    }
}
