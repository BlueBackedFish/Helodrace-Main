using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HarmonyLib;
using Helodrace.Ballistics;
using RimWorld;
using Verse;

namespace Helodrace.Panels
{
    public struct PanelArmorResult
    {
        public PenetratorState output;
        public bool stopped, ricochet;
        public float absorbedJ;
    }

    public static class PanelCombat
    {
        private static readonly AccessTools.StructFieldRef<DamageInfo,float> Penetration = AccessTools.StructFieldRefAccess<DamageInfo,float>("armorPenetrationInt");
        public static PanelArmorResult ResolveArmor(PenetratorState state, PenetratorClass kind, KineticMaterial material,
            float thickness, float effectiveness, Vector3 outwardNormal, ulong seed)
        {
            var rng = new BallisticRng(seed);
            if (KineticBulletModel.TryRicochet(state, outwardNormal, material, ref rng, out PenetratorState bounced, out float absorbed, out _))
                return new PanelArmorResult { output=bounced, stopped=true, ricochet=true, absorbedJ=absorbed };
            if (!BallisticGeometry.TryObliquity(state.Velocity,outwardNormal,out float angle))
                return new PanelArmorResult { output=state };
            KineticLayerResult result=KineticBulletModel.ResolveLayer(state,kind,material,thickness,angle,effectiveness,ref rng);
            return new PanelArmorResult {output=result.Out,stopped=!result.Perforated,absorbedJ=result.AbsorbedJ};
        }

        public static bool TraceFace(Vector3 origin, Vector3 travel, IntVec3 cell, out Rot4 face, out Vector3 point)
        {
            float enter=float.NegativeInfinity,exit=float.PositiveInfinity;
            face=Rot4.Invalid;point=default;
            for(int axis=0;axis<2;axis++)
            {
                float start=axis==0?origin.X:origin.Z,velocity=axis==0?travel.X:travel.Z;
                float low=axis==0?cell.x:cell.z;
                if(MathF.Abs(velocity)<1e-7f){if(start<low||start>low+1)return false;continue;}
                float a=(low-start)/velocity,b=(low+1-start)/velocity;
                float near=MathF.Min(a,b),far=MathF.Max(a,b);
                if(near>=enter)
                {
                    enter=near;
                    face=axis==0?(velocity>0?Rot4.West:Rot4.East):(velocity>0?Rot4.South:Rot4.North);
                }
                exit=MathF.Min(exit,far);
            }
            if(!face.IsValid||enter<0||exit<enter)return false;
            point=origin+travel*enter;return true;
        }

        public static void Wear(Building_WallPanel panel,float amount)
        {
            if(amount<=0||!panel.Spawned)return;
            int hp=Math.Min(panel.HitPoints,Math.Max(1,(int)MathF.Ceiling(amount)));
            panel.HitPoints-=hp;
            if(panel.HitPoints<=0){panel.Destroy(DestroyMode.KillFinalize);return;}
            // Direct HP removal skips Building.PostApplyDamage; register the
            // damage so the panel is offered for repair like any other building.
            panel.Map.listerBuildingsRepairable?.Notify_BuildingTookDamage(panel);
        }
        public static void Resolve(Building host, ref DamageInfo damage)
        {
            if(damage.Def==DamageDefOf.Flame || damage.Def==DamageDefOf.Burn)
            {
                float factor=PanelSurface.FireFactor(host,true,PanelSurface.CurrentFireSource,damage.Angle);
                if(factor<1)
                {
                    float incoming=damage.Amount; damage.SetAmount(incoming*factor);
                    IntVec3? source=PanelSurface.CurrentFireSource;
                    Rot4 face=source.HasValue&&source.Value!=host.Position?PanelSurface.FaceToward(host.Position,source.Value):
                        damage.Angle>=0?PanelSurface.FaceFromDirection(damage.Angle):Rot4.Invalid;
                    foreach(var panel in host.Map.GetComponent<MapComponent_WallPanels>().For(host))
                        if(PanelRules.IsActive(panel)&&(!face.IsValid||panel.Rotation==face)&&panel.Properties.flameMultiplier<1)
                            Wear(panel,incoming*(1-panel.Properties.flameMultiplier)*.5f);
                }
                return;
            }
            if(damage.Def.isExplosive || damage.Def==DamageDefOf.Deterioration)return;
            PanelShot shot=PanelBulletScope.Current;
            bool primary=shot!=null&&shot.host==host&&!shot.resolved&&damage.Def==shot.bullet.DamageDef;
            if(!primary&&damage.Angle<0)return;
            Rot4 incomingFace=primary?shot.face:PanelSurface.FaceFromDirection(damage.Angle);
            if(primary)shot.resolved=true;
            var layers=host.Map.GetComponent<MapComponent_WallPanels>().For(host)
                .Where(p=>p.Rotation==incomingFace&&PanelRules.IsActive(p)).OrderByDescending(p=>p.Properties.slot).ToArray();
            foreach(Building_WallPanel panel in layers)
            {
                if(damage.Amount<=0)break;
                WallPanelProperties props=panel.Properties;
                if(primary&&shot.kinetic&&props.armorMaterial!=null&&props.armorThicknessMeters>0&&!props.aramid)
                {
                    float energy=shot.state.KineticEnergyJ, speed=shot.state.Speed;
                    Vector3 normal=new(GenAdj.CardinalDirections[incomingFace.AsInt].x,0,GenAdj.CardinalDirections[incomingFace.AsInt].z);
                    ulong seed=BallisticSeed.ForImpact(shot.bullet.thingIDNumber,Find.TickManager.TicksGame,panel.thingIDNumber);
                    PanelArmorResult result=ResolveArmor(shot.state,shot.kind,props.armorMaterial.Model,props.armorThicknessMeters,
                        Math.Clamp(panel.HitPoints/(float)panel.MaxHitPoints,.1f,1),normal,seed);
                    shot.state=result.output;
                    Wear(panel,MathF.Max(1,result.absorbedJ*props.armorMaterial.Model.WearPerJoule));
                    if(result.ricochet)
                        Projectile_Ricochet.TryLaunchFromSurface(shot.bullet,shot.map,shot.point+normal*.02f,
                            result.output.Velocity,result.output,shot.kind,speed,damage.Amount,damage.ArmorPenetrationInt);
                    if(result.stopped) { damage.SetAmount(0);break; }
                    damage.SetAmount(damage.Amount*KineticBulletModel.WoundFactor(energy,result.output.KineticEnergyJ));
                    Penetration(ref damage) *= KineticBulletModel.VanillaPenetrationFactor(speed,result.output.Speed);
                }
                else
                {
                    float absorbed=MathF.Min(panel.HitPoints,damage.Amount);
                    Wear(panel,absorbed);damage.SetAmount(MathF.Max(0,damage.Amount-absorbed));
                }
            }
        }
    }

    public sealed class PanelShot
    {
        public Bullet bullet;
        public Building host;
        public Map map;
        public Rot4 face;
        public Vector3 point;
        public bool kinetic,resolved;
        public PenetratorState state;
        public PenetratorClass kind;
    }

    [HarmonyPatch(typeof(Bullet),"Impact")]
    public static class PanelBulletScope
    {
        // Actual protected game field is a compatibility Vector3; convert once.
        private static readonly AccessTools.FieldRef<Projectile,UnityEngine.Vector3> Origin=AccessTools.FieldRefAccess<Projectile,UnityEngine.Vector3>("origin");
        [ThreadStatic] private static Stack<PanelShot> shots;
        public static PanelShot Current=>shots?.Count>0?shots.Peek():null;
        static void Prefix(Bullet __instance,Thing hitThing,out bool __state)
        {
            __state=false;
            PanelShot shot=null;
            if(hitThing is Building host&&host.Spawned&&PanelRules.IsHost(host.def))
            {
                Vector3 direction=BallisticGeometry.TravelDirection(ProjectilePhysics.HeadingDegrees(__instance),0);
                Vector3 origin=Origin(__instance).ToNative();
                bool traced=PanelCombat.TraceFace(origin,direction,host.Position,out Rot4 face,out Vector3 point);
                if(!traced)
                {
                    face=PanelSurface.FaceFromDirection(ProjectilePhysics.HeadingDegrees(__instance));
                    IntVec3 n=GenAdj.CardinalDirections[face.AsInt];
                    point=new(host.Position.x+.5f+n.x*.5f,0,host.Position.z+.5f+n.z*.5f);
                }
                shot=new PanelShot{bullet=__instance,host=host,map=host.Map,face=face,point=point};
                if(__instance is Projectile_Ricochet reflected)
                {
                    shot.kinetic=true;shot.kind=reflected.penetratorClass;
                    shot.state=PenetratorState.Projectile(PenetrationMechanism.KineticBullet,reflected.massKg,direction*reflected.speedMetersPerSecond,0,reflected.diameterM);
                }
                else if(ProjectilePhysics.TryKinetic(__instance,out VehicleKineticRound round,out PenetratorState state,out _,out _)&&round.Mechanism==PenetrationMechanism.KineticBullet)
                {shot.kinetic=true;shot.state=state;shot.kind=round.SmallArmsClass;}
            }
            (shots??=new()).Push(shot);
            __state=true;
        }
        static Exception Finalizer(Exception __exception,bool __state){if(__state)shots.Pop();return __exception;}
    }
}
