using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace;

public sealed class HuntingWeaponExtension:DefModExtension { }
public static class ShootingRecreation
{
    public static Verb VerbFor(Pawn pawn) => pawn?.equipment?.Primary?.GetComp<CompEquippable>()?.PrimaryVerb;
    public static bool CanShoot(Pawn pawn)
    {
        var verb=VerbFor(pawn);var projectile=verb?.verbProps.defaultProjectile;
        return pawn?.Spawned==true&&!pawn.Dead&&!pawn.Downed&&!pawn.WorkTagIsDisabled(WorkTags.Violent)
            &&pawn.health.capacities.CapableOf(PawnCapacityDefOf.Sight)&&verb!=null&&!verb.IsMeleeAttack
            &&verb.verbProps.burstShotCount==1&&projectile?.projectile!=null
            &&!projectile.projectile.flyOverhead&&projectile.projectile.explosionRadius<=0
            &&projectile.projectile.damageDef==DamageDefOf.Bullet&&typeof(Bullet).IsAssignableFrom(projectile.thingClass);
    }
    public static bool HasHuntingGun(Pawn pawn) => CanShoot(pawn)
        &&pawn.equipment.Primary.def.GetModExtension<HuntingWeaponExtension>()!=null;
}

public interface IRecreationRound { RecreationRound Round {get;} }
public sealed class RecreationRound:IExposable
{
    public Thing target;
    public bool active,fired,hit;
    public int deadline;
    private List<Projectile> projectiles=new();
    public void Begin(Thing value)
    {target=value;active=true;fired=hit=false;deadline=Find.TickManager.TicksGame+1200;projectiles.Clear();}
    public void Launch(Projectile p,LocalTargetInfo intended)
    {if(active&&intended.Thing==target&&!projectiles.Contains(p)){fired=true;projectiles.Add(p);}}
    public void Impact(Projectile p,Thing value,bool blocked)
    {if(active&&!blocked&&value==target&&projectiles.Contains(p))hit=true;}
    public bool Resolved => fired&&projectiles.All(p=>p==null||p.Destroyed||!p.Spawned);
    public bool TimedOut => Find.TickManager.TicksGame>deadline;
    public void Cancel(){active=false;projectiles.Clear();}
    public void ExposeData()
    {
        if(Scribe.mode==LoadSaveMode.Saving)projectiles.RemoveAll(p=>p==null||p.Destroyed);
        Scribe_References.Look(ref target,"target");Scribe_Values.Look(ref active,"active");
        Scribe_Values.Look(ref fired,"fired");Scribe_Values.Look(ref hit,"hit");Scribe_Values.Look(ref deadline,"deadline");
        Scribe_Collections.Look(ref projectiles,"projectiles",LookMode.Reference);projectiles??=new();
    }
}
[HarmonyPatch]
public static class Patch_RecreationRoundLaunch
{
    static MethodBase TargetMethod()=>AccessTools.GetDeclaredMethods(typeof(Projectile)).Single(m=>m.Name==nameof(Projectile.Launch)&&m.GetParameters().Length==8);
    static void Postfix(Projectile __instance,Thing launcher,LocalTargetInfo intendedTarget)
    {if(launcher is Pawn p&&p.jobs?.curDriver is IRecreationRound driver)driver.Round?.Launch(__instance,intendedTarget);}
}
[HarmonyPatch(typeof(Bullet),"Impact")]
public static class Patch_RecreationRoundImpact
{
    static void Prefix(Bullet __instance,Thing hitThing,bool blockedByShield)
    {if(__instance.Launcher is Pawn p&&p.jobs?.curDriver is IRecreationRound driver)driver.Round?.Impact(__instance,hitThing,blockedByShield);}
}
