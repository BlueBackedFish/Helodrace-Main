using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.ModernWar;
using RimWorld;
using Verse;

internal static class ModularRangeTests
{
    private static T Bare<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static Thing caster;
    private static Thing weapon;
    private static Map weatherMap;
    private static bool MapFixture(Thing __instance, ref Map __result)
    {
        if (__instance != caster && __instance != weapon) return true;
        __result = weatherMap; return false;
    }

    internal static void Run(Action<bool,string> check, Harmony harmony)
    {
        // Only map ownership and the stat worker are headless fixtures. The native
        // EffectiveRange, weather cap, OutOfRange and production postfix all run.
        StatDefOf.RangedWeapon_RangeMultiplier = Bare<StatDef>();
        harmony.Patch(AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.MapHeld)),
            prefix: new HarmonyMethod(typeof(ModularRangeTests), nameof(MapFixture)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Map)),
            prefix: new HarmonyMethod(typeof(ModularRangeTests), nameof(MapFixture)));
        var equipment = Bare<ThingWithComps>(); equipment.def = Bare<ThingDef>(); weapon=equipment;
        var comp = new CompModularWeaponNode {parent=equipment};
        var eq = new CompEquippable {parent=equipment};
        AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(equipment,new List<ThingComp>{comp,eq});
        caster=Bare<ThingWithComps>();caster.def=Bare<ThingDef>();
        var props=new VerbProperties {verbClass=typeof(Verb_Shoot),range=30,isPrimary=true};
        AccessTools.Field(typeof(ThingDef),"verbs").SetValue(equipment.def,new List<VerbProperties>{props});
        var verb=Bare<Verb_Shoot>();verb.verbProps=props;verb.verbTracker=eq.verbTracker;verb.caster=caster;
        comp.ConvertedStats.RangeFactor=1.2f;
        check(Math.Abs(verb.EffectiveRange-36)<.001f,"Native projectile verb consumes the modular range multiplier once");
        check(Math.Abs(ModularWeaponRangeUtility.PreviewPrimaryRange(equipment)-verb.EffectiveRange)<.001f,
            "Modding panel range agrees with native range without a caster or weather assumptions");
        var target=new IntVec3(35,0,0);
        check(!verb.OutOfRange(IntVec3.Zero,new LocalTargetInfo(target),new CellRect(35,0,1,1)),
            "Native target range test permits a mod-extended shot");
        comp.ConvertedStats.RangeFactor=.8f;
        check(Math.Abs(verb.EffectiveRange-24)<.001f,"Changed parts immediately change native effective range");
        check(verb.OutOfRange(IntVec3.Zero,new LocalTargetInfo(target),new CellRect(35,0,1,1)),
            "Native target range test rejects a shot outside the current build");
        check(props.range==30,"Modding never mutates the shared VerbProperties definition");
        var alternate=Bare<Verb_Shoot>();alternate.verbProps=new VerbProperties {verbClass=typeof(Verb_Shoot),range=20};
        alternate.verbTracker=eq.verbTracker;alternate.caster=caster;
        check(Math.Abs(alternate.EffectiveRange-16)<.001f,"Alternate fire retains its own base range");
        var launcher=Bare<Verb_ShootModularUnderbarrel>();
        launcher.verbProps=new VerbProperties {verbClass=typeof(Verb_ShootModularUnderbarrel),range=27.9f};
        launcher.verbTracker=eq.verbTracker;launcher.caster=caster;
        check(Math.Abs(launcher.EffectiveRange-27.9f)<.001f,"Underbarrel grenades do not inherit the rifle range penalty");
        comp.ConvertedStats.RangeFactor=1.2f;
        check(Math.Abs(launcher.EffectiveRange-27.9f)<.001f,"Underbarrel grenades do not inherit the rifle range bonus");
        weatherMap=Bare<Map>();weatherMap.weatherManager=Bare<WeatherManager>();
        weatherMap.weatherManager.curWeather=Bare<WeatherDef>();weatherMap.weatherManager.curWeather.maxRangeCap=10;
        check(Math.Abs(verb.EffectiveRange-10)<.001f,"A scope cannot exceed the weather range cap");
        comp.ConvertedStats.RangeFactor=.8f;
        check(Math.Abs(verb.EffectiveRange-10)<.001f,"Weather is capped after the build factor, not multiplied twice");
        check(Math.Abs(ModularWeaponRangeUtility.PreviewPrimaryRange(equipment)-verb.EffectiveRange)<.001f,
            "Modding panel range agrees with native weather-limited range");
        check(Math.Abs(launcher.EffectiveRange-10)<.001f,"Launcher weather cap remains intact");
        weatherMap.weatherManager.curWeather.maxRangeCap=0;
        check(verb.EffectiveRange==0,"Zero weather range cap cannot be bypassed or divided by zero");
        weatherMap=null;
        AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(equipment,new List<ThingComp>{eq});
        check(Math.Abs(verb.EffectiveRange-30)<.001f,"Non-modular weapons retain their native range");
    }
}
