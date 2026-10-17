using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using HarmonyLib;
using Helodrace;
using RimWorld;
using UnityEngine;
using Verse;

internal static class DuplexTests
{
    private static T Bare<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private sealed class FixtureProjectile : Projectile_DuplexBullet
    {
        internal FixtureProjectile Companion;
        internal static int Creations;
        internal void Seed(Vector3 start,LocalTargetInfo target)
        {
            origin=start;destination=new Vector3(20,0,0);usedTarget=target;
        }
        internal Vector3 Endpoint => destination;
        protected override Projectile_DuplexBullet CreateCompanion()
        {
            Creations++;
            return Companion = Make(def);
        }
    }
    private static FixtureProjectile Make(ThingDef def)
    {
        var result=Bare<FixtureProjectile>();result.def=def;
        AccessTools.Field(typeof(Thing),"mapIndexOrState").SetValue(result,(sbyte)-1);
        return result;
    }
    private static int launches,impacts;
    private static bool friendly;
    private static Thing equipment;
    private static ThingDef cover;
    private static bool NativeLaunch(Projectile __instance,Vector3 origin,LocalTargetInfo usedTarget,
        bool preventFriendlyFire,Thing equipment,ThingDef targetCoverDef)
    {
        if(!(__instance is Projectile_DuplexBullet))return true;
        launches++;friendly=preventFriendlyFire;DuplexTests.equipment=equipment;cover=targetCoverDef;
        ((FixtureProjectile)__instance).Seed(origin,usedTarget);
        return false;
    }
    private static bool NativeImpact(Bullet __instance)
    {
        if(!(__instance is Projectile_DuplexBullet))return true;
        impacts++;return false;
    }
    internal static void Run(Action<bool,string> check,Harmony harmony,Pawn pawn,DamageDef damage)
    {
        var nativeLaunch=AccessTools.Method(typeof(Projectile),nameof(Projectile.Launch),new[]{typeof(Thing),typeof(Vector3),typeof(LocalTargetInfo),typeof(LocalTargetInfo),typeof(ProjectileHitFlags),typeof(bool),typeof(Thing),typeof(ThingDef)});
        harmony.Patch(nativeLaunch,prefix:new HarmonyMethod(typeof(DuplexTests),nameof(NativeLaunch)){priority=Priority.First});
        harmony.Patch(AccessTools.Method(typeof(Bullet),"Impact"),prefix:new HarmonyMethod(typeof(DuplexTests),nameof(NativeImpact)){priority=Priority.First});
        var def=Bare<ThingDef>();def.projectile=new ProjectileProperties {damageDef=damage,speed=90};
        AccessTools.Field(typeof(ProjectileProperties),"damageAmountBase").SetValue(def.projectile,9);
        AccessTools.Field(typeof(ProjectileProperties),"armorPenetrationBase").SetValue(def.projectile,.14f);
        def.modExtensions=new List<DefModExtension>{new BulletBallisticsExtension(),new DuplexProjectileExtension()};
        var weapon=Bare<ThingWithComps>();var comp=new Helodrace.ModernWar.CompModularWeaponNode {parent=weapon};
        AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(weapon,new List<ThingComp>{comp});
        comp.ConvertedStats.BarrelLengthInches=20;
        var coverDef=Bare<ThingDef>();var first=Make(def);
        launches=impacts=FixtureProjectile.Creations=0;
        Rand.PushState(24681);
        try{first.Launch(pawn,Vector3.zero,new IntVec3(20,0,0),new IntVec3(20,0,0),ProjectileHitFlags.All,true,weapon,coverDef);}
        finally{Rand.PopState();}
        check(launches==2 && FixtureProjectile.Creations==1 && first.Companion!=null,"One duplex launch produces exactly two native bullet launches, without recursive splitting");
        check(friendly && equipment==weapon && cover==coverDef,"Companion preserves friendly-fire policy, equipment and cover metadata");
        var snapshotField=AccessTools.Field(typeof(Projectile_BallisticBullet),"ballistics");
        var a=(BulletBallisticsSnapshot)snapshotField.GetValue(first);var b=(BulletBallisticsSnapshot)snapshotField.GetValue(first.Companion);
        check(a!=null && b!=null && !ReferenceEquals(a,b) && a.referenceDamage==9 && b.referenceDamage==9,"Both bullets capture separate ballistic snapshots rather than duplicating direct damage at one impact");
        var impact=AccessTools.Method(typeof(Bullet),"Impact");impact.Invoke(first,new object[]{pawn,false});impact.Invoke(first.Companion,new object[]{pawn,true});
        check(impacts==2 && FixtureProjectile.Creations==1,"Both bullets use independent native impact/shield handling and do not split on impact");
        var endpoint=first.Companion.Endpoint;
        check(Math.Abs(endpoint.x-20)<.001f && Math.Abs(endpoint.z)<.42f,"Secondary divergence remains bounded around the primary flight endpoint");
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"duplex-"+Guid.NewGuid().ToString("N")+".xml");
        try
        {
            Scribe.saver.InitSaving(path,"test");first.Companion.ExposeData();Scribe.saver.FinalizeSaving();
            var loaded=Make(def);Scribe.loader.InitLoading(path);loaded.ExposeData();Scribe.loader.FinalizeLoading();
            check((bool)AccessTools.Field(typeof(Projectile_DuplexBullet),"companionIssued").GetValue(loaded),"The companion/split guard survives save/load");
            check(((BulletBallisticsSnapshot)snapshotField.GetValue(loaded)).referenceDamage==9,"Duplex save/load preserves the ballistic damage snapshot");
            loaded.Launch(pawn,Vector3.zero,new IntVec3(20,0,0),new IntVec3(20,0,0),ProjectileHitFlags.All);
            check(FixtureProjectile.Creations==1,"A restored companion cannot create another bullet");
        }
        finally{Scribe.ForceStop();if(File.Exists(path))File.Delete(path);}
        Console.WriteLine("PASS: production duplex launch recursion guard, independent ballistic snapshots/impacts, metadata and Scribe roundtrip.");
    }
}
